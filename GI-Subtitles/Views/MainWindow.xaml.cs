using Emgu.CV.Dnn;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using PaddleOCRSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Timers;
using System.Web.UI.WebControls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static System.Net.Mime.MediaTypeNames;
using Path = System.IO.Path;
using System.Media;
using static log4net.Appender.RollingFileAppender;
using System.Runtime.Remoting.Contexts;
using System.Reflection;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;
using NAudio.Wave;
using SoundTouch.Net.NAudioSupport;
using System.Net;
using Microsoft.Win32;
using System.Diagnostics;
using System.Web;
using System.Runtime.InteropServices.ComTypes;
using Newtonsoft.Json;
using System.Security.Policy;
using System.ServiceModel.PeerResolvers;
using System.Net.Http;
using GI_Subtitles.Core.Cache;
using GI_Subtitles.Core.Config;
using GI_Subtitles.Core.UI;
using GI_Subtitles.Models;
using GI_Subtitles.Services.OCR;
using GI_Subtitles.Services.Translation;
using GI_Subtitles.Services.Update;
using GI_Subtitles.Common;
using GI_Subtitles.Core.Screen;
using static GI_Subtitles.Core.Config.Config;
using System.Windows.Threading;

[assembly: log4net.Config.XmlConfigurator(Watch = true)]
namespace GI_Subtitles.Views
{
    public static class Logger
    {
        public static log4net.ILog Log = log4net.LogManager.GetLogger("LogFileAppender");
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        private static int OCR_TIMER = 0;
        private static int UI_TIMER = 0;
        private bool _isOcrRunning = false;
        private readonly SubtitleEpochTracker _subtitleEpochTracker = new SubtitleEpochTracker();
        private readonly DialogueUiStateMachine _dialogueUiStateMachine = new DialogueUiStateMachine();
        private DateTime _lastDialogueUiProbeTime = DateTime.MinValue;
        private static readonly TimeSpan DialogueUiProbeInterval = TimeSpan.FromMilliseconds(250);
        string ocrText = "";
        private NotifyIcon notifyIcon;
        string lastHeader = null;
        string lastContent = null;
        // Use an LRU cache to limit memory usage to 100 entries
        readonly LRUCache<string, string> resDict = new LRUCache<string, string>(100);
        public System.Windows.Threading.DispatcherTimer OCRTimer = new System.Windows.Threading.DispatcherTimer();
        public System.Windows.Threading.DispatcherTimer UITimer = new System.Windows.Threading.DispatcherTimer();
        readonly bool debug = Config.Get<bool>("Debug", false);
        readonly string server = Config.Get<string>("Server", "https://mp3.2langs.com/download");
        readonly string token = Config.Get<string>("Token", "ENGI");
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int y, int Width, int Height, int flags);
        [DllImport("User32.dll")]
        private static extern int GetDpiForSystem();
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID_1 = 9000; // Custom hotkey ID
        private const int HOTKEY_ID_2 = 9001; // Custom hotkey ID
        private const int HOTKEY_ID_3 = 9002; // Custom hotkey ID
        private const int HOTKEY_ID_4 = 9003;
        private const int HOTKEY_ID_REFRESH = 9004;
        private const int HOTKEY_ID_PLAYBACK_SPEED = 9005;
        private const uint MOD_CTRL = 0x0002; // Ctrl key
        private const uint MOD_SHIFT = 0x0004; // Shift key
        private const uint VK_S = 0x53; // Virtual key code for S
        private const uint VK_R = 0x52; // Virtual key code for R
        private const uint VK_H = 0x48; // Virtual key code for H
        private const uint VK_D = 0x44;
        private double Scale = GetDpiForSystem() / 96f;
        List<string> AudioList = new List<string>();
        string InputLanguage = Config.Get<string>("Input");
        string OutputLanguage = Config.Get<string>("Output");
        string Game = Config.Get<string>("Game");
        string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
        INotifyIcon notify;
        SettingsWindow data;
        SoundPlayer player = new SoundPlayer();
        private System.Drawing.Rectangle screenBounds = Screen.PrimaryScreen.Bounds;
        bool ShowText = true;
        bool ChooseRegion = false;
        private IWavePlayer waveOut;
        private MediaFoundationReader mediaReader;
        private SoundTouchWaveProvider soundTouchProvider;
        private string tempFilePath;
        private readonly Queue<string> _audioPlaybackQueue = new Queue<string>();
        private readonly object _audioPlaybackQueueLock = new object();
        private string _pendingDialogueOptionUrl;
        private bool _audioPlaybackQueueActive;
        private int _audioPlaybackGeneration;
        private EventHandler<StoppedEventArgs> _playbackStoppedHandler;
        private static readonly double[] VoicePlaybackSpeeds = { 1.0, 1.25, 1.5, 2.0 };
        private double _voicePlaybackSpeed = NormalizePlaybackSpeed(Config.Get<double>("VoicePlaybackSpeed", 1.0));
        private const int AudioTempCleanupThreshold = 60;
        private const int AudioTempFilesToKeep = 10;
        private int failedCount = 0;
        private bool usingRegion2 = false;
        private bool _isUserMovingWindow = false;
        private bool _forceVoiceReplayRequested = false;
        private bool _forceRefreshPending = false;
        private readonly DispatcherTimer _forceRefreshDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        private DateTime _lastDialogueOptionScanTime = DateTime.MinValue;
        private string _lastDialogueOptionHash;
        private List<DialogueOptionCandidate> _lastDialogueOptions = new List<DialogueOptionCandidate>();
        private int _dialogueOptionMissCount;
        private static readonly TimeSpan DialogueOptionScanInterval = TimeSpan.FromMilliseconds(400);
        private readonly DispatcherTimer _dialogueChoiceDisplayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        private ReleaseManifest availableUpdate;


