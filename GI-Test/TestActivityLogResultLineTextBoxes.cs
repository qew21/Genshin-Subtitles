using System;
using System.Collections.Generic;
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
    /// Control/template seam for the result column (ADR 0016): each projected
    /// line is one read-only TextBox (tag text + content) plus a left-edge
    /// category stripe from <see cref="ActivityLogResultTagColors"/>.
    /// </summary>
    [TestClass]
    public class TestActivityLogResultLineTextBoxes
    {
        private const double StripeWidthPx = 3.0;
        private static readonly Thickness StripeInsetMargin = new Thickness(0, 2, 0, 2);

        [TestMethod]
        public void BrushFor_ReturnsTheAdrPaletteAsFrozenBrushes()
        {
            AssertPalette(ActivityLogResultTag.Ocr, "#004488");
            AssertPalette(ActivityLogResultTag.Original, "#DDAA33");
            AssertPalette(ActivityLogResultTag.Translation, "#BB5566");
            Assert.IsNull(ActivityLogResultTagColors.BrushFor(ActivityLogResultTag.None));
        }

        [TestMethod]
        public void ResultCell_RendersOneReadOnlyTextBoxPerLine_WithCategoryStripe()
        {
            RunOnSta(() =>
            {
                LiveOverlaySession session = new LiveOverlaySession(new MemoryOcrIntervalStore());
                AppendPipelineRow(session, 0);
                ActivityLogWindow window = ShowWindow(session);
                try
                {
                    ItemsControl cell = FindResultLinesHost(window.LogList);
                    Assert.IsNotNull(cell, "result cell ItemsControl missing");

                    IList<FrameworkElement> rows = LineHosts(cell);
                    Assert.AreEqual(3, rows.Count, "expected OCR/原文/译文 line hosts");
                    Assert.AreEqual("[OCR]「ocr-hello-0」", LineTextBox(rows[0]).Text);
                    Assert.AreEqual("[原文]「你好世界-0」", LineTextBox(rows[1]).Text);
                    Assert.AreEqual("[译文]「hello world translation line 0」", LineTextBox(rows[2]).Text);
                    foreach (FrameworkElement row in rows)
                    {
                        TextBox box = LineTextBox(row);
                        Assert.IsTrue(box.IsReadOnly);
                        Assert.AreEqual(TextWrapping.Wrap, box.TextWrapping);
                    }

                    AssertStripe(rows[0], ActivityLogResultTag.Ocr);
                    AssertStripe(rows[1], ActivityLogResultTag.Original);
                    AssertStripe(rows[2], ActivityLogResultTag.Translation);
                }
                finally
                {
                    ForceClose(window);
                }
            });
        }

        [TestMethod]
        public void ResultCell_RendersUntaggedLineWithoutStripe()
        {
            RunOnSta(() =>
            {
                LiveOverlaySession session = new LiveOverlaySession(new MemoryOcrIntervalStore());
                AppendDetectionMissRow(session, 0);
                ActivityLogWindow window = ShowWindow(session);
                try
                {
                    ItemsControl cell = FindResultLinesHost(window.LogList);
                    Assert.IsNotNull(cell, "result cell ItemsControl missing");

                    IList<FrameworkElement> rows = LineHosts(cell);
                    Assert.AreEqual(1, rows.Count);
                    Assert.AreEqual("检测 miss", LineTextBox(rows[0]).Text);
                    AssertStripeCollapsed(rows[0]);
                }
                finally
                {
                    ForceClose(window);
                }
            });
        }

        [TestMethod]
        public void ResultCell_MatchMissLine_BorrowsOriginalStripe()
        {
            RunOnSta(() =>
            {
                LiveOverlaySession session = new LiveOverlaySession(new MemoryOcrIntervalStore());
                AppendMatchMissRow(session, 0);
                ActivityLogWindow window = ShowWindow(session);
                try
                {
                    ItemsControl cell = FindResultLinesHost(window.LogList);
                    Assert.IsNotNull(cell, "result cell ItemsControl missing");

                    IList<FrameworkElement> rows = LineHosts(cell);
                    Assert.AreEqual(2, rows.Count);
                    Assert.AreEqual("[OCR]「ocr-miss-0」", LineTextBox(rows[0]).Text);
                    Assert.AreEqual("[原文] 匹配 miss", LineTextBox(rows[1]).Text);
                    AssertStripe(rows[0], ActivityLogResultTag.Ocr);
                    AssertStripe(rows[1], ActivityLogResultTag.Original);
                }
                finally
                {
                    ForceClose(window);
                }
            });
        }

        [TestMethod]
        public void ResultCell_RebuildsTextAndStripeWhenTheResultLinesBindingRetargets()
        {
            RunOnSta(() =>
            {
                LiveOverlaySession session = new LiveOverlaySession(new MemoryOcrIntervalStore());
                AppendPipelineRow(session, 0);
                ActivityLogWindow window = ShowWindow(session);
                try
                {
                    ItemsControl cell = FindResultLinesHost(window.LogList);
                    Assert.IsNotNull(cell, "result cell ItemsControl missing");
                    Assert.AreEqual(3, LineHosts(cell).Count);
                    AssertStripe(LineHosts(cell)[0], ActivityLogResultTag.Ocr);

                    // Container recycling: DataContext swaps, the binding
                    // re-fires, and the line TextBoxes + stripes must come
                    // from the new row.
                    cell.DataContext = new StubRow
                    {
                        ResultLines = ActivityLogResultComposerHarness.Compose(
                            ActivityLogResultComposerHarness.Row(detectionMiss: true)).Lines
                    };
                    Pump(window.Dispatcher);
                    cell.UpdateLayout();
                    Pump(window.Dispatcher);

                    IList<FrameworkElement> rows = LineHosts(cell);
                    Assert.AreEqual(1, rows.Count);
                    Assert.AreEqual("检测 miss", LineTextBox(rows[0]).Text);
                    AssertStripeCollapsed(rows[0]);

                    cell.DataContext = new StubRow
                    {
                        ResultLines = ActivityLogResultComposerHarness.Compose(
                            ActivityLogResultComposerHarness.Row(
                                ocrText: "retarget-ocr",
                                original: "retarget-orig",
                                translation: "retarget-trans")).Lines
                    };
                    Pump(window.Dispatcher);
                    cell.UpdateLayout();
                    Pump(window.Dispatcher);

                    rows = LineHosts(cell);
                    Assert.AreEqual(3, rows.Count);
                    Assert.AreEqual("[OCR]「retarget-ocr」", LineTextBox(rows[0]).Text);
                    AssertStripe(rows[0], ActivityLogResultTag.Ocr);
                    AssertStripe(rows[1], ActivityLogResultTag.Original);
                    AssertStripe(rows[2], ActivityLogResultTag.Translation);
                }
                finally
                {
                    ForceClose(window);
                }
            });
        }

        private static ActivityLogWindow ShowWindow(LiveOverlaySession session)
        {
            var window = new ActivityLogWindow(session)
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                Left = 40,
                Top = 40,
                Width = 720,
                Height = 400
            };
            window.Show();
            Pump(window.Dispatcher);
            window.UpdateLayout();
            Pump(window.Dispatcher);
            return window;
        }

        private static ItemsControl FindResultLinesHost(ListView list)
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                var container = list.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (container == null)
                {
                    continue;
                }

                ItemsControl host = FindResultLinesHostIn(container);
                if (host != null)
                {
                    return host;
                }
            }

            return null;
        }

        private static ItemsControl FindResultLinesHostIn(DependencyObject root)
        {
            if (root is ItemsControl items
                && items != root as ListView
                && !(items is ListView)
                && items.ItemsSource != null)
            {
                // Result cell host is the only nested ItemsControl bound to lines.
                foreach (object item in items.Items)
                {
                    if (item is ActivityLogResultLine)
                    {
                        return items;
                    }
                }
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                ItemsControl child = FindResultLinesHostIn(VisualTreeHelper.GetChild(root, i));
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static IList<FrameworkElement> LineHosts(ItemsControl cell)
        {
            var rows = new List<FrameworkElement>();
            for (int i = 0; i < cell.Items.Count; i++)
            {
                var container = cell.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                Assert.IsNotNull(container, "missing container for line " + i);
                rows.Add(container);
            }

            return rows;
        }

        private static TextBox LineTextBox(FrameworkElement lineHost)
        {
            TextBox box = FindDescendant<TextBox>(lineHost);
            Assert.IsNotNull(box, "line TextBox missing");
            return box;
        }

        private static void AssertStripe(FrameworkElement lineHost, ActivityLogResultTag tag)
        {
            Border stripe = FindStripeSlot(lineHost);
            Assert.IsNotNull(stripe, "stripe slot missing");
            Assert.AreEqual(Visibility.Visible, stripe.Visibility, tag.ToString());
            Assert.AreEqual(StripeWidthPx, stripe.Width, tag.ToString());
            Assert.AreEqual(StripeInsetMargin, stripe.Margin, tag.ToString());
            Assert.AreSame(
                ActivityLogResultTagColors.BrushFor(tag),
                stripe.Background,
                tag.ToString());
        }

        private static void AssertStripeCollapsed(FrameworkElement lineHost)
        {
            Border stripe = FindStripeSlot(lineHost);
            Assert.IsNotNull(stripe, "stripe slot missing");
            Assert.AreEqual(Visibility.Collapsed, stripe.Visibility);
            Assert.AreEqual(0.0, stripe.Width);
        }

        private static Border FindStripeSlot(DependencyObject root)
        {
            if (root is Border border && Equals(border.Tag, "ResultStripeSlot"))
            {
                return border;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                Border child = FindStripeSlot(VisualTreeHelper.GetChild(root, i));
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static T FindDescendant<T>(DependencyObject root) where T : class
        {
            if (root is T match)
            {
                return match;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                T child = FindDescendant<T>(VisualTreeHelper.GetChild(root, i));
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static void AppendPipelineRow(LiveOverlaySession session, int index)
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

        private static void AppendDetectionMissRow(LiveOverlaySession session, int index)
        {
            MethodInfo append = typeof(LiveOverlaySession).GetMethod(
                "AppendActivityLogRow",
                BindingFlags.Instance | BindingFlags.NonPublic);
            append.Invoke(
                session,
                new object[]
                {
                    new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc).AddSeconds(index),
                    new[] { OperatorJob.Capture, OperatorJob.Ocr },
                    ActivityLogScope.Pair,
                    1,
                    true,
                    null,
                    null,
                    null,
                    null,
                    null,
                    true,
                    false,
                    false
                });
        }

        private static void AppendMatchMissRow(LiveOverlaySession session, int index)
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
                    "ocr-miss-" + index,
                    null,
                    null,
                    false,
                    true,
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

        private static void AssertPalette(ActivityLogResultTag tag, string expectedHex)
        {
            Brush brush = ActivityLogResultTagColors.BrushFor(tag);
            var solid = brush as SolidColorBrush;
            Assert.IsNotNull(solid, tag.ToString());
            Assert.IsTrue(solid.IsFrozen, tag.ToString());
            Assert.AreEqual(
                (Color)ColorConverter.ConvertFromString(expectedHex),
                solid.Color,
                tag.ToString());
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

        private sealed class StubRow
        {
            public IReadOnlyList<ActivityLogResultLine> ResultLines { get; set; }
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
