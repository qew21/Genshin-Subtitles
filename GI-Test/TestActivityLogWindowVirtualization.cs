using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using GI_Subtitles.Core.Overlay;
using GI_Subtitles.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    /// <summary>
    /// Activity-log list virtualization stays covering at large N, and the
    /// recycled row template does not pay an inner ScrollViewer per cell
    /// TextBox (the load-bearing visual-tree cost under pixel virtualization).
    /// </summary>
    [TestClass]
    public class TestActivityLogWindowVirtualization
    {
        private const int LargeN = 1500;

        [TestMethod]
        [Timeout(30000)]
        public void LargeLog_RealizesFewRowContainers_AndCellTextBoxesHaveNoInnerScrollViewer()
        {
            RunOnSta(delegate
            {
                LiveOverlaySession session = new LiveOverlaySession(new MemoryOcrIntervalStore());
                for (int i = 0; i < LargeN; i++)
                {
                    AppendRow(session, i);
                }

                var window = new ActivityLogWindow(session)
                {
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = 40,
                    Top = 40,
                    Width = 720,
                    Height = 480
                };
                window.Show();
                Pump(window.Dispatcher);
                window.UpdateLayout();
                Pump(window.Dispatcher);

                Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(window.LogList));
                Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(window.LogList));

                int containersAtRest = CountListViewItems(window.LogList);
                Assert.IsTrue(
                    containersAtRest > 0 && containersAtRest < 40,
                    "virtualization should realize a small number of row containers at rest; got "
                    + containersAtRest + " for " + LargeN + " rows");

                AssertNoInnerCellScrollViewers(window.LogList);

                ScrollViewer viewer = FindScrollViewer(window.LogList);
                Assert.IsNotNull(viewer, "list ScrollViewer missing");
                int maxDuringJumps = containersAtRest;
                double[] offsets =
                {
                    viewer.ScrollableHeight * 0.25,
                    viewer.ScrollableHeight * 0.5,
                    viewer.ScrollableHeight * 0.75,
                    viewer.ScrollableHeight
                };
                foreach (double offset in offsets)
                {
                    viewer.ScrollToVerticalOffset(offset);
                    window.LogList.UpdateLayout();
                    Pump(window.Dispatcher);
                    int n = CountListViewItems(window.LogList);
                    if (n > maxDuringJumps)
                    {
                        maxDuringJumps = n;
                    }
                }

                Assert.IsTrue(
                    maxDuringJumps < 40,
                    "virtualization should stay covering during thumb-like jumps; max containers "
                    + maxDuringJumps + " for " + LargeN + " rows");
                AssertNoInnerCellScrollViewers(window.LogList);

                ForceClose(window);
            });
        }

        private static void AssertNoInnerCellScrollViewers(ListView list)
        {
            int cellScrollViewers = 0;
            int cellTextBoxes = 0;
            for (int i = 0; i < list.Items.Count; i++)
            {
                var container = list.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (container == null)
                {
                    continue;
                }

                CountTextBoxesAndInnerScrollViewers(container, ref cellTextBoxes, ref cellScrollViewers);
            }

            Assert.IsTrue(cellTextBoxes >= 4, "expected realized cell TextBoxes; got " + cellTextBoxes);
            Assert.AreEqual(
                0,
                cellScrollViewers,
                "cell TextBoxes must not host an inner ScrollViewer (template should use Decorator PART_ContentHost); found "
                + cellScrollViewers + " under " + cellTextBoxes + " TextBoxes");
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
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

        private static int CountListViewItems(DependencyObject root)
        {
            int count = 0;
            CountListViewItems(root, ref count);
            return count;
        }

        private static void CountListViewItems(DependencyObject root, ref int count)
        {
            if (root is ListViewItem)
            {
                count++;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                CountListViewItems(VisualTreeHelper.GetChild(root, i), ref count);
            }
        }

        private static void CountTextBoxesAndInnerScrollViewers(
            DependencyObject root,
            ref int textBoxes,
            ref int scrollViewersInsideTextBoxes)
        {
            if (root is TextBox box)
            {
                textBoxes++;
                scrollViewersInsideTextBoxes += CountType(box, typeof(ScrollViewer));
                return;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                CountTextBoxesAndInnerScrollViewers(
                    VisualTreeHelper.GetChild(root, i),
                    ref textBoxes,
                    ref scrollViewersInsideTextBoxes);
            }
        }

        private static int CountType(DependencyObject root, Type type)
        {
            int count = 0;
            CountType(root, type, ref count);
            return count;
        }

        private static void CountType(DependencyObject root, Type type, ref int count)
        {
            if (type.IsInstanceOfType(root))
            {
                count++;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                CountType(VisualTreeHelper.GetChild(root, i), type, ref count);
            }
        }

        private static void AppendRow(LiveOverlaySession session, int index)
        {
            MethodInfo append = typeof(LiveOverlaySession).GetMethod(
                "AppendActivityLogRow",
                BindingFlags.Instance | BindingFlags.NonPublic);
            append.Invoke(
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
                    "ocr-hello-" + index,
                    "你好世界-" + index,
                    "hello world translation line that wraps for a realistic three-line cell " + index,
                    false,
                    false,
                    false
                });
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
