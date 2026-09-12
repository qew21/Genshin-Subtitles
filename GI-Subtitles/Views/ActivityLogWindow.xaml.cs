using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GI_Subtitles.Core.Config;
using GI_Subtitles.Core.Overlay;

namespace GI_Subtitles.Views
{
    public partial class ActivityLogWindow : Window
    {
        private readonly LiveOverlaySession _session;
        private readonly ObservableCollection<ActivityLogRowView> _rows = new ObservableCollection<ActivityLogRowView>();
        private readonly List<ActivityLogRow> _rowSources = new List<ActivityLogRow>();
        private readonly ActivityLogFollowTail _followTail = new ActivityLogFollowTail();
        private ScrollViewer _scrollViewer;
        private bool _forceClose;
        private bool _opened;
        private bool _applyingFollowTail;
        private bool _operatorScrolling;
        private bool _operatorDraggingBar;
        private bool _scrollBarHooked;
        private int _anchorIndex = -1;
        private ActivityLogRowFilter _filter = new ActivityLogRowFilter(ReadLogDenoise());

        public ActivityLogWindow(LiveOverlaySession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            _session = session;
            InitializeComponent();
            LogList.ItemsSource = _rows;
            Loaded += OnLoaded;
            Closing += OnClosing;
            Application.Current.Exit += OnAppExit;
            _session.ActivityLogChanged += OnActivityLogChanged;
        }

        public void ShowOrFocus(bool stayAboveSettingsDialog = false)
        {
            if (_opened)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
            }

            Topmost = stayAboveSettingsDialog;
            if (!IsVisible)
            {
                Show();
                _opened = true;
                Rebuild();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
        }

        public void ClearStayAbove()
        {
            Topmost = false;
        }

        private static bool ReadLogDenoise()
        {
            return Config.Get("LogDenoise", true);
        }

