using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GI_Subtitles.Models;
using GI_Subtitles.Services.Translation;
using GI_Subtitles.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestTextSearchWindowCancellation
    {
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        [Timeout(30000)]
        public void ClearDuringLoad_DropsLateResultsErrorsAndHistory(bool removeLanguageMap)
        {
            RunScenario((window, directory, requests) =>
            {
                window.InputTextBox.Text = "冒险家协会";
                requests.Add(window.SearchAsync());
                Assert.IsFalse(window.SearchButton.IsEnabled);
                window.ClearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsTrue(window.SearchButton.IsEnabled);
                Assert.IsTrue(window.InputTextBox.IsEnabled);
                Assert.IsEmpty(window.Results);
                if (removeLanguageMap)
                {
                    File.Delete(Path.Combine(directory, "ReviewGame", "TextMapEN.json"));
                }
                window.InputTextBox.Text = "清空后新输入的内容";
            }, (window, directory) =>
            {
                Assert.AreEqual("清空后新输入的内容", window.InputTextBox.Text);
                Assert.IsEmpty(window.Results);
                Assert.IsEmpty(window.SearchHistory);
                Assert.AreEqual(string.Empty, window.StatusText.Text);
                Assert.IsFalse(File.Exists(Path.Combine(directory, "TextSearchHistory.json")));
            });
        }

        [TestMethod]
        [Timeout(30000)]
        public void ClearThenSearchAgain_OnlyPublishesAndRecordsTheNewQuery()
        {
            RunScenario((window, directory, requests) =>
            {
                window.InputTextBox.Text = "冒险家协会";
                requests.Add(window.SearchAsync());
                window.ClearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.InputTextBox.Text = "全年无休";
                requests.Add(window.SearchAsync());
                Assert.IsFalse(window.SearchButton.IsEnabled);
            }, (window, directory) =>
            {
                Assert.AreEqual("全年无休", window.InputTextBox.Text);
                Assert.HasCount(1, window.Results);
                Assert.AreEqual("全年无休", window.Results[0].Input);
                Assert.IsTrue(window.Results[0].IsMatch);
                Assert.HasCount(1, window.SearchHistory);
                Assert.AreEqual("全年无休", window.SearchHistory[0].Query);
                Assert.IsTrue(window.SearchButton.IsEnabled);
                Assert.IsTrue(window.InputTextBox.IsEnabled);
            });
        }

        private static void RunScenario(
            Action<TextSearchWindow, string, List<Task>> whileLoading,
            Action<TextSearchWindow, string> afterLoading)
        {
            GetDispatcher().Invoke(() =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "GI-Search-Cancel-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(directory, "ReviewGame"));
                TextSearchWindow window = null;
                try
                {
                    File.WriteAllText(Path.Combine(directory, "ReviewGame", "TextMapCHS.json"),
                        "{\"1\":\"冒险家协会全年无休。\"}");
                    File.WriteAllText(Path.Combine(directory, "ReviewGame", "TextMapEN.json"),
                        "{\"1\":\"The Adventurers Guild is open all year round.\"}");
                    window = new TextSearchWindow(new[] { new GameMetadata { Name = "ReviewGame" } },
                        directory, "zh-CN", "ReviewGame", "CHS", "EN");

                    // Hold the real loader's serialization gate to make Clear happen
                    // before completion without timing-dependent sleeps or user data.
                    object gate = typeof(TextSearchLookup).GetField("LoadLock",
                        BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    var requests = new List<Task>();
                    Monitor.Enter(gate);
                    try
                    {
                        whileLoading(window, directory, requests);
                    }
                    finally
                    {
                        Monitor.Exit(gate);
                        WaitForRequests(Task.WhenAll(requests));
                    }
                    afterLoading(window, directory);
                }
                finally
                {
                    window?.Close();
                    Directory.Delete(directory, true);
                }
            });
        }

        private static void WaitForRequests(Task requests)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            var frame = new DispatcherFrame();
            timer.Tick += (sender, args) =>
            {
                if (requests.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false;
            };
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            Assert.IsTrue(requests.IsCompleted, "Search requests did not finish after releasing the loader gate.");
            requests.GetAwaiter().GetResult();
        }

        private static readonly object StaGate = new object();
        private static Dispatcher _dispatcher;

        private static Dispatcher GetDispatcher()
        {
            lock (StaGate)
            {
                if (_dispatcher != null) return _dispatcher;
                if (Application.Current != null) return _dispatcher = Application.Current.Dispatcher;

                Exception failure = null;
                using (var ready = new ManualResetEventSlim())
                {
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                            app.Resources.MergedDictionaries.Add(new ResourceDictionary
                            {
                                Source = new Uri("pack://application:,,,/GI-Subtitles;component/Resources/Strings.zh-CN.xaml")
                            });
                            _dispatcher = app.Dispatcher;
                        }
                        catch (Exception ex) { failure = ex; }
                        finally { ready.Set(); }
                        if (_dispatcher != null) Dispatcher.Run();
                    }) { IsBackground = true };
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    ready.Wait();
                }
                if (failure != null) throw new AssertFailedException("Could not start the WPF test dispatcher.", failure);
                return _dispatcher;
            }
        }
    }
}
