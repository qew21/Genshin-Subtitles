using Emgu.CV.Dnn;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using PaddleOCRSharp;
using System;
using System.ComponentModel;
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
using GI_Subtitles.Core.Overlay;
using GI_Subtitles.Core.UI;
using GI_Subtitles.Models;
using GI_Subtitles.Services.OCR;
using GI_Subtitles.Services.Audio;
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
        private readonly double ChangeThreshold = Math.Max(0, Math.Min(1, Config.Get<double>("OCRThreshold", 0.01)));
        private readonly LiveOverlaySession _overlaySession = new LiveOverlaySession(
            new ConfigOcrIntervalStore(),
            new ConfigRegionPairStore(),
            utcNow: null,
            appliedGame: Config.Get("Game", "Genshin"));
        private readonly List<Mat> _pairLastBinary = new List<Mat>();
        private readonly List<Mat> _pairLastOcrBinary = new List<Mat>();
        private readonly List<Bitmap> _pairCapturedBitmaps = new List<Bitmap>();
        private readonly List<Mat> _pairCapturedMats = new List<Mat>();
        private readonly List<System.Windows.Controls.TextBlock> _extraPairBodies = new List<System.Windows.Controls.TextBlock>();
        private readonly OverlayHintChrome _hintChrome = new OverlayHintChrome();
        private readonly DispatcherTimer _hintTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        private readonly List<UIElement> _outlineElements = new List<UIElement>();
        private bool _regionDragging;
        private OverlayRect _dragStartRect = OverlayRect.Invalid;
        private System.Windows.Point _dragStartMouse;
        private int _dragPairIndex = -1;
        private OverlayAdjustTarget _dragTarget = OverlayAdjustTarget.None;
        private bool _dragIsCapture;
        private int _lastPreviewCount;
        private int _lastArmedPairId = -1;
        private OverlayAdjustTarget _lastArmedTarget = OverlayAdjustTarget.None;
        private string _sampledGame;
        private bool _escHotkeyRegistered;
        private const int HotkeyIdAdjustEsc = 9006;
        private const uint VkEscape = 0x1B;
        private static readonly SolidColorBrush CaptureOutlineBrush = CreateFrozenBrush(0x3E, 0xE0, 0x5A);
        private static readonly SolidColorBrush DisplayOutlineBrush = CreateFrozenBrush(0xE6, 0xC3, 0x5C);
        private static readonly SolidColorBrush DarkScreenOutlineBrush = CreateFrozenBrush(0x2A, 0xD4, 0xE8);
        private static readonly SolidColorBrush DialogueOptionOutlineBrush = CreateFrozenBrush(0xA8, 0x5C, 0xE6);
        private static readonly SolidColorBrush AdjustHitFill = CreateFrozenBrush(1, 255, 255, 255);
        private readonly bool _performanceDiagnostics = Config.Get("PerformanceDiagnostics", false);
        private const int DarkScreenAnalysisMaxSide = 960;
        private const int DialogueOptionAnalysisMaxSide = 1920;
        string ocrText = "";
        private NotifyIcon notifyIcon;
        string lastHeader = null;
        string lastContent = null;
        public System.Windows.Threading.DispatcherTimer OCRTimer = new System.Windows.Threading.DispatcherTimer();
        public System.Windows.Threading.DispatcherTimer UITimer = new System.Windows.Threading.DispatcherTimer();
        readonly bool debug = Config.Get<bool>("Debug", false);
        readonly string server = Config.Get<string>("Server", "https://mp3.2langs.com/download");
        readonly string token = Config.Get<string>("Token", "ENGI");
        readonly int distant = Config.Get<int>("Distant", 3);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int y, int Width, int Height, int flags);
        [DllImport("User32.dll")]
        private static extern int GetDpiForSystem();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool StretchBlt(
            IntPtr hdcDest,
            int xDest,
            int yDest,
            int widthDest,
            int heightDest,
            IntPtr hdcSource,
            int xSource,
            int ySource,
            int widthSource,
            int heightSource,
            int rasterOperation);
        [DllImport("gdi32.dll")]
        private static extern int SetStretchBltMode(IntPtr hdc, int stretchMode);
        [DllImport("gdi32.dll")]
        private static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr previousPoint);

        private const int SourceCopyRasterOperation = 0x00CC0020;
        private const int HalftoneStretchMode = 4;
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExLayered = 0x00080000;
        private const int GwlStyle = -16;
        private const int WsDisabled = 0x08000000;

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
        // Use an LRU cache to limit memory usage to 30 entries (mapping from image hash to OCR text)
        LRUCache<string, string> BitmapDict = new LRUCache<string, string>(30);
        List<string> AudioList = new List<string>();
        string InputLanguage = Config.Get<string>("Input");
        string OutputLanguage = Config.Get<string>("Output");
        string Game = Config.Get<string>("Game");
        string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
        INotifyIcon notify;
        SettingsWindow data;
        ActivityLogWindow _activityLogWindow;
        SoundPlayer player = new SoundPlayer();
        private System.Drawing.Rectangle screenBounds = Screen.PrimaryScreen.Bounds;
        bool ShowText = true;
        bool ChooseRegion = false;
        private IWavePlayer waveOut;
        private MediaFoundationReader mediaReader;
        private SoundTouchWaveProvider soundTouchProvider;
        private string tempFilePath;
        private readonly Queue<VoiceAudioSource> _audioPlaybackQueue = new Queue<VoiceAudioSource>();
        private readonly object _audioPlaybackQueueLock = new object();
        private VoiceAudioSource _pendingDialogueOptionSource;
        private bool _audioPlaybackQueueActive;
        private int _audioPlaybackGeneration;
        private EventHandler<StoppedEventArgs> _playbackStoppedHandler;
        private static readonly double[] VoicePlaybackSpeeds = { 1.0, 1.25, 1.5, 1.75, 2.0 };
        private double _voicePlaybackSpeed = NormalizePlaybackSpeed(Config.Get<double>("VoicePlaybackSpeed", 1.0));
        private const int AudioTempCleanupThreshold = 60;
        private const int AudioTempFilesToKeep = 10;
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
        private static readonly TimeSpan DialogueOptionScanInterval =
            TimeSpan.FromMilliseconds(LiveOverlaySession.DialogueOptionScanIntervalMs);
        private static readonly TimeSpan DarkScreenScanInterval =
            TimeSpan.FromMilliseconds(LiveOverlaySession.DarkScreenScanIntervalMs);
        private DateTime _lastDarkScreenScanTime = DateTime.MinValue;
        private string _lastDarkScreenCandidateHash;
        private string _lastDarkScreenOcrHash;
        private int _darkScreenStableFrames;
        private Bitmap _darkScreenBitmap;
        private Mat _darkScreenMat;
        private string _darkScreenPendingHash;
        private Bitmap _dialogueOptionBitmap;
        private Mat _dialogueOptionMat;
        private System.Drawing.Point _dialogueOptionOrigin;
        private double _dialogueOptionConfidence;
        private double _dialogueOptionScaleX = 1.0;
        private double _dialogueOptionScaleY = 1.0;
        private string _pendingExtraPathVoiceKey;
        private ReleaseManifest availableUpdate;
        private readonly LocalVoiceFileResolver _genshinVoiceFileResolver;

        private sealed class VoiceAudioSource
        {
            public string LocalFilePath { get; set; }
            public string RemoteUrl { get; set; }
            public bool LogActivity { get; set; }
        }


        public MainWindow()
        {
            Logger.Log.Debug("Start App");
            _genshinVoiceFileResolver = new LocalVoiceFileResolver(dataDir, "Genshin");
            Task.Run(() => CleanupOldAudioTempFiles());
            InitializeComponent();
            _forceRefreshDebounceTimer.Tick += (sender, args) =>
            {
                _forceRefreshDebounceTimer.Stop();
                ForceRefreshCurrentSubtitle();
            };
            UpdatePlaybackSpeedIndicator();
            _hintTimer.Tick += (sender, args) =>
            {
                _overlaySession.Tick();
                TryStartBusyOcr();
                ApplyHintChrome();
                ApplyOutlineChromeIfChanged();
                if (!_overlaySession.HintVisible && _overlaySession.PreviewOutlines.Count == 0)
                {
                    _hintTimer.Stop();
                }
            };
            _overlaySession.HintChanged += (sender, args) =>
            {
                Dispatcher.BeginInvoke(new Action(OnHintChanged));
            };
            _overlaySession.PreviewChanged += (sender, args) =>
            {
                Dispatcher.BeginInvoke(new Action(OnPreviewChanged));
            };
            _overlaySession.AdjustChanged += (sender, args) =>
            {
                Dispatcher.BeginInvoke(new Action(OnAdjustChanged));
            };
            // Start with the main window fully transparent to avoid showing incomplete UI during heavy startup work.
            // Using Opacity instead of Visibility to ensure Loaded is still raised and initialization runs as usual.
            this.Opacity = 0;
            Loaded += MainWindow_Loaded;
            DragButton.Visibility = Visibility.Collapsed;
            SourceInitialized += (s, e) => ApplyOverlayClickThrough();
        }


        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Get the window handle
            IntPtr handle = new WindowInteropHelper(this).Handle;
            // Listen to window messages
            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(WndProc);

            notify = new INotifyIcon();
            notify.SetSession(_overlaySession);
            notifyIcon = notify.InitializeNotifyIcon(Scale);
            data = new SettingsWindow(version, notify, Scale, _overlaySession);
            data.InitializeKey(handle);
            notify.SetData(data);
            _activityLogWindow = new ActivityLogWindow(_overlaySession);
            notify.SetActivityLogOpener(ShowActivityLog);
            data.OpenActivityLogRequested += (sender, args) => ShowActivityLog();
            data.LogDenoiseChanged += (sender, args) => _activityLogWindow.ApplyLogDenoiseSetting();
            data.IsVisibleChanged += (sender, args) =>
            {
                if (!data.IsVisible)
                {
                    _activityLogWindow.ClearStayAbove();
                }
            };
            CleanupOldUpdatePackages();
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
            if (!_overlaySession.HasValidCapture)
            {
                data.Show();
            }


            data.LoadEngine();

            OCRTimer.Interval = new TimeSpan(0, 0, 0, 0, 100);
            OCRTimer.Tick += GetOCR;    // Delegate: method to execute


            UITimer.Interval = new TimeSpan(0, 0, 0, 0, 500);
            UITimer.Tick += UpdateText;    // Delegate: method to execute

            SetWindowPos(new WindowInteropHelper(this).Handle, -1, 0, 0, 0, 0, 1 | 2);
            SizeOverlayToVirtualScreen();
            ApplyPairOverlay();

            // Show the main window only after initialization is complete, so users don't see a half‑rendered UI.
            this.Opacity = 1;
        }

        public void GetOCR(object sender, EventArgs e)
        {
            if (notify.isContextMenuOpen)
            {
                return;
            }
            if (Interlocked.Exchange(ref OCR_TIMER, 1) == 0)
            {
                try
                {
                    ExtraPathSample extra = CollectExtraPathSample();
                    SampleRegionPairsAndMaybeOcr(extra);
                }
                catch (Exception ex)
                {
                    Logger.Log.Error(ex);
                }
                Interlocked.Exchange(ref OCR_TIMER, 0);
            }
        }

        private void SampleRegionPairsAndMaybeOcr(ExtraPathSample extra)
        {
            ResetCaptureBuffersIfGameChanged();
            IReadOnlyList<RegionPair> pairs = _overlaySession.Pairs;
            int engineCount = Math.Min(LiveOverlaySession.EnginePairCap, pairs.Count);
            EnsurePairBuffers(engineCount);
            var samples = new PairFrameSample[engineCount];

            for (int i = 0; i < engineCount; i++)
            {
                OverlayRect capture = pairs[i].Capture;
                if (!capture.IsValid)
                {
                    continue;
                }

                Bitmap bitmap = null;
                Mat frameMat = null;
                Mat currentBinary = null;
                try
                {
                    bitmap = CaptureRect(capture);
                    frameMat = bitmap.ToMat();
                    currentBinary = PreprocessToBinary(frameMat);
                    bool empty = currentBinary == null || currentBinary.Empty() ||
                                 Cv2.CountNonZero(currentBinary) == 0;
                    bool stable = IsStableVsPrevious(i, currentBinary);
                    bool changed = IsChangedVsLastOcr(i, currentBinary);

                    if (currentBinary != null && !currentBinary.Empty())
                    {
                        _pairLastBinary[i]?.Dispose();
                        _pairLastBinary[i] = currentBinary.Clone();
                    }

                    if (empty && stable)
                    {
                        samples[i] = PairFrameSample.StableNoText();
                    }
                    else if (changed && stable && !empty)
                    {
                        samples[i] = PairFrameSample.ChangedAndStable();
                    }
                    else
                    {
                        samples[i] = PairFrameSample.Unchanged();
                    }

                    ReplaceCaptured(i, bitmap, frameMat);
                    bitmap = null;
                    frameMat = null;
                }
                catch (Exception ex)
                {
                    Logger.Log.Warn($"Pair {i} capture failed: {ex.Message}");
                    bitmap?.Dispose();
                    frameMat?.Dispose();
                }
                finally
                {
                    currentBinary?.Dispose();
                }
            }

            _overlaySession.Beat(extra ?? ExtraPathSample.None, samples);
            string extraVoiceKey = null;
            if (extra != null && extra.DialogueChoiceSelected)
            {
                extraVoiceKey = _pendingExtraPathVoiceKey;
                _pendingExtraPathVoiceKey = null;
            }

            MaybePlayPairVoice(
                extraVoiceKey,
                extra != null ? extra.DialogueChoiceContent : string.Empty,
                string.Empty);

            TryStartBusyOcr();
            ApplyPairOverlay();
        }

        public void UpdateWindowPosition()
        {
            SizeOverlayToVirtualScreen();
            ApplyPairOverlay();
        }

        private void SizeOverlayToVirtualScreen()
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        private void ApplyOverlayClickThrough()
        {
            ApplyOverlayHitMode();
        }

        private void ApplyOverlayHitMode()
        {
            SizeOverlayToVirtualScreen();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int exStyle = GetWindowLong(hwnd, GwlExStyle);
            if (_overlaySession.IsClickThrough)
            {
                int newStyle = exStyle | WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate;
                // The style change is hoisted out of the HitModeApplied
                // arguments: [Conditional("DEBUG")] strips the whole call
                // including argument evaluation, so a change living inside
                // the argument list would silently vanish from Release builds.
                int setResult = SetWindowLong(hwnd, GwlExStyle, newStyle);
                int lastError = Marshal.GetLastWin32Error();
                RegionAdjustDiagnostics.HitModeApplied(
                    hwnd,
                    interactive: false,
                    beforeExStyle: exStyle,
                    newExStyle: newStyle,
                    setResult: setResult,
                    lastError: lastError);
                Background = System.Windows.Media.Brushes.Transparent;
                IsHitTestVisible = false;
                if (OverlayCanvas != null)
                {
                    OverlayCanvas.Background = System.Windows.Media.Brushes.Transparent;
                    OverlayCanvas.IsHitTestVisible = false;
                }
            }
            else
            {
                int newStyle = (exStyle | WsExLayered | WsExToolWindow | WsExNoActivate) & ~WsExTransparent;
                // Same hoisting requirement as the click-through branch above.
                int setResult = SetWindowLong(hwnd, GwlExStyle, newStyle);
                int lastError = Marshal.GetLastWin32Error();
                RegionAdjustDiagnostics.HitModeApplied(
                    hwnd,
                    interactive: true,
                    beforeExStyle: exStyle,
                    newExStyle: newStyle,
                    setResult: setResult,
                    lastError: lastError);
                ClearOverlayDisabledBit(hwnd);
                Background = null;
                IsHitTestVisible = true;
                if (OverlayCanvas != null)
                {
                    OverlayCanvas.Background = null;
                    OverlayCanvas.IsHitTestVisible = true;
                }
            }
        }

        // ShowDialog without an owner disables every top-level window on the
        // thread (the settings window opens that way from the tray menu), and
        // the kernel drops all posted mouse input to a disabled window — armed
        // mode is dead unless the WS_DISABLED bit is cleared alongside the
        // WS_EX_TRANSPARENT bit above.
        private void ClearOverlayDisabledBit(IntPtr hwnd)
        {
            int style = GetWindowLong(hwnd, GwlStyle);
            if ((style & WsDisabled) == 0)
            {
                return;
            }

            int newStyle = style & ~WsDisabled;
            // Same hoisting requirement as ApplyOverlayHitMode: the style
            // change must not live inside [Conditional("DEBUG")] arguments.
            int setResult = SetWindowLong(hwnd, GwlStyle, newStyle);
            int lastError = Marshal.GetLastWin32Error();
            RegionAdjustDiagnostics.DisabledBitCleared(
                hwnd,
                beforeStyle: style,
                newStyle: newStyle,
                setResult: setResult,
                lastError: lastError);
        }

        public void UpdateText(object sender, EventArgs e)
        {
            if (Interlocked.Exchange(ref UI_TIMER, 1) == 0)
            {
                try
                {
                    _overlaySession.Tick();
                    ApplyPairOverlay();
                }
                catch (Exception ex)
                {
                    Logger.Log.Error(ex);
                }
                Interlocked.Exchange(ref UI_TIMER, 0);
            }
        }

        private void ApplyPairOverlay()
        {
            if (OverlayCanvas == null)
            {
                return;
            }

            SizeOverlayToVirtualScreen();
            IReadOnlyList<PairSubtitleBody> bodies = _overlaySession.PairBodies;
            EnsureExtraPairBodies(bodies.Count);

            if (bodies.Count == 0)
            {
                HidePairZeroOverlay();
            }

            for (int i = 0; i < bodies.Count; i++)
            {
                if (i == 0)
                {
                    ApplyPairZeroOverlay(bodies[i]);
                }
                else
                {
                    ApplyExtraPairOverlay(_extraPairBodies[i - 1], bodies[i]);
                }
            }

            for (int i = Math.Max(0, bodies.Count - 1); i < _extraPairBodies.Count; i++)
            {
                _extraPairBodies[i].Visibility = Visibility.Collapsed;
            }

            ApplyDarkScreenOverlay();
            ApplyDialogueChoiceEchoOverlay();
            UpdateHeaderPosition();
        }

        private void HidePairZeroOverlay()
        {
            SubtitleText.Visibility = Visibility.Collapsed;
            if (PlaybackSpeedBadge.Visibility != Visibility.Visible)
            {
                HeaderPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyPairZeroOverlay(PairSubtitleBody body)
        {
            OverlayRect display = body.Display;
            if (!display.IsValid)
            {
                HidePairZeroOverlay();
                return;
            }

            System.Windows.Point canvasPoint = DisplayToCanvas(display);
            double width = display.Width / Scale;
            double height = display.Height / Scale;
            Canvas.SetLeft(SubtitleText, canvasPoint.X);
            Canvas.SetTop(SubtitleText, canvasPoint.Y);
            SubtitleText.Width = width;
            SubtitleText.Height = height;
            SubtitleText.MaxHeight = height;
            SubtitleText.Text = body.Content;
            SubtitleText.FontSize = Config.Get<int>("Size");
            SubtitleText.Visibility = body.Visible ? Visibility.Visible : Visibility.Collapsed;
            System.Windows.Controls.Panel.SetZIndex(SubtitleText, body.RecognitionOrder);

            Canvas.SetLeft(HeaderPanel, canvasPoint.X);
            Canvas.SetTop(HeaderPanel, canvasPoint.Y);
            HeaderPanel.Width = width;
            System.Windows.Controls.Panel.SetZIndex(HeaderPanel, body.RecognitionOrder);

            HeaderText.Text = body.Header;
            HeaderText.Visibility = _overlaySession.SubtitlesVisible && !string.IsNullOrEmpty(body.Header)
                ? Visibility.Visible
                : Visibility.Collapsed;

            bool headerChromeVisible = _overlaySession.SubtitlesVisible &&
                (HeaderText.Visibility == Visibility.Visible ||
                 PlaybackSpeedBadge.Visibility == Visibility.Visible);
            HeaderPanel.Visibility = headerChromeVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyDarkScreenOverlay()
        {
            ExtraPathBody body = _overlaySession.DarkScreenBody;
            if (!body.Visible || !body.Display.IsValid)
            {
                DarkScreenText.Visibility = Visibility.Collapsed;
                return;
            }

            System.Windows.Point canvasPoint = DisplayToCanvas(body.Display);
            Canvas.SetLeft(DarkScreenText, canvasPoint.X);
            Canvas.SetTop(DarkScreenText, canvasPoint.Y);
            DarkScreenText.Width = body.Display.Width / Scale;
            DarkScreenText.Height = body.Display.Height / Scale;
            DarkScreenText.MaxHeight = body.Display.Height / Scale;
            DarkScreenText.FontSize = Config.Get<int>("Size");
            DarkScreenText.Text = string.IsNullOrEmpty(body.Header)
                ? body.Content
                : body.Header + Environment.NewLine + body.Content;
            DarkScreenText.Visibility = Visibility.Visible;
            System.Windows.Controls.Panel.SetZIndex(DarkScreenText, body.RecognitionOrder);
        }

        private void ApplyDialogueChoiceEchoOverlay()
        {
            ExtraPathBody echo = _overlaySession.DialogueChoiceEcho;
            if (!echo.Visible || !echo.Display.IsValid)
            {
                DialogueChoiceText.Visibility = Visibility.Collapsed;
                return;
            }

            System.Windows.Point canvasPoint = DisplayToCanvas(echo.Display);
            Canvas.SetLeft(DialogueChoiceText, canvasPoint.X);
            Canvas.SetTop(DialogueChoiceText, canvasPoint.Y);
            DialogueChoiceText.Width = echo.Display.Width / Scale;
            DialogueChoiceText.Text = echo.Content;
            DialogueChoiceText.Visibility = Visibility.Visible;
            System.Windows.Controls.Panel.SetZIndex(DialogueChoiceText, echo.RecognitionOrder);

            var transform = (System.Windows.Media.TranslateTransform)DialogueChoiceText.RenderTransform;
            if (!echo.FollowsVoicePrimary)
            {
                transform.Y = 0;
                DialogueChoiceText.Height = echo.Display.Height / Scale;
                DialogueChoiceText.MaxHeight = echo.Display.Height / Scale;
                DialogueChoiceText.FontSize = Config.Get<int>("Size");
                return;
            }

            DialogueChoiceText.ClearValue(FrameworkElement.HeightProperty);
            DialogueChoiceText.ClearValue(FrameworkElement.MaxHeightProperty);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (DialogueChoiceText.Visibility != Visibility.Visible)
                    {
                        return;
                    }

                    DialogueChoiceText.UpdateLayout();
                    double echoHeight = DialogueChoiceText.ActualHeight;
                    if (echoHeight <= 0)
                    {
                        echoHeight = 18;
                    }

                    double headerLift = 0;
                    if (_overlaySession.Pairs.Count > 0 &&
                        _overlaySession.VoicePrimaryId == _overlaySession.Pairs[0].Id &&
                        HeaderPanel.Visibility == Visibility.Visible)
                    {
                        HeaderPanel.UpdateLayout();
                        headerLift = HeaderPanel.ActualHeight + 4;
                    }

                    var transform = (System.Windows.Media.TranslateTransform)DialogueChoiceText.RenderTransform;
                    transform.Y = -(echoHeight + 4 + headerLift);
                }
                catch (Exception ex)
                {
                    Logger.Log.Error($"Error updating dialogue-choice echo position: {ex}");
                }
            }), DispatcherPriority.Loaded);
        }

        private void ApplyExtraPairOverlay(System.Windows.Controls.TextBlock block, PairSubtitleBody body)
        {
            if (!body.Visible || !body.Display.IsValid)
            {
                block.Visibility = Visibility.Collapsed;
                return;
            }

            System.Windows.Point canvasPoint = DisplayToCanvas(body.Display);
            Canvas.SetLeft(block, canvasPoint.X);
            Canvas.SetTop(block, canvasPoint.Y);
            block.Width = body.Display.Width / Scale;
            block.Height = body.Display.Height / Scale;
            block.FontSize = Config.Get<int>("Size");
            block.Text = string.IsNullOrEmpty(body.Header)
                ? body.Content
                : body.Header + Environment.NewLine + body.Content;
            block.Visibility = Visibility.Visible;
            System.Windows.Controls.Panel.SetZIndex(block, body.RecognitionOrder);
        }

        private System.Windows.Point DisplayToCanvas(OverlayRect display)
        {
            return new System.Windows.Point(
                display.X / Scale - SystemParameters.VirtualScreenLeft,
                display.Y / Scale - SystemParameters.VirtualScreenTop);
        }

        private void EnsurePairBuffers(int count)
        {
            while (_pairLastBinary.Count < count)
            {
                _pairLastBinary.Add(null);
                _pairLastOcrBinary.Add(null);
                _pairCapturedBitmaps.Add(null);
                _pairCapturedMats.Add(null);
            }
        }

        private void EnsureExtraPairBodies(int pairCount)
        {
            int extraNeeded = Math.Max(0, pairCount - 1);
            while (_extraPairBodies.Count < extraNeeded)
            {
                var block = new System.Windows.Controls.TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = System.Windows.Media.Brushes.White,
                    TextAlignment = TextAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Visibility = Visibility.Collapsed,
                    IsHitTestVisible = false
                };
                block.SetResourceReference(
                    System.Windows.Controls.TextBlock.FontFamilyProperty,
                    "SubtitleFontFamily");
                OverlayCanvas.Children.Add(block);
                _extraPairBodies.Add(block);
            }
        }

        private void ReplaceCaptured(int pairIndex, Bitmap bitmap, Mat frameMat)
        {
            EnsurePairBuffers(pairIndex + 1);
            _pairCapturedBitmaps[pairIndex]?.Dispose();
            _pairCapturedMats[pairIndex]?.Dispose();
            _pairCapturedBitmaps[pairIndex] = bitmap;
            _pairCapturedMats[pairIndex] = frameMat;
        }

        private static Bitmap CaptureRect(OverlayRect rect)
        {
            return CaptureRegion(new[]
            {
                rect.X.ToString(),
                rect.Y.ToString(),
                rect.Width.ToString(),
                rect.Height.ToString()
            });
        }

        private static bool SameShape(Mat left, Mat right)
        {
            return left != null && right != null &&
                   left.Size() == right.Size() &&
                   left.Channels() == right.Channels();
        }

        private bool IsStableVsPrevious(int pairIndex, Mat currentBinary)
        {
            Mat previous = pairIndex < _pairLastBinary.Count ? _pairLastBinary[pairIndex] : null;
            if (previous == null || currentBinary == null || currentBinary.Empty())
            {
                return true;
            }

            if (!SameShape(currentBinary, previous))
            {
                return false;
            }

            using (Mat diff = new Mat())
            {
                Cv2.Absdiff(currentBinary, previous, diff);
                int nonZero = Cv2.CountNonZero(diff);
                double change = (double)nonZero / (diff.Rows * diff.Cols);
                if (debug)
                {
                    Logger.Log.Debug($"Pair {pairIndex} changeRatio(prev)={change:F4}");
                }
                return change <= ChangeThreshold;
            }
        }

        private bool IsChangedVsLastOcr(int pairIndex, Mat currentBinary)
        {
            Mat lastOcr = pairIndex < _pairLastOcrBinary.Count ? _pairLastOcrBinary[pairIndex] : null;
            if (lastOcr == null)
            {
                return true;
            }

            if (currentBinary == null || currentBinary.Empty() || !SameShape(currentBinary, lastOcr))
            {
                return true;
            }

            using (Mat diff = new Mat())
            {
                Cv2.Absdiff(currentBinary, lastOcr, diff);
                int nonZero = Cv2.CountNonZero(diff);
                double change = (double)nonZero / (diff.Rows * diff.Cols);
                if (debug)
                {
                    Logger.Log.Debug($"Pair {pairIndex} changeRatio(ocr)={change:F4}");
                }
                return change > ChangeThreshold;
            }
        }

        private void TryStartBusyOcr()
        {
            if (_isOcrRunning)
            {
                return;
            }

            int? slot = _overlaySession.BusyOcrSlot;
            if (!slot.HasValue)
            {
                return;
            }

            if (slot.Value == LiveOverlaySession.DarkScreenOcrSlot)
            {
                TryStartDarkScreenOcr();
                return;
            }

            if (slot.Value == LiveOverlaySession.DialogueOptionsOcrSlot)
            {
                TryStartDialogueOptionsOcr();
                return;
            }

            int idx = slot.Value;
            if (idx < 0 || idx >= _pairCapturedMats.Count || _pairCapturedMats[idx] == null)
            {
                return;
            }

            Mat lastBinary = idx < _pairLastBinary.Count ? _pairLastBinary[idx] : null;
            if (lastBinary != null)
            {
                EnsurePairBuffers(idx + 1);
                _pairLastOcrBinary[idx]?.Dispose();
                _pairLastOcrBinary[idx] = lastBinary.Clone();
            }

            Mat frame = _pairCapturedMats[idx];
            Bitmap bitmap = _pairCapturedBitmaps[idx];
            _pairCapturedMats[idx] = null;
            _pairCapturedBitmaps[idx] = null;
            SetWindowPos(new WindowInteropHelper(this).Handle, -1, 0, 0, 0, 0, 1 | 2);
            _ = TriggerOcrAsync(frame, bitmap, pairIndex: idx);
        }

        private void TryStartDarkScreenOcr()
        {
            if (_darkScreenMat == null || _darkScreenBitmap == null)
            {
                _overlaySession.CompleteOcr(miss: true);
                TryStartBusyOcr();
                return;
            }

            Mat frame = _darkScreenMat;
            Bitmap bitmap = _darkScreenBitmap;
            string hash = _darkScreenPendingHash;
            _darkScreenMat = null;
            _darkScreenBitmap = null;
            _darkScreenPendingHash = null;
            _lastDarkScreenOcrHash = hash;
            SetWindowPos(new WindowInteropHelper(this).Handle, -1, 0, 0, 0, 0, 1 | 2);
            _ = TriggerOcrAsync(frame, bitmap, darkScreenHash: hash);
        }

        private void TryStartDialogueOptionsOcr()
        {
            if (_dialogueOptionMat == null || _dialogueOptionBitmap == null)
            {
                _overlaySession.CompleteOcr(miss: true);
                TryStartBusyOcr();
                return;
            }

            Mat frame = _dialogueOptionMat;
            Bitmap bitmap = _dialogueOptionBitmap;
            System.Drawing.Point origin = _dialogueOptionOrigin;
            double confidence = _dialogueOptionConfidence;
            double scaleX = _dialogueOptionScaleX;
            double scaleY = _dialogueOptionScaleY;
            _dialogueOptionMat = null;
            _dialogueOptionBitmap = null;
            _ = RecognizeDialogueOptionsAsync(frame, bitmap, origin, scaleX, scaleY, confidence);
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

                    var transform = (System.Windows.Media.TranslateTransform)HeaderPanel.RenderTransform;
                    transform.Y = -(headerHeight + 4);
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

        /// <summary>
        /// Async trigger OCR: execute the time-consuming OCR and hash matching logic in the background thread, only call when the subtitle pixel changes significantly.
        /// </summary>
        /// <param name="frameToProcess">Image Mat for OCR (caller has already Clone)</param>
        /// <param name="target">Original screenshot Bitmap, used for debugging and setting preview image</param>
        private async Task TriggerOcrAsync(
            Mat frameToProcess,
            Bitmap target,
            bool forceRefresh = false,
            int? pairIndex = null,
            string darkScreenHash = null)
        {
            _isOcrRunning = true;
            string ocrGame = _overlaySession.AppliedGame;
            Stopwatch recognitionStopwatch = _performanceDiagnostics ? Stopwatch.StartNew() : null;
            string recognizedText = null;
            bool recognitionCompleted = false;
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        if (frameToProcess == null || frameToProcess.Empty())
                        {
                            return;
                        }

                        string bitStr = ImageProcessor.ComputeRobustHash(frameToProcess);

                        if (!forceRefresh &&
                            BitmapDict.TryGetValue(bitStr, out string cachedOcrText) &&
                            !string.IsNullOrWhiteSpace(cachedOcrText))
                        {
                            recognizedText = cachedOcrText;
                            recognitionCompleted = true;
                        }
                        else
                        {
                            string matchedImageHash = forceRefresh
                                ? null
                                : ImageProcessor.FindSimilarImageHash(bitStr, BitmapDict, maxDistance: distant);
                            if (matchedImageHash != null)
                            {
                                recognizedText = BitmapDict[matchedImageHash];
                                BitmapDict[bitStr] = recognizedText; // LRU cache automatically manages size
                                recognitionCompleted = true;
                            }
                            else
                            {
                                OCRResult ocrResult = data.engine.DetectTextFromMat(frameToProcess);
                                recognizedText = ocrResult?.Text ?? string.Empty;
                                recognitionCompleted = true;

                                if (debug)
                                {
                                    try
                                    {
                                        string fileName = DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss_ffffff") + ".png";
                                        Logger.Log.Debug(fileName);
                                        target.Save(Path.Combine(dataDir, fileName));
                                        Logger.Log.Debug($"OCR Text: {recognizedText}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Log.Error($"Failed to save debug image: {ex}");
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(recognizedText))
                                {
                                    BitmapDict[bitStr] = recognizedText;
                                }
                            }
                        }

                        if (!recognitionCompleted)
                        {
                            return;
                        }

                        ocrText = recognizedText;
                        Logger.Log.Debug($"OCR Content: {recognizedText}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Error(ex);
                    }
                });

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (data.IsVisible && target != null)
                        {
                            data.SetImage(target);
                        }
                        else
                        {
                            target?.Dispose();
                        }

                        if (string.Equals(ocrGame, _overlaySession.AppliedGame, StringComparison.Ordinal))
                        {
                            ApplyRecognizedText(recognizedText, recognitionCompleted, forceRefresh, pairIndex);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Error(ex);
                    }
                });
            }
            finally
            {
                int processedWidth = frameToProcess?.IsDisposed == false ? frameToProcess.Width : 0;
                int processedHeight = frameToProcess?.IsDisposed == false ? frameToProcess.Height : 0;
                if (!string.IsNullOrEmpty(darkScreenHash) &&
                    (!recognitionCompleted || string.IsNullOrWhiteSpace(recognizedText)))
                {
                    // Allow an unchanged candidate to retry after a transient OCR miss.
                    _lastDarkScreenOcrHash = null;
                }
                _isOcrRunning = false;
                frameToProcess?.Dispose();

                if (recognitionStopwatch != null)
                {
                    Logger.Log.Info(
                        $"OCR pipeline completed: frame={processedWidth}x{processedHeight}, " +
                        $"elapsedMs={recognitionStopwatch.ElapsedMilliseconds}, completed={recognitionCompleted}");
                }

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_forceRefreshPending)
                    {
                        _forceRefreshPending = false;
                        ForceRefreshCurrentSubtitle();
                        return;
                    }

                    TryStartBusyOcr();
                }));
            }
        }

        private void ApplyRecognizedText(
            string recognizedText,
            bool recognitionCompleted,
            bool forceRefresh,
            int? pairIndex)
        {
            bool usable = recognitionCompleted && recognizedText != null && recognizedText.Length >= 2;
            string header = "";
            string content = "";
            string key = "";
            string original = "";
            bool matchMiss = false;
            if (usable)
            {
                matchMiss = !TryMatchOcrText(recognizedText, out header, out content, out key, out original);
            }

            int appliedPair = pairIndex ?? 0;
            if (forceRefresh)
            {
                if (usable)
                {
                    _forceVoiceReplayRequested = true;
                    _overlaySession.ApplyPairResult(
                        appliedPair,
                        miss: false,
                        content,
                        header,
                        recognizedText,
                        original,
                        matchMiss,
                        force: true);
                    MaybePlayPairVoice(key, content, header);
                    ApplyPairOverlay();
                    _overlaySession.Refresh(hasCaptureRegion: true, foundText: true);
                }
                else
                {
                    Logger.Log.Warn("Forced OCR refresh produced no usable text; keeping the current subtitle without replay.");
                    _overlaySession.ApplyPairResult(appliedPair, miss: true, force: true);
                    _overlaySession.Refresh(hasCaptureRegion: true, foundText: false);
                }
                return;
            }

            if (_overlaySession.BusyOcrSlot == LiveOverlaySession.DarkScreenOcrSlot)
            {
                if (!usable)
                {
                    _overlaySession.NoteOcrMiss();
                    _overlaySession.CompleteOcr(miss: true);
                    return;
                }

                _overlaySession.CompleteOcr(
                    miss: false,
                    content,
                    header,
                    recognizedText,
                    original,
                    matchMiss);
                MaybePlayPairVoice(key, content, header);
                ApplyPairOverlay();
                return;
            }

            if (!pairIndex.HasValue)
            {
                return;
            }

            if (!usable)
            {
                _overlaySession.NoteOcrMiss();
                _overlaySession.CompleteOcr(miss: true);
                return;
            }

            _overlaySession.CompleteOcr(
                miss: false,
                content,
                header,
                recognizedText,
                original,
                matchMiss);
            MaybePlayPairVoice(key, content, header);
            ApplyPairOverlay();
        }

        private bool TryMatchOcrText(
            string recognizedText,
            out string header,
            out string content,
            out string key,
            out string original)
        {
            header = "";
            content = "";
            key = "";
            original = "";
            if (string.IsNullOrEmpty(recognizedText) || recognizedText.Length <= 1)
            {
                return false;
            }

            if (_overlaySession.TryGetCachedMatch(recognizedText, out string cachedRes))
            {
                key = _overlaySession.GetCachedMatchKey(cachedRes);
                original = key ?? "";
                string[] parts = cachedRes.Split(new[] { "\n\n" }, StringSplitOptions.None);
                if (parts.Length >= 2)
                {
                    header = parts[0];
                    content = parts[1];
                }
                else
                {
                    content = cachedRes;
                }

                return !string.IsNullOrEmpty(header) || !string.IsNullOrEmpty(content);
            }

            MatchResult matchResult = data.Matcher.FindMatchWithHeaderSeparated(recognizedText, out key);
            header = matchResult.Header ?? "";
            content = matchResult.Content ?? "";
            original = JoinSubtitleParts(matchResult.MatchedHeader, matchResult.MatchedContent);
            if (string.IsNullOrEmpty(original))
            {
                original = key ?? "";
            }

            string res = string.IsNullOrEmpty(header) ? content : (header + "\n\n" + content);
            Logger.Log.Debug($"Convert ocrResult for {recognizedText}: header={header}, content={content}, key={key}");
            _overlaySession.RememberMatch(recognizedText, res, key);

            bool matched = !string.IsNullOrEmpty(header) || !string.IsNullOrEmpty(content);
            if (!matched)
            {
                _overlaySession.NoteMatchMiss();
            }

            return matched;
        }

        private static string JoinSubtitleParts(string header, string content)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                return content ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return header;
            }

            return header + "\n" + content;
        }

        private void MaybePlayPairVoice(string key, string content, string header)
        {
            VoicePlayRequest request = _overlaySession.TakeVoicePlayRequest();
            if (request == null)
            {
                return;
            }

            if (request.ExtraPath)
            {
                if (!Config.Get<bool>("PlayVoice", false) || string.IsNullOrEmpty(key))
                {
                    _overlaySession.NoteVoicePlaybackEnded();
                    return;
                }

                string extraAudioKey = VoiceContentHelper.CalculateMd5Hash(key);
                PlayDialogueOptionAudio(extraAudioKey, logActivity: true);
                return;
            }

            bool forceVoiceReplay = _forceVoiceReplayRequested;
            bool contentChanged = forceVoiceReplay || content != lastContent;

            lastHeader = header;
            lastContent = content;
            _forceVoiceReplayRequested = false;

            if (!Config.Get<bool>("PlayVoice", false) || !contentChanged || string.IsNullOrEmpty(key))
            {
                return;
            }

            if (!forceVoiceReplay && AudioList.Contains(key))
            {
                return;
            }

            string audioKey = VoiceContentHelper.CalculateMd5Hash(key);
            PlayMainAudio(audioKey, logActivity: true);
            if (!AudioList.Contains(key))
            {
                AudioList.Add(key);
            }
        }

        private void ForceRefreshCurrentSubtitle()
        {
            if (_isOcrRunning)
            {
                _forceRefreshPending = true;
                return;
            }

            try
            {
                if (!_overlaySession.TryGetVoicePrimaryCapture(out int pairIndex, out OverlayRect capture))
                {
                    _overlaySession.Refresh(hasCaptureRegion: false, foundText: false);
                    return;
                }

                Bitmap target = CaptureRect(capture);
                Mat frame = target.ToMat();
                EnsurePairBuffers(pairIndex + 1);
                Mat binary = PreprocessToBinary(frame);
                if (binary != null)
                {
                    _pairLastBinary[pairIndex]?.Dispose();
                    _pairLastOcrBinary[pairIndex]?.Dispose();
                    _pairLastBinary[pairIndex] = binary.Clone();
                    _pairLastOcrBinary[pairIndex] = binary;
                }
                _overlaySession.ResetOcrInterval();
                _ = TriggerOcrAsync(frame, target, forceRefresh: true, pairIndex: pairIndex);
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Failed to force refresh current subtitle: {ex}");
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

        private ExtraPathSample CollectExtraPathSample()
        {
            if (!_overlaySession.HasValidCapture ||
                !TryGetFirstValidCaptureScreen(out System.Drawing.Rectangle screen))
            {
                DisposeDarkScreenHold();
                DisposeDialogueOptionHold();
                return ExtraPathSample.None;
            }

            ExtraPathSample extra = ObserveDarkScreen(screen);
            return ObserveDialogueOptions(screen, extra);
        }

        private bool TryGetFirstValidCaptureScreen(out System.Drawing.Rectangle screen)
        {
            screen = System.Drawing.Rectangle.Empty;
            IReadOnlyList<RegionPair> pairs = _overlaySession.Pairs;
            int engineCount = Math.Min(LiveOverlaySession.EnginePairCap, pairs.Count);
            for (int i = 0; i < engineCount; i++)
            {
                OverlayRect capture = pairs[i].Capture;
                if (!capture.IsValid)
                {
                    continue;
                }

                var anchor = new System.Drawing.Point(
                    capture.X + capture.Width / 2,
                    capture.Y + capture.Height / 2);
                screen = Screen.GetBounds(anchor);
                return true;
            }

            return false;
        }

        private ExtraPathSample ObserveDarkScreen(System.Drawing.Rectangle screen)
        {
            if (!_overlaySession.DarkScreenScanOn)
            {
                DisposeDarkScreenHold();
                return ExtraPathSample.None;
            }

            DateTime now = DateTime.UtcNow;
            if (now - _lastDarkScreenScanTime < DarkScreenScanInterval)
            {
                return ExtraPathSample.None;
            }

            _lastDarkScreenScanTime = now;

            Bitmap searchBitmap = null;
            Mat searchMat = null;
            Bitmap candidateBitmap = null;
            Mat candidateFrame = null;
            bool heldCandidate = false;
            try
            {
                var searchBounds = new System.Drawing.Rectangle(
                    screen.Left + (int)Math.Round(screen.Width * 0.05),
                    screen.Top + (int)Math.Round(screen.Height * 0.20),
                    (int)Math.Round(screen.Width * 0.90),
                    (int)Math.Round(screen.Height * 0.45));

                searchBitmap = CaptureRectangleScaled(searchBounds, DarkScreenAnalysisMaxSide);
                searchMat = LimitFrameSize(searchBitmap.ToMat(), DarkScreenAnalysisMaxSide);
                searchBitmap.Dispose();
                searchBitmap = null;
                bool found = DarkScreenSubtitleDetector.TryFindSubtitleRegion(
                    searchMat,
                    out OpenCvSharp.Rect candidateRegion,
                    out bool isDarkScreen,
                    out double darkRatio,
                    out double brightRatio);

                if (!isDarkScreen)
                {
                    ResetDarkScreenCandidate();
                    DisposeDarkScreenHold();
                    return ExtraPathSample.DarkScreenEnded();
                }

                if (!found)
                {
                    ResetDarkScreenCandidate();
                    DisposeDarkScreenHold();
                    if (debug)
                    {
                        Logger.Log.Debug(
                            $"Dark screen detected without subtitle candidate: dark={darkRatio:F3}, bright={brightRatio:F4}");
                    }
                    return ExtraPathSample.DarkScreenWithoutCandidate();
                }

                var bitmapRegion = new OpenCvSharp.Rect(
                    candidateRegion.X,
                    candidateRegion.Y,
                    candidateRegion.Width,
                    candidateRegion.Height);
                candidateFrame = new Mat(searchMat, bitmapRegion).Clone();
                candidateBitmap = candidateFrame.ToBitmap();
                string candidateHash = ImageProcessor.ComputeRobustHash(candidateFrame);
                double bandScaleX = searchBounds.Width / (double)searchMat.Width;
                double bandScaleY = searchBounds.Height / (double)searchMat.Height;
                var absoluteBand = new OverlayRect(
                    searchBounds.Left + (int)Math.Round(candidateRegion.X * bandScaleX),
                    searchBounds.Top + (int)Math.Round(candidateRegion.Y * bandScaleY),
                    (int)Math.Round(candidateRegion.Width * bandScaleX),
                    (int)Math.Round(candidateRegion.Height * bandScaleY));

                if (!string.IsNullOrEmpty(_lastDarkScreenCandidateHash) &&
                    ImageProcessor.CalculateHammingDistance(
                        candidateHash,
                        _lastDarkScreenCandidateHash) <= 2)
                {
                    _darkScreenStableFrames++;
                }
                else
                {
                    _darkScreenStableFrames = 1;
                }
                _lastDarkScreenCandidateHash = candidateHash;

                bool needsOcr = _darkScreenStableFrames >= 2 &&
                    (string.IsNullOrEmpty(_lastDarkScreenOcrHash) ||
                     ImageProcessor.CalculateHammingDistance(
                         candidateHash,
                         _lastDarkScreenOcrHash) > 2);

                if (needsOcr)
                {
                    HoldDarkScreenFrames(candidateBitmap, candidateFrame, candidateHash);
                    candidateBitmap = null;
                    candidateFrame = null;
                    heldCandidate = true;
                    Logger.Log.Debug(
                        $"Stable dark-screen subtitle detected: dark={darkRatio:F3}, bright={brightRatio:F4}, " +
                        $"candidate={candidateRegion}");
                }

                return ExtraPathSample.DarkScreenCandidate(absoluteBand, needsOcr);
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"Dark-screen subtitle scan failed: {ex.Message}");
                ResetDarkScreenCandidate();
                return ExtraPathSample.None;
            }
            finally
            {
                if (!heldCandidate)
                {
                    candidateFrame?.Dispose();
                    candidateBitmap?.Dispose();
                }
                searchMat?.Dispose();
                searchBitmap?.Dispose();
            }
        }

        private void ResetDarkScreenCandidate()
        {
            _lastDarkScreenCandidateHash = null;
            _lastDarkScreenOcrHash = null;
            _darkScreenStableFrames = 0;
        }

        private void HoldDarkScreenFrames(Bitmap bitmap, Mat mat, string hash)
        {
            DisposeDarkScreenHold();
            _darkScreenBitmap = bitmap;
            _darkScreenMat = mat;
            _darkScreenPendingHash = hash;
        }

        private void DisposeDarkScreenHold()
        {
            _darkScreenBitmap?.Dispose();
            _darkScreenMat?.Dispose();
            _darkScreenBitmap = null;
            _darkScreenMat = null;
            _darkScreenPendingHash = null;
        }

        private ExtraPathSample ObserveDialogueOptions(System.Drawing.Rectangle screen, ExtraPathSample extra)
        {
            extra = extra ?? ExtraPathSample.None;
            if (!_overlaySession.AllowsDialogueOptionScan ||
                DateTime.UtcNow - _lastDialogueOptionScanTime < DialogueOptionScanInterval)
            {
                return extra;
            }

            _lastDialogueOptionScanTime = DateTime.UtcNow;

            Bitmap screenBitmap = null;
            Mat screenMat = null;
            try
            {
                screenBitmap = CaptureRectangleScaled(screen, DialogueOptionAnalysisMaxSide);
                screenMat = LimitFrameSize(screenBitmap.ToMat(), DialogueOptionAnalysisMaxSide);
                screenBitmap.Dispose();
                screenBitmap = null;
                double coordinateScaleX = screen.Width / (double)screenMat.Width;
                double coordinateScaleY = screen.Height / (double)screenMat.Height;

                double threshold = Config.Get("DialogueOptionTemplateThreshold", 0.74);
                if (!DialogueOptionDetector.TryFindTextRegion(
                        screenMat,
                        out OpenCvSharp.Rect relativeTextRegion,
                        out double confidence,
                        threshold))
                {
                    bool hadCandidates = _lastDialogueOptions.Count > 0;
                    string choice = TryTakeDialogueChoice();
                    if (!string.IsNullOrEmpty(choice))
                    {
                        return extra == ExtraPathSample.None
                            ? ExtraPathSample.DialogueChoice(choice)
                            : extra.WithDialogueChoice(choice);
                    }

                    // Only after a non-empty candidate list was cleared without a click
                    // (2-miss dismiss). Skip idle scans and Ready→first-OCR gaps.
                    if (hadCandidates && _lastDialogueOptions.Count == 0)
                    {
                        return extra == ExtraPathSample.None
                            ? ExtraPathSample.DialogueOptionsEnded()
                            : extra.WithDialogueOptionsEnded();
                    }

                    return extra;
                }

                _dialogueOptionMissCount = 0;
                var bitmapRegion = new OpenCvSharp.Rect(
                    relativeTextRegion.X,
                    relativeTextRegion.Y,
                    relativeTextRegion.Width,
                    relativeTextRegion.Height);
                Mat optionFrame = new Mat(screenMat, bitmapRegion).Clone();
                Bitmap optionBitmap = optionFrame.ToBitmap();
                string optionHash = ImageProcessor.ComputeRobustHash(optionFrame);
                if (string.Equals(optionHash, _lastDialogueOptionHash, StringComparison.Ordinal))
                {
                    optionFrame.Dispose();
                    optionBitmap.Dispose();
                    return extra;
                }

                _lastDialogueOptionHash = optionHash;
                HoldDialogueOptionFrames(
                    optionBitmap,
                    optionFrame,
                    new System.Drawing.Point(
                        screen.Left + (int)Math.Round(relativeTextRegion.X * coordinateScaleX),
                        screen.Top + (int)Math.Round(relativeTextRegion.Y * coordinateScaleY)),
                    confidence,
                    coordinateScaleX,
                    coordinateScaleY);
                return extra == ExtraPathSample.None
                    ? ExtraPathSample.DialogueOptionsReady()
                    : extra.WithDialogueOptionsReady();
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"Dialogue option scan failed: {ex.Message}");
                return extra;
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
            double coordinateScaleX,
            double coordinateScaleY,
            double templateConfidence)
        {
            _isOcrRunning = true;
            string ocrGame = _overlaySession.AppliedGame;
            bool miss = true;
            string ocrText = null;
            try
            {
                OCRResult result = await Task.Run(() => data.engine.DetectTextFromMat(frame));
                var candidates = new List<DialogueOptionCandidate>();
                IEnumerable<PaddleOCRSharp.TextBlock> blocks = result?.TextBlocks ??
                    Enumerable.Empty<PaddleOCRSharp.TextBlock>();
                foreach (PaddleOCRSharp.TextBlock block in blocks
                    .Where(block => !string.IsNullOrWhiteSpace(block.Text) && block.Score >= 0.45f))
                {
                    float minX = block.BoxPoints.Min(point => point.X);
                    float minY = block.BoxPoints.Min(point => point.Y);
                    float maxX = block.BoxPoints.Max(point => point.X);
                    float maxY = block.BoxPoints.Max(point => point.Y);
                    var bounds = System.Drawing.Rectangle.FromLTRB(
                        absoluteOrigin.X + (int)Math.Floor(minX * coordinateScaleX),
                        absoluteOrigin.Y + (int)Math.Floor(minY * coordinateScaleY),
                        absoluteOrigin.X + (int)Math.Ceiling(maxX * coordinateScaleX),
                        absoluteOrigin.Y + (int)Math.Ceiling(maxY * coordinateScaleY));
                    bounds.Inflate(
                        (int)Math.Round(24 * coordinateScaleX),
                        (int)Math.Round(14 * coordinateScaleY));
                    candidates.Add(new DialogueOptionCandidate(block.Text.Trim(), bounds, block.Score));
                }

                _lastDialogueOptions = candidates
                    .OrderBy(candidate => candidate.Bounds.Top)
                    .ThenBy(candidate => candidate.Bounds.Left)
                    .ToList();
                miss = candidates.Count == 0;
                if (miss)
                {
                    // Retry unchanged frames when OCR temporarily returns no usable text.
                    _lastDialogueOptionHash = null;
                }
                Logger.Log.Debug(
                    $"Dialogue options detected: count={candidates.Count}, templateConfidence={templateConfidence:F3}");
                ocrText = string.Join(" / ", candidates.Select(candidate => candidate.Text));
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
                if (string.Equals(ocrGame, _overlaySession.AppliedGame, StringComparison.Ordinal))
                {
                    _overlaySession.CompleteOcr(miss, ocrText: miss ? null : ocrText);
                }
                else
                {
                    _lastDialogueOptionHash = null;
                    _lastDialogueOptions = new List<DialogueOptionCandidate>();
                }

                _ = Dispatcher.BeginInvoke(new Action(TryStartBusyOcr));
            }
        }

        private void HoldDialogueOptionFrames(
            Bitmap bitmap,
            Mat mat,
            System.Drawing.Point origin,
            double confidence,
            double coordinateScaleX,
            double coordinateScaleY)
        {
            DisposeDialogueOptionHold();
            _dialogueOptionBitmap = bitmap;
            _dialogueOptionMat = mat;
            _dialogueOptionOrigin = origin;
            _dialogueOptionConfidence = confidence;
            _dialogueOptionScaleX = coordinateScaleX;
            _dialogueOptionScaleY = coordinateScaleY;
        }

        private void DisposeDialogueOptionHold()
        {
            _dialogueOptionBitmap?.Dispose();
            _dialogueOptionMat?.Dispose();
            _dialogueOptionBitmap = null;
            _dialogueOptionMat = null;
            _dialogueOptionScaleX = 1.0;
            _dialogueOptionScaleY = 1.0;
        }

        private string TryTakeDialogueChoice()
        {
            if (_lastDialogueOptions.Count == 0)
            {
                _lastDialogueOptionHash = null;
                _dialogueOptionMissCount = 0;
                DisposeDialogueOptionHold();
                return null;
            }

            _dialogueOptionMissCount++;
            if (_dialogueOptionMissCount < 2)
            {
                return null;
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
            DisposeDialogueOptionHold();

            if (selected == null)
            {
                return null;
            }

            Logger.Log.Debug($"Selected dialogue option: {selected.Text}");
            MatchResult match = data.Matcher.FindMatchWithHeaderSeparated(selected.Text, out string key);
            _pendingExtraPathVoiceKey = key;
            return string.IsNullOrWhiteSpace(match.Content)
                ? selected.Text
                : match.Content.Trim();
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

        /// <summary>
        /// Captures and downsamples directly through GDI. This avoids allocating a full
        /// 6K/8K bitmap merely to shrink it on the UI thread. The legacy capture path is
        /// retained as a compatibility fallback for unusual display drivers.
        /// </summary>
        private static Bitmap CaptureRectangleScaled(
            System.Drawing.Rectangle bounds,
            int maxSide)
        {
            int longSide = Math.Max(bounds.Width, bounds.Height);
            if (longSide <= maxSide)
            {
                return CaptureRectangle(bounds);
            }

            double scale = maxSide / (double)longSide;
            int targetWidth = Math.Max(1, (int)Math.Round(bounds.Width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(bounds.Height * scale));
            var bitmap = new Bitmap(
                targetWidth,
                targetHeight,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            Graphics graphics = null;
            IntPtr sourceDc = IntPtr.Zero;
            IntPtr destinationDc = IntPtr.Zero;
            Exception captureFailure = null;
            try
            {
                graphics = Graphics.FromImage(bitmap);
                sourceDc = GetDC(IntPtr.Zero);
                if (sourceDc == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to acquire the desktop device context.");
                }

                destinationDc = graphics.GetHdc();
                SetStretchBltMode(destinationDc, HalftoneStretchMode);
                SetBrushOrgEx(destinationDc, 0, 0, IntPtr.Zero);
                if (!StretchBlt(
                        destinationDc,
                        0,
                        0,
                        targetWidth,
                        targetHeight,
                        sourceDc,
                        bounds.Left,
                        bounds.Top,
                        bounds.Width,
                        bounds.Height,
                        SourceCopyRasterOperation))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to capture the scaled screen region.");
                }
            }
            catch (Exception ex)
            {
                captureFailure = ex;
            }
            finally
            {
                if (destinationDc != IntPtr.Zero)
                {
                    graphics?.ReleaseHdc(destinationDc);
                }
                graphics?.Dispose();
                if (sourceDc != IntPtr.Zero)
                {
                    ReleaseDC(IntPtr.Zero, sourceDc);
                }
            }

            if (captureFailure == null)
            {
                return bitmap;
            }

            bitmap.Dispose();
            Logger.Log.Warn($"Scaled screen capture failed; falling back to CopyFromScreen: {captureFailure.Message}");
            return CaptureRectangle(bounds);
        }

        /// <summary>
        /// Caps real-time analysis frames before any grayscale, hash, template matching,
        /// or OCR preprocessing work. Ownership of <paramref name="source"/> transfers to
        /// this method; it is disposed when a resized frame is returned.
        /// </summary>
        private static Mat LimitFrameSize(Mat source, int maxSide)
        {
            if (source == null || source.Empty())
            {
                return source;
            }

            int longSide = Math.Max(source.Width, source.Height);
            if (longSide <= maxSide)
            {
                return source;
            }

            double scale = maxSide / (double)longSide;
            var resized = new Mat();
            try
            {
                Cv2.Resize(
                    source,
                    resized,
                    new OpenCvSharp.Size(),
                    scale,
                    scale,
                    InterpolationFlags.Area);
                return resized;
            }
            catch
            {
                resized.Dispose();
                throw;
            }
            finally
            {
                source.Dispose();
            }
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

        /// <summary>
        /// Preprocess the subtitle region image to binary image (only retain high-light/white pixels), used for stable pixel difference detection.
        /// </summary>
        /// <param name="src">Original Mat (BGR)</param>
        /// <returns>Binary Mat; if failed, return null</returns>
        private Mat PreprocessToBinary(Mat src)
        {
            if (src == null || src.Empty())
            {
                return null;
            }

            Mat gray = new Mat();
            Mat binary = new Mat();
            try
            {
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.Threshold(gray, binary, 220, 255, ThresholdTypes.Binary);
                return binary;
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"PreprocessToBinary failed: {ex}");
                binary?.Dispose();
                return null;
            }
            finally
            {
                gray?.Dispose();
            }
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
            _hintTimer.Stop();
            _hintChrome.Close();
            if (_escHotkeyRegistered)
            {
                UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyIdAdjustEsc);
                _escHotkeyRegistered = false;
            }
            DisposePairBuffers();
            notifyIcon.Dispose();
            notifyIcon = null;
            data.UnregisterAllHotkeys();
            data.RealClose();
        }

        private void ResetCaptureBuffersIfGameChanged()
        {
            if (string.Equals(_sampledGame, _overlaySession.AppliedGame, StringComparison.Ordinal))
            {
                return;
            }

            _sampledGame = _overlaySession.AppliedGame;
            CancelRegionDrag();
            DisposePairBuffers();
            DisposeDarkScreenHold();
            DisposeDialogueOptionHold();
            ResetDarkScreenCandidate();
            _lastDialogueOptionHash = null;
            _lastDialogueOptions = new List<DialogueOptionCandidate>();
        }

        private void CancelRegionDrag()
        {
            if (!_regionDragging)
            {
                return;
            }

            _regionDragging = false;
            _dragPairIndex = -1;
            _dragTarget = OverlayAdjustTarget.None;
            _dragIsCapture = false;
        }

        private void DisposePairBuffers()
        {
            for (int i = 0; i < _pairLastBinary.Count; i++)
            {
                _pairLastBinary[i]?.Dispose();
                _pairLastBinary[i] = null;
            }
            for (int i = 0; i < _pairLastOcrBinary.Count; i++)
            {
                _pairLastOcrBinary[i]?.Dispose();
                _pairLastOcrBinary[i] = null;
            }
            for (int i = 0; i < _pairCapturedBitmaps.Count; i++)
            {
                _pairCapturedBitmaps[i]?.Dispose();
                _pairCapturedBitmaps[i] = null;
            }
            for (int i = 0; i < _pairCapturedMats.Count; i++)
            {
                _pairCapturedMats[i]?.Dispose();
                _pairCapturedMats[i] = null;
            }
        }

        private void PreviewCaptureRegion()
        {
            _overlaySession.PreviewCaptureRegion(
                _overlaySession.HasValidCapture,
                _overlaySession.DarkScreenScanOn);
        }

        private void ShowActivityLog()
        {
            if (_activityLogWindow == null)
            {
                _activityLogWindow = new ActivityLogWindow(_overlaySession);
            }

            bool settingsOpen = data != null && data.IsVisible;
            _activityLogWindow.ShowOrFocus(settingsOpen);
        }

        private void OnHintChanged()
        {
            EnsureChromeTimer();
            ApplyHintChrome();
        }

        private void OnPreviewChanged()
        {
            EnsureChromeTimer();
            ApplyOutlines();
        }

        private void OnAdjustChanged()
        {
            if (_overlaySession.IsClickThrough)
            {
                CancelRegionDrag();
            }

            ApplyOverlayHitMode();
            UpdateAdjustEscHotkey();
            if (!_regionDragging)
            {
                ApplyOutlines();
            }
        }

        private void EnsureChromeTimer()
        {
            if (_overlaySession.HintVisible || _overlaySession.PreviewOutlines.Count > 0)
            {
                _hintTimer.Start();
            }
            else if (!_overlaySession.HintVisible)
            {
                _hintTimer.Stop();
            }
        }

        private void ApplyOutlineChromeIfChanged()
        {
            if (_regionDragging)
            {
                return;
            }

            if (_overlaySession.PreviewOutlines.Count == _lastPreviewCount &&
                _overlaySession.ArmedPairId == _lastArmedPairId &&
                _overlaySession.ArmedTarget == _lastArmedTarget)
            {
                return;
            }

            ApplyOutlines();
        }

        private void ApplyOutlines()
        {
            if (OverlayCanvas == null)
            {
                return;
            }

            ClearOutlineElements();
            foreach (RegionOutline outline in _overlaySession.PreviewOutlines)
            {
                AddOutlineElement(outline, takesMouse: false);
            }

            // Every armed frame is draggable: a pair's capture and display
            // outlines (whichever are valid), and an extra-path display.
            foreach (RegionOutline outline in _overlaySession.AdjustOutlines)
            {
                AddOutlineElement(outline, takesMouse: true);
            }

            _lastPreviewCount = _overlaySession.PreviewOutlines.Count;
            _lastArmedPairId = _overlaySession.ArmedPairId;
            _lastArmedTarget = _overlaySession.ArmedTarget;
        }

        private void ClearOutlineElements()
        {
            for (int i = 0; i < _outlineElements.Count; i++)
            {
                OverlayCanvas.Children.Remove(_outlineElements[i]);
            }

            _outlineElements.Clear();
        }

        private void AddOutlineElement(RegionOutline outline, bool takesMouse)
        {
            if (outline == null || outline.Rect == null || !outline.Rect.IsValid)
            {
                return;
            }

            OverlayRect rect = outline.Rect;
            System.Windows.Point canvasPoint = DisplayToCanvas(rect);
            double width = rect.Width / Scale;
            double height = rect.Height / Scale;
            SolidColorBrush stroke = BrushForOutline(outline);

            var box = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Stroke = stroke,
                StrokeThickness = 3,
                StrokeDashArray = outline.Dashed ? new DoubleCollection { 4, 3 } : null,
                Fill = takesMouse ? AdjustHitFill : null,
                IsHitTestVisible = takesMouse,
                Cursor = takesMouse ? System.Windows.Input.Cursors.SizeAll : System.Windows.Input.Cursors.Arrow,
                Tag = outline
            };
            Canvas.SetLeft(box, canvasPoint.X);
            Canvas.SetTop(box, canvasPoint.Y);
            System.Windows.Controls.Panel.SetZIndex(box, 40);
            OverlayCanvas.Children.Add(box);
            _outlineElements.Add(box);

            if (takesMouse)
            {
                box.MouseLeftButtonDown += RegionAdjust_MouseLeftButtonDown;
                box.MouseMove += RegionAdjust_MouseMove;
                box.MouseLeftButtonUp += RegionAdjust_MouseLeftButtonUp;
            }

            var label = new System.Windows.Controls.TextBlock
            {
                Text = FormatOutlineLabel(outline),
                Foreground = stroke,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, canvasPoint.X);
            Canvas.SetTop(label, canvasPoint.Y - 20);
            System.Windows.Controls.Panel.SetZIndex(label, 41);
            OverlayCanvas.Children.Add(label);
            _outlineElements.Add(label);
        }

        private static SolidColorBrush BrushForOutline(RegionOutline outline)
        {
            switch (outline.Kind)
            {
                case RegionOutlineKind.DarkScreenDisplay:
                case RegionOutlineKind.DarkScreenCandidate:
                    return DarkScreenOutlineBrush;
                case RegionOutlineKind.DialogueOptionDisplay:
                    return DialogueOptionOutlineBrush;
                default:
                    return outline.IsDisplay ? DisplayOutlineBrush : CaptureOutlineBrush;
            }
        }

        private string FormatOutlineLabel(RegionOutline outline)
        {
            switch (outline.Kind)
            {
                case RegionOutlineKind.DarkScreenDisplay:
                    return TryFindResource("Overlay_DarkScreenOutlineLabel") as string ?? "暗屏";
                case RegionOutlineKind.DialogueOptionDisplay:
                    return TryFindResource("Overlay_DialogueOptionOutlineLabel") as string ?? "选项";
                case RegionOutlineKind.DarkScreenCandidate:
                    return TryFindResource("Overlay_DarkScreenCandidateOutlineLabel") as string ?? "检测带";
                default:
                    return FormatPairOutlineLabel(outline.PairOrdinal);
            }
        }

        private string FormatPairOutlineLabel(int ordinal)
        {
            string format = TryFindResource("Overlay_PairOutlineLabel") as string;
            if (string.IsNullOrEmpty(format))
            {
                return "对 " + ordinal;
            }

            try
            {
                return string.Format(format, ordinal);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        private void RegionAdjust_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            bool clickThrough = _overlaySession.IsClickThrough;
            OverlayAdjustTarget target = _overlaySession.ArmedTarget;
            int pairIndex = -1;
            OverlayRect start = null;
            bool dragCapture = false;
            if (target == OverlayAdjustTarget.Pair)
            {
                pairIndex = _overlaySession.ArmedPairIndex;
                if (pairIndex >= 0)
                {
                    // Grabbing a box drags that box's own rectangle: the outline
                    // carried by the grabbed element says which of the pair's two
                    // frames was seized.
                    RegionOutline grabbed = (sender as System.Windows.Shapes.Rectangle)?.Tag as RegionOutline;
                    dragCapture = grabbed != null && !grabbed.IsDisplay;
                    start = dragCapture
                        ? _overlaySession.GetCapture(pairIndex)
                        : _overlaySession.GetDisplay(pairIndex);
                }
            }
            else if (target == OverlayAdjustTarget.DarkScreenDisplay)
            {
                start = _overlaySession.DarkScreenDisplay;
            }
            else if (target == OverlayAdjustTarget.DialogueOptionDisplay)
            {
                start = _overlaySession.DialogueOptionDisplay;
            }

            AdjustMouseExit exit = AdjustMouseGuard.DownExitReason(
                clickThrough,
                target,
                pairIndex,
                start != null && start.IsValid,
                sender is System.Windows.Shapes.Rectangle);
            RegionAdjustTrace.ElementDown(exit, target, pairIndex, start);
            if (exit != AdjustMouseExit.None)
            {
                return;
            }

            var box = (System.Windows.Shapes.Rectangle)sender;
            _regionDragging = true;
            _dragTarget = target;
            _dragPairIndex = pairIndex;
            _dragIsCapture = dragCapture;
            _dragStartRect = start;
            _dragStartMouse = e.GetPosition(OverlayCanvas);
            box.CaptureMouse();
            e.Handled = true;
        }

        private void RegionAdjust_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            AdjustMouseExit exit = AdjustMouseGuard.MoveExitReason(
                _regionDragging,
                e.LeftButton == MouseButtonState.Pressed,
                _dragTarget);
            if (exit != AdjustMouseExit.None)
            {
                return;
            }

            System.Windows.Point now = e.GetPosition(OverlayCanvas);
            double deltaX = now.X - _dragStartMouse.X;
            double deltaY = now.Y - _dragStartMouse.Y;
            var moved = new OverlayRect(
                (int)Math.Round(_dragStartRect.X + deltaX * Scale),
                (int)Math.Round(_dragStartRect.Y + deltaY * Scale),
                _dragStartRect.Width,
                _dragStartRect.Height);
            ApplyDraggedRegion(moved);

            var box = sender as System.Windows.Shapes.Rectangle;
            if (box != null)
            {
                System.Windows.Point canvasPoint = DisplayToCanvas(moved);
                Canvas.SetLeft(box, canvasPoint.X);
                Canvas.SetTop(box, canvasPoint.Y);
            }

            ApplyPairOverlay();
            e.Handled = true;
        }

        private void RegionAdjust_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            AdjustMouseExit exit = AdjustMouseGuard.UpExitReason(_regionDragging);
            RegionAdjustTrace.ElementUp(exit);
            if (exit != AdjustMouseExit.None)
            {
                return;
            }

            var box = sender as System.Windows.Shapes.Rectangle;
            box?.ReleaseMouseCapture();
            _regionDragging = false;
            _dragPairIndex = -1;
            _dragTarget = OverlayAdjustTarget.None;
            _dragIsCapture = false;
            ApplyOutlines();
            data?.RefreshPairPage();
            data?.RefreshExtraPathDisplayRows();
            e.Handled = true;
        }

        private void ApplyDraggedRegion(OverlayRect moved)
        {
            if (_dragTarget == OverlayAdjustTarget.Pair)
            {
                if (_dragIsCapture)
                {
                    // The next OCR beat reads Pairs and samples the new rectangle.
                    _overlaySession.SetCapture(_dragPairIndex, moved);
                }
                else
                {
                    _overlaySession.SetDisplay(_dragPairIndex, moved);
                }

                return;
            }

            if (_dragTarget == OverlayAdjustTarget.DarkScreenDisplay)
            {
                _overlaySession.SetDarkScreenDisplay(moved);
                return;
            }

            if (_dragTarget == OverlayAdjustTarget.DialogueOptionDisplay)
            {
                _overlaySession.SetDialogueOptionDisplay(moved);
            }
        }

        private void UpdateAdjustEscHotkey()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            bool shouldRegister = !_overlaySession.IsClickThrough;
            if (shouldRegister == _escHotkeyRegistered)
            {
                return;
            }

            if (shouldRegister)
            {
                _escHotkeyRegistered = RegisterHotKey(hwnd, HotkeyIdAdjustEsc, 0, VkEscape);
            }
            else
            {
                UnregisterHotKey(hwnd, HotkeyIdAdjustEsc);
                _escHotkeyRegistered = false;
            }
        }

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            return CreateFrozenBrush(255, r, g, b);
        }

        private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        private void ApplyHintChrome()
        {
            if (_overlaySession.HintVisible)
            {
                _hintChrome.Show(ResolveHintText(), ResolveHintScreen());
            }
            else
            {
                _hintChrome.Hide();
            }
        }

        private System.Windows.Rect ResolveHintScreen()
        {
            System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
            var screenRects = new List<OverlayRect>(screens.Length);
            int primaryIndex = 0;
            for (int i = 0; i < screens.Length; i++)
            {
                System.Drawing.Rectangle bounds = screens[i].Bounds;
                screenRects.Add(new OverlayRect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                if (screens[i].Primary)
                {
                    primaryIndex = i;
                }
            }

            OverlayRect target = HintScreenSelection.Select(
                screenRects,
                primaryIndex,
                _overlaySession.HintDisplayCandidates);
            if (!target.IsValid)
            {
                return System.Windows.Rect.Empty;
            }

            // Same mixed-DPI treatment as subtitle placement: physical px over system scale.
            return new System.Windows.Rect(
                target.X / Scale,
                target.Y / Scale,
                target.Width / Scale,
                target.Height / Scale);
        }

        private string ResolveHintText()
        {
            if (string.IsNullOrEmpty(_overlaySession.HintResourceKey))
            {
                return string.Empty;
            }

            string format = TryFindResource(_overlaySession.HintResourceKey) as string;
            if (string.IsNullOrEmpty(format))
            {
                return string.Empty;
            }

            object[] args = _overlaySession.HintFormatArguments;
            if (args == null || args.Length == 0)
            {
                return format;
            }

            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
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
            const int WM_ENABLE = 0x000A;
            // A modal dialog opened while armed re-disables every window on
            // the thread; the arm path cleared the bit once, this keeps it
            // cleared for the lifetime of armed mode.
            if (msg == WM_ENABLE && wParam == IntPtr.Zero && !_overlaySession.IsClickThrough)
            {
                ClearOverlayDisabledBit(hwnd);
            }

            if (msg == WM_HOTKEY)
            {
                if (wParam.ToInt32() == HOTKEY_ID_1)
                {
                    if (OCRTimer.IsEnabled)
                    {
                        _overlaySession.StopRecognition();
                        OCRTimer.Stop();
                        UITimer.Stop();
                        SwitchIcon("mask.ico");
                    }
                    else
                    {
                        _overlaySession.StartRecognition(_overlaySession.HasValidCapture);
                        if (_overlaySession.RecognitionRunning)
                        {
                            OCRTimer.Start();
                            UITimer.Start();
                            SwitchIcon("running.ico");
                        }
                    }
                    handled = true;
                }
                else if (wParam.ToInt32() == HOTKEY_ID_2)
                {
                    if (!ChooseRegion)
                    {
                        ChooseRegion = true;
                        bool selected = notify.ChooseRegion(out int pairId);
                        if (selected)
                        {
                            _overlaySession.CaptureRegionSelected(pairId);
                        }
                        else
                        {
                            _overlaySession.CaptureRegionSelectionCancelled();
                        }
                        ChooseRegion = false;
                    }
                }
                else if (wParam.ToInt32() == HOTKEY_ID_3)
                {
                    if (ShowText)
                    {
                        _overlaySession.HideSubtitles();
                    }
                    else
                    {
                        _overlaySession.ShowSubtitles();
                    }

                    ShowText = _overlaySession.SubtitlesVisible;
                    ApplyPairOverlay();
                }
                else if (wParam.ToInt32() == HOTKEY_ID_4)
                {
                    PreviewCaptureRegion();
                    handled = true;
                }
                else if (wParam.ToInt32() == HotkeyIdAdjustEsc)
                {
                    _overlaySession.CancelRegionAdjust();
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

        private VoiceAudioSource CreateVoiceAudioSource(string audioKey, bool logActivity = false)
        {
            string localFilePath = null;
            if (_overlaySession.AllowsGenshinLocalVoice)
            {
                _genshinVoiceFileResolver.TryResolve(audioKey, out localFilePath);
            }

            return new VoiceAudioSource
            {
                LocalFilePath = localFilePath,
                RemoteUrl = $"{server}?md5={audioKey}&token={token}",
                LogActivity = logActivity
            };
        }

        private void PlayDialogueOptionAudio(string audioKey, bool logActivity = false)
        {
            VoiceAudioSource source = CreateVoiceAudioSource(audioKey, logActivity);
            bool shouldStart;
            int generation;
            lock (_audioPlaybackQueueLock)
            {
                if (_audioPlaybackQueueActive)
                {
                    // Dialogue choices never interrupt current audio or form a backlog.
                    // Keep only the most recently selected choice.
                    _pendingDialogueOptionSource = source;
                    return;
                }

                _audioPlaybackQueue.Enqueue(source);
                shouldStart = !_audioPlaybackQueueActive;
                _audioPlaybackQueueActive = true;
                generation = _audioPlaybackGeneration;
            }

            if (shouldStart)
            {
                _ = ProcessNextAudioAsync(generation);
            }
        }

        private void PlayMainAudio(string audioKey, bool logActivity = false)
        {
            VoiceAudioSource source = CreateVoiceAudioSource(audioKey, logActivity);
            int generation;
            lock (_audioPlaybackQueueLock)
            {
                _audioPlaybackQueue.Clear();
                _pendingDialogueOptionSource = null;
                _audioPlaybackQueue.Enqueue(source);
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
                _pendingDialogueOptionSource = null;
                _audioPlaybackQueueActive = false;
                _audioPlaybackGeneration++;
            }

            DisposeCurrentAudioPlayback();
            NoteVoicePlaybackEndedOnUi();
        }

        private void NoteVoicePlaybackEndedOnUi()
        {
            if (Dispatcher.CheckAccess())
            {
                _overlaySession.NoteVoicePlaybackEnded();
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => _overlaySession.NoteVoicePlaybackEnded()));
        }

        private void StartAudioPlayback(
            string filePath,
            int generation,
            bool allowTempoProcessing = true,
            bool logActivity = false)
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
                            StartAudioPlayback(filePath, generation, allowTempoProcessing: false, logActivity: logActivity);
                            return;
                        }

                        DisposeCurrentAudioPlayback();
                        _ = ProcessNextAudioAsync(generation);
                    }));
                };
                waveOut.PlaybackStopped += _playbackStoppedHandler;
                waveOut.Init(playbackSource);
                waveOut.Play();
                if (logActivity)
                {
                    _overlaySession.NoteVoicePlaybackStarted();
                }
            }
            catch (Exception ex) when (usingSoundTouch)
            {
                Logger.Log.Warn(
                    $"SoundTouch initialization failed; retrying at normal speed: {ex.Message}");
                DisposeCurrentAudioPlayback();
                StartAudioPlayback(filePath, generation, allowTempoProcessing: false, logActivity: logActivity);
            }
        }

        private async Task ProcessNextAudioAsync(int generation)
        {
            while (true)
            {
                VoiceAudioSource source = null;
                lock (_audioPlaybackQueueLock)
                {
                    if (generation != _audioPlaybackGeneration)
                    {
                        return;
                    }

                    if (_audioPlaybackQueue.Count == 0 &&
                        _pendingDialogueOptionSource != null)
                    {
                        _audioPlaybackQueue.Enqueue(_pendingDialogueOptionSource);
                        _pendingDialogueOptionSource = null;
                    }

                    if (_audioPlaybackQueue.Count == 0)
                    {
                        _audioPlaybackQueueActive = false;
                    }
                    else
                    {
                        source = _audioPlaybackQueue.Dequeue();
                    }
                }

                if (source == null)
                {
                    NoteVoicePlaybackEndedOnUi();
                    return;
                }

                if (!string.IsNullOrEmpty(source.LocalFilePath) &&
                    File.Exists(source.LocalFilePath))
                {
                    if (IsAudioTempFile(source.LocalFilePath))
                    {
                        Logger.Log.Debug($"Playing local voice file: {source.LocalFilePath}");
                        await Dispatcher.InvokeAsync(() =>
                        {
                            lock (_audioPlaybackQueueLock)
                            {
                                if (generation != _audioPlaybackGeneration) return;
                            }

                            tempFilePath = source.LocalFilePath;
                            StartAudioPlayback(source.LocalFilePath, generation, logActivity: source.LogActivity);
                        });
                        return;
                    }

                    Logger.Log.Warn(
                        $"Local voice file has an unsupported format; falling back to server: " +
                        source.LocalFilePath);
                }

                string tempFile = Path.GetTempFileName();
                try
                {
                    using (var webClient = new WebClient())
                    {
                        webClient.Headers[HttpRequestHeader.UserAgent] = "GI-Subtitles/1.0";
                        await webClient.DownloadFileTaskAsync(new Uri(source.RemoteUrl), tempFile);
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
                        StartAudioPlayback(tempFile, generation, logActivity: source.LogActivity);
                    });
                    return;
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse response &&
                                              response.StatusCode == HttpStatusCode.NotFound)
                {
                    Logger.Log.Debug($"Audio not found: {source.RemoteUrl}");
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

            _overlaySession.ChangeVoiceSpeed(_voicePlaybackSpeed);
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
            PlayMainAudio(testAudioMd5);
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
            var updateWindow = new UpdateWindow(manifest)
            {
                Owner = this
            };
            updateWindow.ShowDialog();

            if (updateWindow.IgnoreRequested)
            {
                Config.Set("IgnoredUpdateVersion", manifest.Version);
                notify.HideAvailableUpdate();
                availableUpdate = null;
                return;
            }

            if (!updateWindow.InstallRequested)
            {
                return;
            }

            string msi = null;
            try
            {
                var updateFolder = GetUpdateFolder();
                Directory.CreateDirectory(updateFolder);
                var safeVersion = string.Join(
                    "_", (manifest.Version ?? "update").Split(Path.GetInvalidFileNameChars()));
                msi = Path.Combine(updateFolder, $"GI-Subtitles-{safeVersion}.msi");
                notify.ShowUpdateStatus(
                    "Tray_UpdateStarting", "Downloading version {0}: 0%", manifest.Version);
                Action<int> progress = percentage =>
                    notify.ShowUpdateStatus(
                        "Tray_UpdateDownloading", "Downloading version {0}: {1}%",
                        manifest.Version, percentage);
                await DownloadUpdateAsync(new Uri(asset.Url), msi, asset.Size, progress);

                notify.ShowUpdateStatus(
                    "Tray_UpdateVerifying", "Version {0} downloaded; verifying", manifest.Version);
                var downloaded = new FileInfo(msi);
                var actualSha256 = GetSha256(msi);
                if (downloaded.Length != asset.Size || !string.Equals(
                    actualSha256, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log.Error(
                        $"Update verification failed. File: {msi}; " +
                        $"size: {downloaded.Length}/{asset.Size}; " +
                        $"SHA256: {actualSha256}/{asset.Sha256}");
                    File.Delete(msi);
                    throw new InvalidDataException("The downloaded installer did not match the release manifest.");
                }

                Logger.Log.Info($"Update package verified successfully. File: {msi}; SHA256: {actualSha256}");
                CleanupOldUpdatePackages(msi);
                notify.ShowUpdateStatus(
                    "Tray_UpdateInstalling", "Version {0} verified; preparing installation",
                    manifest.Version);
                StartUpdateInstallerCoordinator(msi);
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                notify.RestoreAvailableUpdate();
                Logger.Log.Error($"Failed to download or start application update. File: {msi ?? "(not created)"}; {ex}");
                System.Windows.Forms.MessageBox.Show(
                    GetLocalizedText("Update_Error", "The update could not be downloaded or verified. Please try again later."),
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static async Task DownloadUpdateAsync(
            Uri uri,
            string destination,
            long expectedSize,
            Action<int> progress)
        {
            Logger.Log.Info(
                $"Starting update download. URL: {uri}; target: {destination}; " +
                $"expected size: {expectedSize} bytes");

            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var responseSize = response.Content.Headers.ContentLength;
                var totalSize = responseSize.GetValueOrDefault(expectedSize);
                if (responseSize.HasValue && responseSize.Value != expectedSize)
                {
                    Logger.Log.Warn(
                        $"Update server content length differs from manifest: " +
                        $"{responseSize.Value}/{expectedSize} bytes. Target: {destination}");
                }

                using (var source = await response.Content.ReadAsStreamAsync())
                using (var target = new FileStream(
                    destination, FileMode.Create, FileAccess.Write, FileShare.None,
                    81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    long downloaded = 0;
                    var nextProgress = 10;
                    int bytesRead;
                    while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await target.WriteAsync(buffer, 0, bytesRead);
                        downloaded += bytesRead;

                        if (totalSize > 0)
                        {
                            var percentage = (int)Math.Min(100, downloaded * 100 / totalSize);
                            while (percentage >= nextProgress && nextProgress <= 100)
                            {
                                progress?.Invoke(nextProgress);
                                Logger.Log.Info(
                                    $"Update download progress: {nextProgress}% " +
                                    $"({downloaded}/{totalSize} bytes). Target: {destination}");
                                nextProgress += 10;
                            }
                        }
                    }

                    await target.FlushAsync();
                    Logger.Log.Info(
                        $"Update download completed. Target: {destination}; " +
                        $"downloaded: {downloaded} bytes");
                }
            }
        }

        private static string GetUpdateFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GI-Subtitles",
                "Updates");
        }

        private static void CleanupOldUpdatePackages(string preferredPackage = null, int maximumPackages = 2)
        {
            if (maximumPackages < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumPackages));
            }

            var updateFolder = GetUpdateFolder();
            if (!Directory.Exists(updateFolder))
            {
                return;
            }

            try
            {
                var packages = new DirectoryInfo(updateFolder)
                    .EnumerateFiles("GI-Subtitles-*.msi", SearchOption.TopDirectoryOnly)
                    .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ToList();
                if (packages.Count <= maximumPackages)
                {
                    return;
                }

                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                AddPackageToKeep(keep, packages, preferredPackage);

                var installedVersion = Assembly.GetExecutingAssembly().GetName().Version;
                var installedPackage = packages.FirstOrDefault(file =>
                    IsPackageForVersion(file, installedVersion));
                AddPackageToKeep(keep, packages, installedPackage?.FullName);

                foreach (var package in packages)
                {
                    if (keep.Count >= maximumPackages)
                    {
                        break;
                    }

                    keep.Add(package.FullName);
                }

                foreach (var package in packages.Where(file => !keep.Contains(file.FullName)))
                {
                    try
                    {
                        package.Delete();
                        Logger.Log.Info($"Removed old update package: {package.FullName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Warn($"Failed to remove old update package {package.FullName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"Failed to clean the update package folder {updateFolder}: {ex.Message}");
            }
        }

        private static void AddPackageToKeep(
            HashSet<string> keep,
            IEnumerable<FileInfo> packages,
            string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                return;
            }

            var fullPath = Path.GetFullPath(packagePath);
            var package = packages.FirstOrDefault(file => string.Equals(
                file.FullName, fullPath, StringComparison.OrdinalIgnoreCase));
            if (package != null)
            {
                keep.Add(package.FullName);
            }
        }

        private static bool IsPackageForVersion(FileInfo package, Version versionToMatch)
        {
            const string prefix = "GI-Subtitles-";
            var name = Path.GetFileNameWithoutExtension(package.Name);
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var packageVersionText = name.Substring(prefix.Length);
            var suffix = packageVersionText.IndexOf('-');
            if (suffix >= 0)
            {
                packageVersionText = packageVersionText.Substring(0, suffix);
            }

            return Version.TryParse(packageVersionText, out var packageVersion) &&
                packageVersion.Major == versionToMatch.Major &&
                packageVersion.Minor == versionToMatch.Minor &&
                packageVersion.Build == versionToMatch.Build;
        }

        private static void StartUpdateInstallerCoordinator(string msi)
        {
            var applicationPath = Assembly.GetExecutingAssembly().Location;
            var logFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GI-Subtitles");
            Directory.CreateDirectory(logFolder);
            var applicationLog = Path.Combine(logFolder, "app.log");
            var msiLog = Path.Combine(logFolder, "update-msi.log");
            var currentProcessId = Process.GetCurrentProcess().Id;

            var script = BuildUpdateCoordinatorScript(
                msi, applicationPath, applicationLog, msiLog, currentProcessId);
            var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encodedScript,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var coordinator = Process.Start(startInfo);
            if (coordinator == null)
            {
                throw new InvalidOperationException("The update installer coordinator could not be started.");
            }

            Logger.Log.Info(
                $"Update installer coordinator started. PID: {coordinator.Id}; MSI: {msi}; " +
                $"MSI log: {msiLog}; restart target: {applicationPath}");
        }

        private static string BuildUpdateCoordinatorScript(
            string msi,
            string applicationPath,
            string applicationLog,
            string msiLog,
            int currentProcessId)
        {
            var script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            script.AppendLine("$msiPath = " + ToPowerShellLiteral(msi));
            script.AppendLine("$applicationPath = " + ToPowerShellLiteral(applicationPath));
            script.AppendLine("$applicationLog = " + ToPowerShellLiteral(applicationLog));
            script.AppendLine("$msiLog = " + ToPowerShellLiteral(msiLog));
            script.AppendLine("$oldProcessId = " + currentProcessId);
            script.AppendLine("function Write-UpdaterLog([string]$message) {");
            script.AppendLine("    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss,fff'");
            script.AppendLine("    Add-Content -LiteralPath $applicationLog -Encoding UTF8 -Value (('[INFO ] Time: {0} Content: Updater: {1}' -f $timestamp, $message))");
            script.AppendLine("}");
            script.AppendLine("try {");
            script.AppendLine("    Wait-Process -Id $oldProcessId -ErrorAction SilentlyContinue");
            script.AppendLine("    Write-UpdaterLog ('Application process {0} exited; starting update.' -f $oldProcessId)");
            script.AppendLine("    $msiArguments = '/i \"' + $msiPath + '\" /quiet /norestart /L*v \"' + $msiLog + '\"'");
            script.AppendLine("    Write-UpdaterLog ('Starting installer. MSI: {0}; MSI log: {1}' -f $msiPath, $msiLog)");
            script.AppendLine("    $installer = Start-Process -FilePath 'msiexec.exe' -Verb RunAs -ArgumentList $msiArguments -Wait -PassThru");
            script.AppendLine("    Write-UpdaterLog ('Installer exited with code {0}.' -f $installer.ExitCode)");
            script.AppendLine("    if (@(0, 1641, 3010) -notcontains $installer.ExitCode) { throw ('Installer failed with exit code {0}.' -f $installer.ExitCode) }");
            script.AppendLine("    if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) { throw ('Installed application not found: {0}' -f $applicationPath) }");
            script.AppendLine("    Start-Sleep -Milliseconds 500");
            script.AppendLine("    Write-UpdaterLog ('Restarting application: {0}. Installer source retained at: {1}' -f $applicationPath, $msiPath)");
            script.AppendLine("    Start-Process -FilePath $applicationPath -WorkingDirectory (Split-Path -Parent $applicationPath)");
            script.AppendLine("}");
            script.AppendLine("catch {");
            script.AppendLine("    Write-UpdaterLog ('Update failed: {0}. Package retained at: {1}; MSI log: {2}' -f $_.Exception.Message, $msiPath, $msiLog)");
            script.AppendLine("    if (Test-Path -LiteralPath $applicationPath -PathType Leaf) { Start-Process -FilePath $applicationPath -WorkingDirectory (Split-Path -Parent $applicationPath) }");
            script.AppendLine("}");
            return script.ToString();
        }

        private static string ToPowerShellLiteral(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
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