        public MainWindow()
        {
            Logger.Log.Debug("Start App");
            Task.Run(() => CleanupOldAudioTempFiles());
            InitializeComponent();
            _dialogueChoiceDisplayTimer.Tick += (sender, args) =>
            {
                _dialogueChoiceDisplayTimer.Stop();
                ClearDialogueChoiceHeader();
                UpdateHeaderPosition();
            };
            _forceRefreshDebounceTimer.Tick += (sender, args) =>
            {
                _forceRefreshDebounceTimer.Stop();
                ForceRefreshCurrentSubtitle();
            };
            UpdatePlaybackSpeedIndicator();
            // Start with the main window fully transparent to avoid showing incomplete UI during heavy startup work.
            // Using Opacity instead of Visibility to ensure Loaded is still raised and initialization runs as usual.
            this.Opacity = 0;
            Loaded += MainWindow_Loaded;
            DispatcherTimer _hideButtonTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2),
                IsEnabled = false
            };
            _hideButtonTimer.Tick += (s, e) =>
            {
                DragButton.Visibility = Visibility.Hidden;
                _hideButtonTimer.Stop(); // 执行后停止定时器
            };
            this.MouseEnter += (s, e) => { DragButton.Visibility = Visibility.Visible; _hideButtonTimer.Stop(); };
            // 鼠标移出窗口 → 隐藏拖动按钮
            this.MouseLeave += (s, e) =>
            {
                _hideButtonTimer.Start();
            };
        }


        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Get the window handle
            IntPtr handle = new WindowInteropHelper(this).Handle;
            // Listen to window messages
            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(WndProc);

            notify = new INotifyIcon();
            notifyIcon = notify.InitializeNotifyIcon(Scale);
            data = new SettingsWindow(version, notify, Scale);
            data.InitializeKey(handle);
            notify.SetData(data);
            _ = CheckForUpdateAsync();
            if (!data.FileExists())
            {
                if (Game == "Genshin")
                {
                    if (data.HasMissingRequiredMediumData())
                    {
                        data.IsDataIncomplete = true;
                    }
                }

                if (!data.IsVisible)
                {
                    data.ShowDialog();
                }
            }
            else
            {
                Task.Run(async () => await data.Load());
                Task.Run(async () =>
                {
                    try
                    {
                        var modify = await data.GetRepositoryModificationDate(data.repoUrl, Game);
                        DateTime inputDate = data.GetLocalFileDates(InputLanguage, OutputLanguage, Game);

                        if (DateTime.TryParse(modify, out DateTime repoDate))
                        {
                            if (repoDate > inputDate)
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    notifyIcon.ShowBalloonTip(3000, "Language pack update notification", $"Repository update time: {repoDate}, local modification time: {inputDate}", ToolTipIcon.Info);
                                    string originalTitle = data.Title;
                                    data.Title = $"[Language pack update]{originalTitle}";
                                    if (!data.IsVisible)
                                    {
                                        data.ShowDialog();
                                    }
                                    data.Title = originalTitle;
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Error(ex);
                    }
                }
                );
            }
            if (notify.Region[1] == "0")
            {
                data.Show();
            }


            data.LoadEngine();

            OCRTimer.Interval = new TimeSpan(0, 0, 0, 0, 100);
            OCRTimer.Tick += GetOCR;    // Delegate: method to execute


            UITimer.Interval = new TimeSpan(0, 0, 0, 0, 500);
            UITimer.Tick += UpdateText;    // Delegate: method to execute

            SetWindowPos(new WindowInteropHelper(this).Handle, -1, 0, 0, 0, 0, 1 | 2);
            this.Width = screenBounds.Width;
            this.Top = screenBounds.Bottom / Scale - this.Height;
            this.Left = screenBounds.Left / Scale;
            this.LocationChanged += MainWindow_LocationChanged;

            // Show the main window only after initialization is complete, so users don't see a half‑rendered UI.
            this.Opacity = 1;
        }

        public void GetOCR(object sender, EventArgs e)
        {
            if (notify.isContextMenuOpen)
            {
                return;
            }
            if (TryScanDialogueOptions())
            {
                return;
            }
            if (Interlocked.Exchange(ref OCR_TIMER, 1) == 0)
            {
                try
                {
                    Bitmap target;
                    if (notify.Region[1] == "0")
                    {
                        notify.ChooseRegion();
                    }

                    bool isRegion2Valid = notify.Region2 != null && notify.Region2.Length == 4 &&
                                         int.TryParse(notify.Region2[2], out int region2Width) && region2Width > 0 &&
                                         int.TryParse(notify.Region2[3], out int region2Height) && region2Height > 0;

                    if (failedCount > 4 && isRegion2Valid)
                    {
                        if (usingRegion2)
                        {
                            target = CaptureRegion(notify.Region);
                        }
                        else
                        {
                            target = CaptureRegion(notify.Region2);
                        }
                        failedCount = 0;
                        usingRegion2 = !usingRegion2;
                    }
                    else
                    {
                        if (usingRegion2 && isRegion2Valid)
                        {
                            target = CaptureRegion(notify.Region2);
                        }
                        else
                        {
                            target = CaptureRegion(notify.Region);
                        }
                    }

                    bool passedToOcr = false;
                    Mat frameMat = null;
                    try
                    {
                        frameMat = target.ToMat();
                        UpdateDialogueUiSoftState();
                        using (SubtitleVisualAnalysis analysis = SubtitleVisualAnalyzer.Analyze(frameMat))
                        {
                            SubtitleFrameBatch batch = _subtitleEpochTracker.Process(
                                frameMat,
                                analysis,
                                _dialogueUiStateMachine.State);
                            if (batch != null)
                            {
                                if (_isOcrRunning)
                                {
                                    _subtitleEpochTracker.Complete(batch.Generation, accepted: false);
                                    batch.Dispose();
                                }
                                else
                                {
                                    Logger.Log.Debug(
                                        $"Subtitle generation {batch.Generation} stabilized; start 3-frame OCR review");
                                    SetWindowPos(new WindowInteropHelper(this).Handle, -1, 0, 0, 0, 0, 1 | 2);
                                    _ = TriggerOcrBatchAsync(batch, target);
                                    passedToOcr = true;
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (!passedToOcr)
                        {
                            target?.Dispose();
                        }

                        frameMat?.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log.Error(ex);
                }
                Interlocked.Exchange(ref OCR_TIMER, 0);
            }
        }

        public void UpdateWindowPosition()
        {
            // Base vertical position near the OCR region; precise Top/Height will be adjusted later
            double baseTop = Convert.ToInt16(notify.Region[1]) / Scale + Config.GetPad();

            foreach (var screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.Contains(
                        new System.Drawing.Point(
                            Convert.ToInt16(notify.Region[0]),
                            Convert.ToInt16(notify.Region[1]))))
                {
                    double scale = GetScaleForScreen(screen);
                    double left = screen.Bounds.Left / scale;

                    // Width based on OCR region width with extra padding
                    double width = Convert.ToInt16(notify.Region[2]) / scale + 200;

                    this.Left = left + (screen.Bounds.Width / scale - width) / 2 + Config.GetPadHorizontal();
                    this.Width = Math.Min(width, screen.Bounds.Width / scale);
                    this.Top = baseTop;
                }
            }
            // Height is now content-driven; do not hard-code here
        }

        /// <summary>
        /// Adjust window Height and Top based on actual subtitle content size.
        /// Keeps window within screen bounds.
        /// </summary>
        private void UpdateWindowHeightAndTop()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // 1. Measure content height based only on subtitle text
                    SubtitleText.UpdateLayout();
                    double contentHeight = SubtitleText.ActualHeight;

                    if (contentHeight <= 0)
                    {
                        // Fallback estimation when layout is not ready
                        int fontSize = Config.Get<int>("Size");
                        contentHeight = fontSize;
                    }

                    // 2. Desired window height with margin, clamped to a percentage of screen height
                    double margin = 40;
                    double desiredHeight = contentHeight + margin;

                    Screen targetScreen = null;
                    foreach (var screen in Screen.AllScreens)
                    {
                        if (screen.WorkingArea.Contains(
                                new System.Drawing.Point(
                                    Convert.ToInt16(notify.Region[0]),
                                    Convert.ToInt16(notify.Region[1]))))
                        {
                            targetScreen = screen;
                            break;
                        }
                    }
                    if (targetScreen == null)
                    {
                        targetScreen = Screen.PrimaryScreen;
                    }

                    double screenScale = GetScaleForScreen(targetScreen);
                    double screenHeight = targetScreen.Bounds.Height / screenScale;
                    double screenTop = targetScreen.Bounds.Top / screenScale;
                    double screenBottom = targetScreen.Bounds.Bottom / screenScale;

                    // Cap window height to screen so content never exceeds screen range (fixes large font overflow)
                    double maxWindowHeight = screenBottom - screenTop;
                    desiredHeight = Math.Min(desiredHeight, maxWindowHeight);

                    // Keep the window vertically stable: only clamp Top to keep inside the screen
                    // instead of recomputing it from the OCR region each time (which caused drift).
                    double newTop = this.Top;
                    if (newTop < screenTop)
                    {
                        newTop = screenTop;
                    }
                    if (newTop + desiredHeight > screenBottom)
                    {
                        newTop = screenBottom - desiredHeight;
                    }

                    this.Top = newTop;
                    this.Height = desiredHeight + HeaderPanel.ActualHeight;
                    SubtitleText.MaxHeight = desiredHeight;
                }
                catch (Exception ex)
                {
                    Logger.Log.Error($"Error updating window height/top: {ex}");
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public void UpdateText(object sender, EventArgs e)
        {
            if (Interlocked.Exchange(ref UI_TIMER, 1) == 0)
            {
                Logger.Log.Debug("Start UI");
                try
                {
                    string res = "";
                    string key = "";
                    string header = "";
                    string content = "";

                    if (ocrText.Length > 1)
                    {
                        if (resDict.TryGetValue(ocrText, out string cachedRes))
                        {
                            res = cachedRes;
                            key = resDict[res];
                            string[] parts = res.Split(new[] { "\n\n" }, StringSplitOptions.None);
                            if (parts.Length >= 2)
                            {
                                header = parts[0];
                                content = parts[1];
                            }
                            else
                            {
                                content = res;
                            }
                        }
                        else
                        {
                            // Use the new separation method
                            var matchResult = data.Matcher.FindMatchWithHeaderSeparated(ocrText, out key);
                            header = matchResult.Header ?? "";
                            content = matchResult.Content ?? "";
                            res = string.IsNullOrEmpty(header) ? content : (header + "\n\n" + content);

                            Logger.Log.Debug($"Convert ocrResult for {ocrText}: header={header}, content={content}, key={key}");

                            // Cache still uses the concatenated result for compatibility
                            if (!resDict.ContainsKey(ocrText))
                            {
                                resDict[ocrText] = res;
                                resDict[res] = key;
                            }
                        }
                    }

                    // Check whether the content has changed (mainly check content, which is the main text)
                    bool forceVoiceReplay = _forceVoiceReplayRequested;
                    bool contentChanged = forceVoiceReplay || content != lastContent;
                    bool headerChanged = header != lastHeader;

                    if (contentChanged || headerChanged)
                    {
                        ClearDialogueChoiceHeader();

                        // Set header and content separately
                        if (headerChanged)
                        {
                            lastHeader = header;
                            if (!string.IsNullOrEmpty(header))
                            {
                                HeaderText.Text = header;
                                HeaderText.Visibility = Visibility.Visible;
                                // Delay updating header position until content layout is completed
                                UpdateHeaderPosition();
                            }
                            else
                            {
                                HeaderText.Visibility = Visibility.Collapsed;
                            }
                        }

                        if (contentChanged)
                        {
                            lastContent = content;
                            SubtitleText.Text = content;
                            int fontSize = Config.Get<int>("Size");
                            SubtitleText.FontSize = fontSize;
                            // Delay updating header position until content layout is completed
                            if (HeaderText.Visibility == Visibility.Visible && !string.IsNullOrEmpty(lastHeader))
                            {
                                UpdateHeaderPosition();
                            }
                        }

                        // Play audio (only when content changes, to avoid repeated playback)
                        if (Config.Get<bool>("PlayVoice", false) && contentChanged &&
                            (forceVoiceReplay || !AudioList.Contains(key)) && !string.IsNullOrEmpty(key))
                        {
                            string audioKey = VoiceContentHelper.CalculateMd5Hash(key);
                            PlayMainAudioFromUrl($"{server}?md5={audioKey}&token={token}");
                            if (!AudioList.Contains(key))
                            {
                                AudioList.Add(key);
                            }
                        }

                        // Adapt window height and position when text changes
                        UpdateWindowHeightAndTop();
                    }

                    _forceVoiceReplayRequested = false;
                }
                catch (Exception ex)
                {
                    Logger.Log.Error(ex);
                }
                Interlocked.Exchange(ref UI_TIMER, 0);
            }
        }

        /// <summary>
        /// Update the header position by dynamically calculating the upward offset based on the actual height of the content (supports multiple lines)
        /// </summary>
        private void UpdateHeaderPosition()
        {
            // Wait for layout to complete before calculating to ensure ActualHeight can be obtained
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (HeaderPanel.Visibility != Visibility.Visible)
                        return;

                    // Force layout update to get accurate ActualHeight
                    SubtitleText.UpdateLayout();

                    // Get the actual height of the content (considering multiple lines)
                    double contentHeight = SubtitleText.ActualHeight;
                    if (contentHeight <= 0)
                    {
                        // If ActualHeight has not been calculated, use the font size as an estimate for a single line height
                        int fontSize = Config.Get<int>("Size");
                        contentHeight = fontSize;
                    }

                    // Get the actual height of the header
                    HeaderPanel.UpdateLayout();
                    double headerHeight = HeaderPanel.ActualHeight;
                    if (headerHeight <= 0)
                    {
                        headerHeight = 14; // Header font size is 14
                    }

                    // Calculate upward offset: half of content height + half of header height + spacing
                    var transform = (System.Windows.Media.TranslateTransform)HeaderPanel.RenderTransform;
                    transform.Y = -(contentHeight / 2.0 + headerHeight / 2.0 + 4); // 4 is the spacing
                }
                catch (Exception ex)
                {
                    Logger.Log.Error($"Error updating header position: {ex}");
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }


        /// <summary>
        /// Capture a screen region and fix memory leak issues.
        /// Optimization: directly return a Bitmap that must be disposed by the caller, avoiding memory issues caused by Clone().
        /// </summary>
        public static Bitmap CaptureRegion(string[] region)
        {
            if (region == null || region.Length < 4)
            {
                Logger.Log.Error($"Invalid region array: length={region?.Length ?? 0}");
                throw new ArgumentException("Region array must have at least 4 elements", nameof(region));
            }

            if (!int.TryParse(region[0], out int x) ||
                !int.TryParse(region[1], out int y) ||
                !int.TryParse(region[2], out int width) ||
                !int.TryParse(region[3], out int height))
            {
                Logger.Log.Error($"Invalid region values: x={region[0]}, y={region[1]}, width={region[2]}, height={region[3]}");
                throw new ArgumentException("Region values must be valid integers", nameof(region));
            }

            // Validate that width and height must be greater than 0
            if (width <= 0 || height <= 0)
            {
                Logger.Log.Error($"Invalid region dimensions: width={width}, height={height}");
                throw new ArgumentException($"Region dimensions must be positive: width={width}, height={height}");
            }

            // Validate that the coordinates are within the screen bounds (optional, but helpful for debugging)
            try
            {
                var screenBounds = Screen.GetBounds(new System.Drawing.Point(x, y));
                if (x < screenBounds.Left || y < screenBounds.Top ||
                    x + width > screenBounds.Right || y + height > screenBounds.Bottom)
                {
                    Logger.Log.Warn($"Region may be outside screen bounds: x={x}, y={y}, width={width}, height={height}, screen={screenBounds}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"Could not validate screen bounds: {ex.Message}");
            }

            Bitmap bitmap = null;
            try
            {
                bitmap = new Bitmap(width, height);
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
                }
                return bitmap; // Directly return; the caller is responsible for disposing it
            }
            catch (Exception ex)
            {
                // Ensure resources are released if an error occurs
                bitmap?.Dispose();
                Logger.Log.Error($"Failed to capture region: x={x}, y={y}, width={width}, height={height}, error={ex.Message}");
                throw;
            }
        }

        private void UpdateDialogueUiSoftState()
        {
            if (!string.Equals(Game, "Genshin", StringComparison.OrdinalIgnoreCase) ||
                DateTime.UtcNow - _lastDialogueUiProbeTime < DialogueUiProbeInterval)
            {
                return;
            }

            _lastDialogueUiProbeTime = DateTime.UtcNow;
            try
            {
                string[] subtitleRegion = usingRegion2 && IsValidRegion(notify.Region2)
                    ? notify.Region2
                    : notify.Region;
                System.Drawing.Rectangle gameScreen = screenBounds;
                if (IsValidRegion(subtitleRegion) &&
                    int.TryParse(subtitleRegion[0], out int subtitleX) &&
                    int.TryParse(subtitleRegion[1], out int subtitleY))
                {
                    gameScreen = Screen.GetBounds(new System.Drawing.Point(subtitleX, subtitleY));
                }

                double gameScale = gameScreen.Height / 1080.0;
                int width = Math.Min(gameScreen.Width, Math.Max(120, (int)Math.Round(250 * gameScale)));
                int height = Math.Min(gameScreen.Height, Math.Max(60, (int)Math.Round(115 * gameScale)));
                string[] probeRegion =
                {
                    gameScreen.Left.ToString(),
                    gameScreen.Top.ToString(),
                    width.ToString(),
                    height.ToString()
                };
                using Bitmap probeBitmap = CaptureRegion(probeRegion);
                using Mat probe = probeBitmap.ToMat();
                bool detected = GenshinDialogueUiDetector.TryDetect(
                    probe,
                    gameScale,
                    out double confidence);
                DialogueUiPresence state = _dialogueUiStateMachine.Update(detected);
                if (debug)
                {
                    Logger.Log.Debug($"Dialogue UI soft gate: state={state}, confidence={confidence:F3}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"Dialogue UI soft gate failed: {ex.Message}");
            }
        }

        /// <summary>
        /// OCR three stable frames and publish only the consensus result for the current generation.
        /// </summary>
        private async Task TriggerOcrBatchAsync(
            SubtitleFrameBatch batch,
            Bitmap target,
            bool forceRefresh = false)
        {
            _isOcrRunning = true;
            SubtitleConsensusResult consensus = null;
            bool recognitionCompleted = false;
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var results = new List<OCRResult>(batch.Frames.Count);
                        foreach (Mat frame in batch.Frames)
                        {
                            if (frame == null || frame.Empty())
                            {
                                continue;
                            }

                            OCRResult result = data.engine.DetectTextFromMat(frame);
                            results.Add(result);
                        }

                        consensus = SubtitleConsensusSelector.Select(results, data.Matcher);
                        recognitionCompleted = true;
                        if (debug)
                        {
                            string fileName = DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss_ffffff") + ".png";
                            target.Save(Path.Combine(dataDir, fileName));
                            Logger.Log.Debug(
                                $"OCR generation {batch.Generation}: agreement={consensus.AgreementCount}/{results.Count}, " +
                                $"confidence={consensus.Confidence:F3}, key={consensus.MatchedKey}, text={consensus.Text}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Error(ex);
                    }
                });

                bool usable = recognitionCompleted &&
                              consensus != null &&
                              !string.IsNullOrWhiteSpace(consensus.Text) &&
                              consensus.Text.Length >= 2;
                bool current = _subtitleEpochTracker.Complete(batch.Generation, usable);
                if (!current)
                {
                    Logger.Log.Debug($"Discard stale OCR result for subtitle generation {batch.Generation}");
                    target?.Dispose();
                    return;
                }

                UpdateWindowPosition();
                if (data.IsVisible)
                {
                    data.SetImage(target);
                }
                else
                {
                    target?.Dispose();
                }

                if (usable)
                {
                    ocrText = consensus.Text;
                    Logger.Log.Debug($"Locked OCR generation {batch.Generation}: {consensus.Text}");
                    if (forceRefresh)
                    {
                        _forceVoiceReplayRequested = true;
                        UpdateText(null, EventArgs.Empty);
                    }
                }
                else
                {
                    failedCount++;
                    if (forceRefresh)
                    {
                        Logger.Log.Warn(
                            "Forced OCR refresh produced no usable consensus; keeping the current subtitle without replay.");
                    }
                }
            }
            finally
            {
                _isOcrRunning = false;
                batch?.Dispose();

                if (_forceRefreshPending)
                {
                    _forceRefreshPending = false;
                    _ = Dispatcher.BeginInvoke(new Action(ForceRefreshCurrentSubtitle));
                }
            }
        }

        private async void ForceRefreshCurrentSubtitle()
        {
            if (_isOcrRunning)
            {
                _forceRefreshPending = true;
                return;
            }

            var frames = new List<Mat>(3);
            Bitmap target = null;
            bool batchStarted = false;
            _isOcrRunning = true;
            try
            {
                string[] region = usingRegion2 && IsValidRegion(notify.Region2)
                    ? notify.Region2
                    : notify.Region;

                if (!IsValidRegion(region))
                {
                    notify.ChooseRegion();
                    return;
                }

                for (int i = 0; i < 3; i++)
                {
                    Bitmap sample = CaptureRegion(region);
                    frames.Add(sample.ToMat());
                    target?.Dispose();
                    target = sample;
                    if (i < 2)
                    {
                        await Task.Delay(100);
                    }
                }

                SubtitleFrameBatch batch = _subtitleEpochTracker.CreateManualBatch(frames);
                batchStarted = true;
                await TriggerOcrBatchAsync(batch, target, forceRefresh: true);
                target = null;
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Failed to force refresh current subtitle: {ex}");
            }
            finally
            {
                foreach (Mat frame in frames)
                {
                    frame?.Dispose();
                }
                target?.Dispose();
                if (!batchStarted)
                {
                    _isOcrRunning = false;
                    if (_forceRefreshPending)
                    {
                        _forceRefreshPending = false;
                        _ = Dispatcher.BeginInvoke(new Action(ForceRefreshCurrentSubtitle));
                    }
                }
            }
        }

        private void RequestForceRefreshCurrentSubtitle()
        {
            _forceRefreshDebounceTimer.Stop();
            _forceRefreshDebounceTimer.Start();
        }

        private static bool IsValidRegion(string[] region)
        {
            return region != null && region.Length == 4 &&
                   int.TryParse(region[2], out int width) && width > 0 &&
                   int.TryParse(region[3], out int height) && height > 0;
        }

        private bool TryScanDialogueOptions()
        {
            if (!string.Equals(Game, "Genshin", StringComparison.OrdinalIgnoreCase) ||
                !Config.Get("RecognizeDialogueOptions", false) ||
                DateTime.UtcNow - _lastDialogueOptionScanTime < DialogueOptionScanInterval)
            {
                return false;
            }

            _lastDialogueOptionScanTime = DateTime.UtcNow;
            if (_isOcrRunning || !IsValidRegion(notify.Region))
            {
                return false;
            }

            Bitmap screenBitmap = null;
            Mat screenMat = null;
            try
            {
                var anchor = new System.Drawing.Point(
                    int.Parse(notify.Region[0]),
                    int.Parse(notify.Region[1]));
                System.Drawing.Rectangle bounds = Screen.GetBounds(anchor);
                screenBitmap = CaptureRectangle(bounds);
                screenMat = screenBitmap.ToMat();

                double threshold = Config.Get("DialogueOptionTemplateThreshold", 0.74);
                if (!DialogueOptionDetector.TryFindTextRegion(
                        screenMat,
                        out OpenCvSharp.Rect relativeTextRegion,
                        out double confidence,
                        threshold))
                {
                    HandleDialogueOptionsMissing();
                    return false;
                }

                _dialogueOptionMissCount = 0;
                var bitmapRegion = new System.Drawing.Rectangle(
                    relativeTextRegion.X,
                    relativeTextRegion.Y,
                    relativeTextRegion.Width,
                    relativeTextRegion.Height);
                Bitmap optionBitmap = screenBitmap.Clone(
                    bitmapRegion,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                Mat optionFrame = optionBitmap.ToMat();
                string optionHash = ImageProcessor.ComputeRobustHash(optionFrame);
                if (string.Equals(optionHash, _lastDialogueOptionHash, StringComparison.Ordinal))
                {
                    optionFrame.Dispose();
                    optionBitmap.Dispose();
                    return true;
                }

                _lastDialogueOptionHash = optionHash;
                var absoluteOrigin = new System.Drawing.Point(
                    bounds.Left + relativeTextRegion.X,
                    bounds.Top + relativeTextRegion.Y);
                _ = RecognizeDialogueOptionsAsync(optionFrame, optionBitmap, absoluteOrigin, confidence);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"Dialogue option scan failed: {ex.Message}");
                return false;
            }
            finally
            {
                screenMat?.Dispose();
                screenBitmap?.Dispose();
            }
        }

        private async Task RecognizeDialogueOptionsAsync(
            Mat frame,
            Bitmap bitmap,
            System.Drawing.Point absoluteOrigin,
            double templateConfidence)
        {
            _isOcrRunning = true;
            try
            {
                OCRResult result = await Task.Run(() => data.engine.DetectTextFromMat(frame));
                var candidates = new List<DialogueOptionCandidate>();
                foreach (PaddleOCRSharp.TextBlock block in result.TextBlocks
                    .Where(block => !string.IsNullOrWhiteSpace(block.Text) && block.Score >= 0.45f))
                {
                    float minX = block.BoxPoints.Min(point => point.X);
                    float minY = block.BoxPoints.Min(point => point.Y);
                    float maxX = block.BoxPoints.Max(point => point.X);
                    float maxY = block.BoxPoints.Max(point => point.Y);
                    var bounds = System.Drawing.Rectangle.FromLTRB(
                        absoluteOrigin.X + (int)Math.Floor(minX),
                        absoluteOrigin.Y + (int)Math.Floor(minY),
                        absoluteOrigin.X + (int)Math.Ceiling(maxX),
                        absoluteOrigin.Y + (int)Math.Ceiling(maxY));
                    bounds.Inflate(24, 14);
                    candidates.Add(new DialogueOptionCandidate(block.Text.Trim(), bounds, block.Score));
                }

                _lastDialogueOptions = candidates
                    .OrderBy(candidate => candidate.Bounds.Top)
                    .ThenBy(candidate => candidate.Bounds.Left)
                    .ToList();
                if (candidates.Count == 0)
                {
                    // Retry unchanged frames when OCR temporarily returns no usable text.
                    _lastDialogueOptionHash = null;
                }
                Logger.Log.Debug(
                    $"Dialogue options detected: count={candidates.Count}, templateConfidence={templateConfidence:F3}");
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"Dialogue option OCR failed: {ex.Message}");
            }
            finally
            {
                frame?.Dispose();
                bitmap?.Dispose();
                _isOcrRunning = false;
            }
        }

        private void HandleDialogueOptionsMissing()
        {
            if (_lastDialogueOptions.Count == 0)
            {
                _lastDialogueOptionHash = null;
                _dialogueOptionMissCount = 0;
                return;
            }

            _dialogueOptionMissCount++;
            if (_dialogueOptionMissCount < 2)
            {
                return;
            }

            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            DialogueOptionCandidate selected = _lastDialogueOptions
                .Where(candidate => candidate.Bounds.Contains(cursor))
                .OrderBy(candidate => DistanceSquared(candidate.Bounds, cursor))
                .ThenByDescending(candidate => candidate.Score)
                .FirstOrDefault();

            _lastDialogueOptions = new List<DialogueOptionCandidate>();
            _lastDialogueOptionHash = null;
            _dialogueOptionMissCount = 0;

            if (selected == null)
            {
                return;
            }

            Logger.Log.Debug($"Selected dialogue option: {selected.Text}");
            ShowDialogueChoice(selected.Text);
        }

        private void ShowDialogueChoice(string recognizedText)
        {
            MatchResult match = data.Matcher.FindMatchWithHeaderSeparated(recognizedText, out string key);
            string displayText = string.IsNullOrWhiteSpace(match.Content)
                ? recognizedText
                : match.Content.Trim();

            DialogueChoiceText.Text = $"◆ {displayText}";
            DialogueChoiceText.Visibility = Visibility.Visible;
            HeaderText.Visibility = Visibility.Collapsed;
            _dialogueChoiceDisplayTimer.Stop();
            _dialogueChoiceDisplayTimer.Start();
            UpdateHeaderPosition();
            UpdateWindowHeightAndTop();

            if (Config.Get<bool>("PlayVoice", false) && !string.IsNullOrEmpty(key))
            {
                string audioKey = VoiceContentHelper.CalculateMd5Hash(key);
                PlayDialogueOptionAudioFromUrl($"{server}?md5={audioKey}&token={token}");
            }
        }

        private void ClearDialogueChoiceHeader()
        {
            if (DialogueChoiceText.Visibility != Visibility.Visible)
            {
                return;
            }

            DialogueChoiceText.Text = string.Empty;
            DialogueChoiceText.Visibility = Visibility.Collapsed;
            _dialogueChoiceDisplayTimer.Stop();
            HeaderText.Visibility = string.IsNullOrEmpty(lastHeader)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private static long DistanceSquared(
            System.Drawing.Rectangle bounds,
            System.Drawing.Point point)
        {
            long dx = bounds.Left + bounds.Width / 2L - point.X;
            long dy = bounds.Top + bounds.Height / 2L - point.Y;
            return dx * dx + dy * dy;
        }

        private static Bitmap CaptureRectangle(System.Drawing.Rectangle bounds)
        {
            var bitmap = new Bitmap(
                bounds.Width,
                bounds.Height,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    bounds.Left,
                    bounds.Top,
                    0,
                    0,
                    bounds.Size,
                    CopyPixelOperation.SourceCopy);
            }
            return bitmap;
        }

        private sealed class DialogueOptionCandidate
        {
            public DialogueOptionCandidate(string text, System.Drawing.Rectangle bounds, float score)
            {
                Text = text;
                Bounds = bounds;
                Score = score;
            }

            public string Text { get; }
            public System.Drawing.Rectangle Bounds { get; }
            public float Score { get; }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            MoveWindowByUserDrag();
        }

        private static void CleanupOldAudioTempFiles()
        {
            try
            {
                string tempDirectory = Path.GetTempPath();
                Regex legacyAudioFileName = new Regex(
                    @"^tmp[0-9a-f]{1,4}\.tmp$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                List<FileInfo> audioTempFiles = Directory
                    .EnumerateFiles(tempDirectory, "tmp*.tmp", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .Where(file => legacyAudioFileName.IsMatch(file.Name) && IsAudioTempFile(file.FullName))
                    .OrderByDescending(file => file.CreationTimeUtc)
                    .ToList();

                if (audioTempFiles.Count <= AudioTempCleanupThreshold)
                {
                    return;
                }

                int deletedCount = 0;
                foreach (FileInfo file in audioTempFiles.Skip(AudioTempFilesToKeep))
                {
                    try
                    {
                        file.Delete();
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Warn($"Failed to delete audio temp file {file.FullName}: {ex.Message}");
                    }
                }

                Logger.Log.Info(
                    $"Audio temp cleanup completed: found {audioTempFiles.Count}, " +
                    $"kept {AudioTempFilesToKeep}, deleted {deletedCount}.");
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"Audio temp cleanup failed: {ex.Message}");
            }
        }

        private static bool IsAudioTempFile(string filePath)
        {
            try
            {
                byte[] header = new byte[12];
                int bytesRead;
                using (FileStream stream = new FileStream(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    bytesRead = stream.Read(header, 0, header.Length);
                }

                if (bytesRead >= 3 && header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3')
                {
                    return true;
                }

                // MPEG audio frame sync, including MP3 and ADTS AAC returned by the voice server.
                if (bytesRead >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
                {
                    return true;
                }

                return bytesRead >= 12 &&
                       header[0] == (byte)'R' && header[1] == (byte)'I' &&
                       header[2] == (byte)'F' && header[3] == (byte)'F' &&
                       header[8] == (byte)'W' && header[9] == (byte)'A' &&
                       header[10] == (byte)'V' && header[11] == (byte)'E';
            }
            catch
            {
                return false;
            }
        }


        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopAudio();
            _subtitleEpochTracker.Dispose();
            notifyIcon.Dispose();
            notifyIcon = null;
            data.UnregisterAllHotkeys();
            data.RealClose();
        }

        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            if (!_isUserMovingWindow || notify?.Region == null || notify.Region.Length < 4)
            {
                return;
            }

            int pad = Convert.ToInt16(this.Top - Convert.ToInt16(notify.Region[1]) / Scale);
            int padHorizontal = CalculatePadHorizontal();
            Config.Set("Pad", new int[] { pad, padHorizontal });
        }

        private int CalculatePadHorizontal()
        {
            int regionX = Convert.ToInt16(notify.Region[0]);
            int regionY = Convert.ToInt16(notify.Region[1]);
            int regionWidth = Convert.ToInt16(notify.Region[2]);

            foreach (var screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.Contains(new System.Drawing.Point(regionX, regionY)))
                {
                    double scale = GetScaleForScreen(screen);
                    double left = screen.Bounds.Left / scale;
                    double width = regionWidth / scale + 200;
                    double baseLeft = left + (screen.Bounds.Width / scale - width) / 2;
                    return Convert.ToInt16(this.Left - baseLeft);
                }
            }

            return Config.GetPadHorizontal();
        }

        private void MoveWindowByUserDrag()
        {
            try
            {
                _isUserMovingWindow = true;
                DragMove();
            }
            finally
            {
                _isUserMovingWindow = false;
            }
        }


        public void SwitchIcon(string iconName)
        {
            Uri iconUri = new Uri($"pack://application:,,,/Resources/{iconName}");
            Stream iconStream = System.Windows.Application.GetResourceStream(iconUri).Stream;

            // Create a new Icon object
            Icon newIcon = new Icon(iconStream);

            // Update the NotifyIcon's icon
            notifyIcon.Icon = newIcon;
        }

        // Handle window messages
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                if (wParam.ToInt32() == HOTKEY_ID_1)
                {
                    if (OCRTimer.IsEnabled)
                    {
                        OCRTimer.Stop();
                        UITimer.Stop();
                        SystemSounds.Hand.Play();
                        SwitchIcon("mask.ico");
                    }
                    else
                    {
                        OCRTimer.Start();
                        UITimer.Start();
                        SystemSounds.Exclamation.Play();
                        SwitchIcon("running.ico");
                    }
                    handled = true;
                }
                else if (wParam.ToInt32() == HOTKEY_ID_2)
                {
                    if (!ChooseRegion)
                    {
                        ChooseRegion = true;
                        notify.ChooseRegion();
                        ChooseRegion = false;
                    }
                }
                else if (wParam.ToInt32() == HOTKEY_ID_3)
                {
                    ShowText = !ShowText;
                    SubtitleText.Visibility = ShowText ? Visibility.Visible : Visibility.Collapsed;
                    HeaderText.Visibility = ShowText ? Visibility.Visible : Visibility.Collapsed;
                    HeaderPanel.Visibility = ShowText ? Visibility.Visible : Visibility.Collapsed;
                    if (ShowText)
                    {
                        SystemSounds.Hand.Play();
                    }
                    else
                    {
                        SystemSounds.Exclamation.Play();
                    }
                }
                else if (wParam.ToInt32() == HOTKEY_ID_4)
                {
                    notify.ShowRegionOverlay();
                    handled = true;
                }
                else if (wParam.ToInt32() == HOTKEY_ID_REFRESH)
                {
                    RequestForceRefreshCurrentSubtitle();
                    handled = true;
                }
                else if (wParam.ToInt32() == HOTKEY_ID_PLAYBACK_SPEED)
                {
                    CycleVoicePlaybackSpeed();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }



        public void PlayAudio(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"File {filePath} not found.");
                return;
            }
            player.SoundLocation = filePath;
            player.Play();
        }

        private void PlayDialogueOptionAudioFromUrl(string url)
        {
            bool shouldStart;
            int generation;
            lock (_audioPlaybackQueueLock)
            {
                if (_audioPlaybackQueueActive)
                {
                    // Dialogue choices never interrupt current audio or form a backlog.
                    // Keep only the most recently selected choice.
                    _pendingDialogueOptionUrl = url;
                    return;
                }

                _audioPlaybackQueue.Enqueue(url);
                shouldStart = !_audioPlaybackQueueActive;
                _audioPlaybackQueueActive = true;
                generation = _audioPlaybackGeneration;
            }

            if (shouldStart)
            {
                _ = ProcessNextAudioAsync(generation);
            }
        }

        private void PlayMainAudioFromUrl(string url)
        {
            int generation;
            lock (_audioPlaybackQueueLock)
            {
                _audioPlaybackQueue.Clear();
                _pendingDialogueOptionUrl = null;
                _audioPlaybackQueue.Enqueue(url);
                _audioPlaybackQueueActive = true;
                generation = ++_audioPlaybackGeneration;
            }

            DisposeCurrentAudioPlayback();
            _ = ProcessNextAudioAsync(generation);
        }

        public void StopAudio()
        {
            lock (_audioPlaybackQueueLock)
            {
                _audioPlaybackQueue.Clear();
                _pendingDialogueOptionUrl = null;
                _audioPlaybackQueueActive = false;
                _audioPlaybackGeneration++;
            }

            DisposeCurrentAudioPlayback();
        }

        private void StartAudioPlayback(
            string filePath,
            int generation,
            bool allowTempoProcessing = true)
        {
            DisposeCurrentAudioPlayback();
            bool usingSoundTouch =
                allowTempoProcessing &&
                Math.Abs(_voicePlaybackSpeed - 1.0) >= 0.001;

            try
            {
                mediaReader = new MediaFoundationReader(filePath);
                IWaveProvider playbackSource = mediaReader;
                if (usingSoundTouch)
                {
                    IWaveProvider floatingPointSource =
                        mediaReader.ToSampleProvider().ToWaveProvider();
                    soundTouchProvider = new SoundTouchWaveProvider(floatingPointSource, null)
                    {
                        Tempo = _voicePlaybackSpeed,
                        Pitch = 1.0,
                        Rate = 1.0
                    };
                    soundTouchProvider.OptimizeForSpeech();
                    playbackSource = soundTouchProvider;
                }

                waveOut = new WaveOutEvent();
                IWavePlayer currentPlayer = waveOut;
                _playbackStoppedHandler = (sender, args) =>
                {
                    if (!ReferenceEquals(sender, currentPlayer))
                    {
                        return;
                    }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!ReferenceEquals(waveOut, currentPlayer))
                        {
                            return;
                        }

                        if (args.Exception != null && usingSoundTouch)
                        {
                            Logger.Log.Warn(
                                $"SoundTouch playback failed; retrying at normal speed: {args.Exception.Message}");
                            StartAudioPlayback(filePath, generation, allowTempoProcessing: false);
                            return;
                        }

                        DisposeCurrentAudioPlayback();
                        _ = ProcessNextAudioAsync(generation);
                    }));
                };
                waveOut.PlaybackStopped += _playbackStoppedHandler;
                waveOut.Init(playbackSource);
                waveOut.Play();
            }
            catch (Exception ex) when (usingSoundTouch)
            {
                Logger.Log.Warn(
                    $"SoundTouch initialization failed; retrying at normal speed: {ex.Message}");
                DisposeCurrentAudioPlayback();
                StartAudioPlayback(filePath, generation, allowTempoProcessing: false);
            }
        }

        private async Task ProcessNextAudioAsync(int generation)
        {
            while (true)
            {
                string url;
                lock (_audioPlaybackQueueLock)
                {
                    if (generation != _audioPlaybackGeneration)
                    {
                        return;
                    }

                    if (_audioPlaybackQueue.Count == 0 &&
                        !string.IsNullOrEmpty(_pendingDialogueOptionUrl))
                    {
                        _audioPlaybackQueue.Enqueue(_pendingDialogueOptionUrl);
                        _pendingDialogueOptionUrl = null;
                    }

                    if (_audioPlaybackQueue.Count == 0)
                    {
                        _audioPlaybackQueueActive = false;
                        return;
                    }

                    url = _audioPlaybackQueue.Dequeue();
                }

                string tempFile = Path.GetTempFileName();
                try
                {
                    using (var webClient = new WebClient())
                    {
                        webClient.Headers[HttpRequestHeader.UserAgent] = "GI-Subtitles/1.0";
                        await webClient.DownloadFileTaskAsync(new Uri(url), tempFile);
                    }

                    if (!IsAudioTempFile(tempFile))
                    {
                        throw new InvalidDataException("Downloaded voice file has an unsupported format.");
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        lock (_audioPlaybackQueueLock)
                        {
                            if (generation != _audioPlaybackGeneration)
                            {
                                TryDeleteAudioTempFile(tempFile);
                                return;
                            }
                        }

                        tempFilePath = tempFile;
                        StartAudioPlayback(tempFile, generation);
                    });
                    return;
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse response &&
                                              response.StatusCode == HttpStatusCode.NotFound)
                {
                    Logger.Log.Debug($"Audio not found: {url}");
                }
                catch (Exception ex)
                {
                    Logger.Log.Warn($"Voice playback preparation failed: {ex.Message}");
                }

                TryDeleteAudioTempFile(tempFile);
            }
        }

        private void DisposeCurrentAudioPlayback()
        {
            IWavePlayer currentPlayer = waveOut;
            if (currentPlayer != null && _playbackStoppedHandler != null)
            {
                currentPlayer.PlaybackStopped -= _playbackStoppedHandler;
            }

            _playbackStoppedHandler = null;
            waveOut = null;
            currentPlayer?.Stop();
            currentPlayer?.Dispose();
            soundTouchProvider?.Clear();
            soundTouchProvider = null;
            mediaReader?.Dispose();
            mediaReader = null;
        }

        private static void TryDeleteAudioTempFile(string filePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
                // Old audio files are cleaned up at startup.
            }
        }

        private void CycleVoicePlaybackSpeed()
        {
            int currentIndex = Array.FindIndex(
                VoicePlaybackSpeeds,
                speed => Math.Abs(speed - _voicePlaybackSpeed) < 0.001);
            int nextIndex = (currentIndex + 1) % VoicePlaybackSpeeds.Length;
            _voicePlaybackSpeed = VoicePlaybackSpeeds[nextIndex];
            Config.Set("VoicePlaybackSpeed", _voicePlaybackSpeed);
            UpdatePlaybackSpeedIndicator();

            bool restartCurrentAudio = waveOut?.PlaybackState == PlaybackState.Playing &&
                                       !string.IsNullOrEmpty(tempFilePath) &&
                                       File.Exists(tempFilePath);
            if (restartCurrentAudio)
            {
                int generation;
                lock (_audioPlaybackQueueLock)
                {
                    generation = _audioPlaybackGeneration;
                }
                StartAudioPlayback(tempFilePath, generation);
            }

            notifyIcon?.ShowBalloonTip(
                1200,
                "GI-Subtitles",
                $"Voice playback speed: {_voicePlaybackSpeed:0.##}x",
                ToolTipIcon.Info);
        }

        private void UpdatePlaybackSpeedIndicator()
        {
            if (PlaybackSpeedText == null)
            {
                return;
            }

            PlaybackSpeedText.Text = $"{_voicePlaybackSpeed:0.##}×";
            PlaybackSpeedBadge.ToolTip = $"Voice playback speed: {_voicePlaybackSpeed:0.##}x";
            PlaybackSpeedBadge.Visibility = Math.Abs(_voicePlaybackSpeed - 1.0) < 0.001
                ? Visibility.Collapsed
                : Visibility.Visible;
            UpdateHeaderPosition();
        }

        public void PlayVoiceTest()
        {
            const string testAudioMd5 = "6f3ea6152a7864d324404f8d93a70a1a";
            PlayMainAudioFromUrl($"{server}?md5={testAudioMd5}&token={token}");
        }

        private static double NormalizePlaybackSpeed(double speed)
        {
            return VoicePlaybackSpeeds
                .OrderBy(candidate => Math.Abs(candidate - speed))
                .First();
        }

        public static double GetScaleForScreen(Screen screen)
        {
            // Get the center point of the screen's working area
            System.Drawing.Point screenCenter = new System.Drawing.Point(
                screen.Bounds.Left + screen.Bounds.Width / 2,
                screen.Bounds.Top + screen.Bounds.Height / 2
            );

            // Get the screen handle
            IntPtr monitorHandle = NativeMethods.MonitorFromPoint(screenCenter, 2); // MONITOR_DEFAULTTONEAREST

            // Get DPI value
            uint dpiX, dpiY;
            NativeMethods.GetDpiForMonitor(monitorHandle, NativeMethods.MonitorDpiType.EffectiveDpi, out dpiX, out dpiY);

            // Calculate scale factor (base DPI is 96)
            return dpiX / 96.0;
        }


        private async Task CheckForUpdateAsync()
        {
            try
            {
                var manifestUrl = Config.Get("ReleaseManifest", UpdateChecker.DefaultManifestUrl);
                string responseText;
                using (var client = new HttpClient())
                {
                    responseText = await client.GetStringAsync(manifestUrl);
                }

                var manifest = UpdateChecker.ParseManifest(responseText);
                var installationId = Config.Get<string>("UpdateInstallationId", null);
                if (string.IsNullOrWhiteSpace(installationId))
                {
                    installationId = Guid.NewGuid().ToString("N");
                    Config.Set("UpdateInstallationId", installationId);
                }

                var ignoredVersion = Config.Get<string>("IgnoredUpdateVersion", null);
                if (!UpdateChecker.ShouldOfferUpdate(manifest, version, ignoredVersion, installationId))
                {
                    return;
                }

                availableUpdate = manifest;
                await Dispatcher.InvokeAsync(() =>
                    notify.ShowAvailableUpdate(manifest.Version, async (sender, args) =>
                        await ShowAvailableUpdateAsync()));
            }
            catch (Exception ex)
            {
                // Update checks must never interrupt application startup.
                Logger.Log.Error($"Failed to check for application updates: {ex}");
            }
        }

        private async Task ShowAvailableUpdateAsync()
        {
            var manifest = availableUpdate;
            if (manifest == null || !manifest.Assets.TryGetValue(UpdateChecker.WindowsMsiAsset, out var asset))
            {
                return;
            }

            var title = GetLocalizedText("Update_Title", "Software Update");
            var template = GetLocalizedText(
                "Update_Message",
                "Version {0} is available.\n\nPublished: {1}\n\nWhat's new:\n{2}\n\nChoose Yes to download and install, or No to ignore this version.");
            var result = System.Windows.Forms.MessageBox.Show(
                string.Format(template, manifest.Version, manifest.PublishedAt, manifest.ReleaseNotes),
                title,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == System.Windows.Forms.DialogResult.No)
            {
                Config.Set("IgnoredUpdateVersion", manifest.Version);
                notify.HideAvailableUpdate();
                availableUpdate = null;
                return;
            }

            try
            {
                var msi = Path.Combine(
                    Path.GetTempPath(),
                    $"GI-Subtitles-{manifest.Version}-{Guid.NewGuid():N}.msi");
                using (var client = new WebClient())
                {
                    await client.DownloadFileTaskAsync(new Uri(asset.Url), msi);
                }

                var downloaded = new FileInfo(msi);
                if (downloaded.Length != asset.Size ||
                    !string.Equals(GetSha256(msi), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(msi);
                    throw new InvalidDataException("The downloaded installer did not match the release manifest.");
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/i \"{msi}\" /quiet /norestart",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(startInfo);
                Logger.Log.Debug($"Start installation: msiexec {startInfo.Arguments}");
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Failed to install application update: {ex}");
                System.Windows.Forms.MessageBox.Show(
                    GetLocalizedText("Update_Error", "The update could not be downloaded or verified. Please try again later."),
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static string GetSha256(string file)
        {
            using (var stream = File.OpenRead(file))
            using (var sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string GetLocalizedText(string key, string fallback)
        {
            try
            {
                return System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }
        private void DragButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Console.WriteLine("DragButton_MouseDown");
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                MoveWindowByUserDrag();
            }
        }
        public class NativeMethods
        {
            public enum MonitorDpiType
            {
                EffectiveDpi = 0,
                AngularDpi = 1,
                RawDpi = 2
            }

            [DllImport("Shcore.dll")]
            public static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

            [DllImport("User32.dll")]
            public static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint flags);
        }
    }
}
