using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GI_Subtitles.Core.Overlay;
using GI_Subtitles.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    /// <summary>
    /// Copy-on-select (ADR 0011): mouse-up with a non-empty in-cell selection
    /// publishes the text without freezing the UI thread on the clipboard write.
    /// </summary>
    [TestClass]
    public class TestActivityLogWindowCopyOnSelect
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void SetClipboardWithRetry_ReturnsImmediately_AndPublishesText()
        {
            RunOnSta(delegate
            {
                string sample = "activity-log-copy-on-select-" + Guid.NewGuid().ToString("N");
                MethodInfo helper = typeof(ActivityLogWindow).GetMethod(
                    "SetClipboardWithRetry",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(helper);

                var clock = Stopwatch.StartNew();
                helper.Invoke(null, new object[] { sample });
                clock.Stop();

                string got = WaitForClipboardText(sample, 2000);
                Assert.IsTrue(
                    clock.Elapsed.TotalMilliseconds < 50.0,
                    "UI blocked on clipboard queue: " + clock.Elapsed.TotalMilliseconds.ToString("0.0") + " ms");
                Assert.AreEqual(sample, got);
            });
        }

        [TestMethod]
        public void SelectRelease_CopiesSelection_WithoutLongUiFreeze()
        {
            RunOnSta(delegate
            {
                LiveOverlaySession session = new LiveOverlaySession(new MemoryOcrIntervalStore());
                AppendRow(session, 0);
                AppendRow(session, 1);
                var window = new ActivityLogWindow(session)
                {
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = 60,
                    Top = 60,
                    Width = 720,
                    Height = 400
                };
                window.Show();
                Pump(window.Dispatcher);
                window.UpdateLayout();
                Pump(window.Dispatcher);

                // ADR 0016: copy-on-select targets the line TextBox that
                // started the selection, not a single cell-wide TextBox.
                TextBox lineBox = FindResultLineTextBox(window.LogList);
                Assert.IsNotNull(lineBox, "result line TextBox missing");
                lineBox.Focus();
                Pump(window.Dispatcher);

                lineBox.Select(0, 0);
                double plainMs = TimeMouseUp(window, lineBox);

                int take = Math.Min(24, lineBox.Text.Length);
                lineBox.Select(0, take);
                string expected = lineBox.SelectedText;
                double selectMs = TimeMouseUp(window, lineBox);
                string got = WaitForClipboardText(expected, 2000);

                ForceClose(window);

                Assert.IsTrue(plainMs < 100.0, "plain click release regressed: " + plainMs.ToString("0.0"));
                Assert.IsTrue(
                    selectMs < 500.0,
                    "select-release froze UI: " + selectMs.ToString("0.0") + " ms");
                Assert.AreEqual(expected, got, "copy-on-select did not publish the selection");
            });
        }

        [TestMethod]
        public void ContextMenuCopy_UsesLineSelection_WhenPlacementTargetIsParentHost()
        {
            RunOnSta(delegate
            {
                LiveOverlaySession session = new LiveOverlaySession(new MemoryOcrIntervalStore());
                AppendRow(session, 0);
                var window = new ActivityLogWindow(session)
                {
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = 60,
                    Top = 60,
                    Width = 720,
                    Height = 400
                };
                window.Show();
                Pump(window.Dispatcher);
                window.UpdateLayout();
                Pump(window.Dispatcher);

                TextBox lineBox = FindResultLineTextBox(window.LogList);
                Assert.IsNotNull(lineBox, "result line TextBox missing");
                ItemsControl host = FindAncestorItemsControl(lineBox);
                Assert.IsNotNull(host, "result lines ItemsControl missing");

                int take = Math.Min(16, lineBox.Text.Length);
                lineBox.Select(0, take);
                string expected = lineBox.SelectedText;
                Assert.IsFalse(string.IsNullOrEmpty(expected));

                var menu = (ContextMenu)window.FindResource("LogCopyMenu");
                menu.PlacementTarget = host;
                MethodInfo opened = typeof(ActivityLogWindow).GetMethod(
                    "CopyMenu_Opened",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                opened.Invoke(window, new object[] { menu, new RoutedEventArgs() });
                var copyItem = (MenuItem)menu.Items[0];
                Assert.IsTrue(copyItem.IsEnabled, "copy item should enable for in-line selection");

                MethodInfo click = typeof(ActivityLogWindow).GetMethod(
                    "CopyMenuItem_Click",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                click.Invoke(window, new object[] { copyItem, new RoutedEventArgs() });
                string got = WaitForClipboardText(expected, 2000);

                ForceClose(window);
                Assert.AreEqual(expected, got, "context menu did not copy the line selection");
            });
        }

        private static string WaitForClipboardText(string expected, int timeoutMs)
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    string got = Clipboard.GetText();
                    if (got == expected)
                    {
                        return got;
                    }
                }
                catch (COMException)
                {
                }
                catch (ExternalException)
                {
                }

                Thread.Sleep(20);
            }

            try
            {
                return Clipboard.GetText();
            }
            catch
            {
                return null;
            }
        }

        private static double TimeMouseUp(ActivityLogWindow window, TextBox cell)
        {
            MethodInfo handler = typeof(ActivityLogWindow).GetMethod(
                "LogList_PreviewMouseLeftButtonUp",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(handler);
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                Source = cell
            };
            var clock = Stopwatch.StartNew();
            handler.Invoke(window, new object[] { window.LogList, args });
            Pump(window.Dispatcher);
            clock.Stop();
            return clock.Elapsed.TotalMilliseconds;
        }

        private static TextBox FindResultLineTextBox(ListView list)
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                var container = list.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (container == null)
                {
                    continue;
                }

                TextBox line = FindResultLineTextBoxIn(container);
                if (line != null)
                {
                    return line;
                }
            }

            return null;
        }

        private static TextBox FindResultLineTextBoxIn(DependencyObject root)
        {
            if (root is TextBox box
                && box.TextWrapping == TextWrapping.Wrap
                && box.DataContext is ActivityLogResultLine)
            {
                return box;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                TextBox child = FindResultLineTextBoxIn(VisualTreeHelper.GetChild(root, i));
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static ItemsControl FindAncestorItemsControl(DependencyObject element)
        {
            while (element != null)
            {
                element = VisualTreeHelper.GetParent(element);
                if (element is ItemsControl items && !(items is ListView))
                {
                    return items;
                }
            }

            return null;
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
                    "hello world translation line " + index,
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
