using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using GI_Subtitles.Core.Overlay;

namespace GI_Subtitles.Views
{
    public partial class ActivityLogWindow : Window
    {
        private readonly LiveOverlaySession _session;
        private readonly ObservableCollection<ActivityLogRowView> _rows = new ObservableCollection<ActivityLogRowView>();
        private readonly List<ActivityLogRow> _rowSources = new List<ActivityLogRow>();
        private bool _forceClose;
        private bool _opened;

        public ActivityLogWindow(LiveOverlaySession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            _session = session;
            InitializeComponent();
            LogList.ItemsSource = _rows;
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

        private void OnActivityLogChanged(object sender, ActivityLogChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsVisible)
                {
                    return;
                }

                SyncRows(e);
            }));
        }

        private void Rebuild()
        {
            _rows.Clear();
            _rowSources.Clear();
            SyncRows();
        }

        private void SyncRows(ActivityLogChangedEventArgs change = null)
        {
            IReadOnlyList<ActivityLogRow> log = _session.ActivityLog;
            if (change == null)
            {
                for (int i = 0; i < log.Count; i++)
                {
                    AddRow(log[i]);
                }
            }
            else
            {
                int removeCount = Math.Min(change.RemovedCount, _rowSources.Count);
                for (int i = 0; i < removeCount; i++)
                {
                    _rowSources.RemoveAt(0);
                    _rows.RemoveAt(0);
                }

                if (change.AddedRow != null)
                {
                    AddRow(change.AddedRow);
                }

                if (change.UpdatedRow != null)
                {
                    int index = _rowSources.IndexOf(change.UpdatedRow);
                    if (index >= 0)
                    {
                        ApplyProjection(_rows[index], change.UpdatedRow);
                    }
                }
            }

            EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddRow(ActivityLogRow row)
        {
            _rows.Add(Project(row));
            _rowSources.Add(row);
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

            return string.Join(separator, parts);
        }

        private void ApplyResult(ActivityLogRowView view, ActivityLogRow row)
        {
            view.ResultProjection = ActivityLogResultComposer.Compose(row, ResolveText);
        }

        private void ResultRichTextBox_DataContextChanged(
            object sender,
            DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ActivityLogRowView oldView)
            {
                oldView.PropertyChanged -= ResultView_PropertyChanged;
            }

            if (e.NewValue is ActivityLogRowView newView)
            {
                newView.PropertyChanged += ResultView_PropertyChanged;
            }

            RenderResult(sender as RichTextBox);
        }

        private void ResultView_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ActivityLogRowView.ResultProjection))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    RenderResultForView(sender as ActivityLogRowView)));
            }
        }

        private void RenderResultForView(ActivityLogRowView view)
        {
            if (view == null)
            {
                return;
            }

            foreach (RichTextBox box in FindResultBoxes())
            {
                if (ReferenceEquals(box.DataContext, view))
                {
                    RenderResult(box);
                }
            }
        }

        private IEnumerable<RichTextBox> FindResultBoxes()
        {
            return FindVisualChildren<RichTextBox>(this);
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
            where T : DependencyObject
        {
            if (root == null)
            {
                yield break;
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed)
                {
                    yield return typed;
                }

                foreach (T descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }

        private void RenderResult(RichTextBox box)
        {
            if (box == null)
            {
                return;
            }

            box.Document.Blocks.Clear();
            ActivityLogRowView view = box.DataContext as ActivityLogRowView;
            ActivityLogResultProjection projection = view?.ResultProjection;
            if (projection == null)
            {
                return;
            }

            foreach (ActivityLogResultLine line in projection.Lines)
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0)
                };
                if (!string.IsNullOrEmpty(line.TagText))
                {
                    paragraph.Inlines.Add(new Run(line.TagText)
                    {
                        Foreground = TagBrush(line.Tag)
                    });
                }

                paragraph.Inlines.Add(new Run(line.ContentText));
                box.Document.Blocks.Add(paragraph);
            }
        }

        private static Brush TagBrush(ActivityLogResultTag tag)
        {
            switch (tag)
            {
                case ActivityLogResultTag.Ocr:
                    return Brushes.SlateGray;
                case ActivityLogResultTag.Original:
                    return Brushes.SteelBlue;
                case ActivityLogResultTag.Translation:
                    return Brushes.DarkGreen;
                default:
                    return Brushes.Transparent;
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
    }

    internal sealed class ActivityLogRowView : INotifyPropertyChanged
    {
        private string _time;
        private string _regionPair;
        private string _job;
        private ActivityLogResultProjection _resultProjection;
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

        public ActivityLogResultProjection ResultProjection
        {
            get { return _resultProjection; }
            set { SetField(ref _resultProjection, value, nameof(ResultProjection)); }
        }

        public bool IsRepeat
        {
            get { return _isRepeat; }
            set { SetField(ref _isRepeat, value, nameof(IsRepeat)); }
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
