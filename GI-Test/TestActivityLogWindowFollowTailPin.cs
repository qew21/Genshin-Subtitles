using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GI_Subtitles.Core.Overlay;
using GI_Subtitles.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    /// <summary>
    /// Window-level follow-tail pin: while armed, a new visible row leaves the
    /// viewport on the newest content; while the operator has scrolled up,
    /// updates do not move the viewport and 有新记录 appears.
    /// </summary>
    [TestClass]
    public class TestActivityLogWindowFollowTailPin
    {
        [TestMethod]
        public void Following_AppendVisibleRow_LeavesViewportOnNewest()
        {
            RunOnSta(delegate
            {
                EnsureApplication();
                LiveOverlaySession session = CreateSession();
                for (int i = 0; i < 40; i++)
                {
                    AppendPipelineRow(session, i, isRepeat: false);
                }

                ActivityLogWindow window = OpenWindow(session);
                try
                {
                    ScrollViewer viewer = RequireScrollViewer(window);
                    Assert.IsTrue(
                        ActivityLogFollowTail.IsAtBottom(viewer.VerticalOffset, viewer.ScrollableHeight),
                        "precondition: opened window follows at the bottom");

                    AppendPipelineRow(session, 40, isRepeat: false);
                    Pump(window.Dispatcher);
                    window.UpdateLayout();
                    Pump(window.Dispatcher);

                    ObservableCollection<ActivityLogRowView> rows = RowsOf(window);
                    Assert.AreEqual(41, rows.Count);
                    Assert.IsTrue(
                        ActivityLogFollowTail.IsAtBottom(viewer.VerticalOffset, viewer.ScrollableHeight),
                        "Armed follow-tail must leave the viewport on the newest visible row");
                    Assert.AreEqual(Visibility.Collapsed, NewRecordsButtonOf(window).Visibility);
                    Assert.IsTrue(FollowTailOf(window).IsFollowing);
                }
                finally
                {
                    ForceClose(window);
                }
            });
        }

        // Regression for the stay-on-tail ScrollChanged loop: opening with many
        // rows while follow is armed used to fight virtualization extent jitter
        // forever and freeze the window.
        [TestMethod]
        [Timeout(20000)]
        public void Following_OpenWithManyRows_ThenAppend_DoesNotHang()
        {
            RunOnSta(delegate
            {
                EnsureApplication();
                LiveOverlaySession session = CreateSession();
                const int n = 800;
                for (int i = 0; i < n; i++)
                {
                    AppendPipelineRow(session, i, isRepeat: false);
                }

                ActivityLogWindow window = OpenWindow(session);
                try
                {
                    ScrollViewer viewer = RequireScrollViewer(window);
                    Assert.AreEqual(n, RowsOf(window).Count);
                    Assert.IsTrue(
                        ActivityLogFollowTail.IsAtBottom(viewer.VerticalOffset, viewer.ScrollableHeight),
                        "Opened following window must settle on the newest row");

                    AppendPipelineRow(session, n, isRepeat: false);
                    Pump(window.Dispatcher);
                    window.UpdateLayout();
                    Pump(window.Dispatcher);

                    Assert.AreEqual(n + 1, RowsOf(window).Count);
                    Assert.IsTrue(
                        ActivityLogFollowTail.IsAtBottom(viewer.VerticalOffset, viewer.ScrollableHeight),
                        "Append while following must still pin without hanging");
                }
                finally
                {
                    ForceClose(window);
                }
            });
        }

        [TestMethod]
        public void ScrolledUp_AppendVisibleRow_DoesNotMoveViewport_ShowsNewRecords()
        {
            RunOnSta(delegate
            {
                EnsureApplication();
                LiveOverlaySession session = CreateSession();
                for (int i = 0; i < 40; i++)
                {
                    AppendPipelineRow(session, i, isRepeat: false);
                }

                ActivityLogWindow window = OpenWindow(session);
                try
                {
                    ScrollViewer viewer = RequireScrollViewer(window);
                    Assert.IsTrue(viewer.ScrollableHeight > 0, "precondition: content overflows viewport");

                    viewer.ScrollToVerticalOffset(0);
                    window.UpdateLayout();
                    Pump(window.Dispatcher);
                    FollowTailOf(window).OperatorViewportAtBottom(false);

                    double offsetBefore = viewer.VerticalOffset;
                    AppendPipelineRow(session, 40, isRepeat: false);
                    Pump(window.Dispatcher);
                    window.UpdateLayout();
                    Pump(window.Dispatcher);

                    Assert.AreEqual(offsetBefore, viewer.VerticalOffset, 0.5,
                        "Updates must not move the viewport while the operator has scrolled up");
                    Assert.AreEqual(Visibility.Visible, NewRecordsButtonOf(window).Visibility);
                    Assert.IsFalse(FollowTailOf(window).IsFollowing);
                }
                finally
                {
                    ForceClose(window);
                }
            });
        }

        [TestMethod]
        public void ScrolledUp_JumpToNewest_PinsAndKeepsFollowingOnAppend()
        {
            RunOnSta(delegate
            {
                EnsureApplication();
                LiveOverlaySession session = CreateSession();
                for (int i = 0; i < 40; i++)
                {
                    AppendPipelineRow(session, i, isRepeat: false);
                }

                ActivityLogWindow window = OpenWindow(session);
                try
                {
                    ScrollViewer viewer = RequireScrollViewer(window);
                    viewer.ScrollToVerticalOffset(0);
                    window.UpdateLayout();
                    Pump(window.Dispatcher);
                    FollowTailOf(window).OperatorViewportAtBottom(false);
                    FollowTailOf(window).VisibleContentAdded();
                    InvokePrivate(window, "ApplyFollowTail");
                    Assert.AreEqual(Visibility.Visible, NewRecordsButtonOf(window).Visibility);

                    InvokePrivate(window, "NewRecordsButton_Click", NewRecordsButtonOf(window), new RoutedEventArgs());
                    Pump(window.Dispatcher);
                    window.UpdateLayout();
                    Pump(window.Dispatcher);

                    Assert.IsTrue(
                        ActivityLogFollowTail.IsAtBottom(viewer.VerticalOffset, viewer.ScrollableHeight),
                        "有新记录 must jump to the newest content");
                    Assert.AreEqual(Visibility.Collapsed, NewRecordsButtonOf(window).Visibility);
                    Assert.IsTrue(FollowTailOf(window).IsFollowing);

                    AppendPipelineRow(session, 40, isRepeat: false);
                    Pump(window.Dispatcher);
                    window.UpdateLayout();
                    Pump(window.Dispatcher);

                    Assert.IsTrue(
                        ActivityLogFollowTail.IsAtBottom(viewer.VerticalOffset, viewer.ScrollableHeight),
                        "After 有新记录, follow stays armed and pins to newer visible rows");
                    Assert.AreEqual(Visibility.Collapsed, NewRecordsButtonOf(window).Visibility);
                }
                finally
                {
                    ForceClose(window);
                }
            });
        }

        private static void InvokePrivate(ActivityLogWindow window, string methodName, params object[] args)
        {
            MethodInfo method = typeof(ActivityLogWindow).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, methodName + " not found");
            method.Invoke(window, args.Length == 0 ? null : args);
        }

        private static ScrollViewer RequireScrollViewer(ActivityLogWindow window)
        {
            ScrollViewer viewer = FindScrollViewer(window.LogList);
            Assert.IsNotNull(viewer, "ListView ScrollViewer not found");
            return viewer;
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

        private static ActivityLogFollowTail FollowTailOf(ActivityLogWindow window)
        {
            FieldInfo field = typeof(ActivityLogWindow).GetField(
                "_followTail",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (ActivityLogFollowTail)field.GetValue(window);
        }

        private static FrameworkElement NewRecordsButtonOf(ActivityLogWindow window)
        {
            FieldInfo field = typeof(ActivityLogWindow).GetField(
                "NewRecordsButton",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                return (FrameworkElement)field.GetValue(window);
            }

            return (FrameworkElement)window.FindName("NewRecordsButton");
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

                // Another activity-log window test class may already own
                // Application.Current on its STA thread; hop there instead of
                // starting a second STA that cannot touch those windows.
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
