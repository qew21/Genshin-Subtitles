using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
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
        private int _consumedCount;

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
            _rows.Clear();
            _rowSources.Clear();
            _consumedCount = 0;
            SyncRows();
        }

        private void SyncRows()
        {
            IReadOnlyList<ActivityLogRow> log = _session.ActivityLog;
            for (int i = _consumedCount; i < log.Count; i++)
            {
                ActivityLogRow row = log[i];
                _rows.Add(Project(row));
                _rowSources.Add(row);
            }

            _consumedCount = log.Count;

            // Mutation-only notifies (e.g. voice folded into a prior row) refresh
            // already-projected views without appending.
            if (_consumedCount == _rowSources.Count)
            {
                for (int i = 0; i < _rowSources.Count; i++)
                {
                    ApplyProjection(_rows[i], _rowSources[i]);
                }
            }

            EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
            ActivityLogResultProjection projection = ActivityLogResultComposer.Compose(row, ResolveText);
            view.Result = projection.PlainText;
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
        private string _result;
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
