using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GI_Subtitles.Core.Overlay;
using GI_Subtitles.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreConfig = GI_Subtitles.Core.Config.Config;

namespace GI_Test
{
    /// <summary>
    /// Append path for the activity log window: a newly consumed row is
    /// projected once; already-shown rows whose snapshots did not change
    /// must not get a fresh result-column projection (no ResultLines notify).
    /// </summary>
    [TestClass]
    public class TestActivityLogWindowAppendProjection
    {
        [TestMethod]
        public void Append_DoesNotRaiseResultLinesOnAlreadyShownUnchangedRows()
        {
            RunOnSta(delegate
            {
                EnsureApplication();
                LiveOverlaySession session = CreateSession();
                AppendPipelineRow(session, index: 0, isRepeat: false);
                AppendPipelineRow(session, index: 1, isRepeat: false);

                ActivityLogWindow window = OpenWindow(session);
                try
                {
                    ObservableCollection<ActivityLogRowView> rows = RowsOf(window);
                    Assert.AreEqual(2, rows.Count);

                    IReadOnlyList<ActivityLogResultLine> firstLines = rows[0].ResultLines;
                    int resultLinesChanges = 0;
                    PropertyChangedEventHandler handler = delegate(object sender, PropertyChangedEventArgs e)
                    {
                        if (e.PropertyName == "ResultLines")
                        {
                            resultLinesChanges++;
                        }
                    };
                    rows[0].PropertyChanged += handler;
                    try
                    {
                        AppendPipelineRow(session, index: 2, isRepeat: false);
                        Pump(window.Dispatcher);

                        Assert.AreEqual(0, resultLinesChanges,
                            "Already-shown rows must not re-project ResultLines on append");
                        Assert.AreEqual(3, rows.Count);
                        Assert.AreSame(firstLines, rows[0].ResultLines,
                            "Unchanged row should keep the same ResultLines instance");
                    }
                    finally
                    {
                        rows[0].PropertyChanged -= handler;
                    }
                }
                finally
                {
                    ForceClose(window);
                }
            });
        }

        [TestMethod]
        public void VoiceJobFoldedIntoShownRow_UpdatesJob_WithoutResultLinesNotify()
        {
            RunOnSta(delegate
            {
                EnsureApplication();
                LiveOverlaySession session = CreateSession();
                AppendPipelineRow(session, index: 0, isRepeat: false);

                ActivityLogWindow window = OpenWindow(session);
                try
                {
                    ObservableCollection<ActivityLogRowView> rows = RowsOf(window);
                    Assert.AreEqual(1, rows.Count);
                    string jobBefore = rows[0].Job;
                    IReadOnlyList<ActivityLogResultLine> linesBefore = rows[0].ResultLines;
                    Assert.IsFalse(
                        string.IsNullOrEmpty(jobBefore) || jobBefore.Contains("配音"),
                        "precondition: voice badge not already in the job column");

                    int resultLinesChanges = 0;
                    PropertyChangedEventHandler handler = delegate(object sender, PropertyChangedEventArgs e)
                    {
                        if (e.PropertyName == "ResultLines")
                        {
                            resultLinesChanges++;
                        }
                    };
                    rows[0].PropertyChanged += handler;
                    try
                    {
                        // Session mutates the already-consumed row, then notifies —
                        // the same signal as an append, but Consume returns nothing.
                        FieldInfo pending = typeof(LiveOverlaySession).GetField(
                            "_pendingVoiceLogIndex",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        Assert.IsNotNull(pending);
                        pending.SetValue(session, 0);
                        session.NoteVoicePlaybackStarted();
                        Pump(window.Dispatcher);

                        Assert.AreNotEqual(jobBefore, rows[0].Job,
                            "Voice job folded into the snapshot must refresh the job column");
                        Assert.IsTrue(rows[0].Job.Contains("配音"));
                        Assert.AreEqual(0, resultLinesChanges,
                            "Result snapshot did not change; ResultLines must stay quiet");
                        Assert.AreSame(linesBefore, rows[0].ResultLines);
                    }
                    finally
                    {
                        rows[0].PropertyChanged -= handler;
                    }
                }
                finally
                {
                    ForceClose(window);
                }
            });
        }

        [TestMethod]
        public void LogDenoiseRebuild_ShowsFilteredSetWhenHideRepeatsToggles()
        {
            RunOnSta(delegate
            {
                EnsureApplication();
                bool? previous = CoreConfig.Contains("LogDenoise")
                    ? (bool?)CoreConfig.Get("LogDenoise", true)
                    : null;
                try
                {
                    CoreConfig.Set("LogDenoise", true);
                    LiveOverlaySession session = CreateSession();
                    AppendPipelineRow(session, index: 0, isRepeat: false);
                    AppendPipelineRow(session, index: 1, isRepeat: true);
                    AppendPipelineRow(session, index: 2, isRepeat: false);

                    ActivityLogWindow window = OpenWindow(session);
                    try
                    {
                        ObservableCollection<ActivityLogRowView> rows = RowsOf(window);
                        Assert.AreEqual(2, rows.Count);
                        Assert.IsFalse(rows[0].IsRepeat);
                        Assert.IsFalse(rows[1].IsRepeat);

                        CoreConfig.Set("LogDenoise", false);
                        window.ApplyLogDenoiseSetting();
                        Pump(window.Dispatcher);

                        Assert.AreEqual(3, rows.Count);
                        Assert.IsFalse(rows[0].IsRepeat);
                        Assert.IsTrue(rows[1].IsRepeat);
                        Assert.IsFalse(rows[2].IsRepeat);

                        CoreConfig.Set("LogDenoise", true);
                        window.ApplyLogDenoiseSetting();
                        Pump(window.Dispatcher);

                        Assert.AreEqual(2, rows.Count);
                        Assert.IsFalse(rows[0].IsRepeat);
                        Assert.IsFalse(rows[1].IsRepeat);
                    }
                    finally
                    {
                        ForceClose(window);
                    }
                }
                finally
                {
                    if (previous.HasValue)
                    {
                        CoreConfig.Set("LogDenoise", previous.Value);
                    }
                    else
                    {
                        CoreConfig.Remove("LogDenoise");
                    }
                }
            });
        }

        private static ActivityLogWindow OpenWindow(LiveOverlaySession session)
        {
            var window = new ActivityLogWindow(session)
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                Left = 40,
                Top = 40,
                Width = 640,
                Height = 360
            };
            window.Show();
            Pump(window.Dispatcher);
            window.UpdateLayout();
            Pump(window.Dispatcher);
            return window;
        }