        public void ApplyLogDenoiseSetting()
        {
            // The settings checkbox toggled: re-project now while the window is
            // open; a hidden window picks the setting up in its next Rebuild.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsVisible || ReadLogDenoise() == _filter.HideRepeats)
                {
                    return;
                }

                Rebuild();
            }));
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _scrollViewer = FindScrollViewer(LogList);
            if (_scrollViewer != null)
            {
                _scrollViewer.ApplyTemplate();
                _scrollViewer.ScrollChanged += OnScrollChanged;
                _scrollViewer.PreviewMouseWheel += OnOperatorScrollPreview;
                HookVerticalScrollBar(_scrollViewer);
            }

            LogList.PreviewMouseWheel += OnOperatorScrollPreview;
            Rebuild();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_forceClose)
            {
                return;
            }

            e.Cancel = true;
            Hide();
        }

        private void OnAppExit(object sender, ExitEventArgs e)
        {
            _session.ActivityLogChanged -= OnActivityLogChanged;
            Application.Current.Exit -= OnAppExit;
            _forceClose = true;
        }

        private void OnActivityLogChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsVisible)
                {
                    return;
                }

                SyncRows();
            }));
        }

        private void Rebuild()
        {
            _filter = new ActivityLogRowFilter(ReadLogDenoise());
            _rows.Clear();
            _rowSources.Clear();
            _anchorIndex = -1;
            SyncRows(announceVisibleAdds: false);
        }

        private void SyncRows(bool announceVisibleAdds = true)
        {
            // Append path: project only newly consumed rows. Re-projecting every
            // already-shown row made ResultLines fire even when the snapshot
            // did not change. Mutation-only notifies (Consume returns nothing)
            // still refresh existing views — e.g. voice folded into a prior row.
            // Hidden repeats are still consumed so they are never reconsidered;
            // the session record itself is never modified (ADR 0008 / 0010).
            IReadOnlyList<ActivityLogRow> shown = _filter.Consume(_session.ActivityLog);
            foreach (ActivityLogRow row in shown)
            {
                _rows.Add(Project(row));
                _rowSources.Add(row);
            }

            if (shown.Count == 0)
            {
                for (int i = 0; i < _rowSources.Count; i++)
                {
                    ApplyProjection(_rows[i], _rowSources[i]);
                }
            }

            EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (announceVisibleAdds && shown.Count > 0)
            {
                _followTail.VisibleContentAdded();
                ApplyFollowTail();
            }
            else if (!announceVisibleAdds)
            {
                ApplyFollowTail();
            }
        }

        private ActivityLogRowView Project(ActivityLogRow row)
        {
            var view = new ActivityLogRowView();
            ApplyProjection(view, row);
            return view;
        }

        private void ApplyProjection(ActivityLogRowView view, ActivityLogRow row)
        {
            view.Time = row.UtcTimestamp.ToLocalTime().ToString("HH:mm:ss");
            view.RegionPair = ResolveRegionPair(row);
            view.Job = ResolveJobs(row);
            ApplyResult(view, row);
            view.IsRepeat = row.IsRepeat;
        }

        private string ResolveRegionPair(ActivityLogRow row)
        {
            switch (row.Scope)
            {
                case ActivityLogScope.DarkScreen:
                    return ResolveText("ActivityLog_Scope_DarkScreen", null);
                case ActivityLogScope.DialogueOptions:
                    return ResolveText("ActivityLog_Scope_DialogueOptions", null);
                case ActivityLogScope.Pair:
                    if (row.PairOrdinal.HasValue)
                    {
                        string key = row.VoicePrimary
                            ? "ActivityLog_Scope_VoicePrimary"
                            : "ActivityLog_Scope_Pair";
                        return ResolveText(key, new object[] { row.PairOrdinal.Value });
                    }

                    return ResolveText("ActivityLog_Scope_Global", null);
                default:
                    return ResolveText("ActivityLog_Scope_Global", null);
            }
        }

        private string ResolveJobs(ActivityLogRow row)
        {
            IReadOnlyList<OperatorJob> jobs = row.Jobs;
            if (jobs == null || jobs.Count == 0)
            {
                return ResolveText(JobResourceKey(row.Job), null);
            }

            string separator = ResolveText("ActivityLog_JobSeparator", null);
            if (string.IsNullOrEmpty(separator))
            {
                separator = " · ";
            }

            var parts = new string[jobs.Count];
            for (int i = 0; i < jobs.Count; i++)
            {
                parts[i] = ResolveText(JobResourceKey(jobs[i]), null);
            }

            string joined = string.Join(separator, parts);
            if (row.IsRepeat)
            {
                string repeatBadge = ResolveText("ActivityLog_RepeatBadge", null);
                if (!string.IsNullOrEmpty(repeatBadge))
                {
                    joined += separator + repeatBadge;
                }
            }

            return joined;
        }

        // One Compose call feeds the result cell (ADR 0016): the segments
        // drive one TextBox per line; PlainText drives the row-copy TSV.
        // Copied text always equals displayed text.
        private void ApplyResult(ActivityLogRowView view, ActivityLogRow row)
        {
            ActivityLogResultProjection projection = ActivityLogResultComposer.Compose(row, ResolveText);
            string plainText = projection.PlainText;
            // ResultLines is a fresh list each Compose; only retarget when the
            // plain text changed so mutation refreshes do not churn the column.
            if (view.ResultLines == null
                || !string.Equals(view.Result, plainText, StringComparison.Ordinal))
            {
                view.Result = plainText;
                view.ResultLines = projection.Lines;
            }
        }

        private static string JobResourceKey(OperatorJob job)
        {
            switch (job)
            {
                case OperatorJob.StartRecognition:
                    return "ActivityLog_Job_StartRecognition";
                case OperatorJob.StopRecognition:
                    return "ActivityLog_Job_StopRecognition";
                case OperatorJob.HideSubtitles:
                    return "ActivityLog_Job_HideSubtitles";
                case OperatorJob.ShowSubtitles:
                    return "ActivityLog_Job_ShowSubtitles";
                case OperatorJob.BoxCapture:
                    return "ActivityLog_Job_BoxCapture";
                case OperatorJob.Refresh:
                    return "ActivityLog_Job_Refresh";
                case OperatorJob.VoiceSpeed:
                    return "ActivityLog_Job_VoiceSpeed";
                case OperatorJob.Preview:
                    return "ActivityLog_Job_Preview";
                case OperatorJob.Capture:
                    return "ActivityLog_Job_Capture";
                case OperatorJob.Ocr:
                    return "ActivityLog_Job_Ocr";
                case OperatorJob.Match:
                    return "ActivityLog_Job_Match";
                case OperatorJob.Voice:
                    return "ActivityLog_Job_Voice";
                case OperatorJob.LanguagePackLoad:
                    return "ActivityLog_Job_LanguagePackLoad";
                case OperatorJob.LanguagePackDownload:
                    return "ActivityLog_Job_LanguagePackDownload";
                default:
                    return string.Empty;
            }
        }

        private string ResolveText(string resourceKey, object[] formatArguments)
        {
            if (string.IsNullOrEmpty(resourceKey))
            {
                return string.Empty;
            }

            string format = TryFindResource(resourceKey) as string;
            if (string.IsNullOrEmpty(format))
            {
                return string.Empty;
            }

            if (formatArguments == null || formatArguments.Length == 0)
            {
                return format;
            }

            try
            {
                return string.Format(format, formatArguments);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        private void OnOperatorScrollPreview(object sender, MouseWheelEventArgs e)
        {
            MarkOperatorScrolling();
        }

        private void MarkOperatorScrolling()
        {
            _operatorScrolling = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                _operatorScrolling = false;
            }));
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_scrollViewer == null || _applyingFollowTail)
            {
                return;
            }

            HookVerticalScrollBar(_scrollViewer);

            if (_operatorDraggingBar || _operatorScrolling)
            {
                _operatorScrolling = false;
                _followTail.OperatorViewportAtBottom(IsViewportAtBottom());
                ApplyFollowTail();
                return;
            }

            if (!_followTail.IsFollowing)
            {
                return;
            }

            // Layout / virtualization / wrap remeasure report extent and viewport
            // changes. Keep the sticky pin with a shortfall scroll only when the
            // viewport has actually left the bottom slack — always scrolling on
            // ExtentHeightChange fights estimate jitter and can loop forever.
            if ((e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
                && !IsViewportAtBottom())
            {
                ScrollToNewestQuiet();
            }
        }

        private void NewRecordsButton_Click(object sender, RoutedEventArgs e)
        {
            _followTail.JumpToNewest();
            ApplyFollowTail();
        }

        private void ApplyFollowTail()
        {
            NewRecordsButton.Visibility = _followTail.ShowNewRecords
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (_followTail.IsFollowing)
            {
                PinToEnd();
            }
        }

        private void PinToEnd()
        {
            if (_applyingFollowTail)
            {
                return;
            }

            ScrollToNewestQuiet();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!_followTail.IsFollowing || _scrollViewer == null || IsViewportAtBottom())
                {
                    return;
                }

                ScrollToNewestQuiet();
            }));
        }

        private void ScrollToNewestQuiet()
        {
            if (_scrollViewer == null)
            {
                if (_rows.Count > 0)
                {
                    _applyingFollowTail = true;
                    try
                    {
                        LogList.ScrollIntoView(_rows[_rows.Count - 1]);
                    }
                    finally
                    {
                        _applyingFollowTail = false;
                    }
                }

                return;
            }

            double target = _scrollViewer.ScrollableHeight;
            if (ActivityLogFollowTail.IsAtBottom(_scrollViewer.VerticalOffset, target))
            {
                return;
            }

            _applyingFollowTail = true;
            try
            {
                // Scroll only the shortfall to the current end — not ScrollToEnd(),
                // which walks the whole virtualized extent. While already near the
                // tail this is ~one row (wheel-step cost class).
                _scrollViewer.ScrollToVerticalOffset(target);
            }
            finally
            {
                _applyingFollowTail = false;
            }
        }

        private bool IsViewportAtBottom()
        {
            return _scrollViewer != null
                && ActivityLogFollowTail.IsAtBottom(_scrollViewer.VerticalOffset, _scrollViewer.ScrollableHeight);
        }

        private void EndBarDrag()
        {
            _operatorDraggingBar = false;
        }

        private void HookVerticalScrollBar(ScrollViewer viewer)
        {
            if (_scrollBarHooked || viewer == null)
            {
                return;
            }

            ScrollBar bar = FindVerticalScrollBar(viewer);
            if (bar == null)
            {
                return;
            }

            _scrollBarHooked = true;
            bar.PreviewMouseDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    _operatorDraggingBar = true;
                }
            };
            bar.PreviewMouseUp += (s, e) => EndBarDrag();
            bar.LostMouseCapture += (s, e) => EndBarDrag();
        }

        private static bool IsViewportScrollKey(Key key)
        {
            return key == Key.PageUp
                || key == Key.PageDown
                || key == Key.Up
                || key == Key.Down
                || key == Key.Home
                || key == Key.End;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            if (root is ScrollViewer viewer)
            {
                return viewer;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                ScrollViewer child = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static ScrollBar FindVerticalScrollBar(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            var bar = root as ScrollBar;
            if (bar != null && bar.Orientation == Orientation.Vertical)
            {
                return bar;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                ScrollBar child = FindVerticalScrollBar(VisualTreeHelper.GetChild(root, i));
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        // The cell TextBox consumes the bubbling mouse-down, so ListView row
        // selection has to run in the tunneling preview phase instead — but
        // only for clicks that land on a cell; anywhere else the native
        // selection handling stays in charge and must not apply twice.
        private void RowItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) == null)
            {
                return;
            }

            var item = (ListViewItem)sender;
            int index = LogList.Items.IndexOf(item.Content);
            if (index < 0)
            {
                return;
            }

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.None;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.None;
            if (shift && _anchorIndex >= 0 && _anchorIndex < LogList.Items.Count)
            {
                if (!ctrl)
                {
                    LogList.UnselectAll();
                }

                int first = Math.Min(_anchorIndex, index);
                int last = Math.Max(_anchorIndex, index);
                for (int i = first; i <= last; i++)
                {
                    object row = LogList.Items[i];
                    if (!LogList.SelectedItems.Contains(row))
                    {
                        LogList.SelectedItems.Add(row);
                    }
                }

                return;
            }

            if (ctrl)
            {
                item.IsSelected = !item.IsSelected;
            }
            else
            {
                LogList.UnselectAll();
                item.IsSelected = true;
            }

            _anchorIndex = index;
        }

        private void LogList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            TextBox cell = FindAncestor<TextBox>(e.OriginalSource as DependencyObject);
            if (cell != null && cell.SelectionLength > 0)
            {
                SetClipboardWithRetry(cell.SelectedText);
            }
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsViewportScrollKey(e.Key))
            {
                MarkOperatorScrolling();
            }

            if (e.Key != Key.C || (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.None)
            {
                return;
            }

            if (GetCellSelection(null) != null)
            {
                return; // the focused cell copies its own selection natively
            }

            if (CopySelectedRowsToClipboard())
            {
                e.Handled = true;
            }
        }

        private void CopyMenu_Opened(object sender, RoutedEventArgs e)
        {
            var menu = (ContextMenu)sender;
            MenuItem copyItem = menu.Items.OfType<MenuItem>().FirstOrDefault();
            if (copyItem != null)
            {
                copyItem.IsEnabled = GetCellSelection(menu) != null || LogList.SelectedItems.Count > 0;
            }
        }

        private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menu = ((MenuItem)sender).Parent as ContextMenu;
            string selection = GetCellSelection(menu);
            if (selection != null)
            {
                SetClipboardWithRetry(selection);
                return;
            }

            CopySelectedRowsToClipboard();
        }

        // While a context menu is open, keyboard focus sits on the menu, so
        // the right-clicked cell has to come from the placement target. The
        // result column hosts N line TextBoxes; PlacementTarget may be one of
        // them or a parent (ItemsControl / list), so walk for a selection.
        private static string GetCellSelection(ContextMenu menu)
        {
            TextBox cell = null;
            if (menu != null)
            {
                cell = menu.PlacementTarget as TextBox;
                if (cell == null || cell.SelectionLength == 0)
                {
                    cell = FindTextBoxWithSelection(menu.PlacementTarget as DependencyObject);
                }
            }

            if (cell == null || cell.SelectionLength == 0)
            {
                cell = Keyboard.FocusedElement as TextBox;
            }

            if (cell != null && cell.SelectionLength > 0)
            {
                return cell.SelectedText;
            }

            return null;
        }

        private static TextBox FindTextBoxWithSelection(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            if (root is TextBox box && box.SelectionLength > 0)
            {
                return box;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                TextBox child = FindTextBoxWithSelection(VisualTreeHelper.GetChild(root, i));
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private bool CopySelectedRowsToClipboard()
        {
            if (LogList.SelectedItems.Count == 0)
            {
                return false;
            }

            var lines = new List<string>();
            foreach (ActivityLogRowView row in _rows)
            {
                if (LogList.SelectedItems.Contains(row))
                {
                    lines.Add(row.ToTsv());
                }
            }

            SetClipboardWithRetry(string.Join(Environment.NewLine, lines));
            return true;
        }

        // Copy-on-select runs on mouse-up. WPF Clipboard.SetText blocks the UI
        // thread inside OpenClipboard / OleFlushClipboard; when another process
        // holds the clipboard that freeze lasts seconds even for two log rows.
        // Enqueue a bounded WinForms write on one shared STA worker instead —
        // mouse-up returns immediately, retries happen off the UI thread, and
        // failure still stays silent per ADR 0011.
        private static readonly object ClipboardGate = new object();
        private static string _clipboardPending;
        private static Thread _clipboardWorker;
        private static readonly AutoResetEvent ClipboardSignal = new AutoResetEvent(false);

        private static void SetClipboardWithRetry(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            lock (ClipboardGate)
            {
                _clipboardPending = text;
                if (_clipboardWorker == null || !_clipboardWorker.IsAlive)
                {
                    _clipboardWorker = new Thread(ClipboardWorker)
                    {
                        IsBackground = true,
                        Name = "ActivityLogClipboard"
                    };
                    _clipboardWorker.SetApartmentState(ApartmentState.STA);
                    _clipboardWorker.Start();
                }
            }

            ClipboardSignal.Set();
        }

        private static void ClipboardWorker()
        {
            while (true)
            {
                ClipboardSignal.WaitOne();
                string text;
                lock (ClipboardGate)
                {
                    text = _clipboardPending;
                    _clipboardPending = null;
                }

                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                try
                {
                    System.Windows.Forms.Clipboard.SetDataObject(text, true, 10, 50);
                }
                catch (COMException)
                {
                }
                catch (ExternalException)
                {
                }
            }
        }

        private static T FindAncestor<T>(DependencyObject element) where T : class
        {
            while (element != null && !(element is T))
            {
                var content = element as FrameworkContentElement;
                element = content != null
                    ? content.Parent
                    : VisualTreeHelper.GetParent(element);
            }

            return element as T;
        }
    }

    internal sealed class ActivityLogRowView : INotifyPropertyChanged
    {
        private string _time;
        private string _regionPair;
        private string _job;
        private string _result;
        private IReadOnlyList<ActivityLogResultLine> _resultLines;
        private bool _isRepeat;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Time
        {
            get { return _time; }
            set { SetField(ref _time, value, nameof(Time)); }
        }

        public string RegionPair
        {
            get { return _regionPair; }
            set { SetField(ref _regionPair, value, nameof(RegionPair)); }
        }

        public string Job
        {
            get { return _job; }
            set { SetField(ref _job, value, nameof(Job)); }
        }

        public string Result
        {
            get { return _result; }
            set { SetField(ref _result, value, nameof(Result)); }
        }

        public IReadOnlyList<ActivityLogResultLine> ResultLines
        {
            get { return _resultLines; }
            set { SetField(ref _resultLines, value, nameof(ResultLines)); }
        }

        public bool IsRepeat
        {
            get { return _isRepeat; }
            set { SetField(ref _isRepeat, value, nameof(IsRepeat)); }
        }

        public string ToTsv()
        {
            return Time + "\t" + RegionPair + "\t" + Job + "\t" + Result;
        }

        private void SetField<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