        private static void AppendPipelineRow(LiveOverlaySession session, int index, bool isRepeat)
        {
            AppendMethod().Invoke(
                session,
                new object[]
                {
                    new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc).AddSeconds(index),
                    new[] { OperatorJob.Capture, OperatorJob.Ocr, OperatorJob.Match },
                    ActivityLogScope.Pair,
                    1,
                    true,
                    null,
                    null,
                    "ocr-" + index,
                    "original-" + index,
                    "translation-" + index,
                    false,
                    false,
                    isRepeat
                });
        }

        private static MethodInfo _appendMethod;

        private static MethodInfo AppendMethod()
        {
            if (_appendMethod == null)
            {
                _appendMethod = typeof(LiveOverlaySession).GetMethod(
                    "AppendActivityLogRow",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(_appendMethod, "AppendActivityLogRow not found");
            }

            return _appendMethod;
        }

        private static LiveOverlaySession CreateSession()
        {
            return new LiveOverlaySession(new MemoryOcrIntervalStore());
        }

        private static ObservableCollection<ActivityLogRowView> RowsOf(ActivityLogWindow window)
        {
            FieldInfo field = typeof(ActivityLogWindow).GetField(
                "_rows",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (ObservableCollection<ActivityLogRowView>)field.GetValue(window);
        }

        private static void ForceClose(ActivityLogWindow window)
        {
            FieldInfo field = typeof(ActivityLogWindow).GetField(
                "_forceClose",
                BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(window, true);
            window.Close();
        }

        private static void EnsureApplication()
        {
            if (Application.Current == null)
            {
                var app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/GI-Subtitles;component/Resources/Strings.zh-CN.xaml",
                        UriKind.Absolute)
                });
            }
            else if (Application.Current.Resources.MergedDictionaries.Count == 0)
            {
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/GI-Subtitles;component/Resources/Strings.zh-CN.xaml",
                        UriKind.Absolute)
                });
            }
        }

        private static void Pump(Dispatcher dispatcher)
        {
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }

        // One STA dispatcher for the class: WPF Application/Window affinity
        // cannot hop across the per-test threads that a Join-and-forget STA
        // helper would create.
        private static readonly object StaGate = new object();
        private static Dispatcher _staDispatcher;
        private static Exception _staStartFailure;

        private static void RunOnSta(Action action)
        {
            EnsureStaDispatcher();
            Exception failure = null;
            _staDispatcher.Invoke(delegate
            {
                try
                {
                    EnsureApplication();
                    action();
                }
                catch (Exception e)
                {
                    failure = e;
                }
            });
            if (failure != null)
            {
                throw new AssertFailedException(failure.Message, failure);
            }
        }

        private static void EnsureStaDispatcher()
        {
            lock (StaGate)
            {
                if (_staDispatcher != null)
                {
                    return;
                }

                // Another activity-log window test may already own Application;
                // share its dispatcher so Window affinity stays on one STA.
                if (Application.Current != null)
                {
                    _staDispatcher = Application.Current.Dispatcher;
                    return;
                }

                var ready = new ManualResetEvent(false);
                var thread = new Thread(delegate()
                {
                    try
                    {
                        EnsureApplication();
                        _staDispatcher = Dispatcher.CurrentDispatcher;
                    }
                    catch (Exception e)
                    {
                        _staStartFailure = e;
                    }
                    finally
                    {
                        ready.Set();
                    }

                    if (_staDispatcher != null)
                    {
                        Dispatcher.Run();
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                ready.WaitOne();
                if (_staStartFailure != null)
                {
                    throw new AssertFailedException(_staStartFailure.Message, _staStartFailure);
                }

                Assert.IsNotNull(_staDispatcher, "STA dispatcher failed to start");
            }
        }

        private sealed class MemoryOcrIntervalStore : IOcrIntervalStore
        {
            public int Read(int defaultValue)
            {
                return defaultValue;
            }

            public void Write(int milliseconds)
            {
            }
        }
    }
}
