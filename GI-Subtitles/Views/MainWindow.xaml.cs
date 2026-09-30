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
        private DateTime _lastAutoRegionSearchStartedUtc = DateTime.MinValue;
        private DateTime _lastOcrStartedUtc = DateTime.MinValue;
        private DateTime _lastSamplingStartedUtc = DateTime.MinValue;
        private bool _autoRegionOcrInFlight;
        private bool _autoRegionDetectionLocked;
        private bool _matcherNotReadyLogged;
        private long _matcherNotReadySinceTicks;
        private int _autoRegionCcMissesSinceBroadScan;
        private string _autoRegionRepairReason = "startup-validation";
        private string _lastAutoRegionRepairEvidenceSignature;
        private long _ocrDiagnosticSequence;
        private long _samplingDiagnosticSequence;
        private long _lastUpdateTextTickTimestamp;
        private int _samplingTicksSkippedMenu;
        private int _samplingTicksSkippedOcrBusy;
        private int _samplingTicksSkippedSamplerBusy;
        private const int DebugSamplingOverlayEntryLimit = 8;
        private const int DebugSamplingMaxLineCells = 46;
        private const long DebugOverlayMaxFilterPixels = 220000;
        private const long DebugOverlayMaxFastFilterPixels = 400000;
        private const double DebugSamplingHorizontalMargin = 8;
        private const double DebugSamplingBottomMargin = 5;
        private const double DebugSamplingPanelWidth = 350;
        private static int _debugOverlayFilteringDisabled;
        private static int _debugOverlayFilterWarningLogged;
        private static int _debugOverlayFilterSlowWarningLogged;
        private readonly List<string> _debugSamplingOverlayEntries = new List<string>();
        private readonly Dictionary<DispatcherOperation, long> _dispatcherOperationStartTicks =
            new Dictionary<DispatcherOperation, long>();
        private bool _autoRegionRepairPending;
        private readonly object _extraPathSync = new object();
        private readonly double ChangeThreshold = Math.Max(0, Math.Min(1, Config.Get<double>("OCRThreshold", 0.01)));
        private readonly LiveOverlaySession _overlaySession = new LiveOverlaySession(
            new ConfigOcrIntervalStore(),
            new ConfigRegionPairStore(),
            utcNow: null,
            appliedGame: Config.Get("Game", "Genshin"),
            idleTimeoutStore: new ConfigSubtitleIdleTimeoutStore());
        private readonly List<Mat> _pairLastBinary = new List<Mat>();
        private readonly List<Mat> _pairLastOcrBinary = new List<Mat>();
        private readonly List<Mat> _pairPendingOcrBinary = new List<Mat>();
        private readonly List<Bitmap> _pairCapturedBitmaps = new List<Bitmap>();
        private readonly List<Mat> _pairCapturedMats = new List<Mat>();
        private readonly List<PairOcrDiagnosticContext> _pairOcrDiagnosticContexts =
            new List<PairOcrDiagnosticContext>();
        private readonly List<System.Windows.Controls.TextBlock> _extraPairBodies = new List<System.Windows.Controls.TextBlock>();
        private readonly OverlayHintChrome _hintChrome = new OverlayHintChrome();
        private readonly DispatcherTimer _hintTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        private readonly DispatcherTimer _dragHandleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        private readonly DispatcherTimer _debugSamplingStatsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        private readonly object _debugCaptureMaskSync = new object();
        private System.Drawing.Rectangle[] _debugCaptureMaskBounds = new System.Drawing.Rectangle[0];
        private System.Drawing.Rectangle _debugCapturePanelBounds = System.Drawing.Rectangle.Empty;
        private bool _debugCaptureMaskUnavailable;
        private Process _debugSamplingProcess;
        private TimeSpan _lastDebugSamplingProcessCpuTime;
        private long _lastDebugSamplingCpuTimestamp;
        private readonly List<UIElement> _outlineElements = new List<UIElement>();
        private bool _regionDragging;
        private bool _dragHandleInteractive;
        private bool _debugControlInteractive;
        private bool _debugSamplingMinimized;
        private bool _dragHandleDragging;
        private bool _dragHandleFinishing;
        private int _dragHandlePairIndex = -1;
        private OverlayRect _dragHandleStartRect = OverlayRect.Invalid;
        private OverlayRect _dragHandlePreviewRect = OverlayRect.Invalid;
        private int _dragHandleContentBottomOffset;
        private System.Windows.Point _dragHandleStartMouseScreen;
        private readonly TranslateTransform _dragHandleRenderTransform = new TranslateTransform();
        private readonly TranslateTransform _subtitleDragRenderTransform = new TranslateTransform();
        private readonly TranslateTransform _headerDragRenderTransform = new TranslateTransform();
        private readonly TranslateTransform _headerPositionRenderTransform = new TranslateTransform(0, -20);
        private readonly TransformGroup _headerRenderTransformGroup = new TransformGroup();
        private OverlayRect _dragStartRect = OverlayRect.Invalid;
        private System.Windows.Point _dragStartMouseScreen;
        private RegionResizeEdges _dragResizeEdges;
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
        private bool _debugSamplingOverlayEnabled = Config.Get("DebugSamplingOverlayEnabled", false);
        private const int DarkScreenAnalysisMaxSide = 960;
        private const int DialogueOptionAnalysisMaxSide = 1920;
        private const int PreviewOutlineBoxZIndex = int.MaxValue - 2;
        private const int PreviewOutlineLabelZIndex = int.MaxValue - 1;

        [Flags]
        private enum RegionResizeEdges
        {
            None = 0,
            Left = 1,
            Top = 2,
            Right = 4,
            Bottom = 8
        }

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

        [DllImport("ntdll.dll")]
        private static extern int RtlGetVersion(ref NativeOsVersionInfo versionInfo);

        private const uint WdaExcludeFromCapture = 0x00000011;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeOsVersionInfo
        {
            public int Size;
            public int MajorVersion;
            public int MinorVersion;
            public int BuildNumber;
            public int PlatformId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string ServicePack;
        }

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
        // Subtitle-filtered and generic region OCR can produce different text for the same frame.
        private readonly LRUCache<string, string> RegionBitmapDict = new LRUCache<string, string>(30);
        private readonly LRUCache<string, bool> AudioList = new LRUCache<string, bool>(4096);
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
        private static readonly double[] VoicePlaybackSpeeds = { 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 };
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
            public Action<bool, string> Completion { get; set; }
            public string RequestLabel { get; set; }
            public string AudioKey { get; set; }
        }

        private sealed class SubtitleTextCandidate
        {
            public string Text { get; set; }
            public System.Drawing.Rectangle Bounds { get; set; }
            public float Score { get; set; }
            public int LineCount { get; set; }
        }

        private sealed class AutoRegionScanResult
        {
            public OCRResult OcrResult { get; set; }
            public System.Drawing.Rectangle OcrBounds { get; set; }
            public System.Drawing.Rectangle? VisualCandidateBounds { get; set; }
            public double CandidateDetectionMs { get; set; }
            public double OcrMs { get; set; }
            public double WorkerMs { get; set; }
            public string ScanMode { get; set; }
            public string TriggerReason { get; set; }
        }

        private sealed class PairSamplingRequest
        {
            public int PairIndex { get; set; }
            public bool IsPrimary { get; set; }
            public OverlayRect Capture { get; set; }
            public Mat PreviousBinary { get; set; }
            public Mat PreviousOcrBinary { get; set; }
        }

        private sealed class SamplingSetupTiming
        {
            public double ResetGameStateMs { get; set; }
            public double PairSnapshotMs { get; set; }
            public double BufferSetupMs { get; set; }
            public double PreviousFrameCloneMs { get; set; }
            public double TotalMs { get; set; }
            public int SkippedMenuTicks { get; set; }
            public int SkippedOcrBusyTicks { get; set; }
            public int SkippedSamplerBusyTicks { get; set; }
            public bool HasVisibleSubtitle { get; set; }
        }

        private sealed class PairSamplingResult
        {
            public int PairIndex { get; set; }
            public Bitmap Bitmap { get; set; }
            public Mat Frame { get; set; }
            public Mat Binary { get; set; }
            public PairFrameSample Sample { get; set; }
            public string Decision { get; set; }
            public bool Empty { get; set; }
            public double CaptureMs { get; set; }
            public double ChangeRatioFromPrevious { get; set; }
            public double ChangeRatioFromLastOcr { get; set; }
        }

        private sealed class PairOcrDiagnosticContext
        {
            public long FirstPixelChangeTicks { get; set; }
            public long FirstPixelChangeSampleId { get; set; }
            public long QualifiedTicks { get; set; }
            public long QualifiedSampleId { get; set; }

            public void Reset()
            {
                FirstPixelChangeTicks = 0;
                FirstPixelChangeSampleId = 0;
                QualifiedTicks = 0;
                QualifiedSampleId = 0;
            }
        }

        private sealed class SamplingBatchResult
        {
            public ExtraPathSample Extra { get; set; }
            public List<PairSamplingResult> Pairs { get; set; } = new List<PairSamplingResult>();
            public List<string> PairCaptureTimings { get; set; } = new List<string>();
            public double QueueMs { get; set; }
            public double ExtraPathMs { get; set; }
            public double CaptureMs { get; set; }
            public double PrimaryCaptureMs { get; set; }
            public double SecondaryCaptureMs { get; set; }
            public double BitmapToMatMs { get; set; }
            public double PreprocessMs { get; set; }
            public double CompareMs { get; set; }
            public double ExtraCaptureMs { get; set; }
            public double ExtraBitmapToMatMs { get; set; }
            public double ExtraMatToBitmapMs { get; set; }
            public double ExtraPreprocessMs { get; set; }
            public double ExtraDetectionMs { get; set; }
            public double DarkScreenCaptureMs { get; set; }
            public double DarkScreenBitmapToMatMs { get; set; }
            public double DarkScreenPreprocessMs { get; set; }
            public double DarkScreenDetectionMs { get; set; }
            public double DialogueCaptureMs { get; set; }
            public double DialogueBitmapToMatMs { get; set; }
            public double DialoguePreprocessMs { get; set; }
            public double DialogueDetectionMs { get; set; }
            public string DarkScreenScanState { get; set; }
            public string DialogueScanState { get; set; }
            public double DarkScreenRatio { get; set; }
            public double DarkScreenBrightRatio { get; set; }
            public double DialogueConfidence { get; set; }
            public double WorkerMs { get; set; }
            public string ExtraPathState { get; set; }
            public List<string> PairFrameDecisions { get; set; } = new List<string>();
            public List<string> PairFrameStates { get; set; } = new List<string>();
        }

        private sealed class AutoRegionCaptureResult
        {
            public Bitmap Bitmap { get; set; }
            public Mat Frame { get; set; }
            public double QueueMs { get; set; }
            public double CaptureMs { get; set; }
            public double BitmapToMatMs { get; set; }
            public double WorkerMs { get; set; }
        }

        private sealed class ForceRefreshCaptureResult
        {
            public Bitmap Bitmap { get; set; }
            public Mat Frame { get; set; }
            public Mat Binary { get; set; }
            public double CaptureMs { get; set; }
            public double BitmapToMatMs { get; set; }
            public double PreprocessMs { get; set; }
        }


        public MainWindow()
        {
            Logger.Log.Debug("Start App");
            if (debug)
            {
                Logger.Log.Info("[Diagnostics] enabled=true, screenshotSaving=disabled");
            }
            _genshinVoiceFileResolver = new LocalVoiceFileResolver(dataDir, "Genshin");
            Task.Run(() => CleanupOldAudioTempFiles());
            InitializeComponent();
            DragButton.RenderTransform = _dragHandleRenderTransform;
            SubtitleText.RenderTransform = _subtitleDragRenderTransform;
            _headerRenderTransformGroup.Children.Add(_headerPositionRenderTransform);
            _headerRenderTransformGroup.Children.Add(_headerDragRenderTransform);
            HeaderPanel.RenderTransform = _headerRenderTransformGroup;
            HeaderPanel.SizeChanged += HeaderPanel_SizeChanged;
            OverlayCanvas.SizeChanged += (s, e) =>
            {
                if (DebugSamplingMinimizeButton.Visibility == Visibility.Visible)
                {
                    PositionDebugSamplingControls();
                    UpdateDebugCaptureMaskBounds();
                }
            };
            if (debug)
            {
                Dispatcher.Hooks.OperationStarted += OnDispatcherOperationStarted;
                Dispatcher.Hooks.OperationCompleted += OnDispatcherOperationCompleted;
                Dispatcher.Hooks.OperationAborted += OnDispatcherOperationAborted;
            }
            _forceRefreshDebounceTimer.Tick += (sender, args) =>
            {
                _forceRefreshDebounceTimer.Stop();
                ForceRefreshCurrentSubtitle();
            };
            UpdatePlaybackSpeedIndicator();
            _dragHandleTimer.Tick += (sender, args) =>
            {
                UpdateDragHandle();
                UpdateDebugControlInteraction();
            };
            _debugSamplingStatsTimer.Tick += (sender, args) => UpdateDebugSamplingResourceUsage();
            DragButton.PreviewMouseLeftButtonDown += DragHandle_MouseLeftButtonDown;
            DragButton.PreviewMouseMove += DragHandle_MouseMove;
            DragButton.PreviewMouseLeftButtonUp += DragHandle_MouseLeftButtonUp;
            DragButton.LostMouseCapture += DragHandle_LostMouseCapture;
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
            SourceInitialized += (s, e) =>
            {
                ApplyOverlayClickThrough();
                ExcludeOverlayFromCapture();
            };
        }

        public event EventHandler DebugSamplingOverlayEnabledChanged;

        public bool DebugSamplingOverlayEnabled
        {
            get { return _debugSamplingOverlayEnabled; }
        }

        private bool IsDebugSamplingOverlayActive
        {
            get { return _debugSamplingOverlayEnabled && _overlaySession.RecognitionRunning; }
        }

        private bool ShouldCollectSamplingDiagnostics
        {
            get { return debug || IsDebugSamplingOverlayActive; }
        }

        public void SetDebugSamplingOverlayEnabled(bool enabled)
        {
            if (_debugSamplingOverlayEnabled == enabled)
            {
                UpdateDebugSamplingOverlayVisibility();
                return;
            }

            _debugSamplingOverlayEnabled = enabled;
            Config.Set("DebugSamplingOverlayEnabled", enabled);
            if (!enabled)
            {
                _debugSamplingOverlayEntries.Clear();
                _debugSamplingMinimized = false;
            }

            UpdateDebugSamplingOverlayVisibility();
            DebugSamplingOverlayEnabledChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DebugSamplingCloseButton_Click(object sender, RoutedEventArgs e)
        {
            SetDebugSamplingOverlayEnabled(false);
        }

        private void DebugSamplingMinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            _debugSamplingMinimized = !_debugSamplingMinimized;
            UpdateDebugSamplingOverlayVisibility();
        }

        private void DebugSamplingSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            data?.OpenOtherSettings();
        }

        private void UpdateDebugSamplingOverlayVisibility()
        {
            if (DebugSamplingPanel == null ||
                DebugSamplingCloseButton == null ||
                DebugSamplingMinimizeButton == null ||
                DebugSamplingSettingsButton == null ||
                OverlayCanvas == null)
            {
                return;
            }

            bool visible = _debugSamplingOverlayEnabled && _overlaySession.RecognitionRunning;
            bool wasVisible = DebugSamplingMinimizeButton.Visibility == Visibility.Visible;
            if (visible)
            {
                if (!wasVisible)
                {
                    _debugSamplingOverlayEntries.Clear();
                    DebugSamplingText.Text = string.Empty;
                }

                DebugSamplingPanel.Visibility = _debugSamplingMinimized
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                DebugSamplingCloseButton.Visibility = _debugSamplingMinimized
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                DebugSamplingMinimizeButton.Visibility = Visibility.Visible;
                DebugSamplingSettingsButton.Visibility = Visibility.Visible;
                PositionDebugSamplingControls();
                if (!_debugSamplingStatsTimer.IsEnabled)
                {
                    StartDebugSamplingResourceMonitoring();
                    _debugSamplingStatsTimer.Start();
                }
            }
            else
            {
                DebugSamplingPanel.Visibility = Visibility.Collapsed;
                DebugSamplingCloseButton.Visibility = Visibility.Collapsed;
                DebugSamplingMinimizeButton.Visibility = Visibility.Collapsed;
                DebugSamplingSettingsButton.Visibility = Visibility.Collapsed;
                _debugSamplingStatsTimer.Stop();
                if (_debugControlInteractive)
                {
                    _debugControlInteractive = false;
                    ApplyOverlayHitMode();
                }
            }

            UpdateDebugCaptureMaskBounds();
            if (visible)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(UpdateDebugCaptureMaskBounds));
            }
        }

        private void PositionDebugSamplingControls()
        {
            if (OverlayCanvas == null ||
                DebugSamplingPanel == null ||
                DebugSamplingCloseButton == null ||
                DebugSamplingMinimizeButton == null ||
                DebugSamplingSettingsButton == null)
            {
                return;
            }

            Canvas.SetLeft(DebugSamplingPanel, DebugSamplingHorizontalMargin);
            Canvas.SetTop(DebugSamplingPanel, GetDebugSamplingPanelTop());
            if (_debugSamplingMinimized)
            {
                DebugSamplingMinimizeButton.Width = 36;
                DebugSamplingMinimizeButton.Height = 36;
                DebugSamplingMinimizeButton.Content = "Restore";
                DebugSamplingMinimizeButton.ContentTemplate = TryFindResource("DebugRestoreGlyphTemplate") as DataTemplate;
                DebugSamplingMinimizeButton.Template = TryFindResource("DebugRestoreButtonTemplate") as ControlTemplate;
                DebugSamplingMinimizeButton.ToolTip = TryFindResource("Debug_RestoreTooltip") as string
                    ?? "Restore debug panel";
                Canvas.SetLeft(DebugSamplingMinimizeButton, DebugSamplingHorizontalMargin);
                Canvas.SetTop(
                    DebugSamplingMinimizeButton,
                    Math.Max(
                        0,
                        OverlayCanvas.ActualHeight - DebugSamplingMinimizeButton.Height - DebugSamplingBottomMargin));
                Canvas.SetLeft(
                    DebugSamplingSettingsButton,
                    DebugSamplingHorizontalMargin + DebugSamplingMinimizeButton.Width + 4);
                Canvas.SetTop(
                    DebugSamplingSettingsButton,
                    Canvas.GetTop(DebugSamplingMinimizeButton) +
                        (DebugSamplingMinimizeButton.Height - DebugSamplingSettingsButton.Height) / 2);
                return;
            }

            DebugSamplingMinimizeButton.Width = 22;
            DebugSamplingMinimizeButton.Height = 22;
            DebugSamplingMinimizeButton.Content = "Minimize";
            DebugSamplingMinimizeButton.ContentTemplate = TryFindResource("DebugMinimizeGlyphTemplate") as DataTemplate;
            DebugSamplingMinimizeButton.Template = TryFindResource("DebugControlButtonTemplate") as ControlTemplate;
            DebugSamplingMinimizeButton.ToolTip = TryFindResource("Debug_MinimizeTooltip") as string
                ?? "Minimize debug panel";
            double panelTop = GetDebugSamplingPanelTop();
            double closeLeft = DebugSamplingHorizontalMargin + DebugSamplingPanelWidth -
                DebugSamplingCloseButton.Width - 6;
            Canvas.SetLeft(DebugSamplingCloseButton, closeLeft);
            Canvas.SetTop(DebugSamplingCloseButton, panelTop + 3);
            Canvas.SetLeft(DebugSamplingMinimizeButton, closeLeft - DebugSamplingMinimizeButton.Width - 4);
            Canvas.SetTop(DebugSamplingMinimizeButton, panelTop + 3);
            Canvas.SetLeft(
                DebugSamplingSettingsButton,
                Canvas.GetLeft(DebugSamplingMinimizeButton) - DebugSamplingSettingsButton.Width - 4);
            Canvas.SetTop(DebugSamplingSettingsButton, panelTop + 3);
        }

        private void StartDebugSamplingResourceMonitoring()
        {
            try
            {
                if (_debugSamplingProcess == null || _debugSamplingProcess.HasExited)
                {
                    _debugSamplingProcess?.Dispose();
                    _debugSamplingProcess = Process.GetCurrentProcess();
                }

                _debugSamplingProcess.Refresh();
                _lastDebugSamplingProcessCpuTime = _debugSamplingProcess.TotalProcessorTime;
                _lastDebugSamplingCpuTimestamp = Stopwatch.GetTimestamp();
                UpdateDebugSamplingResourceUsage();
            }
            catch (Exception ex)
            {
                DebugResourceText.Text = TryFindResource("Debug_ProcessResourcesUnavailable") as string
                    ?? "Process CPU / memory unavailable";
                Logger.Log.Debug($"Unable to read process resource usage: {ex.Message}");
            }
        }

        private double GetDebugSamplingPanelTop()
        {
            return Math.Max(
                0,
                OverlayCanvas.ActualHeight - DebugSamplingPanel.Height - DebugSamplingBottomMargin);
        }

        private void UpdateDebugCaptureMaskBounds()
        {
            if (OverlayCanvas == null)
            {
                return;
            }

            System.Drawing.Rectangle panelBounds;
            var bounds = new List<System.Drawing.Rectangle>(3);
            bool mappingSucceeded =
                TryGetDebugCaptureBounds(DebugSamplingPanel, out panelBounds) &&
                AddDebugCaptureBounds(DebugSamplingCloseButton, bounds) &&
                AddDebugCaptureBounds(DebugSamplingMinimizeButton, bounds) &&
                AddDebugCaptureBounds(DebugSamplingSettingsButton, bounds);
            if (panelBounds.Width > 0 && panelBounds.Height > 0)
            {
                bounds.Insert(0, panelBounds);
            }

            lock (_debugCaptureMaskSync)
            {
                _debugCaptureMaskBounds = bounds.ToArray();
                _debugCapturePanelBounds = panelBounds;
                _debugCaptureMaskUnavailable = !mappingSucceeded;
            }
        }

        private static bool AddDebugCaptureBounds(
            FrameworkElement element,
            List<System.Drawing.Rectangle> bounds)
        {
            System.Drawing.Rectangle screenBounds;
            if (!TryGetDebugCaptureBounds(element, out screenBounds))
            {
                return false;
            }

            if (screenBounds.Width > 0 && screenBounds.Height > 0)
            {
                bounds.Add(screenBounds);
            }
            return true;
        }

        private static bool TryGetDebugCaptureBounds(
            FrameworkElement element,
            out System.Drawing.Rectangle screenBounds)
        {
            screenBounds = System.Drawing.Rectangle.Empty;
            if (element == null || element.Visibility != Visibility.Visible)
            {
                return true;
            }

            if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                Logger.Log.Warn("Debug overlay is visible but its bounds are not ready; OCR sampling will be skipped until layout completes.");
                return false;
            }

            try
            {
                System.Windows.Point topLeft = element.PointToScreen(new System.Windows.Point(0, 0));
                System.Windows.Point bottomRight = element.PointToScreen(
                    new System.Windows.Point(element.ActualWidth, element.ActualHeight));
                screenBounds = System.Drawing.Rectangle.FromLTRB(
                    (int)Math.Floor(Math.Min(topLeft.X, bottomRight.X)),
                    (int)Math.Floor(Math.Min(topLeft.Y, bottomRight.Y)),
                    (int)Math.Ceiling(Math.Max(topLeft.X, bottomRight.X)),
                    (int)Math.Ceiling(Math.Max(topLeft.Y, bottomRight.Y)));
                if (screenBounds.Width > 0 && screenBounds.Height > 0)
                {
                    return true;
                }

                Logger.Log.Warn("Debug overlay has empty screen bounds; OCR sampling will be skipped until layout completes.");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                Logger.Log.Warn($"Could not map debug overlay bounds to the desktop for OCR masking: {ex.Message}");
                return false;
            }
        }

        internal void FilterDebugOverlayFromCapture(
            Bitmap bitmap,
            System.Drawing.Rectangle captureBounds)
        {
            if (bitmap == null || captureBounds.Width <= 0 || captureBounds.Height <= 0)
            {
                return;
            }

            System.Drawing.Rectangle[] masks;
            System.Drawing.Rectangle panelBounds;
            bool maskUnavailable;
            lock (_debugCaptureMaskSync)
            {
                masks = (System.Drawing.Rectangle[])_debugCaptureMaskBounds.Clone();
                panelBounds = _debugCapturePanelBounds;
                maskUnavailable = _debugCaptureMaskUnavailable;
            }

            if (maskUnavailable)
            {
                // Fail closed during layout or coordinate-mapping transitions so
                // visible debug text can never leak into OCR.
                throw new InvalidOperationException(
                    "Debug overlay bounds are unavailable; skipping this OCR capture to avoid reading debug text.");
            }

            if (masks.Length == 0)
            {
                return;
            }

            double scaleX = bitmap.Width / (double)captureBounds.Width;
            double scaleY = bitmap.Height / (double)captureBounds.Height;
            var destinationMasks = new List<System.Drawing.Rectangle>(masks.Length);
            var destinationControls = new List<System.Drawing.Rectangle>(masks.Length);
            foreach (System.Drawing.Rectangle screenMask in masks)
            {
                System.Drawing.Rectangle intersection = System.Drawing.Rectangle.Intersect(
                    captureBounds,
                    screenMask);
                if (intersection.Width <= 0 || intersection.Height <= 0)
                {
                    continue;
                }

                int left = (int)Math.Floor((intersection.Left - captureBounds.Left) * scaleX);
                int top = (int)Math.Floor((intersection.Top - captureBounds.Top) * scaleY);
                int right = (int)Math.Ceiling((intersection.Right - captureBounds.Left) * scaleX);
                int bottom = (int)Math.Ceiling((intersection.Bottom - captureBounds.Top) * scaleY);
                var destinationMask = System.Drawing.Rectangle.FromLTRB(
                    Math.Max(0, left),
                    Math.Max(0, top),
                    Math.Min(bitmap.Width, right),
                    Math.Min(bitmap.Height, bottom));
                if (destinationMask.Width > 0 && destinationMask.Height > 0)
                {
                    destinationMasks.Add(destinationMask);
                    if (panelBounds.Width <= 0 || panelBounds.Height <= 0 ||
                        screenMask != panelBounds)
                    {
                        destinationControls.Add(destinationMask);
                    }
                }
            }

            System.Drawing.Rectangle destinationPanel = System.Drawing.Rectangle.Empty;
            if (panelBounds.Width > 0 && panelBounds.Height > 0)
            {
                System.Drawing.Rectangle intersection = System.Drawing.Rectangle.Intersect(
                    captureBounds,
                    panelBounds);
                if (intersection.Width > 0 && intersection.Height > 0)
                {
                    int left = (int)Math.Floor((intersection.Left - captureBounds.Left) * scaleX);
                    int top = (int)Math.Floor((intersection.Top - captureBounds.Top) * scaleY);
                    int right = (int)Math.Ceiling((intersection.Right - captureBounds.Left) * scaleX);
                    int bottom = (int)Math.Ceiling((intersection.Bottom - captureBounds.Top) * scaleY);
                    destinationPanel = System.Drawing.Rectangle.FromLTRB(
                        Math.Max(0, left),
                        Math.Max(0, top),
                        Math.Min(bitmap.Width, right),
                        Math.Min(bitmap.Height, bottom));
                }
            }

            if (destinationMasks.Count == 0 && destinationPanel.IsEmpty)
            {
                return;
            }

            FilterDebugOverlayPixels(
                bitmap,
                destinationMasks,
                destinationControls,
                destinationPanel,
                panelBounds.Width > 0
                    ? panelBounds.Width / DebugSamplingPanelWidth * scaleX
                    : 1.0);
        }

        private static void FilterDebugOverlayPixels(
            Bitmap bitmap,
            List<System.Drawing.Rectangle> colorKeyBounds,
            List<System.Drawing.Rectangle> controlBounds,
            System.Drawing.Rectangle panelBounds,
            double panelScale)
        {
            if (Interlocked.CompareExchange(ref _debugOverlayFilteringDisabled, 0, 0) != 0)
            {
                return;
            }

            System.Drawing.Imaging.PixelFormat format = bitmap.PixelFormat;
            int bytesPerPixel;
            if (format == System.Drawing.Imaging.PixelFormat.Format24bppRgb)
            {
                bytesPerPixel = 3;
            }
            else if (format == System.Drawing.Imaging.PixelFormat.Format32bppArgb ||
                     format == System.Drawing.Imaging.PixelFormat.Format32bppRgb ||
                     format == System.Drawing.Imaging.PixelFormat.Format32bppPArgb)
            {
                bytesPerPixel = 4;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported screenshot pixel format for debug color filtering: {format}.");
            }

            var fullBounds = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            System.Drawing.Rectangle scanBounds = System.Drawing.Rectangle.Empty;
            foreach (System.Drawing.Rectangle bounds in colorKeyBounds)
            {
                scanBounds = scanBounds.IsEmpty
                    ? bounds
                    : System.Drawing.Rectangle.Union(scanBounds, bounds);
            }
            scanBounds = System.Drawing.Rectangle.Intersect(fullBounds, scanBounds);
            if (scanBounds.IsEmpty)
            {
                return;
            }

            long scanPixels = (long)scanBounds.Width * scanBounds.Height;
            if (scanPixels > DebugOverlayMaxFilterPixels)
            {
                if (scanPixels > DebugOverlayMaxFastFilterPixels)
                {
                    if (Interlocked.Exchange(ref _debugOverlayFilterWarningLogged, 1) == 0)
                    {
                        Logger.Log.Warn(
                            $"Debug overlay filtering skipped to protect capture speed: " +
                            $"{scanBounds.Width}x{scanBounds.Height} pixels exceeds the " +
                            $"{DebugOverlayMaxFastFilterPixels}-pixel limit.");
                    }
                    return;
                }

                long fastStarted = Stopwatch.GetTimestamp();
                FilterDebugOverlayPixelsFast(
                    bitmap,
                    format,
                    bytesPerPixel,
                    colorKeyBounds,
                    panelBounds,
                    scanBounds);
                DisableDebugOverlayFilteringIfSlow(fastStarted);
                return;
            }

            long started = Stopwatch.GetTimestamp();
            System.Drawing.Imaging.BitmapData data = bitmap.LockBits(
                fullBounds,
                System.Drawing.Imaging.ImageLockMode.ReadWrite,
                format);
            try
            {
                // SetWindowDisplayAffinity may already have removed the overlay
                // from a capture. Only adjust the panel background when its
                // reserved color key proves the overlay pixels are present.
                bool overlayPixelsPresent = false;
                bool panelPixelsPresent = false;
                bool panelCoversScan = !panelBounds.IsEmpty && panelBounds.Contains(scanBounds);
                unsafe
                {
                    byte* firstPixel = (byte*)data.Scan0;
                    for (int y = scanBounds.Top; y < scanBounds.Bottom && !overlayPixelsPresent; y++)
                    {
                        byte* row = firstPixel + y * data.Stride;
                        for (int x = scanBounds.Left; x < scanBounds.Right; x++)
                        {
                            if (!panelCoversScan && !IsInsideAnyBounds(x, y, colorKeyBounds))
                            {
                                continue;
                            }

                            int pixel = x * bytesPerPixel;
                            if (IsDebugColorKeyPixel(row, pixel))
                            {
                                overlayPixelsPresent = true;
                                panelPixelsPresent = panelBounds.Contains(x, y);
                                break;
                            }
                        }
                    }

                    if (overlayPixelsPresent)
                    {
                        double panelRadius = 4.0 * panelScale;
                        double panelBorder = Math.Max(1.0, panelScale);
                        double panelFillRadius = Math.Max(0, panelRadius - panelBorder);
                        for (int y = scanBounds.Top; y < scanBounds.Bottom; y++)
                        {
                            byte* row = firstPixel + y * data.Stride;
                            bool rowTouchesPanel = panelBounds.Top <= y && y < panelBounds.Bottom;

                            for (int x = scanBounds.Left; x < scanBounds.Right; x++)
                            {
                                bool isDebugPixel = panelCoversScan || IsInsideAnyBounds(x, y, colorKeyBounds);
                                bool insideControl = IsInsideAnyBounds(x, y, controlBounds);
                                bool restorePanel = panelPixelsPresent && !insideControl && rowTouchesPanel &&
                                    IsInsideDebugPanelFill(x, y, panelBounds, panelBorder, panelFillRadius);
                                if (!isDebugPixel && !restorePanel)
                                {
                                    continue;
                                }

                                int pixel = x * bytesPerPixel;
                                if (isDebugPixel && IsDebugColorKeyPixel(row, pixel))
                                {
                                    row[pixel] = 0;
                                    row[pixel + 1] = 0;
                                    row[pixel + 2] = 0;
                                    if (bytesPerPixel == 4)
                                    {
                                        row[pixel + 3] = 255;
                                    }
                                    continue;
                                }

                                // Reconstruct pixels under the translucent panel so white
                                // subtitles keep their original brightness for the binary gate.
                                if (restorePanel)
                                {
                                    row[pixel] = RestorePanelChannel(row[pixel], 51);
                                    row[pixel + 1] = RestorePanelChannel(row[pixel + 1], 39);
                                    row[pixel + 2] = RestorePanelChannel(row[pixel + 2], 32);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            DisableDebugOverlayFilteringIfSlow(started);
        }

        private static unsafe void FilterDebugOverlayPixelsFast(
            Bitmap bitmap,
            System.Drawing.Imaging.PixelFormat format,
            int bytesPerPixel,
            List<System.Drawing.Rectangle> colorKeyBounds,
            System.Drawing.Rectangle panelBounds,
            System.Drawing.Rectangle scanBounds)
        {
            var fullBounds = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            System.Drawing.Imaging.BitmapData data = bitmap.LockBits(
                fullBounds,
                System.Drawing.Imaging.ImageLockMode.ReadWrite,
                format);
            try
            {
                byte* firstPixel = (byte*)data.Scan0;
                bool panelCoversScan = !panelBounds.IsEmpty && panelBounds.Contains(scanBounds);

                for (int y = scanBounds.Top; y < scanBounds.Bottom; y++)
                {
                    byte* row = firstPixel + y * data.Stride;
                    for (int x = scanBounds.Left; x < scanBounds.Right; x++)
                    {
                        if (!panelCoversScan && !IsInsideAnyBounds(x, y, colorKeyBounds))
                        {
                            continue;
                        }

                        int pixel = x * bytesPerPixel;
                        if (IsDebugColorKeyPixel(row, pixel))
                        {
                            row[pixel] = 0;
                            row[pixel + 1] = 0;
                            row[pixel + 2] = 0;
                            if (bytesPerPixel == 4)
                            {
                                row[pixel + 3] = 255;
                            }
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static void DisableDebugOverlayFilteringIfSlow(long started)
        {
            double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs >= 10.0)
            {
                Interlocked.Exchange(ref _debugOverlayFilteringDisabled, 1);
                if (Interlocked.Exchange(ref _debugOverlayFilterSlowWarningLogged, 1) == 0)
                {
                    Logger.Log.Warn(
                        $"Debug overlay filtering took {elapsedMs:F1} ms; filtering is disabled for later captures.");
                }
            }
        }

        private static bool IsInsideAnyBounds(
            int x,
            int y,
            List<System.Drawing.Rectangle> bounds)
        {
            for (int i = 0; i < bounds.Count; i++)
            {
                if (bounds[i].Contains(x, y))
                {
                    return true;
                }
            }
            return false;
        }

        private static unsafe bool IsDebugColorKeyPixel(byte* row, int pixel)
        {
            byte blue = row[pixel];
            byte green = row[pixel + 1];
            byte red = row[pixel + 2];
            return green >= red + 20 && green >= blue + 20 &&
                red >= 100 && blue >= 100 && green >= 130;
        }

        private static bool IsInsideDebugPanelFill(
            int x,
            int y,
            System.Drawing.Rectangle bounds,
            double border,
            double radius)
        {
            if (bounds.IsEmpty)
            {
                return false;
            }

            double left = bounds.Left + border;
            double top = bounds.Top + border;
            double right = bounds.Right - border;
            double bottom = bounds.Bottom - border;
            double px = x + 0.5;
            double py = y + 0.5;
            if (px < left || px >= right || py < top || py >= bottom)
            {
                return false;
            }

            double cornerRadius = Math.Min(radius, Math.Min((right - left) / 2, (bottom - top) / 2));
            bool leftCorner = px < left + cornerRadius;
            bool rightCorner = px >= right - cornerRadius;
            bool topCorner = py < top + cornerRadius;
            bool bottomCorner = py >= bottom - cornerRadius;
            if ((!leftCorner && !rightCorner) || (!topCorner && !bottomCorner))
            {
                return true;
            }

            double cornerX = leftCorner ? left + cornerRadius : rightCorner ? right - cornerRadius : px;
            double cornerY = topCorner ? top + cornerRadius : bottomCorner ? bottom - cornerRadius : py;
            double dx = px - cornerX;
            double dy = py - cornerY;
            return dx * dx + dy * dy <= cornerRadius * cornerRadius;
        }

        private static byte RestorePanelChannel(byte composited, int panelChannel)
        {
            const int alpha = 128;
            int restored = (composited * 255 - panelChannel * alpha + (255 - alpha) / 2) /
                (255 - alpha);
            return (byte)Math.Max(0, Math.Min(255, restored));
        }

        private void UpdateDebugSamplingResourceUsage()
        {
            if (_debugSamplingProcess == null || DebugResourceText == null)
            {
                return;
            }

            try
            {
                _debugSamplingProcess.Refresh();
                long nowTimestamp = Stopwatch.GetTimestamp();
                TimeSpan cpuTime = _debugSamplingProcess.TotalProcessorTime;
                double cpuPercent = 0;
                if (_lastDebugSamplingCpuTimestamp > 0)
                {
                    double elapsedMs = (nowTimestamp - _lastDebugSamplingCpuTimestamp) * 1000.0 / Stopwatch.Frequency;
                    if (elapsedMs > 0)
                    {
                        cpuPercent = Math.Max(0,
                            (cpuTime - _lastDebugSamplingProcessCpuTime).TotalMilliseconds * 100.0 /
                            (elapsedMs * Math.Max(1, Environment.ProcessorCount)));
                    }
                }

                double workingSetMb = _debugSamplingProcess.WorkingSet64 / (1024.0 * 1024.0);
                string resourceFormat = TryFindResource("Debug_ProcessResources") as string
                    ?? "Process CPU {0:F1}%  Memory {1:F0} MB";
                DebugResourceText.Text = string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    resourceFormat,
                    cpuPercent,
                    workingSetMb);
                _lastDebugSamplingProcessCpuTime = cpuTime;
                _lastDebugSamplingCpuTimestamp = nowTimestamp;
            }
            catch (Exception ex)
            {
                DebugResourceText.Text = TryFindResource("Debug_ProcessResourcesUnavailable") as string
                    ?? "Process CPU / memory unavailable";
                Logger.Log.Debug($"Unable to refresh process resource usage: {ex.Message}");
            }
        }


        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Stopwatch loadedStopwatch = debug ? Stopwatch.StartNew() : null;
            if (debug)
            {
                Logger.Log.Info("[Startup timing] MainWindow_Loaded entered");
            }

            // Get the window handle
            IntPtr handle = new WindowInteropHelper(this).Handle;
            // Listen to window messages
            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(WndProc);

            notify = new INotifyIcon();
            notify.SetSession(_overlaySession);
            notifyIcon = notify.InitializeNotifyIcon(Scale);
            Stopwatch settingsInitStopwatch = debug ? Stopwatch.StartNew() : null;
            data = new SettingsWindow(version, notify, Scale, _overlaySession, this);
            if (debug)
            {
                Logger.Log.Info(
                    $"[Startup timing] SettingsWindow constructed elapsedMs={settingsInitStopwatch.Elapsed.TotalMilliseconds:F1}");
            }
            data.InitializeKey(handle);
            notify.SetData(data);
            notify.EnsurePairDisplayRegions();
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
            bool settingsOpenedAtStartup = false;
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
                    data.OpenSettings();
                    settingsOpenedAtStartup = true;
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
                                        data.OpenSettings();
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
            Stopwatch engineLoadStopwatch = debug ? Stopwatch.StartNew() : null;
            data.LoadEngine();
            if (debug)
            {
                Logger.Log.Info(
                    $"[Startup timing] OCR engine ready elapsedMs={engineLoadStopwatch.Elapsed.TotalMilliseconds:F1}, " +
                    $"mainWindowLoadedElapsedMs={loadedStopwatch.Elapsed.TotalMilliseconds:F1}");
            }

            UpdateOcrSamplingInterval();
            OCRTimer.Tick += GetOCR;    // Delegate: method to execute


            UITimer.Interval = new TimeSpan(0, 0, 0, 0, 500);
            UITimer.Tick += UpdateText;    // Delegate: method to execute

            _dragHandleTimer.Start();

            SetWindowPos(new WindowInteropHelper(this).Handle, -1, 0, 0, 0, 0, 1 | 2);
            SizeOverlayToVirtualScreen();
            ApplyPairOverlay();
            UpdateDebugSamplingOverlayVisibility();

            // Show the main window only after initialization is complete, so users don't see a half‑rendered UI.
            this.Opacity = 1;
            if (debug)
            {
                Logger.Log.Info(
                    $"[Startup timing] MainWindow_Loaded completed elapsedMs={loadedStopwatch.Elapsed.TotalMilliseconds:F1}, " +
                    $"matcherReady={data?.Matcher != null}, engineReady={data?.engine != null}");
            }

            // An empty region layout is valid for a background/tray startup.
            // Open settings only for the one-time legacy Region2 review, after
            // the overlay has its real virtual-screen size so Preview all can
            // render its outlines immediately.
            if (_overlaySession.LegacyRegion2ReviewPending &&
                !settingsOpenedAtStartup &&
                !data.IsVisible)
            {
                data.RefreshPairPage();
                data.Show();
            }
        }

        public void GetOCR(object sender, EventArgs e)
        {
            UpdateOcrSamplingInterval();
            if (notify?.isContextMenuOpen == true)
            {
                if (ShouldCollectSamplingDiagnostics)
                {
                    int skipped = Interlocked.Increment(ref _samplingTicksSkippedMenu);
                    if (skipped == 1)
                    {
                        AddDebugSamplingOverlayEntry("托盘菜单打开，暂时停止读取画面");
                    }
                }
                return;
            }

            if (_isOcrRunning)
            {
                if (ShouldCollectSamplingDiagnostics)
                {
                    Interlocked.Increment(ref _samplingTicksSkippedOcrBusy);
                    AddDebugSamplingOverlayEntry("正在识别文字，稍后继续读取");
                }
                return;
            }

            if (Interlocked.CompareExchange(ref OCR_TIMER, 1, 0) != 0)
            {
                if (ShouldCollectSamplingDiagnostics)
                {
                    Interlocked.Increment(ref _samplingTicksSkippedSamplerBusy);
                    AddDebugSamplingOverlayEntry("正在读取画面，稍后继续");
                }
                return;
            }

            long diagnosticId = ShouldCollectSamplingDiagnostics
                ? Interlocked.Increment(ref _samplingDiagnosticSequence)
                : 0;
            long sampleStartedTimestamp = Stopwatch.GetTimestamp();
            DateTime sampleStartedUtc = DateTime.UtcNow;
            long previousSampleStartedTicks = _lastSamplingStartedUtc == DateTime.MinValue
                ? 0
                : (long)(sampleStartedUtc - _lastSamplingStartedUtc).TotalMilliseconds;
            _lastSamplingStartedUtc = sampleStartedUtc;
            var requests = new List<PairSamplingRequest>();
            try
            {
                Stopwatch setupStopwatch = debug ? Stopwatch.StartNew() : null;
                var setupTiming = new SamplingSetupTiming
                {
                    SkippedMenuTicks = Interlocked.Exchange(ref _samplingTicksSkippedMenu, 0),
                    SkippedOcrBusyTicks = Interlocked.Exchange(ref _samplingTicksSkippedOcrBusy, 0),
                    SkippedSamplerBusyTicks = Interlocked.Exchange(ref _samplingTicksSkippedSamplerBusy, 0)
                };
                Stopwatch stageStopwatch = debug ? Stopwatch.StartNew() : null;
                ResetCaptureBuffersIfGameChanged();
                setupTiming.ResetGameStateMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;

                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                IReadOnlyList<RegionPair> pairs = _overlaySession.Pairs;
                int engineCount = Math.Min(LiveOverlaySession.EnginePairCap, pairs.Count);
                int primaryPairIndex = FindPrimaryPairIndex(pairs);
                IReadOnlyList<PairSubtitleBody> subtitleBodies = _overlaySession.PairBodies;
                bool hasVisibleSubtitle = false;
                for (int i = 0; i < subtitleBodies.Count; i++)
                {
                    if (subtitleBodies[i].Visible)
                    {
                        hasVisibleSubtitle = true;
                        break;
                    }
                }
                setupTiming.HasVisibleSubtitle = hasVisibleSubtitle;
                setupTiming.PairSnapshotMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;

                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                EnsurePairBuffers(engineCount);
                setupTiming.BufferSetupMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                string sampledGame = _overlaySession.AppliedGame;
                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                for (int i = 0; i < engineCount; i++)
                {
                    OverlayRect capture = pairs[i].Capture;
                    if (!capture.IsValid)
                    {
                        continue;
                    }

                    requests.Add(new PairSamplingRequest
                    {
                        PairIndex = i,
                        IsPrimary = i == primaryPairIndex,
                        Capture = capture,
                        PreviousBinary = _pairLastBinary[i]?.Clone(),
                        PreviousOcrBinary = _pairLastOcrBinary[i]?.Clone()
                    });
                }
                setupTiming.PreviousFrameCloneMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;

                setupTiming.TotalMs = setupStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                long taskQueuedTimestamp = Stopwatch.GetTimestamp();
                _ = ProcessSampleBatchAsync(
                    requests,
                    sampledGame,
                    primaryPairIndex,
                    diagnosticId,
                    previousSampleStartedTicks,
                    setupTiming,
                    sampleStartedTimestamp,
                    taskQueuedTimestamp);
                requests = null;
            }
            catch (Exception ex)
            {
                DisposeSamplingRequests(requests);
                Interlocked.Exchange(ref OCR_TIMER, 0);
                Logger.Log.Error($"OCR frame sampling could not start: {ex}");
            }
        }

        private void UpdateOcrSamplingInterval()
        {
            // Sampling performs capture, image conversion, and preprocessing even
            // when the frame is ultimately rejected before OCR. Keep it no faster
            // than the configured OCR cadence, with a 200 ms safety floor.
            int intervalMs = Math.Max(
                LiveOverlaySession.UiMinOcrIntervalMs,
                _overlaySession.EngineOcrIntervalMs);
            TimeSpan interval = TimeSpan.FromMilliseconds(intervalMs);
            if (OCRTimer.Interval != interval)
            {
                OCRTimer.Interval = interval;
            }
        }

        private async Task ProcessSampleBatchAsync(
            List<PairSamplingRequest> requests,
            string sampledGame,
            int primaryPairIndex,
            long diagnosticId,
            long sampleIntervalMs,
            SamplingSetupTiming setupTiming,
            long sampleStartedTimestamp,
            long taskQueuedTimestamp)
        {
            SamplingBatchResult batch = null;
            Exception failure = null;
            try
            {
                batch = await Task.Run(() => CaptureAndPrepareSampleBatch(
                    requests,
                    taskQueuedTimestamp,
                    diagnosticId,
                    setupTiming.HasVisibleSubtitle)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            long workerCompletedTimestamp = Stopwatch.GetTimestamp();
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                DisposeSamplingBatch(batch);
                DisposeSamplingRequests(requests);
                Interlocked.Exchange(ref OCR_TIMER, 0);
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => CompleteSampleBatch(
                batch,
                requests,
                failure,
                sampledGame,
                primaryPairIndex,
                diagnosticId,
                sampleIntervalMs,
                setupTiming,
                sampleStartedTimestamp,
                workerCompletedTimestamp)));
        }

        private SamplingBatchResult CaptureAndPrepareSampleBatch(
            List<PairSamplingRequest> requests,
            long taskQueuedTimestamp,
            long diagnosticId,
            bool hasVisibleSubtitle)
        {
            Stopwatch workerStopwatch = Stopwatch.StartNew();
            long workerStartedTimestamp = Stopwatch.GetTimestamp();
            var result = new SamplingBatchResult
            {
                QueueMs = (workerStartedTimestamp - taskQueuedTimestamp) * 1000.0 / Stopwatch.Frequency
            };

            Stopwatch extraStopwatch = debug ? Stopwatch.StartNew() : null;
            try
            {
                result.Extra = CollectExtraPathSample(result, diagnosticId, hasVisibleSubtitle);
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"Additional subtitle sampling failed: {ex.Message}");
                result.Extra = ExtraPathSample.None;
                result.ExtraPathState = "error";
            }
            result.ExtraPathMs = extraStopwatch?.Elapsed.TotalMilliseconds ?? 0;

            foreach (PairSamplingRequest request in requests)
            {
                Bitmap bitmap = null;
                Mat frame = null;
                Mat binary = null;
                try
                {
                    Stopwatch stageStopwatch = debug ? Stopwatch.StartNew() : null;
                    bitmap = CaptureRect(request.Capture);
                    double pairCaptureMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                    result.CaptureMs += pairCaptureMs;
                    if (request.IsPrimary)
                    {
                        result.PrimaryCaptureMs += pairCaptureMs;
                    }
                    else
                    {
                        result.SecondaryCaptureMs += pairCaptureMs;
                    }
                    if (debug)
                    {
                        result.PairCaptureTimings.Add(
                            $"pair{request.PairIndex}{(request.IsPrimary ? "-primary" : string.Empty)}=" +
                            $"{request.Capture.Width}x{request.Capture.Height}:{pairCaptureMs:F1}ms" +
                            (bitmap == null ? ":no-frame" : string.Empty));
                    }
                    if (bitmap == null)
                    {
                    if (ShouldCollectSamplingDiagnostics)
                        {
                            result.PairFrameDecisions.Add($"pair{request.PairIndex}:no-frame");
                        }
                        continue;
                    }

                    stageStopwatch = debug ? Stopwatch.StartNew() : null;
                    frame = bitmap.ToMat();
                    result.BitmapToMatMs += stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;

                    stageStopwatch = debug ? Stopwatch.StartNew() : null;
                    binary = PreprocessToBinary(frame);
                    bool empty = binary == null || binary.Empty() || Cv2.CountNonZero(binary) == 0;
                    result.PreprocessMs += stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;

                    stageStopwatch = debug ? Stopwatch.StartNew() : null;
                    bool stable = IsStableVsPrevious(
                        binary,
                        request.PreviousBinary,
                        out double changeRatioFromPrevious);
                    bool changed = IsChangedVsLastOcr(
                        binary,
                        request.PreviousOcrBinary,
                        out double changeRatioFromLastOcr);
                    result.CompareMs += stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;

                    PairFrameSample sample = empty && stable
                        ? PairFrameSample.StableNoText()
                        : changed && stable && !empty
                            ? PairFrameSample.ChangedAndStable()
                            : PairFrameSample.Unchanged();
                    string decision = empty && stable
                        ? "clear-empty"
                        : changed && stable
                            ? "queue-ocr"
                            : stable ? "unchanged" : "wait-stable";

                    result.Pairs.Add(new PairSamplingResult
                    {
                        PairIndex = request.PairIndex,
                        CaptureMs = pairCaptureMs,
                        Bitmap = bitmap,
                        Frame = frame,
                        Binary = binary,
                        Sample = sample,
                        Decision = decision,
                        Empty = empty,
                        ChangeRatioFromPrevious = changeRatioFromPrevious,
                        ChangeRatioFromLastOcr = changeRatioFromLastOcr
                    });
                    if (ShouldCollectSamplingDiagnostics)
                    {
                        result.PairFrameDecisions.Add($"pair{request.PairIndex}:{decision}");
                    }
                    if (debug)
                    {
                        result.PairFrameStates.Add(
                            $"pair{request.PairIndex}:diffPrev={FormatDiagnosticRatio(changeRatioFromPrevious)}," +
                            $"diffOcr={FormatDiagnosticRatio(changeRatioFromLastOcr)}," +
                            $"stable={stable},changed={changed},empty={empty},decision={decision}");
                    }
                    bitmap = null;
                    frame = null;
                    binary = null;
                }
                catch (Exception ex)
                {
                    if (ShouldCollectSamplingDiagnostics)
                    {
                        result.PairFrameDecisions.Add(
                            $"pair{request.PairIndex}:capture-error({ex.GetType().Name})");
                    }
                    Logger.Log.Warn($"Pair {request.PairIndex} capture/preprocess failed: {ex.Message}");
                }
                finally
                {
                    bitmap?.Dispose();
                    frame?.Dispose();
                    binary?.Dispose();
                    request.PreviousBinary?.Dispose();
                    request.PreviousBinary = null;
                    request.PreviousOcrBinary?.Dispose();
                    request.PreviousOcrBinary = null;
                }
            }

            result.WorkerMs = workerStopwatch.Elapsed.TotalMilliseconds;
            return result;
        }

        private void CompleteSampleBatch(
            SamplingBatchResult batch,
            List<PairSamplingRequest> requests,
            Exception failure,
            string sampledGame,
            int primaryPairIndex,
            long diagnosticId,
            long sampleIntervalMs,
            SamplingSetupTiming setupTiming,
            long sampleStartedTimestamp,
            long workerCompletedTimestamp)
        {
            long callbackStartedTimestamp = Stopwatch.GetTimestamp();
            double uiWaitMs = (callbackStartedTimestamp - workerCompletedTimestamp) * 1000.0 / Stopwatch.Frequency;
            Stopwatch applyStopwatch = debug ? Stopwatch.StartNew() : null;
            try
            {
                if (failure != null)
                {
                    Logger.Log.Error($"OCR frame sampling failed: {failure}");
                    if (ShouldCollectSamplingDiagnostics)
                    {
                        AddDebugSamplingOverlayEntry(
                            "画面读取失败，请查看日志");
                    }
                    return;
                }

                if (!_overlaySession.RecognitionRunning ||
                    !string.Equals(sampledGame, _overlaySession.AppliedGame, StringComparison.Ordinal))
                {
                    if (ShouldCollectSamplingDiagnostics)
                    {
                        AddDebugSamplingOverlayEntry(
                            "识别设置已变化，忽略旧结果");
                    }
                    return;
                }

                int engineCount = Math.Min(LiveOverlaySession.EnginePairCap, _overlaySession.Pairs.Count);
                EnsurePairBuffers(engineCount);
                var samples = new PairFrameSample[engineCount];
                foreach (PairSamplingResult pair in batch.Pairs)
                {
                    int pairIndex = pair.PairIndex;
                    if (pairIndex < 0 || pairIndex >= engineCount)
                    {
                        continue;
                    }

                    samples[pairIndex] = pair.Sample;
                    UpdatePairOcrDiagnosticContext(
                        pairIndex,
                        pair,
                        diagnosticId,
                        sampleStartedTimestamp);
                    if (pair.Binary != null && !pair.Binary.Empty())
                    {
                        _pairLastBinary[pairIndex]?.Dispose();
                        _pairLastBinary[pairIndex] = pair.Binary;
                        pair.Binary = null;
                    }

                    ReplaceCaptured(pairIndex, pair.Bitmap, pair.Frame);
                    pair.Bitmap = null;
                    pair.Frame = null;
                }

                _overlaySession.Beat(batch.Extra ?? ExtraPathSample.None, samples);
                if (ShouldRequestAutoRegionOcr())
                {
                    _overlaySession.RequestAutoRegionOcr();
                }
                else
                {
                    _overlaySession.CancelQueuedAutoRegionOcr();
                }

                string extraVoiceKey = null;
                if (batch.Extra != null && batch.Extra.DialogueChoiceSelected)
                {
                    extraVoiceKey = _pendingExtraPathVoiceKey;
                    _pendingExtraPathVoiceKey = null;
                }

                MaybePlayPairVoice(
                    extraVoiceKey,
                    batch.Extra != null ? batch.Extra.DialogueChoiceContent : string.Empty,
                    string.Empty);
                ApplyPairOverlay();
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Failed to apply OCR sampling result: {ex}");
            }
            finally
            {
                double applyMs = applyStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                DisposeSamplingBatch(batch);
                DisposeSamplingRequests(requests);
                Interlocked.Exchange(ref OCR_TIMER, 0);

                if (batch != null)
                {
                    if (ShouldCollectSamplingDiagnostics)
                    {
                        AddDebugSamplingOutcome(batch);
                    }

                    if (debug)
                    {
                        double totalMs = (Stopwatch.GetTimestamp() - sampleStartedTimestamp) * 1000.0 / Stopwatch.Frequency;
                        Logger.Log.Debug(
                        $"[Sampling timing #{diagnosticId}] intervalMs={sampleIntervalMs}, " +
                        $"configuredIntervalMs={OCRTimer.Interval.TotalMilliseconds:F0}, " +
                        $"setupMs={setupTiming.TotalMs:F1}, resetGameMs={setupTiming.ResetGameStateMs:F1}, " +
                        $"pairSnapshotMs={setupTiming.PairSnapshotMs:F1}, " +
                        $"bufferSetupMs={setupTiming.BufferSetupMs:F1}, " +
                        $"previousFrameCloneMs={setupTiming.PreviousFrameCloneMs:F1}, " +
                        $"workerQueueMs={batch.QueueMs:F1}, " +
                        $"extraPathMs={batch.ExtraPathMs:F1}, regionCaptureTotalMs={batch.CaptureMs:F1}, " +
                        $"primaryCaptureMs={batch.PrimaryCaptureMs:F1}, " +
                        $"secondaryCaptureMs={batch.SecondaryCaptureMs:F1}, " +
                        $"perRegionCapture=[{string.Join(";", batch.PairCaptureTimings)}], " +
                        $"bitmapToMatMs={batch.BitmapToMatMs:F1}, " +
                        $"preprocessMs={batch.PreprocessMs:F1}, extraCaptureMs={batch.ExtraCaptureMs:F1}, " +
                        $"extraBitmapToMatMs={batch.ExtraBitmapToMatMs:F1}, " +
                        $"extraMatToBitmapMs={batch.ExtraMatToBitmapMs:F1}, " +
                        $"extraPreprocessMs={batch.ExtraPreprocessMs:F1}, " +
                        $"extraDetectionMs={batch.ExtraDetectionMs:F1}, " +
                        $"darkScan=[{batch.DarkScreenScanState},capture={batch.DarkScreenCaptureMs:F1}," +
                        $"bitmapToMat={batch.DarkScreenBitmapToMatMs:F1},preprocess={batch.DarkScreenPreprocessMs:F1}," +
                        $"detection={batch.DarkScreenDetectionMs:F1},darkRatio={batch.DarkScreenRatio:F3}," +
                        $"brightRatio={batch.DarkScreenBrightRatio:F4}], " +
                        $"dialogueScan=[{batch.DialogueScanState},capture={batch.DialogueCaptureMs:F1}," +
                        $"bitmapToMat={batch.DialogueBitmapToMatMs:F1},preprocess={batch.DialoguePreprocessMs:F1}," +
                        $"detection={batch.DialogueDetectionMs:F1},confidence={batch.DialogueConfidence:F3}], " +
                        $"compareMs={batch.CompareMs:F1}, workerMs={batch.WorkerMs:F1}, " +
                        $"workerToUiMs={uiWaitMs:F1}, uiApplyMs={applyMs:F1}, totalMs={totalMs:F1}, " +
                        $"skippedTicks=[menu={setupTiming.SkippedMenuTicks},ocrBusy={setupTiming.SkippedOcrBusyTicks}," +
                        $"samplerBusy={setupTiming.SkippedSamplerBusyTicks}], " +
                        $"regions={batch.Pairs.Count}, frameStates=[{string.Join(";", batch.PairFrameStates)}], " +
                        $"extraPathState={batch.ExtraPathState ?? "unknown"}, " +
                        $"busyOcrSlot={_overlaySession.BusyOcrSlot?.ToString() ?? "none"}, " +
                        $"ocrQueueCount={_overlaySession.OcrQueue.Count}, " +
                        $"ocrRunning={_isOcrRunning}");
                    }
                }

                ContinueOcrPipeline();
            }
        }

        private void AddDebugSamplingOutcome(SamplingBatchResult batch)
        {
            bool ocrPending = _overlaySession.BusyOcrSlot.HasValue || _overlaySession.OcrQueue.Count > 0;
            string frameDecisions = batch.PairFrameDecisions.Count == 0
                ? "暂时没有可读取的画面"
                : string.Join("；", batch.PairFrameDecisions.Select(FormatFrameDecisionForOverlay));

            if (ocrPending)
            {
                AddDebugSamplingOverlayEntry("画面有变化，正在识别文字");
                return;
            }

            AddDebugSamplingOverlayEntry(frameDecisions);
        }

        private static string FormatFrameDecisionForOverlay(string value)
        {
            int separator = value?.IndexOf(':') ?? -1;
            if (separator < 0)
            {
                return "画面状态暂不可用";
            }

            string source = value.Substring(0, separator);
            int pairIndex = -1;
            if (source.StartsWith("pair", StringComparison.Ordinal))
            {
                int.TryParse(source.Substring(4), out pairIndex);
            }
            string region = pairIndex >= 0 ? $"区域{pairIndex + 1}" : "画面";
            string decision = value.Substring(separator + 1);
            string description;
            if (decision == "queue-ocr")
            {
                description = "发现新文字";
            }
            else if (decision == "wait-stable")
            {
                description = "画面变化中";
            }
            else if (decision == "unchanged")
            {
                description = "没有新内容";
            }
            else if (decision == "clear-empty")
            {
                description = "内容已清除";
            }
            else if (decision == "no-frame")
            {
                description = "暂时无法读取";
            }
            else if (decision.StartsWith("capture-error", StringComparison.Ordinal))
            {
                description = "读取失败";
            }
            else
            {
                description = "状态暂不可用";
            }

            return $"{region}{description}";
        }

        private void AddDebugSamplingOverlayEntry(string message)
        {
            if (!IsDebugSamplingOverlayActive ||
                DebugSamplingPanel == null ||
                DebugSamplingText == null ||
                OverlayCanvas == null)
            {
                return;
            }

            string entry = FitDebugSamplingLine($"{DateTime.Now:HH:mm:ss}  {message}");
            if (_debugSamplingOverlayEntries.Count > 0)
            {
                string latest = _debugSamplingOverlayEntries[_debugSamplingOverlayEntries.Count - 1];
                const int timestampAndSpacingLength = 10;
                if (latest.Length >= timestampAndSpacingLength && entry.Length >= timestampAndSpacingLength &&
                    string.Equals(
                        latest.Substring(timestampAndSpacingLength),
                        entry.Substring(timestampAndSpacingLength),
                        StringComparison.Ordinal))
                {
                    _debugSamplingOverlayEntries[_debugSamplingOverlayEntries.Count - 1] = entry;
                    DebugSamplingText.Text = string.Join(Environment.NewLine, _debugSamplingOverlayEntries);
                    DebugSamplingScrollViewer?.ScrollToEnd();
                    PositionDebugSamplingControls();
                    return;
                }
            }

            _debugSamplingOverlayEntries.Add(entry);
            while (_debugSamplingOverlayEntries.Count > DebugSamplingOverlayEntryLimit)
            {
                _debugSamplingOverlayEntries.RemoveAt(0);
            }

            DebugSamplingText.Text = string.Join(Environment.NewLine, _debugSamplingOverlayEntries);
            DebugSamplingScrollViewer?.ScrollToEnd();
            PositionDebugSamplingControls();
        }

        private static string FitDebugSamplingLine(string line)
        {
            line = (line ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
            int maxCells = DebugSamplingMaxLineCells - 4; // Keep at least two full-width characters free.
            if (GetDebugSamplingDisplayCells(line) <= maxCells)
            {
                return line;
            }

            const string ellipsis = "…";
            int contentLimit = maxCells - 2;
            int cells = 0;
            int length = 0;
            while (length < line.Length)
            {
                int charCells = IsWideDebugSamplingCharacter(line[length]) ? 2 : 1;
                if (cells + charCells > contentLimit)
                {
                    break;
                }

                cells += charCells;
                length++;
            }

            return line.Substring(0, length).TrimEnd() + ellipsis;
        }

        private static int GetDebugSamplingDisplayCells(string value)
        {
            int cells = 0;
            foreach (char character in value)
            {
                cells += IsWideDebugSamplingCharacter(character) ? 2 : 1;
            }
            return cells;
        }

        private static bool IsWideDebugSamplingCharacter(char character)
        {
            return (character >= '\u2E80' && character <= '\u9FFF') ||
                (character >= '\uAC00' && character <= '\uD7AF') ||
                (character >= '\uF900' && character <= '\uFAFF') ||
                (character >= '\uFE30' && character <= '\uFE6F') ||
                (character >= '\uFF01' && character <= '\uFF60') ||
                (character >= '\uFFE0' && character <= '\uFFE6') ||
                char.IsSurrogate(character);
        }

        private static void DisposeSamplingRequests(IEnumerable<PairSamplingRequest> requests)
        {
            if (requests == null)
            {
                return;
            }

            foreach (PairSamplingRequest request in requests)
            {
                request.PreviousBinary?.Dispose();
                request.PreviousOcrBinary?.Dispose();
            }
        }

        private static void DisposeSamplingBatch(SamplingBatchResult batch)
        {
            if (batch?.Pairs == null)
            {
                return;
            }

            foreach (PairSamplingResult pair in batch.Pairs)
            {
                pair.Bitmap?.Dispose();
                pair.Frame?.Dispose();
                pair.Binary?.Dispose();
                pair.Bitmap = null;
                pair.Frame = null;
                pair.Binary = null;
            }
        }

        private void QueueAutoRegionRepairFromEvidence(string reason, string recognizedText)
        {
            if (_autoRegionDetectionLocked)
            {
                return;
            }

            string normalizedText = Regex.Replace(recognizedText ?? string.Empty, @"\s+", " ").Trim();
            string signature = reason + "|" + normalizedText;
            if (string.Equals(signature, _lastAutoRegionRepairEvidenceSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastAutoRegionRepairEvidenceSignature = signature;
            _autoRegionRepairPending = true;
            _autoRegionRepairReason = reason;
        }

        private int FindPrimaryPairIndex(IReadOnlyList<RegionPair> pairs)
        {
            if (pairs == null)
            {
                return -1;
            }

            for (int i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].Id == _overlaySession.VoicePrimaryId)
                {
                    return i;
                }
            }

            return pairs.Count == 0 ? -1 : 0;
        }

        private bool ShouldRequestAutoRegionOcr()
        {
            if (_autoRegionDetectionLocked ||
                !_overlaySession.RecognitionRunning ||
                data == null ||
                data.engine == null ||
                data.Matcher == null ||
                _autoRegionOcrInFlight)
            {
                return false;
            }

            IReadOnlyList<RegionPair> pairs = _overlaySession.Pairs;
            int primaryIndex = FindPrimaryPairIndex(pairs);
            bool primaryCaptureMissing = primaryIndex < 0 || !pairs[primaryIndex].Capture.IsValid;
            if (primaryCaptureMissing)
            {
                _autoRegionRepairReason = "missing-primary-capture";
                int missingRegionRetryDelay = Math.Max(500, _overlaySession.EngineOcrIntervalMs * 2);
                return DateTime.UtcNow - _lastAutoRegionSearchStartedUtc >=
                       TimeSpan.FromMilliseconds(missingRegionRetryDelay);
            }

            if (!_autoRegionRepairPending)
            {
                return false;
            }

            int retryDelay = Math.Max(5000, _overlaySession.EngineOcrIntervalMs * 4);
            return DateTime.UtcNow - _lastAutoRegionSearchStartedUtc >=
                   TimeSpan.FromMilliseconds(retryDelay);
        }

        public void UpdateWindowPosition()
        {
            SizeOverlayToVirtualScreen();
            ApplyPairOverlay();
            UpdateDebugCaptureMaskBounds();
            if (DebugSamplingMinimizeButton?.Visibility == Visibility.Visible)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(UpdateDebugCaptureMaskBounds));
            }
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

        private void ExcludeOverlayFromCapture()
        {
            var version = new NativeOsVersionInfo
            {
                Size = Marshal.SizeOf(typeof(NativeOsVersionInfo)),
                ServicePack = new string('\0', 128)
            };
            if (RtlGetVersion(ref version) != 0 ||
                version.MajorVersion < 10 ||
                (version.MajorVersion == 10 && version.BuildNumber < 19041))
            {
                Logger.Log.Info(
                    "Overlay capture exclusion requires Windows 10 version 2004 or later; " +
                    "the desktop capture may include the subtitle overlay on this Windows version.");
                return;
            }

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (!SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture))
            {
                Logger.Log.Warn(
                    "Windows could not exclude the subtitle overlay from screen capture: " +
                    new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
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
            if (_overlaySession.IsClickThrough && !_dragHandleInteractive && !_debugControlInteractive)
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
            if (debug)
            {
                long now = Stopwatch.GetTimestamp();
                long previous = Interlocked.Exchange(ref _lastUpdateTextTickTimestamp, now);
                if (previous != 0)
                {
                    double gapMs = (now - previous) * 1000.0 / Stopwatch.Frequency;
                    if (gapMs >= 750)
                    {
                        Logger.Log.Warn($"[Dispatcher timer gap] UpdateText tick gapMs={gapMs:F1}");
                    }
                }
            }

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
            HeaderText.Visibility = Visibility.Collapsed;
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

            double displayScale = GetDisplayScale(display);
            System.Windows.Point canvasPoint = DisplayToCanvas(display, displayScale);
            double width = display.Width / displayScale;
            double height = display.Height / displayScale;
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
            HeaderText.Visibility = _overlaySession.RecognitionRunning &&
                _overlaySession.SubtitlesVisible &&
                !string.IsNullOrEmpty(body.Header)
                ? Visibility.Visible
                : Visibility.Collapsed;

            bool headerChromeVisible = _overlaySession.RecognitionRunning &&
                _overlaySession.SubtitlesVisible &&
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

            double displayScale = GetDisplayScale(body.Display);
            System.Windows.Point canvasPoint = DisplayToCanvas(body.Display, displayScale);
            Canvas.SetLeft(DarkScreenText, canvasPoint.X);
            Canvas.SetTop(DarkScreenText, canvasPoint.Y);
            DarkScreenText.Width = body.Display.Width / displayScale;
            DarkScreenText.Height = body.Display.Height / displayScale;
            DarkScreenText.MaxHeight = body.Display.Height / displayScale;
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

            double displayScale = GetDisplayScale(echo.Display);
            System.Windows.Point canvasPoint = DisplayToCanvas(echo.Display, displayScale);
            Canvas.SetLeft(DialogueChoiceText, canvasPoint.X);
            Canvas.SetTop(DialogueChoiceText, canvasPoint.Y);
            DialogueChoiceText.Width = echo.Display.Width / displayScale;
            DialogueChoiceText.Text = echo.Content;
            DialogueChoiceText.Visibility = Visibility.Visible;
            System.Windows.Controls.Panel.SetZIndex(DialogueChoiceText, echo.RecognitionOrder);

            var transform = (System.Windows.Media.TranslateTransform)DialogueChoiceText.RenderTransform;
            if (!echo.FollowsVoicePrimary)
            {
                transform.Y = 0;
                DialogueChoiceText.Height = echo.Display.Height / displayScale;
                DialogueChoiceText.MaxHeight = echo.Display.Height / displayScale;
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

            double displayScale = GetDisplayScale(body.Display);
            System.Windows.Point canvasPoint = DisplayToCanvas(body.Display, displayScale);
            Canvas.SetLeft(block, canvasPoint.X);
            Canvas.SetTop(block, canvasPoint.Y);
            block.Width = body.Display.Width / displayScale;
            block.Height = body.Display.Height / displayScale;
            block.FontSize = Config.Get<int>("Size");
            block.Text = string.IsNullOrEmpty(body.Header)
                ? body.Content
                : body.Header + Environment.NewLine + body.Content;
            block.Visibility = Visibility.Visible;
            System.Windows.Controls.Panel.SetZIndex(block, body.RecognitionOrder);
        }

        private System.Windows.Point DisplayToCanvas(OverlayRect display)
        {
            return DisplayToCanvas(display, GetDisplayScale(display));
        }

        private System.Windows.Point DisplayToCanvas(OverlayRect display, double displayScale)
        {
            return new System.Windows.Point(
                display.X / displayScale - SystemParameters.VirtualScreenLeft,
                display.Y / displayScale - SystemParameters.VirtualScreenTop);
        }

        private double GetDisplayScale(OverlayRect display)
        {
            if (display == null || !display.IsValid)
            {
                return Scale;
            }

            var anchor = new System.Drawing.Point(
                display.X + display.Width / 2,
                display.Y + display.Height / 2);
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Bounds.Contains(anchor))
                {
                    return GetScaleForScreen(screen);
                }
            }

            return Scale;
        }

        private void EnsurePairBuffers(int count)
        {
            while (_pairLastBinary.Count < count)
            {
                _pairLastBinary.Add(null);
                _pairLastOcrBinary.Add(null);
                _pairPendingOcrBinary.Add(null);
                _pairCapturedBitmaps.Add(null);
                _pairCapturedMats.Add(null);
                _pairOcrDiagnosticContexts.Add(new PairOcrDiagnosticContext());
            }
        }

        private void UpdatePairOcrDiagnosticContext(
            int pairIndex,
            PairSamplingResult sample,
            long samplingId,
            long sampleStartedTimestamp)
        {
            if (!debug || pairIndex < 0 || pairIndex >= _pairOcrDiagnosticContexts.Count || sample == null)
            {
                return;
            }

            PairOcrDiagnosticContext context = _pairOcrDiagnosticContexts[pairIndex];
            if (!sample.Empty && sample.ChangeRatioFromPrevious > ChangeThreshold &&
                context.FirstPixelChangeTicks == 0)
            {
                context.FirstPixelChangeTicks = sampleStartedTimestamp;
                context.FirstPixelChangeSampleId = samplingId;
            }

            if (sample.Sample != null && sample.Sample.Empty && sample.Sample.Stable)
            {
                context.Reset();
                return;
            }

            if (sample.Sample != null && sample.Sample.Changed && sample.Sample.Stable && !sample.Empty)
            {
                if (context.FirstPixelChangeTicks == 0)
                {
                    context.FirstPixelChangeTicks = sampleStartedTimestamp;
                    context.FirstPixelChangeSampleId = samplingId;
                }

                if (context.QualifiedTicks == 0)
                {
                    context.QualifiedTicks = sampleStartedTimestamp;
                    context.QualifiedSampleId = samplingId;
                }
            }
            else if (sample.Sample != null && sample.Sample.Stable && !sample.Sample.Changed &&
                     context.QualifiedTicks == 0)
            {
                context.Reset();
            }
        }

        private void CommitPairOcrBaseline(int pairIndex)
        {
            if (pairIndex < 0 || pairIndex >= _pairPendingOcrBinary.Count)
            {
                return;
            }

            _pairLastOcrBinary[pairIndex]?.Dispose();
            _pairLastOcrBinary[pairIndex] = _pairPendingOcrBinary[pairIndex];
            _pairPendingOcrBinary[pairIndex] = null;
        }

        private void DiscardPairOcrBaseline(int pairIndex)
        {
            if (pairIndex < 0 || pairIndex >= _pairPendingOcrBinary.Count)
            {
                return;
            }

            _pairPendingOcrBinary[pairIndex]?.Dispose();
            _pairPendingOcrBinary[pairIndex] = null;
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
                    IsHitTestVisible = false,
                    RenderTransform = new TranslateTransform()
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

        private Bitmap CaptureRect(OverlayRect rect)
        {
            var bounds = new System.Drawing.Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
            return CaptureRectangle(bounds);
        }

        private static bool SameShape(Mat left, Mat right)
        {
            return left != null && right != null &&
                   left.Size() == right.Size() &&
                   left.Channels() == right.Channels();
        }

        private static string FormatDiagnosticRatio(double ratio)
        {
            return ratio < 0 ? "n/a" : ratio.ToString("F4");
        }

        private bool IsStableVsPrevious(
            Mat currentBinary,
            Mat previous,
            out double changeRatio)
        {
            changeRatio = -1;
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
                changeRatio = (double)nonZero / (diff.Rows * diff.Cols);
                return changeRatio <= ChangeThreshold;
            }
        }

        private bool IsChangedVsLastOcr(
            Mat currentBinary,
            Mat lastOcr,
            out double changeRatio)
        {
            changeRatio = -1;
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
                changeRatio = (double)nonZero / (diff.Rows * diff.Cols);
                return changeRatio > ChangeThreshold;
            }
        }

        private static bool TextMayBeClipped(OCRResult result, int width, int height)
        {
            if (result?.TextBlocks == null || width <= 0 || height <= 0)
            {
                return false;
            }

            float horizontalMargin = Math.Max(3, width * 0.025f);
            float verticalMargin = Math.Max(3, height * 0.06f);
            foreach (PaddleOCRSharp.TextBlock block in result.TextBlocks)
            {
                if (block == null || block.Score < 0.4f || block.BoxPoints == null || block.BoxPoints.Length == 0)
                {
                    continue;
                }

                float minX = block.BoxPoints.Min(point => point.X);
                float minY = block.BoxPoints.Min(point => point.Y);
                float maxX = block.BoxPoints.Max(point => point.X);
                float maxY = block.BoxPoints.Max(point => point.Y);
                if (minX <= horizontalMargin ||
                    maxX >= width - horizontalMargin ||
                    minY <= verticalMargin ||
                    maxY >= height - verticalMargin)
                {
                    return true;
                }
            }

            return false;
        }

        private void TryStartBusyOcr()
        {
            if (_isOcrRunning || Interlocked.CompareExchange(ref OCR_TIMER, 0, 0) != 0)
            {
                return;
            }

            int? slot = _overlaySession.BusyOcrSlot;
            if (!slot.HasValue)
            {
                return;
            }

            bool slotNeedsMatcher = slot.Value != LiveOverlaySession.AutoRegionOcrSlot &&
                slot.Value != LiveOverlaySession.DialogueOptionsOcrSlot;
            if (slotNeedsMatcher &&
                (data == null || data.Matcher == null))
            {
                if (!_matcherNotReadyLogged)
                {
                    _matcherNotReadyLogged = true;
                    _matcherNotReadySinceTicks = Stopwatch.GetTimestamp();
                    if (debug)
                    {
                        Logger.Log.Warn(
                            $"[OCR gate] waiting for matcher; busySlot={slot.Value}, " +
                            $"queueCount={_overlaySession.OcrQueue.Count}, engineReady={data?.engine != null}");
                    }
                }
                return;
            }

            if (_matcherNotReadyLogged && data?.Matcher != null)
            {
                double waitMs = _matcherNotReadySinceTicks == 0
                    ? 0
                    : (Stopwatch.GetTimestamp() - _matcherNotReadySinceTicks) * 1000.0 / Stopwatch.Frequency;
                _matcherNotReadyLogged = false;
                _matcherNotReadySinceTicks = 0;
                if (debug)
                {
                    Logger.Log.Info($"[OCR gate] matcher ready; delayed OCR start waitMs={waitMs:F1}");
                }
            }

            if (slot.Value == LiveOverlaySession.AutoRegionOcrSlot)
            {
                TryStartAutoRegionOcr(_overlaySession.BusyOcrGeneration ?? -1);
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
            EnsurePairBuffers(idx + 1);
            PairOcrDiagnosticContext diagnosticContext = _pairOcrDiagnosticContexts[idx];
            long firstPixelChangeTicks = diagnosticContext.FirstPixelChangeTicks;
            long firstPixelChangeSampleId = diagnosticContext.FirstPixelChangeSampleId;
            long qualifiedTicks = diagnosticContext.QualifiedTicks;
            long qualifiedSampleId = diagnosticContext.QualifiedSampleId;
            diagnosticContext.Reset();
            _pairPendingOcrBinary[idx]?.Dispose();
            _pairPendingOcrBinary[idx] = lastBinary?.Clone();

            Mat frame = _pairCapturedMats[idx];
            Bitmap bitmap = _pairCapturedBitmaps[idx];
            _pairCapturedMats[idx] = null;
            _pairCapturedBitmaps[idx] = null;
            SetWindowPos(new WindowInteropHelper(this).Handle, -1, 0, 0, 0, 0, 1 | 2);
            _ = TriggerOcrAsync(
                frame,
                bitmap,
                pairIndex: idx,
                sourceSampleId: qualifiedSampleId,
                firstPixelChangeSampleId: firstPixelChangeSampleId,
                firstPixelChangeTicks: firstPixelChangeTicks,
                qualifiedTicks: qualifiedTicks);
        }

        private void ContinueOcrPipeline()
        {
            if (_forceRefreshPending && !_isOcrRunning &&
                Interlocked.CompareExchange(ref OCR_TIMER, 0, 0) == 0)
            {
                _forceRefreshPending = false;
                ForceRefreshCurrentSubtitle();
                return;
            }

            TryStartBusyOcr();
        }

        private void TryStartAutoRegionOcr(int generation)
        {
            if (_autoRegionDetectionLocked || _autoRegionOcrInFlight)
            {
                return;
            }

            if (data == null ||
                data.engine == null ||
                data.Matcher == null)
            {
                _overlaySession.CompleteAutoRegionOcr(generation);
                return;
            }

            if (!TryGetFirstValidCaptureScreen(out System.Drawing.Rectangle screenBounds))
            {
                screenBounds = Screen.PrimaryScreen?.Bounds ?? System.Drawing.Rectangle.Empty;
            }
            if (screenBounds.Width <= 0 || screenBounds.Height <= 0)
            {
                _overlaySession.CompleteAutoRegionOcr(generation);
                return;
            }

            var searchBounds = new System.Drawing.Rectangle(
                screenBounds.Left + (int)Math.Round(screenBounds.Width * 0.09),
                screenBounds.Top + (int)Math.Round(screenBounds.Height * 0.38),
                (int)Math.Round(screenBounds.Width * 0.82),
                (int)Math.Round(screenBounds.Height * 0.62));
            DateTime previousSearchStartedUtc = _lastAutoRegionSearchStartedUtc;
            _lastAutoRegionSearchStartedUtc = DateTime.UtcNow;
            string triggerReason = _autoRegionRepairReason;
            if (debug && previousSearchStartedUtc != DateTime.MinValue)
            {
                Logger.Log.Debug(
                    $"Automatic subtitle scan interval: " +
                    $"trigger={triggerReason}, " +
                    $"elapsedMs={(long)(_lastAutoRegionSearchStartedUtc - previousSearchStartedUtc).TotalMilliseconds}");
            }

            IReadOnlyList<RegionPair> pairs = _overlaySession.Pairs;
            int primaryIndex = FindPrimaryPairIndex(pairs);
            int expectedPrimaryPairId = primaryIndex >= 0 ? pairs[primaryIndex].Id : 0;
            OverlayRect expectedPrimaryCapture = OverlayRect.Invalid;
            if (primaryIndex >= 0 && pairs[primaryIndex].Capture != null)
            {
                OverlayRect currentCapture = pairs[primaryIndex].Capture;
                expectedPrimaryCapture = new OverlayRect(
                    currentCapture.X,
                    currentCapture.Y,
                    currentCapture.Width,
                    currentCapture.Height);
            }

            // Reserve the OCR slot before starting capture so the dispatcher timer
            // cannot queue another sampling pass while the frame is being prepared.
            _autoRegionOcrInFlight = true;
            _isOcrRunning = true;
            _ = CaptureAutoRegionFrameAsync(
                screenBounds,
                searchBounds,
                generation,
                triggerReason,
                expectedPrimaryPairId,
                expectedPrimaryCapture,
                debug ? Stopwatch.GetTimestamp() : 0);
        }

        private async Task CaptureAutoRegionFrameAsync(
            System.Drawing.Rectangle screenBounds,
            System.Drawing.Rectangle searchBounds,
            int generation,
            string triggerReason,
            int expectedPrimaryPairId,
            OverlayRect expectedPrimaryCapture,
            long pipelineStartedTimestamp)
        {
            AutoRegionCaptureResult captured = null;
            Exception failure = null;
            long captureTaskQueuedTimestamp = debug ? Stopwatch.GetTimestamp() : 0;
            try
            {
                captured = await Task.Run(() =>
                {
                    var result = new AutoRegionCaptureResult();
                    long captureWorkerStartedTimestamp = debug
                        ? Stopwatch.GetTimestamp()
                        : 0;
                    if (debug)
                    {
                        result.QueueMs = (captureWorkerStartedTimestamp - captureTaskQueuedTimestamp) *
                            1000.0 / Stopwatch.Frequency;
                    }
                    Stopwatch captureWorkerStopwatch = debug ? Stopwatch.StartNew() : null;
                    Stopwatch stageStopwatch = debug ? Stopwatch.StartNew() : null;
                    result.Bitmap = CaptureRectangle(searchBounds);
                    result.CaptureMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                    if (result.Bitmap == null)
                    {
                        result.WorkerMs = captureWorkerStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                        return result;
                    }

                    stageStopwatch = debug ? Stopwatch.StartNew() : null;
                    result.Frame = result.Bitmap.ToMat();
                    result.BitmapToMatMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                    result.WorkerMs = captureWorkerStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                    return result;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                captured?.Frame?.Dispose();
                captured?.Bitmap?.Dispose();
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_overlaySession.IsAutoRegionOcrCurrent(
                    generation, expectedPrimaryPairId, expectedPrimaryCapture))
                {
                    captured?.Frame?.Dispose();
                    captured?.Bitmap?.Dispose();
                    CompleteAutoRegionCapture(generation);
                    return;
                }

                if (failure != null)
                {
                    if (debug)
                    {
                        double capturePipelineMs = (Stopwatch.GetTimestamp() - pipelineStartedTimestamp) *
                            1000.0 / Stopwatch.Frequency;
                        Logger.Log.Debug(
                            $"[AutoRegion capture] trigger={triggerReason}, result=exception, " +
                            $"totalMs={capturePipelineMs:F1}, error={failure.Message}");
                    }
                    Logger.Log.Warn(
                        $"Automatic subtitle-region scan could not capture the screen " +
                        $"(trigger={triggerReason}): {failure.Message}");
                    captured?.Frame?.Dispose();
                    captured?.Bitmap?.Dispose();
                    ClearAutoRegionRepairAfterUnconfirmedScan();
                    CompleteAutoRegionCapture(generation);
                    return;
                }

                if (captured?.Frame == null || captured.Bitmap == null)
                {
                    if (debug)
                    {
                        double capturePipelineMs = (Stopwatch.GetTimestamp() - pipelineStartedTimestamp) *
                            1000.0 / Stopwatch.Frequency;
                        Logger.Log.Debug(
                            $"[AutoRegion capture] trigger={triggerReason}, result=no-frame, " +
                            $"queueMs={captured?.QueueMs ?? 0:F1}, " +
                            $"captureMs={captured?.CaptureMs ?? 0:F1}, " +
                            $"bitmapToMatMs={captured?.BitmapToMatMs ?? 0:F1}, " +
                            $"workerMs={captured?.WorkerMs ?? 0:F1}, totalMs={capturePipelineMs:F1}");
                    }
                    ClearAutoRegionRepairAfterUnconfirmedScan();
                    CompleteAutoRegionCapture(generation);
                    return;
                }

                _ = RecognizeAutoRegionAsync(
                    captured.Frame,
                    captured.Bitmap,
                    screenBounds,
                    searchBounds,
                    generation,
                    triggerReason,
                    expectedPrimaryPairId,
                    expectedPrimaryCapture,
                    captured.QueueMs,
                    captured.CaptureMs,
                    captured.BitmapToMatMs,
                    captured.WorkerMs,
                    pipelineStartedTimestamp);
            }));
        }

        private void CompleteAutoRegionCapture(int generation)
        {
            _isOcrRunning = false;
            _autoRegionOcrInFlight = false;
            _overlaySession.CompleteAutoRegionOcr(generation);
            ContinueOcrPipeline();
        }

        private void ClearAutoRegionRepairAfterUnconfirmedScan(bool preservePending = false)
        {
            if (preservePending)
            {
                return;
            }

            IReadOnlyList<RegionPair> pairs = _overlaySession.Pairs;
            int primaryIndex = FindPrimaryPairIndex(pairs);
            if (primaryIndex < 0 || !pairs[primaryIndex].Capture.IsValid)
            {
                return;
            }

            _autoRegionRepairPending = false;
            _autoRegionRepairReason = "awaiting-primary-ocr-evidence";
        }

        private async Task RecognizeAutoRegionAsync(
            Mat frame,
            Bitmap bitmap,
            System.Drawing.Rectangle screenBounds,
            System.Drawing.Rectangle searchBounds,
            int generation,
            string triggerReason,
            int expectedPrimaryPairId,
            OverlayRect expectedPrimaryCapture,
            double captureQueueMs,
            double captureMs,
            double bitmapToMatMs,
            double captureWorkerMs,
            long pipelineStartedTimestamp)
        {
            string ocrGame = _overlaySession.AppliedGame;
            int broadFallbackMissLimit = _overlaySession.HasValidCapture ? 2 : 6;
            Stopwatch postProcessStopwatch = null;
            AutoRegionScanResult scan = null;
            try
            {
                scan = await Task.Run(() =>
                {
                    Stopwatch workerStopwatch = debug ? Stopwatch.StartNew() : null;
                    Stopwatch candidateStopwatch = debug ? Stopwatch.StartNew() : null;
                    System.Drawing.Rectangle? visualBounds = null;
                    System.Drawing.Rectangle ocrBounds = searchBounds;
                    Mat ocrFrame = frame;
                    Mat candidateFrame = null;
                    try
                    {
                        System.Drawing.Rectangle? localCandidate = SubtitleTextRegionDetector.FindCandidate(frame);
                        double candidateDetectionMs = candidateStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                        if (localCandidate.HasValue)
                        {
                            System.Drawing.Rectangle local = localCandidate.Value;
                            visualBounds = new System.Drawing.Rectangle(
                                searchBounds.Left + local.Left,
                                searchBounds.Top + local.Top,
                                local.Width,
                                local.Height);

                            OverlayRect expanded = CreateAutoCaptureRegion(visualBounds.Value, screenBounds);
                            var expandedBounds = new System.Drawing.Rectangle(
                                expanded.X,
                                expanded.Y,
                                expanded.Width,
                                expanded.Height);
                            bool broadFallbackDue = Interlocked.CompareExchange(
                                ref _autoRegionCcMissesSinceBroadScan,
                                0,
                                0) >= broadFallbackMissLimit;
                            if (broadFallbackDue)
                            {
                                Interlocked.Exchange(ref _autoRegionCcMissesSinceBroadScan, 0);
                            }
                            else
                            {
                                ocrBounds = System.Drawing.Rectangle.Intersect(expandedBounds, searchBounds);
                                if (ocrBounds.Width > 0 && ocrBounds.Height > 0)
                                {
                                    var localOcrBounds = new OpenCvSharp.Rect(
                                        ocrBounds.Left - searchBounds.Left,
                                        ocrBounds.Top - searchBounds.Top,
                                        ocrBounds.Width,
                                        ocrBounds.Height);
                                    candidateFrame = new Mat(frame, localOcrBounds);
                                    ocrFrame = candidateFrame;
                                }
                                else
                                {
                                    ocrBounds = searchBounds;
                                }
                            }
                        }
                        else
                        {
                            int missedCandidates = Interlocked.Increment(ref _autoRegionCcMissesSinceBroadScan);
                            if (missedCandidates < broadFallbackMissLimit)
                            {
                                return new AutoRegionScanResult
                                {
                                    OcrBounds = searchBounds,
                                    CandidateDetectionMs = candidateDetectionMs,
                                    WorkerMs = workerStopwatch?.Elapsed.TotalMilliseconds ?? 0,
                                    ScanMode = "connected-components-skip",
                                    TriggerReason = triggerReason
                                };
                            }

                            // A slow full-band OCR scan protects against unusual
                            // subtitle colors or styles the connected-component
                            // proposal misses.
                            Interlocked.Exchange(ref _autoRegionCcMissesSinceBroadScan, 0);
                        }

                        Stopwatch ocrStopwatch = debug ? Stopwatch.StartNew() : null;
                        OCRResult ocrResult = data.engine.DetectTextFromMat(ocrFrame);
                        return new AutoRegionScanResult
                        {
                            OcrResult = ocrResult,
                            OcrBounds = ocrBounds,
                            VisualCandidateBounds = visualBounds,
                            CandidateDetectionMs = candidateDetectionMs,
                            OcrMs = ocrStopwatch?.Elapsed.TotalMilliseconds ?? 0,
                            WorkerMs = workerStopwatch?.Elapsed.TotalMilliseconds ?? 0,
                            ScanMode = localCandidate.HasValue
                                ? (ocrBounds == searchBounds ? "connected-components-full-band" : "connected-components-cropped")
                                : "full-band-fallback",
                            TriggerReason = triggerReason
                        };
                    }
                    finally
                    {
                        candidateFrame?.Dispose();
                    }
                });
                postProcessStopwatch = debug ? Stopwatch.StartNew() : null;

                if (!string.Equals(ocrGame, _overlaySession.AppliedGame, StringComparison.Ordinal) ||
                    !_overlaySession.IsAutoRegionOcrCurrent(
                        generation, expectedPrimaryPairId, expectedPrimaryCapture))
                {
                    return;
                }

                if (scan.OcrResult == null)
                {
                    int ccMisses = Interlocked.CompareExchange(ref _autoRegionCcMissesSinceBroadScan, 0, 0);
                    Logger.Log.Debug(
                        $"Connected-component scan found no lower-center subtitle candidate; " +
                        $"OCR was skipped (mode={scan.ScanMode}, misses={ccMisses}/{broadFallbackMissLimit}).");
                    bool broadFallbackStillPending = scan.ScanMode == "connected-components-skip" &&
                        ccMisses < broadFallbackMissLimit;
                    ClearAutoRegionRepairAfterUnconfirmedScan(preservePending: broadFallbackStillPending);
                    return;
                }

                SubtitleTextCandidate candidate = FindMappedSubtitleCandidate(
                    scan.OcrResult,
                    scan.OcrBounds,
                    screenBounds);
                if (candidate == null && scan.VisualCandidateBounds.HasValue)
                {
                    candidate = FindOcrConfirmedCandidate(
                        scan.OcrResult,
                        scan.OcrBounds,
                        scan.VisualCandidateBounds.Value,
                        screenBounds);
                }
                if (candidate == null)
                {
                    if (scan.VisualCandidateBounds.HasValue)
                    {
                        Interlocked.Increment(ref _autoRegionCcMissesSinceBroadScan);
                    }
                    else
                    {
                        Interlocked.Exchange(ref _autoRegionCcMissesSinceBroadScan, 0);
                    }
                    Logger.Log.Debug("Automatic subtitle-region scan found no OCR-confirmed text near the candidate.");
                    ClearAutoRegionRepairAfterUnconfirmedScan();
                    return;
                }

                if (!IsQualifiedAutoRegionCandidate(candidate))
                {
                    Logger.Log.Debug(
                        "Automatic subtitle scan candidate rejected: " +
                        $"requires at least two OCR lines and both speaker name and dialogue; lines={candidate.LineCount}.");
                    ClearAutoRegionRepairAfterUnconfirmedScan();
                    return;
                }

                Interlocked.Exchange(ref _autoRegionCcMissesSinceBroadScan, 0);

                IReadOnlyList<RegionPair> pairs = _overlaySession.Pairs;
                int primaryIndex = FindPrimaryPairIndex(pairs);
                OverlayRect currentCapture = primaryIndex >= 0
                    ? pairs[primaryIndex].Capture
                    : OverlayRect.Invalid;
                System.Drawing.Rectangle detectionBounds = scan.VisualCandidateBounds.HasValue
                    ? System.Drawing.Rectangle.Union(scan.VisualCandidateBounds.Value, candidate.Bounds)
                    : candidate.Bounds;
                OverlayRect recommendedCapture = CreateAutoCaptureRegion(detectionBounds, screenBounds);
                if (ContainsRegion(currentCapture, detectionBounds))
                {
                    if (primaryIndex >= 0 && !pairs[primaryIndex].Display.IsValid)
                    {
                        OverlayRect defaultDisplay = CreateAutoDisplayRegion(
                            candidate.Bounds,
                            currentCapture,
                            screenBounds);
                        _overlaySession.ApplyAutoDetectedRegion(
                            generation,
                            expectedPrimaryPairId,
                            expectedPrimaryCapture,
                            currentCapture,
                            defaultDisplay);
                        ApplyPairOverlay();
                    }

                    _autoRegionRepairPending = false;
                    _autoRegionRepairReason = "configured-region-confirmed";
                    Logger.Log.Debug("Automatic scan confirmed the configured capture region.");
                }
                else
                {
                    OverlayRect capture = recommendedCapture;
                    OverlayRect display = CreateAutoDisplayRegion(candidate.Bounds, capture, screenBounds);
                    if (_overlaySession.ApplyAutoDetectedRegion(
                        generation,
                        expectedPrimaryPairId,
                        expectedPrimaryCapture,
                        capture,
                        display))
                    {
                        DisposePairBuffers();
                        ApplyPairOverlay();
                        Logger.Log.Info(
                            $"Automatic subtitle region updated: game={ocrGame}, capture={capture.ToCsv()}");
                    }
                    else
                    {
                        Logger.Log.Debug("Automatic subtitle region was not changed; process scan remains unlocked.");
                        ClearAutoRegionRepairAfterUnconfirmedScan();
                        return;
                    }

                    _autoRegionRepairPending = false;
                    _autoRegionRepairReason = "region-auto-corrected";
                }

                LockAutoRegionDetection(
                    "automatic scan confirmed a multi-line speaker/dialogue region");

            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"Automatic subtitle-region OCR failed: {ex.Message}");
                ClearAutoRegionRepairAfterUnconfirmedScan();
            }
            finally
            {
                if (debug)
                {
                    int ocrBlockCount = scan?.OcrResult?.TextBlocks?.Count() ?? 0;
                    double totalMs = (Stopwatch.GetTimestamp() - pipelineStartedTimestamp) *
                        1000.0 / Stopwatch.Frequency;
                    Logger.Log.Debug(
                        $"Automatic subtitle scan timing: mode={scan?.ScanMode ?? "failed"}, " +
                        $"trigger={scan?.TriggerReason ?? triggerReason}, " +
                        $"search={searchBounds.Width}x{searchBounds.Height}, " +
                        $"ocr={scan?.OcrBounds.Width ?? 0}x{scan?.OcrBounds.Height ?? 0}, " +
                        $"captureMs={captureMs:F1}, bitmapToMatMs={bitmapToMatMs:F1}, " +
                        $"captureQueueMs={captureQueueMs:F1}, captureWorkerMs={captureWorkerMs:F1}, " +
                        $"ccMs={(scan?.CandidateDetectionMs ?? 0):F1}, " +
                        $"ocrMs={(scan?.OcrMs ?? 0):F1}, workerMs={(scan?.WorkerMs ?? 0):F1}, " +
                        $"postProcessMs={(postProcessStopwatch?.Elapsed.TotalMilliseconds ?? 0):F1}, " +
                        $"totalMs={totalMs:F1}, blocks={ocrBlockCount}");
                }
                frame?.Dispose();
                bitmap?.Dispose();
                _isOcrRunning = false;
                _autoRegionOcrInFlight = false;
                _overlaySession.CompleteAutoRegionOcr(generation);
                _ = Dispatcher.BeginInvoke(new Action(ContinueOcrPipeline));
            }
        }

        private SubtitleTextCandidate FindMappedSubtitleCandidate(
            OCRResult result,
            System.Drawing.Rectangle searchBounds,
            System.Drawing.Rectangle screenBounds)
        {
            var blocks = new List<SubtitleTextCandidate>();
            IEnumerable<PaddleOCRSharp.TextBlock> recognizedBlocks = result?.TextBlocks ??
                Enumerable.Empty<PaddleOCRSharp.TextBlock>();
            foreach (PaddleOCRSharp.TextBlock block in recognizedBlocks)
            {
                if (block == null || string.IsNullOrWhiteSpace(block.Text) ||
                    block.Score < 0.42f || block.BoxPoints == null || block.BoxPoints.Length == 0)
                {
                    continue;
                }

                float minX = block.BoxPoints.Min(point => point.X);
                float minY = block.BoxPoints.Min(point => point.Y);
                float maxX = block.BoxPoints.Max(point => point.X);
                float maxY = block.BoxPoints.Max(point => point.Y);
                var bounds = System.Drawing.Rectangle.FromLTRB(
                    searchBounds.Left + (int)Math.Floor(minX),
                    searchBounds.Top + (int)Math.Floor(minY),
                    searchBounds.Left + (int)Math.Ceiling(maxX),
                    searchBounds.Top + (int)Math.Ceiling(maxY));
                if (!screenBounds.Contains(bounds) ||
                    bounds.Left + bounds.Width / 2 < screenBounds.Left + screenBounds.Width * 0.12 ||
                    bounds.Left + bounds.Width / 2 > screenBounds.Right - screenBounds.Width * 0.12 ||
                    bounds.Top + bounds.Height / 2 < screenBounds.Top + screenBounds.Height * 0.45)
                {
                    continue;
                }

                blocks.Add(new SubtitleTextCandidate
                {
                    Text = block.Text.Trim(),
                    Bounds = bounds,
                    Score = block.Score
                });
            }

            blocks = blocks
                .OrderByDescending(block => block.Bounds.Top + block.Bounds.Height / 2)
                .ThenBy(block => Math.Abs(
                    block.Bounds.Left + block.Bounds.Width / 2 -
                    (screenBounds.Left + screenBounds.Width / 2)))
                .Take(8)
                .OrderBy(block => block.Bounds.Top)
                .ThenBy(block => block.Bounds.Left)
                .ToList();

            SubtitleTextCandidate best = null;
            double bestScore = double.MinValue;
            for (int start = 0; start < blocks.Count; start++)
            {
                string combinedText = string.Empty;
                System.Drawing.Rectangle combinedBounds = System.Drawing.Rectangle.Empty;
                float combinedConfidence = 0;
                // A speaker label plus two or three wrapped subtitle lines can
                // arrive as separate OCR blocks; evaluate the whole cluster.
                for (int end = start; end < Math.Min(blocks.Count, start + 4); end++)
                {
                    SubtitleTextCandidate block = blocks[end];
                    if (end > start)
                    {
                        SubtitleTextCandidate previous = blocks[end - 1];
                        int verticalGap = block.Bounds.Top - previous.Bounds.Bottom;
                        int maxGap = Math.Max(
                            (int)Math.Round(screenBounds.Height * 0.025),
                            previous.Bounds.Height * 2);
                        int centerDelta = Math.Abs(
                            (block.Bounds.Left + block.Bounds.Width / 2) -
                            (previous.Bounds.Left + previous.Bounds.Width / 2));
                        if (verticalGap > maxGap || centerDelta > screenBounds.Width * 0.28)
                        {
                            break;
                        }
                    }

                    combinedText = string.IsNullOrEmpty(combinedText)
                        ? block.Text
                        : combinedText + "\n" + block.Text;
                    combinedBounds = combinedBounds.IsEmpty
                        ? block.Bounds
                        : System.Drawing.Rectangle.Union(combinedBounds, block.Bounds);
                    combinedConfidence += block.Score;

                    foreach (string text in end == start
                        ? new[] { combinedText }
                        : new[] { combinedText, combinedText.Replace("\n", string.Empty) })
                    {
                        if (text.Length < 2)
                        {
                            continue;
                        }

                        MatchResult match = data.Matcher.FindMatchWithHeaderSeparated(text, out _);
                        if (string.IsNullOrWhiteSpace(match.Header) && string.IsNullOrWhiteSpace(match.Content))
                        {
                            continue;
                        }

                        double xCenter = combinedBounds.Left + combinedBounds.Width / 2.0;
                        double yCenter = combinedBounds.Top + combinedBounds.Height / 2.0;
                        double centerPenalty = Math.Abs(xCenter - (screenBounds.Left + screenBounds.Width / 2.0)) /
                            Math.Max(1, screenBounds.Width);
                        double verticalPenalty = Math.Abs(yCenter - (screenBounds.Top + screenBounds.Height * 0.77)) /
                            Math.Max(1, screenBounds.Height);
                        double score = combinedConfidence / (end - start + 1)
                            + (end > start ? 0.08 : 0)
                            - centerPenalty * 0.18
                            - verticalPenalty * 0.12;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = new SubtitleTextCandidate
                            {
                                Text = text,
                                Bounds = combinedBounds,
                                Score = (float)score,
                                LineCount = CountNonEmptyLines(combinedText)
                            };
                        }
                    }
                }
            }

            return best;
        }

        private SubtitleTextCandidate FindOcrConfirmedCandidate(
            OCRResult result,
            System.Drawing.Rectangle ocrBounds,
            System.Drawing.Rectangle visualHint,
            System.Drawing.Rectangle screenBounds)
        {
            var acceptanceBounds = System.Drawing.Rectangle.Inflate(
                visualHint,
                Math.Max(1, (int)Math.Round(screenBounds.Width * 0.08)),
                Math.Max(1, (int)Math.Round(screenBounds.Height * 0.16)));
            var accepted = new List<SubtitleTextCandidate>();
            IEnumerable<PaddleOCRSharp.TextBlock> blocks = result?.TextBlocks ??
                Enumerable.Empty<PaddleOCRSharp.TextBlock>();
            foreach (PaddleOCRSharp.TextBlock block in blocks)
            {
                if (block == null || string.IsNullOrWhiteSpace(block.Text) ||
                    block.Score < 0.42f || block.BoxPoints == null || block.BoxPoints.Length == 0)
                {
                    continue;
                }

                float minX = block.BoxPoints.Min(point => point.X);
                float minY = block.BoxPoints.Min(point => point.Y);
                float maxX = block.BoxPoints.Max(point => point.X);
                float maxY = block.BoxPoints.Max(point => point.Y);
                var bounds = System.Drawing.Rectangle.FromLTRB(
                    ocrBounds.Left + (int)Math.Floor(minX),
                    ocrBounds.Top + (int)Math.Floor(minY),
                    ocrBounds.Left + (int)Math.Ceiling(maxX),
                    ocrBounds.Top + (int)Math.Ceiling(maxY));
                if (System.Drawing.Rectangle.Intersect(bounds, acceptanceBounds).IsEmpty ||
                    bounds.Left + bounds.Width / 2 < screenBounds.Left + screenBounds.Width * 0.12 ||
                    bounds.Left + bounds.Width / 2 > screenBounds.Right - screenBounds.Width * 0.12 ||
                    bounds.Top + bounds.Height / 2 < screenBounds.Top + screenBounds.Height * 0.45)
                {
                    continue;
                }

                accepted.Add(new SubtitleTextCandidate
                {
                    Text = block.Text.Trim(),
                    Bounds = bounds,
                    Score = block.Score
                });
            }

            if (accepted.Count == 0)
            {
                return null;
            }

            accepted = accepted
                .OrderBy(block => Math.Abs(
                    block.Bounds.Left + block.Bounds.Width / 2.0 -
                    (visualHint.Left + visualHint.Width / 2.0)))
                .ThenBy(block => Math.Abs(
                    block.Bounds.Top + block.Bounds.Height / 2.0 -
                    (visualHint.Top + visualHint.Height / 2.0)))
                .Take(4)
                .ToList();
            System.Drawing.Rectangle combinedBounds = accepted
                .Select(block => block.Bounds)
                .Aggregate(System.Drawing.Rectangle.Union);
            return new SubtitleTextCandidate
            {
                Text = string.Join("\n", accepted.Select(block => block.Text)),
                Bounds = combinedBounds,
                Score = accepted.Average(block => block.Score),
                LineCount = CountNonEmptyLines(string.Join("\n", accepted.Select(block => block.Text)))
            };
        }

        private bool IsQualifiedAutoRegionCandidate(SubtitleTextCandidate candidate)
        {
            if (candidate == null || candidate.LineCount < 2 || data?.Matcher == null)
            {
                return false;
            }

            MatchResult match = data.Matcher.FindMatchWithHeaderSeparated(candidate.Text, out _);
            return !string.IsNullOrWhiteSpace(match.Header) &&
                !string.IsNullOrWhiteSpace(match.Content);
        }

        private void LockAutoRegionDetection(string reason)
        {
            if (_autoRegionDetectionLocked)
            {
                return;
            }

            _autoRegionDetectionLocked = true;
            _autoRegionRepairPending = false;
            _autoRegionRepairReason = "multi-line-region-locked";
            _overlaySession.CancelQueuedAutoRegionOcr();
            Logger.Log.Info(
                "Automatic subtitle-region scan locked for this process: " + reason + ".");
        }

        private static int CountNonEmptyLines(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Count(line => !string.IsNullOrWhiteSpace(line));
        }

        private static bool ContainsRegion(OverlayRect capture, System.Drawing.Rectangle region)
        {
            if (capture == null || !capture.IsValid)
            {
                return false;
            }

            return region.Left >= capture.X &&
                   region.Top >= capture.Y &&
                   region.Right <= capture.X + capture.Width &&
                   region.Bottom <= capture.Y + capture.Height;
        }

        private static OverlayRect CreateAutoCaptureRegion(
            System.Drawing.Rectangle subtitle,
            System.Drawing.Rectangle screenBounds)
        {
            if (subtitle.Width <= 0 || subtitle.Height <= 0 ||
                screenBounds.Width <= 0 || screenBounds.Height <= 0)
            {
                return OverlayRect.Invalid;
            }

            // Follow the detected subtitle rows instead of assigning a fixed
            // fraction of the selected screen. Scale the safety margin from the
            // detected text height, with a small cap for unusually tall boxes.
            int horizontalPadding = Math.Max(
                2,
                Math.Min(
                    (int)Math.Round(subtitle.Height * 0.10),
                    (int)Math.Round(screenBounds.Width * 0.025)));
            int verticalPadding = Math.Max(
                2,
                Math.Min(
                    (int)Math.Round(subtitle.Height * 0.10),
                    (int)Math.Round(screenBounds.Height * 0.025)));
            System.Drawing.Rectangle paddedBounds = System.Drawing.Rectangle.Inflate(
                subtitle,
                horizontalPadding,
                verticalPadding);
            System.Drawing.Rectangle clippedBounds = System.Drawing.Rectangle.Intersect(
                paddedBounds,
                screenBounds);
            if (clippedBounds.Width <= 0 || clippedBounds.Height <= 0)
            {
                return OverlayRect.Invalid;
            }

            return new OverlayRect(
                clippedBounds.Left,
                clippedBounds.Top,
                clippedBounds.Width,
                clippedBounds.Height);
        }

        private static OverlayRect CreateAutoDisplayRegion(
            System.Drawing.Rectangle subtitle,
            OverlayRect capture,
            System.Drawing.Rectangle screenBounds)
        {
            int width = Math.Min(
                capture.Width,
                Math.Max(subtitle.Width + 72, (int)Math.Round(screenBounds.Width * 0.48)));
            int height = Math.Min(
                Math.Max(72, (int)Math.Round(screenBounds.Height * 0.12)),
                Math.Max(1, screenBounds.Height));
            int centerX = subtitle.Left + subtitle.Width / 2;
            int x = Math.Max(screenBounds.Left, Math.Min(centerX - width / 2, screenBounds.Right - width));
            int gap = Math.Max(12, (int)Math.Round(screenBounds.Height * 0.018));
            int y = subtitle.Bottom + gap;
            if (y + height > screenBounds.Bottom)
            {
                y = screenBounds.Bottom - height;
            }

            if (y < subtitle.Bottom)
            {
                y = Math.Max(screenBounds.Top, subtitle.Top - height - gap);
            }

            return new OverlayRect(x, y, width, height);
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
            if (HeaderPanel == null || HeaderPanel.Visibility != Visibility.Visible)
            {
                return;
            }

            double headerHeight = HeaderPanel.ActualHeight;
            if (headerHeight <= 0)
            {
                headerHeight = HeaderText.FontSize;
            }

            double targetOffset = -(headerHeight + 4);
            if (Math.Abs(_headerPositionRenderTransform.Y - targetOffset) > 0.1)
            {
                _headerPositionRenderTransform.Y = targetOffset;
            }
        }

        private void HeaderPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateHeaderPosition();
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
            string darkScreenHash = null,
            long sourceSampleId = 0,
            long firstPixelChangeSampleId = 0,
            long firstPixelChangeTicks = 0,
            long qualifiedTicks = 0)
        {
            _isOcrRunning = true;
            string ocrGame = _overlaySession.AppliedGame;
            // Per-pair OCR already has an explicitly configured capture area.
            // Keep all text inside it; filter only requests without a pair region.
            bool useSubtitleTextFilter = !pairIndex.HasValue;
            LRUCache<string, string> imageTextCache = useSubtitleTextFilter
                ? BitmapDict
                : RegionBitmapDict;
            long diagnosticId = ShouldCollectSamplingDiagnostics
                ? Interlocked.Increment(ref _ocrDiagnosticSequence)
                : 0;
            long ocrStartedTimestamp = Stopwatch.GetTimestamp();
            DateTime previousOcrStartedUtc = _lastOcrStartedUtc;
            _lastOcrStartedUtc = DateTime.UtcNow;
            if (ShouldCollectSamplingDiagnostics)
            {
                string sourceKind = pairIndex.HasValue
                    ? forceRefresh ? "pair-force-refresh" : "pair-live"
                    : !string.IsNullOrEmpty(darkScreenHash) ? "dark-screen" : "other";
                if (debug)
                {
                    Logger.Log.Debug(
                        $"[OCR lifecycle #{diagnosticId}] started source={sourceKind}, " +
                        $"busySlot={_overlaySession.BusyOcrSlot?.ToString() ?? "none"}, " +
                        $"queueCount={_overlaySession.OcrQueue.Count}, matcherReady={data?.Matcher != null}");
                }
                string startMessage = pairIndex.HasValue
                    ? $"开始识别区域{pairIndex.Value + 1}"
                    : "开始识别画面文字";
                AddDebugSamplingOverlayEntry(startMessage);
            }
            if (debug && pairIndex.HasValue)
            {
                double firstChangeToStartMs = firstPixelChangeTicks == 0
                    ? -1
                    : (ocrStartedTimestamp - firstPixelChangeTicks) * 1000.0 / Stopwatch.Frequency;
                double stableToStartMs = qualifiedTicks == 0
                    ? -1
                    : (ocrStartedTimestamp - qualifiedTicks) * 1000.0 / Stopwatch.Frequency;
                Logger.Log.Debug(
                    $"[OCR lifecycle #{diagnosticId}] source=pair, pair={pairIndex.Value}, " +
                    $"sourceSample=#{sourceSampleId}, firstChangeSample=#{firstPixelChangeSampleId}, " +
                    $"firstChangeToStartMs={firstChangeToStartMs:F1}, stableToStartMs={stableToStartMs:F1}, " +
                    $"frame={frameToProcess?.Width ?? 0}x{frameToProcess?.Height ?? 0}");
            }
            if (debug && previousOcrStartedUtc != DateTime.MinValue)
            {
                Logger.Log.Debug(
                    $"[OCR timing #{diagnosticId}] requestIntervalMs=" +
                    $"{(long)(_lastOcrStartedUtc - previousOcrStartedUtc).TotalMilliseconds}");
            }
            Stopwatch recognitionStopwatch = _performanceDiagnostics ? Stopwatch.StartNew() : null;
            string recognizedText = null;
            string recognitionSource = "not-started";
            bool recognitionCompleted = false;
            bool recognizedTextMayBeClipped = false;
            double hashMs = 0;
            double similarCacheMs = 0;
            double ocrMs = 0;
            int detectedTextRegionCount = 0;
            int recognizedTextRegionCount = 0;
            double textDetectionMs = 0;
            double subtitleSelectionMs = 0;
            double textRecognitionMs = 0;
            long workerCompletedTimestamp = 0;
            try
            {
                await Task.Run(() =>
                {
                    Stopwatch workerStopwatch = debug ? Stopwatch.StartNew() : null;
                    try
                    {
                        if (frameToProcess == null || frameToProcess.Empty())
                        {
                            recognitionSource = "empty-frame";
                            return;
                        }

                        Stopwatch hashStopwatch = debug ? Stopwatch.StartNew() : null;
                        string bitStr = ImageProcessor.ComputeRobustHash(frameToProcess);
                        hashMs = hashStopwatch?.Elapsed.TotalMilliseconds ?? 0;

                        if (!forceRefresh &&
                            imageTextCache.TryGetValue(bitStr, out string cachedOcrText) &&
                            !string.IsNullOrWhiteSpace(cachedOcrText))
                        {
                            recognitionSource = "exact-image-cache";
                            recognizedText = cachedOcrText;
                            recognitionCompleted = true;
                        }
                        else
                        {
                            Stopwatch similarCacheStopwatch = debug ? Stopwatch.StartNew() : null;
                            // Approximate hashes can alias a short dialogue line with
                            // a visually similar blank frame. Keep fuzzy reuse for the
                            // primary subtitle layout, but require exact matches for
                            // generic region OCR.
                            string matchedImageHash = forceRefresh || !useSubtitleTextFilter
                                ? null
                                : ImageProcessor.FindSimilarImageHash(bitStr, imageTextCache, maxDistance: distant);
                            similarCacheMs = similarCacheStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                            if (matchedImageHash != null)
                            {
                                recognitionSource = "similar-image-cache";
                                recognizedText = imageTextCache[matchedImageHash];
                                imageTextCache[bitStr] = recognizedText; // LRU cache automatically manages size
                                recognitionCompleted = true;
                            }
                            else
                            {
                                recognitionSource = useSubtitleTextFilter
                                    ? "paddle-ocr-subtitle-filter"
                                    : "paddle-ocr-region";
                                Stopwatch ocrStopwatch = debug ? Stopwatch.StartNew() : null;
                                OCRResult ocrResult = useSubtitleTextFilter
                                    ? data.engine.DetectSubtitleTextFromMat(frameToProcess)
                                    : data.engine.DetectTextFromMat(frameToProcess);
                                ocrMs = ocrStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                                detectedTextRegionCount = ocrResult?.DetectedTextRegionCount ?? 0;
                                recognizedTextRegionCount = ocrResult?.RecognizedTextRegionCount ?? 0;
                                textDetectionMs = ocrResult?.TextDetectionElapsedMilliseconds ?? 0;
                                subtitleSelectionMs = ocrResult?.SubtitleSelectionElapsedMilliseconds ?? 0;
                                textRecognitionMs = ocrResult?.TextRecognitionElapsedMilliseconds ?? 0;
                                recognizedText = ocrResult?.Text ?? string.Empty;
                                recognizedTextMayBeClipped = TextMayBeClipped(
                                    ocrResult,
                                    frameToProcess.Width,
                                    frameToProcess.Height);
                                recognitionCompleted = true;

                                if (debug)
                                {
                                    Logger.Log.Debug($"OCR Text: {recognizedText}");
                                }

                                if (!string.IsNullOrWhiteSpace(recognizedText))
                                {
                                    imageTextCache[bitStr] = recognizedText;
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
                    finally
                    {
                        if (workerStopwatch != null)
                        {
                            Logger.Log.Debug(
                                $"[OCR timing #{diagnosticId}] workerMs={workerStopwatch.Elapsed.TotalMilliseconds:F1}, " +
                                $"frame={frameToProcess?.Width ?? 0}x{frameToProcess?.Height ?? 0}, " +
                                $"source={recognitionSource}, hashMs={hashMs:F1}, " +
                                $"similarCacheMs={similarCacheMs:F1}, paddleOcrMs={ocrMs:F1}, " +
                                $"textDetectionMs={textDetectionMs:F1}, " +
                                $"subtitleSelectionMs={subtitleSelectionMs:F1}, " +
                                $"textRecognitionMs={textRecognitionMs:F1}, " +
                                $"detectedTextRegions={detectedTextRegionCount}, " +
                                $"recognizedTextRegions={recognizedTextRegionCount}, " +
                                $"screenshotSaving=disabled, textLength={recognizedText?.Length ?? 0}, " +
                                $"completed={recognitionCompleted}");
                            workerCompletedTimestamp = Stopwatch.GetTimestamp();
                        }
                    }
                });

                if (ShouldCollectSamplingDiagnostics && workerCompletedTimestamp > 0)
                {
                    double workerElapsedMs =
                        (workerCompletedTimestamp - ocrStartedTimestamp) * 1000.0 / Stopwatch.Frequency;
                    AddDebugSamplingOverlayEntry(
                        string.IsNullOrWhiteSpace(recognizedText)
                            ? $"没有读到文字 · {workerElapsedMs / 1000.0:F1}秒"
                            : $"已读到文字 · {workerElapsedMs / 1000.0:F1}秒");
                    if (debug)
                    {
                        double workerToContinuationMs =
                            (Stopwatch.GetTimestamp() - workerCompletedTimestamp) * 1000.0 / Stopwatch.Frequency;
                        Logger.Log.Debug(
                            $"[OCR timing #{diagnosticId}] workerToUiContinuationMs={workerToContinuationMs:F1}");
                    }
                }

                long uiDispatchRequestedTimestamp = Stopwatch.GetTimestamp();
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    double uiDispatchWaitMs =
                        (Stopwatch.GetTimestamp() - uiDispatchRequestedTimestamp) * 1000.0 / Stopwatch.Frequency;
                    double setImageMs = 0;
                    double applyRecognizedTextMs = 0;
                    bool subtitleMatched = false;
                    bool applyCompleted = false;
                    try
                    {
                        if (data.IsVisible && target != null)
                        {
                            Stopwatch setImageStopwatch = debug ? Stopwatch.StartNew() : null;
                            data.SetImage(target);
                            setImageMs = setImageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                        }
                        if (string.Equals(ocrGame, _overlaySession.AppliedGame, StringComparison.Ordinal))
                        {
                            Stopwatch applyStopwatch = debug ? Stopwatch.StartNew() : null;
                            ApplyRecognizedText(
                                recognizedText,
                                recognitionCompleted,
                                forceRefresh,
                                pairIndex,
                                recognizedTextMayBeClipped,
                                diagnosticId,
                                out subtitleMatched);
                            applyRecognizedTextMs = applyStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                            applyCompleted = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Error(ex);
                    }
                    finally
                    {
                        if (debug)
                        {
                            Logger.Log.Debug(
                                $"[OCR timing #{diagnosticId}] setImageMs={setImageMs:F1}, " +
                                $"applyRecognizedTextMs={applyRecognizedTextMs:F1}, " +
                                $"uiDispatchWaitMs={uiDispatchWaitMs:F1}");
                            double fromPixelChangeToUiMs = firstPixelChangeTicks == 0
                                ? -1
                                : (Stopwatch.GetTimestamp() - firstPixelChangeTicks) * 1000.0 / Stopwatch.Frequency;
                            Logger.Log.Debug(
                                $"[OCR lifecycle #{diagnosticId}] resultApplied={applyCompleted}, sourceSample=#{sourceSampleId}, " +
                                $"recognized={recognitionCompleted}, matched={subtitleMatched}, " +
                                $"textLength={recognizedText?.Length ?? 0}, " +
                                $"firstChangeToUiMs={fromPixelChangeToUiMs:F1}");
                        }

                        if (ShouldCollectSamplingDiagnostics)
                        {
                            string resultMessage = !applyCompleted
                                ? "识别完成，字幕没有更新"
                                : subtitleMatched
                                    ? "字幕已更新"
                                    : "没有找到对应字幕";
                            AddDebugSamplingOverlayEntry(resultMessage);
                        }
                    }
                });
            }
            finally
            {
                if (pairIndex.HasValue)
                {
                    DiscardPairOcrBaseline(pairIndex.Value);
                }

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
                target?.Dispose();

                if (recognitionStopwatch != null)
                {
                    Logger.Log.Info(
                        $"OCR pipeline completed: frame={processedWidth}x{processedHeight}, " +
                        $"elapsedMs={recognitionStopwatch.ElapsedMilliseconds}, completed={recognitionCompleted}");
                }

                _ = Dispatcher.BeginInvoke(new Action(ContinueOcrPipeline));
            }
        }

        private void ApplyRecognizedText(
            string recognizedText,
            bool recognitionCompleted,
            bool forceRefresh,
            int? pairIndex,
            bool textMayBeClipped,
            long diagnosticId,
            out bool subtitleMatched)
        {
            subtitleMatched = false;
            bool usable = recognitionCompleted && recognizedText != null && recognizedText.Length >= 2;
            string header = "";
            string content = "";
            string key = "";
            string original = "";
            bool matchMiss = false;
            if (usable)
            {
                matchMiss = !TryMatchOcrText(
                    recognizedText,
                    out header,
                    out content,
                    out key,
                    out original,
                    diagnosticId);
                subtitleMatched = !matchMiss;
            }

            if (pairIndex.HasValue && pairIndex.Value < _overlaySession.Pairs.Count)
            {
                RegionPair pair = _overlaySession.Pairs[pairIndex.Value];
                if (pair.Id == _overlaySession.VoicePrimaryId)
                {
                    if (textMayBeClipped || (usable && matchMiss))
                    {
                        string repairReason = textMayBeClipped
                            ? "primary-text-clipped"
                            : "primary-ocr-match-miss";
                        QueueAutoRegionRepairFromEvidence(repairReason, recognizedText);
                    }
                    else if (usable)
                    {
                        if (pair.Display.IsValid)
                        {
                            _autoRegionRepairPending = false;
                            _autoRegionRepairReason = "primary-region-ocr-confirmed";
                            if (CountNonEmptyLines(recognizedText) >= 2 &&
                                !string.IsNullOrWhiteSpace(header) &&
                                !string.IsNullOrWhiteSpace(content))
                            {
                                LockAutoRegionDetection(
                                    "primary OCR confirmed at least two lines with speaker name and dialogue");
                            }
                            else
                            {
                                _overlaySession.CancelQueuedAutoRegionOcr();
                            }
                        }
                        else
                        {
                            QueueAutoRegionRepairFromEvidence(
                                "primary-display-region-missing",
                                recognizedText);
                        }
                    }
                }
            }

            int appliedPair = pairIndex ?? 0;
            if (forceRefresh)
            {
                if (usable)
                {
                    if (matchMiss)
                    {
                        DiscardPairOcrBaseline(appliedPair);
                    }
                    else
                    {
                        CommitPairOcrBaseline(appliedPair);
                    }
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
                    DiscardPairOcrBaseline(appliedPair);
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
                bool clearedEmptySecondaryPair = recognitionCompleted &&
                    string.IsNullOrWhiteSpace(recognizedText) &&
                    pairIndex.Value != FindPrimaryPairIndex(_overlaySession.Pairs);
                if (clearedEmptySecondaryPair)
                {
                    // A stable secondary-region frame produced no text. Clear its
                    // old dialogue while preserving non-empty match misses, which
                    // can be partial typewriter text.
                    _overlaySession.ClearPairSubtitleContent(pairIndex.Value);
                }

                if (recognitionCompleted)
                {
                    // An empty but completed OCR result is still the result for this
                    // exact frame. Commit it so an unchanged blank frame is not sent
                    // to the OCR engine again on every sampling tick.
                    CommitPairOcrBaseline(pairIndex.Value);
                }
                else
                {
                    DiscardPairOcrBaseline(pairIndex.Value);
                }
                _overlaySession.NoteOcrMiss();
                _overlaySession.CompleteOcr(miss: true);
                if (clearedEmptySecondaryPair)
                {
                    ApplyPairOverlay();
                }
                return;
            }

            _overlaySession.CompleteOcr(
                miss: false,
                content,
                header,
                recognizedText,
                original,
                matchMiss);
            // OCR and matching are deterministic for an unchanged frame. Keep its
            // baseline even when the recognized text has no subtitle match; a later
            // visual change will still exceed the threshold and schedule fresh OCR.
            CommitPairOcrBaseline(pairIndex.Value);
            MaybePlayPairVoice(key, content, header);
            ApplyPairOverlay();
        }

        private bool TryMatchOcrText(
            string recognizedText,
            out string header,
            out string content,
            out string key,
            out string original,
            long diagnosticId)
        {
            Stopwatch matchStopwatch = debug ? Stopwatch.StartNew() : null;
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

                if (matchStopwatch != null)
                {
                    Logger.Log.Debug(
                        $"[OCR timing #{diagnosticId}] subtitleMatch=overlay-cache, " +
                        $"elapsedMs={matchStopwatch.Elapsed.TotalMilliseconds:F1}, textLength={recognizedText.Length}");
                }
                return !string.IsNullOrEmpty(header) || !string.IsNullOrEmpty(content);
            }

            if (data == null || data.Matcher == null)
            {
                Logger.Log.Warn(
                    $"[OCR timing #{diagnosticId}] subtitleMatch=deferred, reason=matcher-not-ready, " +
                    $"textLength={recognizedText.Length}");
                return false;
            }

            MatchResult matchResult = data.Matcher.FindMatchWithHeaderSeparated(recognizedText, out key);
            if (matchStopwatch != null)
            {
                Logger.Log.Debug(
                    $"[OCR timing #{diagnosticId}] subtitleMatch=matcher, " +
                    $"elapsedMs={matchStopwatch.Elapsed.TotalMilliseconds:F1}, textLength={recognizedText.Length}");
            }
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
                _overlaySession.NoteVoicePlaybackEnded();
                return;
            }

            if (!forceVoiceReplay && AudioList.ContainsKey(key))
            {
                _overlaySession.NoteVoicePlaybackEnded();
                return;
            }

            string audioKey = VoiceContentHelper.CalculateMd5Hash(key);
            PlayMainAudio(audioKey, logActivity: true);
            if (!AudioList.ContainsKey(key))
            {
                AudioList[key] = true;
            }
        }

        private void ForceRefreshCurrentSubtitle()
        {
            if (_isOcrRunning || Interlocked.CompareExchange(ref OCR_TIMER, 0, 0) != 0)
            {
                _forceRefreshPending = true;
                return;
            }

            if (data == null || data.Matcher == null)
            {
                _forceRefreshPending = true;
                if (debug)
                {
                    Logger.Log.Warn("[OCR gate] force refresh deferred until matcher is ready");
                }
                return;
            }

            try
            {
                if (!_overlaySession.TryGetVoicePrimaryCapture(out int pairIndex, out OverlayRect capture))
                {
                    _overlaySession.Refresh(hasCaptureRegion: false, foundText: false);
                    TryStartBusyOcr();
                    return;
                }

                string capturedGame = _overlaySession.AppliedGame;
                _isOcrRunning = true;
                _ = CaptureForceRefreshAsync(
                    capture,
                    pairIndex,
                    capturedGame,
                    Stopwatch.GetTimestamp());
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Failed to force refresh current subtitle: {ex}");
            }
        }

        private async Task CaptureForceRefreshAsync(
            OverlayRect capture,
            int pairIndex,
            string capturedGame,
            long pipelineStartedTimestamp)
        {
            ForceRefreshCaptureResult result = null;
            Exception failure = null;
            try
            {
                result = await Task.Run(() =>
                {
                    var sample = new ForceRefreshCaptureResult();
                    var bounds = new System.Drawing.Rectangle(
                        capture.X,
                        capture.Y,
                        capture.Width,
                        capture.Height);
                    Stopwatch stageStopwatch = debug ? Stopwatch.StartNew() : null;
                    sample.Bitmap = CaptureRectangle(bounds);
                    sample.CaptureMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                    if (sample.Bitmap == null)
                    {
                        return sample;
                    }

                    stageStopwatch = debug ? Stopwatch.StartNew() : null;
                    sample.Frame = sample.Bitmap.ToMat();
                    sample.BitmapToMatMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                    stageStopwatch = debug ? Stopwatch.StartNew() : null;
                    sample.Binary = PreprocessToBinary(sample.Frame);
                    sample.PreprocessMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                    return sample;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                result?.Bitmap?.Dispose();
                result?.Frame?.Dispose();
                result?.Binary?.Dispose();
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (failure != null)
                {
                    Logger.Log.Error($"Failed to capture forced OCR refresh: {failure}");
                    result?.Bitmap?.Dispose();
                    result?.Frame?.Dispose();
                    result?.Binary?.Dispose();
                    _isOcrRunning = false;
                    ContinueOcrPipeline();
                    return;
                }

                if (result?.Bitmap == null || result.Frame == null ||
                    !string.Equals(capturedGame, _overlaySession.AppliedGame, StringComparison.Ordinal))
                {
                    result?.Bitmap?.Dispose();
                    result?.Frame?.Dispose();
                    result?.Binary?.Dispose();
                    _isOcrRunning = false;
                    ContinueOcrPipeline();
                    return;
                }

                EnsurePairBuffers(pairIndex + 1);
                if (result.Binary != null)
                {
                    _pairLastBinary[pairIndex]?.Dispose();
                    _pairLastOcrBinary[pairIndex]?.Dispose();
                    _pairLastBinary[pairIndex] = result.Binary.Clone();
                    _pairLastOcrBinary[pairIndex] = result.Binary;
                    result.Binary = null;
                }

                _overlaySession.ResetOcrInterval();
                if (debug)
                {
                    double totalMs = (Stopwatch.GetTimestamp() - pipelineStartedTimestamp) *
                        1000.0 / Stopwatch.Frequency;
                    Logger.Log.Debug(
                        $"[Force refresh sampling] captureMs={result.CaptureMs:F1}, " +
                        $"bitmapToMatMs={result.BitmapToMatMs:F1}, preprocessMs={result.PreprocessMs:F1}, " +
                        $"totalMs={totalMs:F1}");
                }

                Bitmap bitmap = result.Bitmap;
                Mat frame = result.Frame;
                result.Bitmap = null;
                result.Frame = null;
                result.Binary?.Dispose();
                result.Binary = null;
                _ = TriggerOcrAsync(frame, bitmap, forceRefresh: true, pairIndex: pairIndex);
            }));
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

        private ExtraPathSample CollectExtraPathSample(
            SamplingBatchResult timing,
            long diagnosticId,
            bool hasVisibleSubtitle)
        {
            if (!_overlaySession.HasValidCapture ||
                !TryGetFirstValidCaptureScreen(out System.Drawing.Rectangle screen))
            {
                DisposeDarkScreenHold();
                DisposeDialogueOptionHold();
                timing.DarkScreenScanState = "no-valid-capture";
                timing.DialogueScanState = "no-valid-capture";
                timing.ExtraPathState = "dark=no-valid-capture,dialogue=no-valid-capture";
                return ExtraPathSample.None;
            }

            ExtraPathSample extra = ObserveDarkScreen(screen, timing, diagnosticId, hasVisibleSubtitle);
            ExtraPathSample result = ObserveDialogueOptions(screen, timing, extra);
            timing.ExtraPathState =
                $"dark={timing.DarkScreenScanState ?? "not-run"},dialogue={timing.DialogueScanState ?? "not-run"}";
            return result;
        }

        private bool TryGetFirstValidCaptureScreen(
            out System.Drawing.Rectangle screen)
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

        private ExtraPathSample ObserveDarkScreen(
            System.Drawing.Rectangle screen,
            SamplingBatchResult timing,
            long diagnosticId,
            bool hasVisibleSubtitle)
        {
            if (!_overlaySession.DarkScreenScanOn)
            {
                timing.DarkScreenScanState = "disabled";
                DisposeDarkScreenHold();
                return ExtraPathSample.None;
            }

            if (hasVisibleSubtitle)
            {
                timing.DarkScreenScanState = "skipped-visible-subtitle";
                ResetDarkScreenCandidate();
                DisposeDarkScreenHold();
                return ExtraPathSample.DarkScreenEnded();
            }

            DateTime now = DateTime.UtcNow;
            lock (_extraPathSync)
            {
                if (now - _lastDarkScreenScanTime < DarkScreenScanInterval)
                {
                    timing.DarkScreenScanState = "interval-skip";
                    return ExtraPathSample.None;
                }

                _lastDarkScreenScanTime = now;
            }

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

                Stopwatch stageStopwatch = debug ? Stopwatch.StartNew() : null;
                searchBitmap = CaptureRectangleScaled(
                    searchBounds,
                    DarkScreenAnalysisMaxSide);
                double stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraCaptureMs += stageMs;
                timing.DarkScreenCaptureMs += stageMs;
                if (searchBitmap == null)
                {
                    timing.DarkScreenScanState = "capture-failed";
                    return ExtraPathSample.None;
                }

                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                Mat rawSearchMat = searchBitmap.ToMat();
                stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraBitmapToMatMs += stageMs;
                timing.DarkScreenBitmapToMatMs += stageMs;
                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                searchMat = LimitFrameSize(rawSearchMat, DarkScreenAnalysisMaxSide);
                stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraPreprocessMs += stageMs;
                timing.DarkScreenPreprocessMs += stageMs;
                searchBitmap.Dispose();
                searchBitmap = null;
                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                bool found = DarkScreenSubtitleDetector.TryFindSubtitleRegion(
                    searchMat,
                    out OpenCvSharp.Rect candidateRegion,
                    out bool isDarkScreen,
                    out double darkRatio,
                    out double brightRatio);
                stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraDetectionMs += stageMs;
                timing.DarkScreenDetectionMs += stageMs;
                timing.DarkScreenRatio = darkRatio;
                timing.DarkScreenBrightRatio = brightRatio;

                if (!isDarkScreen)
                {
                    timing.DarkScreenScanState = "not-dark";
                    ResetDarkScreenCandidate();
                    DisposeDarkScreenHold();
                    return ExtraPathSample.DarkScreenEnded();
                }

                if (!found)
                {
                    timing.DarkScreenScanState = "dark-no-candidate";
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
                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                candidateFrame = new Mat(searchMat, bitmapRegion).Clone();
                stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraPreprocessMs += stageMs;
                timing.DarkScreenPreprocessMs += stageMs;
                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                candidateBitmap = candidateFrame.ToBitmap();
                stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraMatToBitmapMs += stageMs;
                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                string candidateHash = ImageProcessor.ComputeRobustHash(candidateFrame);
                stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraDetectionMs += stageMs;
                timing.DarkScreenDetectionMs += stageMs;
                double bandScaleX = searchBounds.Width / (double)searchMat.Width;
                double bandScaleY = searchBounds.Height / (double)searchMat.Height;
                var absoluteBand = new OverlayRect(
                    searchBounds.Left + (int)Math.Round(candidateRegion.X * bandScaleX),
                    searchBounds.Top + (int)Math.Round(candidateRegion.Y * bandScaleY),
                    (int)Math.Round(candidateRegion.Width * bandScaleX),
                    (int)Math.Round(candidateRegion.Height * bandScaleY));

                bool needsOcr;
                lock (_extraPathSync)
                {
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

                    needsOcr = _darkScreenStableFrames >= 2 &&
                        (string.IsNullOrEmpty(_lastDarkScreenOcrHash) ||
                         ImageProcessor.CalculateHammingDistance(
                             candidateHash,
                             _lastDarkScreenOcrHash) > 2);

                    if (needsOcr)
                    {
                        DisposeDarkScreenHold();
                        _darkScreenBitmap = candidateBitmap;
                        _darkScreenMat = candidateFrame;
                        _darkScreenPendingHash = candidateHash;
                        candidateBitmap = null;
                        candidateFrame = null;
                        heldCandidate = true;
                    }
                }

                if (needsOcr)
                {
                    timing.DarkScreenScanState = "candidate-queued-for-ocr";
                    if (debug)
                    {
                        Logger.Log.Debug(
                            $"[DarkScreen scan #{diagnosticId}] state=candidate-queued-for-ocr, " +
                            $"dark={darkRatio:F3}, bright={brightRatio:F4}, " +
                            $"candidate={candidateRegion}");
                    }
                }
                else
                {
                    timing.DarkScreenScanState = "candidate-not-stable-or-unchanged";
                }

                return ExtraPathSample.DarkScreenCandidate(absoluteBand, needsOcr);
            }
            catch (Exception ex)
            {
                timing.DarkScreenScanState = "error";
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
            lock (_extraPathSync)
            {
                _lastDarkScreenCandidateHash = null;
                _lastDarkScreenOcrHash = null;
                _darkScreenStableFrames = 0;
            }
        }

        private void DisposeDarkScreenHold()
        {
            lock (_extraPathSync)
            {
                _darkScreenBitmap?.Dispose();
                _darkScreenMat?.Dispose();
                _darkScreenBitmap = null;
                _darkScreenMat = null;
                _darkScreenPendingHash = null;
            }
        }

        private ExtraPathSample ObserveDialogueOptions(
            System.Drawing.Rectangle screen,
            SamplingBatchResult timing,
            ExtraPathSample extra)
        {
            extra = extra ?? ExtraPathSample.None;
            if (!_overlaySession.AllowsDialogueOptionScan)
            {
                timing.DialogueScanState = "not-armed";
                return extra;
            }

            DateTime scanTime = DateTime.UtcNow;
            lock (_extraPathSync)
            {
                if (scanTime - _lastDialogueOptionScanTime < DialogueOptionScanInterval)
                {
                    timing.DialogueScanState = "interval-skip";
                    return extra;
                }

                _lastDialogueOptionScanTime = scanTime;
            }

            Bitmap screenBitmap = null;
            Mat screenMat = null;
            try
            {
                Stopwatch stageStopwatch = debug ? Stopwatch.StartNew() : null;
                screenBitmap = CaptureRectangleScaled(
                    screen,
                    DialogueOptionAnalysisMaxSide);
                double stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraCaptureMs += stageMs;
                timing.DialogueCaptureMs += stageMs;
                if (screenBitmap == null)
                {
                    timing.DialogueScanState = "capture-failed";
                    return extra;
                }

                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                Mat rawScreenMat = screenBitmap.ToMat();
                stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraBitmapToMatMs += stageMs;
                timing.DialogueBitmapToMatMs += stageMs;
                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                screenMat = LimitFrameSize(rawScreenMat, DialogueOptionAnalysisMaxSide);
                stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraPreprocessMs += stageMs;
                timing.DialoguePreprocessMs += stageMs;
                screenBitmap.Dispose();
                screenBitmap = null;
                double coordinateScaleX = screen.Width / (double)screenMat.Width;
                double coordinateScaleY = screen.Height / (double)screenMat.Height;

                double threshold = Config.Get("DialogueOptionTemplateThreshold", 0.74);
                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                if (!DialogueOptionDetector.TryFindTextRegion(
                        screenMat,
                        out OpenCvSharp.Rect relativeTextRegion,
                        out double confidence,
                        threshold))
                {
                    stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                    timing.ExtraDetectionMs += stageMs;
                    timing.DialogueDetectionMs += stageMs;
                    timing.DialogueScanState = "no-candidate";
                    bool hadCandidates;
                    bool candidatesCleared;
                    string choice;
                    lock (_extraPathSync)
                    {
                        hadCandidates = _lastDialogueOptions.Count > 0;
                        choice = TryTakeDialogueChoice();
                        candidatesCleared = hadCandidates && _lastDialogueOptions.Count == 0;
                    }
                    if (!string.IsNullOrEmpty(choice))
                    {
                        return extra == ExtraPathSample.None
                            ? ExtraPathSample.DialogueChoice(choice)
                            : extra.WithDialogueChoice(choice);
                    }

                    // Only after a non-empty candidate list was cleared without a click
                    // (2-miss dismiss). Skip idle scans and Ready→first-OCR gaps.
                    if (candidatesCleared)
                    {
                        return extra == ExtraPathSample.None
                            ? ExtraPathSample.DialogueOptionsEnded()
                            : extra.WithDialogueOptionsEnded();
                    }

                    return extra;
                }
                stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraDetectionMs += stageMs;
                timing.DialogueDetectionMs += stageMs;
                timing.DialogueConfidence = confidence;

                lock (_extraPathSync)
                {
                    _dialogueOptionMissCount = 0;
                }
                var bitmapRegion = new OpenCvSharp.Rect(
                    relativeTextRegion.X,
                    relativeTextRegion.Y,
                    relativeTextRegion.Width,
                    relativeTextRegion.Height);
                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                Mat optionFrame = new Mat(screenMat, bitmapRegion).Clone();
                stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraPreprocessMs += stageMs;
                timing.DialoguePreprocessMs += stageMs;
                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                Bitmap optionBitmap = optionFrame.ToBitmap();
                stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraMatToBitmapMs += stageMs;
                stageStopwatch = debug ? Stopwatch.StartNew() : null;
                string optionHash = ImageProcessor.ComputeRobustHash(optionFrame);
                stageMs = stageStopwatch?.Elapsed.TotalMilliseconds ?? 0;
                timing.ExtraDetectionMs += stageMs;
                timing.DialogueDetectionMs += stageMs;
                bool duplicate;
                lock (_extraPathSync)
                {
                    duplicate = string.Equals(optionHash, _lastDialogueOptionHash, StringComparison.Ordinal);
                    if (!duplicate)
                    {
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
                    }
                }

                if (duplicate)
                {
                    timing.DialogueScanState = "duplicate-candidate";
                    optionFrame.Dispose();
                    optionBitmap.Dispose();
                    return extra;
                }

                timing.DialogueScanState = "new-candidate-queued-for-ocr";

                return extra == ExtraPathSample.None
                    ? ExtraPathSample.DialogueOptionsReady()
                    : extra.WithDialogueOptionsReady();
            }
            catch (Exception ex)
            {
                timing.DialogueScanState = "error";
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

                _ = Dispatcher.BeginInvoke(new Action(ContinueOcrPipeline));
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
            lock (_extraPathSync)
            {
                DisposeDialogueOptionHold();
                _dialogueOptionBitmap = bitmap;
                _dialogueOptionMat = mat;
                _dialogueOptionOrigin = origin;
                _dialogueOptionConfidence = confidence;
                _dialogueOptionScaleX = coordinateScaleX;
                _dialogueOptionScaleY = coordinateScaleY;
            }
        }

        private void DisposeDialogueOptionHold()
        {
            lock (_extraPathSync)
            {
                _dialogueOptionBitmap?.Dispose();
                _dialogueOptionMat?.Dispose();
                _dialogueOptionBitmap = null;
                _dialogueOptionMat = null;
                _dialogueOptionScaleX = 1.0;
                _dialogueOptionScaleY = 1.0;
            }
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

        private Bitmap CaptureRectangle(System.Drawing.Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new ArgumentException("The requested capture area is invalid.", nameof(bounds));
            }

            // Capture the configured desktop region directly; no window discovery or HWND capture is used.
            Bitmap bitmap = CaptureDesktopRectangle(bounds);
            try
            {
                FilterDebugOverlayFromCapture(bitmap, bounds);
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static Bitmap CaptureDesktopRectangle(System.Drawing.Rectangle bounds)
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
        /// Captures and downsamples an analysis region from the desktop.
        /// </summary>
        private Bitmap CaptureRectangleScaled(
            System.Drawing.Rectangle bounds,
            int maxSide)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new ArgumentException("The requested capture area is invalid.", nameof(bounds));
            }

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
                try
                {
                    FilterDebugOverlayFromCapture(bitmap, bounds);
                    return bitmap;
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
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

        private static string ReadAudioHeader(string filePath)
        {
            try
            {
                byte[] header = new byte[12];
                int bytesRead;
                using (FileStream stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    bytesRead = stream.Read(header, 0, header.Length);
                }

                return bytesRead == 0
                    ? "empty"
                    : BitConverter.ToString(header, 0, bytesRead);
            }
            catch (Exception ex)
            {
                return "unreadable:" + ex.GetType().Name;
            }
        }

        private static bool IsVoiceTestRequest(string requestLabel)
        {
            return string.Equals(requestLabel, "test", StringComparison.Ordinal);
        }


        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (debug)
            {
                Dispatcher.Hooks.OperationStarted -= OnDispatcherOperationStarted;
                Dispatcher.Hooks.OperationCompleted -= OnDispatcherOperationCompleted;
                Dispatcher.Hooks.OperationAborted -= OnDispatcherOperationAborted;
            }
            StopAudio();
            _dragHandleTimer.Stop();
            _debugSamplingStatsTimer.Stop();
            _debugSamplingProcess?.Dispose();
            _debugSamplingProcess = null;
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

        private void OnDispatcherOperationStarted(object sender, DispatcherHookEventArgs e)
        {
            if (e?.Operation != null)
            {
                _dispatcherOperationStartTicks[e.Operation] = Stopwatch.GetTimestamp();
            }
        }

        private void OnDispatcherOperationCompleted(object sender, DispatcherHookEventArgs e)
        {
            if (e?.Operation == null || !_dispatcherOperationStartTicks.TryGetValue(e.Operation, out long started))
            {
                return;
            }

            _dispatcherOperationStartTicks.Remove(e.Operation);
            double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs < 100)
            {
                return;
            }

            Delegate callback = null;
            try
            {
                Type operationType = e.Operation.GetType();
                callback = operationType
                    .GetProperty("Delegate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(e.Operation, null) as Delegate;
                if (callback == null)
                {
                    callback = operationType
                        .GetField("_method", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.GetValue(e.Operation) as Delegate;
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Debug($"Could not inspect Dispatcher callback: {ex.GetType().Name}");
            }
            string callbackName = callback?.Method == null
                ? $"unknown:{e.Operation.GetType().FullName}"
                : $"{callback.Method.DeclaringType?.FullName}.{callback.Method.Name}";
            Logger.Log.Warn(
                $"[Dispatcher operation] durationMs={elapsedMs:F1}, " +
                $"priority={e.Operation.Priority}, callback={callbackName}");
        }

        private void OnDispatcherOperationAborted(object sender, DispatcherHookEventArgs e)
        {
            if (e?.Operation != null)
            {
                _dispatcherOperationStartTicks.Remove(e.Operation);
            }
        }

        private void ResetCaptureBuffersIfGameChanged()
        {
            if (string.Equals(_sampledGame, _overlaySession.AppliedGame, StringComparison.Ordinal))
            {
                return;
            }

            _sampledGame = _overlaySession.AppliedGame;
            _autoRegionRepairPending = !_autoRegionDetectionLocked;
            _autoRegionRepairReason = "game-selection-changed";
            _lastAutoRegionRepairEvidenceSignature = null;
            _lastAutoRegionSearchStartedUtc = DateTime.MinValue;
            Interlocked.Exchange(ref _autoRegionCcMissesSinceBroadScan, 0);
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
            for (int i = 0; i < _pairPendingOcrBinary.Count; i++)
            {
                _pairPendingOcrBinary[i]?.Dispose();
                _pairPendingOcrBinary[i] = null;
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
            foreach (PairOcrDiagnosticContext context in _pairOcrDiagnosticContexts)
            {
                context.Reset();
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
            UpdateDragHandle();
        }

        private void UpdateDragHandle()
        {
            if (DragButton == null || _dragHandleDragging)
            {
                return;
            }

            bool canShow = _overlaySession.RecognitionRunning &&
                _overlaySession.IsClickThrough &&
                !_regionDragging;
            OverlayRect display = OverlayRect.Invalid;
            int hoveredPairIndex = -1;
            if (canShow)
            {
                IReadOnlyList<RegionPair> pairs = _overlaySession.Pairs;
                IReadOnlyList<PairSubtitleBody> bodies = _overlaySession.PairBodies;
                System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
                int highestRecognitionOrder = int.MinValue;
                bool selectedBodyVisible = false;
                int pairCount = Math.Min(pairs.Count, bodies.Count);
                for (int i = 0; i < pairCount; i++)
                {
                    OverlayRect candidate = bodies[i].Display;
                    if (candidate == null || !candidate.IsValid)
                    {
                        continue;
                    }

                    var candidateBounds = new System.Drawing.Rectangle(
                        candidate.X,
                        candidate.Y,
                        candidate.Width,
                        candidate.Height);
                    if (candidateBounds.Contains(cursor) &&
                        (hoveredPairIndex < 0 ||
                         (bodies[i].Visible && !selectedBodyVisible) ||
                         (bodies[i].Visible == selectedBodyVisible &&
                          bodies[i].RecognitionOrder >= highestRecognitionOrder)))
                    {
                        hoveredPairIndex = i;
                        highestRecognitionOrder = bodies[i].RecognitionOrder;
                        selectedBodyVisible = bodies[i].Visible;
                        display = candidate;
                    }
                }
            }

            bool hoveringDragButton = canShow && IsCursorOverElement(DragButton);
            if (hoveredPairIndex < 0 && hoveringDragButton &&
                _dragHandlePairIndex >= 0 && _dragHandlePairIndex < _overlaySession.Pairs.Count)
            {
                hoveredPairIndex = _dragHandlePairIndex;
                display = _overlaySession.Pairs[hoveredPairIndex].Display;
            }

            bool hoveringDisplay = hoveredPairIndex >= 0 && display != null && display.IsValid;
            _dragHandlePairIndex = hoveringDisplay ? hoveredPairIndex : -1;
            if (hoveringDisplay)
            {
                PositionDragHandle(display);
                DragButton.Visibility = Visibility.Visible;
                System.Windows.Controls.Panel.SetZIndex(DragButton, int.MaxValue);
            }
            else
            {
                DragButton.Visibility = Visibility.Collapsed;
            }

            if (_dragHandleInteractive != hoveringDisplay)
            {
                _dragHandleInteractive = hoveringDisplay;
                ApplyOverlayHitMode();
            }
        }

        private void UpdateDebugControlInteraction()
        {
            bool hoveringDebugControl = _overlaySession.IsClickThrough && !_regionDragging &&
                (IsCursorOverElement(DebugSamplingCloseButton) ||
                 IsCursorOverElement(DebugSamplingMinimizeButton) ||
                 IsCursorOverElement(DebugSamplingSettingsButton));
            if (_debugControlInteractive != hoveringDebugControl)
            {
                _debugControlInteractive = hoveringDebugControl;
                ApplyOverlayHitMode();
            }
        }

        private static bool IsCursorOverElement(FrameworkElement element)
        {
            if (element == null || element.Visibility != Visibility.Visible ||
                element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                return false;
            }

            try
            {
                System.Windows.Point topLeft = element.PointToScreen(new System.Windows.Point(0, 0));
                System.Windows.Point bottomRight = element.PointToScreen(
                    new System.Windows.Point(element.ActualWidth, element.ActualHeight));
                var bounds = System.Drawing.Rectangle.FromLTRB(
                    (int)Math.Floor(Math.Min(topLeft.X, bottomRight.X)),
                    (int)Math.Floor(Math.Min(topLeft.Y, bottomRight.Y)),
                    (int)Math.Ceiling(Math.Max(topLeft.X, bottomRight.X)),
                    (int)Math.Ceiling(Math.Max(topLeft.Y, bottomRight.Y)));
                return bounds.Contains(System.Windows.Forms.Cursor.Position);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void PositionDragHandle(OverlayRect display)
        {
            if (display == null || !display.IsValid)
            {
                return;
            }

            _dragHandleRenderTransform.X = 0;
            _dragHandleRenderTransform.Y = 0;
            double displayScale = GetDisplayScale(display);
            System.Windows.Point canvasPoint = DisplayToCanvas(display, displayScale);
            double displayWidth = display.Width / displayScale;
            Canvas.SetLeft(DragButton, canvasPoint.X + Math.Max(0, displayWidth - DragButton.Width) / 2.0);
            Canvas.SetTop(DragButton, canvasPoint.Y + 2);
        }

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_dragHandleInteractive || !_overlaySession.IsClickThrough)
            {
                return;
            }

            IReadOnlyList<RegionPair> pairs = _overlaySession.Pairs;
            IReadOnlyList<PairSubtitleBody> bodies = _overlaySession.PairBodies;
            int pairIndex = _dragHandlePairIndex;
            if (pairIndex < 0 || pairIndex >= pairs.Count || pairIndex >= bodies.Count ||
                !pairs[pairIndex].Display.IsValid || !bodies[pairIndex].Display.IsValid)
            {
                return;
            }

            _dragHandlePairIndex = pairIndex;
            _dragHandleStartRect = pairs[pairIndex].Display;
            _dragHandlePreviewRect = _dragHandleStartRect;
            System.Windows.Point startMouse = e.GetPosition(OverlayCanvas);
            _dragHandleDragging = true;
            ResetDragVisualTransforms(pairIndex);
            _dragHandleStartMouseScreen = OverlayCanvas.PointToScreen(startMouse);
            _dragHandleContentBottomOffset = GetDragContentBottomOffset(pairIndex, _dragHandleStartRect);
            if (!DragButton.CaptureMouse())
            {
                _dragHandleDragging = false;
                _dragHandlePairIndex = -1;
                _dragHandleStartRect = OverlayRect.Invalid;
                _dragHandlePreviewRect = OverlayRect.Invalid;
                _dragHandleContentBottomOffset = 0;
                _dragHandleStartMouseScreen = new System.Windows.Point();
                return;
            }

            e.Handled = true;
        }

        private void DragHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_dragHandleDragging || e.LeftButton != MouseButtonState.Pressed ||
                _dragHandlePairIndex < 0 || !_dragHandleStartRect.IsValid)
            {
                return;
            }

            System.Windows.Point current = e.GetPosition(OverlayCanvas);
            System.Windows.Point currentScreen = OverlayCanvas.PointToScreen(current);
            int x = (int)Math.Round(_dragHandleStartRect.X +
                currentScreen.X - _dragHandleStartMouseScreen.X);
            int y = (int)Math.Round(_dragHandleStartRect.Y +
                currentScreen.Y - _dragHandleStartMouseScreen.Y);
            System.Drawing.Rectangle virtualBounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            x = Math.Max(virtualBounds.Left, Math.Min(x, virtualBounds.Right - _dragHandleStartRect.Width));
            int maxY = virtualBounds.Bottom - Math.Max(1, _dragHandleContentBottomOffset);
            y = Math.Max(virtualBounds.Top, Math.Min(y, maxY));

            var moved = new OverlayRect(
                x,
                y,
                _dragHandleStartRect.Width,
                _dragHandleStartRect.Height);
            _dragHandlePreviewRect = moved;
            ApplyDraggedDisplayVisual(_dragHandlePairIndex, moved);
            e.Handled = true;
        }

        private void ApplyDraggedDisplayVisual(int pairIndex, OverlayRect display)
        {
            if (!_dragHandleStartRect.IsValid || OverlayCanvas == null)
            {
                return;
            }

            System.Windows.Point startCanvas = OverlayCanvas.PointFromScreen(
                new System.Windows.Point(_dragHandleStartRect.X, _dragHandleStartRect.Y));
            System.Windows.Point movedCanvas = OverlayCanvas.PointFromScreen(
                new System.Windows.Point(display.X, display.Y));
            double deltaX = movedCanvas.X - startCanvas.X;
            double deltaY = movedCanvas.Y - startCanvas.Y;
            _dragHandleRenderTransform.X = deltaX;
            _dragHandleRenderTransform.Y = deltaY;
            if (pairIndex == 0)
            {
                _subtitleDragRenderTransform.X = deltaX;
                _subtitleDragRenderTransform.Y = deltaY;
                _headerDragRenderTransform.X = deltaX;
                _headerDragRenderTransform.Y = deltaY;
                return;
            }

            int extraIndex = pairIndex - 1;
            if (extraIndex >= 0 && extraIndex < _extraPairBodies.Count)
            {
                TranslateTransform transform = _extraPairBodies[extraIndex].RenderTransform as TranslateTransform;
                if (transform != null)
                {
                    transform.X = deltaX;
                    transform.Y = deltaY;
                }
            }
        }

        private int GetDragContentBottomOffset(int pairIndex, OverlayRect display)
        {
            IReadOnlyList<PairSubtitleBody> bodies = _overlaySession.PairBodies;
            if (pairIndex < 0 || pairIndex >= bodies.Count || !bodies[pairIndex].Visible)
            {
                return display.Height;
            }

            FrameworkElement contentElement = null;
            System.Windows.Rect contentBounds = System.Windows.Rect.Empty;

            if (pairIndex == 0)
            {
                contentElement = SubtitleText;
                SubtitleText.UpdateLayout();
                string text = SubtitleText.Text ?? string.Empty;
                int lastCharacter = text.Length - 1;
                while (lastCharacter >= 0 && (text[lastCharacter] == '\r' || text[lastCharacter] == '\n'))
                {
                    lastCharacter--;
                }

                if (lastCharacter >= 0)
                {
                    contentBounds = SubtitleText.GetRectFromCharacterIndex(lastCharacter, true);
                }
            }
            else
            {
                int extraIndex = pairIndex - 1;
                if (extraIndex >= 0 && extraIndex < _extraPairBodies.Count)
                {
                    System.Windows.Controls.TextBlock block = _extraPairBodies[extraIndex];
                    contentElement = block;
                    block.UpdateLayout();
                    if (!string.IsNullOrEmpty(block.Text))
                    {
                        contentBounds = block.ContentEnd.GetCharacterRect(
                            System.Windows.Documents.LogicalDirection.Backward);
                    }
                }
            }

            if (contentElement != null && !contentBounds.IsEmpty)
            {
                System.Windows.Point elementOrigin = contentElement.PointToScreen(new System.Windows.Point(0, 0));
                System.Windows.Point contentBottom = contentElement.PointToScreen(
                    new System.Windows.Point(0, contentBounds.Bottom));
                double offset = contentBottom.Y - elementOrigin.Y;
                if (offset > 0 && offset <= display.Height)
                {
                    return (int)Math.Ceiling(offset);
                }
            }

            // Keep the whole configured display area visible when WPF has not
            // produced measurable text geometry for this drag target.
            Logger.Log.Warn("Could not measure subtitle text bounds during drag; keeping the full display area on screen.");
            return display.Height;
        }

        private void ResetDragVisualTransforms(int pairIndex)
        {
            _dragHandleRenderTransform.X = 0;
            _dragHandleRenderTransform.Y = 0;
            if (pairIndex == 0)
            {
                _subtitleDragRenderTransform.X = 0;
                _subtitleDragRenderTransform.Y = 0;
                _headerDragRenderTransform.X = 0;
                _headerDragRenderTransform.Y = 0;
                return;
            }

            int extraIndex = pairIndex - 1;
            if (extraIndex >= 0 && extraIndex < _extraPairBodies.Count)
            {
                TranslateTransform transform = _extraPairBodies[extraIndex].RenderTransform as TranslateTransform;
                if (transform != null)
                {
                    transform.X = 0;
                    transform.Y = 0;
                }
            }
        }

        private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragHandleDragging)
            {
                return;
            }

            FinishDragHandle(commit: true);
            e.Handled = true;
        }

        private void DragHandle_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_dragHandleDragging && !_dragHandleFinishing)
            {
                FinishDragHandle(commit: true);
            }
        }

        private void FinishDragHandle(bool commit)
        {
            if (!_dragHandleDragging)
            {
                return;
            }

            _dragHandleFinishing = true;
            int pairIndex = _dragHandlePairIndex;
            OverlayRect finalDisplay = commit ? _dragHandlePreviewRect : _dragHandleStartRect;
            if (commit && pairIndex >= 0 && finalDisplay != null && finalDisplay.IsValid)
            {
                // SetDisplay persists the pair layout when the drag is released.
                _overlaySession.SetDisplay(pairIndex, finalDisplay);
            }

            ResetDragVisualTransforms(pairIndex);
            _dragHandleDragging = false;
            _dragHandlePairIndex = -1;
            _dragHandleStartRect = OverlayRect.Invalid;
            _dragHandlePreviewRect = OverlayRect.Invalid;
            _dragHandleContentBottomOffset = 0;
            _dragHandleStartMouseScreen = new System.Windows.Point();
            DragButton.ReleaseMouseCapture();
            _dragHandleFinishing = false;
            ApplyPairOverlay();
            data?.RefreshPairPage();
            UpdateDragHandle();
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
            double displayScale = GetDisplayScale(rect);
            System.Windows.Point canvasPoint = DisplayToCanvas(rect, displayScale);
            double width = rect.Width / displayScale;
            double height = rect.Height / displayScale;
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
                ToolTip = takesMouse
                    ? TryFindResource("Overlay_AdjustHint") as string ?? "Drag inside a frame to move it; drag an edge to resize it."
                    : null,
                Tag = outline
            };
            Canvas.SetLeft(box, canvasPoint.X);
            Canvas.SetTop(box, canvasPoint.Y);
            System.Windows.Controls.Panel.SetZIndex(box, PreviewOutlineBoxZIndex);
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
            System.Windows.Controls.Panel.SetZIndex(label, PreviewOutlineLabelZIndex);
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
                return "区域对 " + ordinal;
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
            _dragResizeEdges = GetResizeEdges(box, e.GetPosition(box));
            _dragStartMouseScreen = OverlayCanvas.PointToScreen(e.GetPosition(OverlayCanvas));
            box.Cursor = CursorForResizeEdges(_dragResizeEdges);
            box.CaptureMouse();
            e.Handled = true;
        }

        private void RegionAdjust_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var box = sender as System.Windows.Shapes.Rectangle;
            if (!_regionDragging)
            {
                if (box != null)
                {
                    box.Cursor = CursorForResizeEdges(GetResizeEdges(box, e.GetPosition(box)));
                }

                return;
            }

            AdjustMouseExit exit = AdjustMouseGuard.MoveExitReason(
                _regionDragging,
                e.LeftButton == MouseButtonState.Pressed,
                _dragTarget);
            if (exit != AdjustMouseExit.None)
            {
                return;
            }

            System.Windows.Point nowScreen = OverlayCanvas.PointToScreen(e.GetPosition(OverlayCanvas));
            int deltaX = (int)Math.Round(nowScreen.X - _dragStartMouseScreen.X);
            int deltaY = (int)Math.Round(nowScreen.Y - _dragStartMouseScreen.Y);
            OverlayRect moved = _dragResizeEdges == RegionResizeEdges.None
                ? MoveRegion(_dragStartRect, deltaX, deltaY)
                : ResizeRegion(_dragStartRect, deltaX, deltaY, _dragResizeEdges);
            ApplyDraggedRegion(moved);

            if (box != null)
            {
                double displayScale = GetDisplayScale(moved);
                System.Windows.Point canvasPoint = DisplayToCanvas(moved, displayScale);
                Canvas.SetLeft(box, canvasPoint.X);
                Canvas.SetTop(box, canvasPoint.Y);
                box.Width = moved.Width / displayScale;
                box.Height = moved.Height / displayScale;
                box.Cursor = CursorForResizeEdges(_dragResizeEdges);
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
            _dragResizeEdges = RegionResizeEdges.None;
            _dragStartRect = OverlayRect.Invalid;
            _dragStartMouseScreen = new System.Windows.Point();
            ApplyOutlines();
            data?.RefreshPairPage();
            data?.RefreshExtraPathDisplayRows();
            e.Handled = true;
        }

        private static RegionResizeEdges GetResizeEdges(
            System.Windows.Shapes.Rectangle box,
            System.Windows.Point point)
        {
            if (box == null)
            {
                return RegionResizeEdges.None;
            }

            double horizontalTolerance = Math.Min(8, box.ActualWidth / 3);
            double verticalTolerance = Math.Min(8, box.ActualHeight / 3);
            RegionResizeEdges edges = RegionResizeEdges.None;
            if (point.X <= horizontalTolerance)
            {
                edges |= RegionResizeEdges.Left;
            }
            else if (point.X >= box.ActualWidth - horizontalTolerance)
            {
                edges |= RegionResizeEdges.Right;
            }

            if (point.Y <= verticalTolerance)
            {
                edges |= RegionResizeEdges.Top;
            }
            else if (point.Y >= box.ActualHeight - verticalTolerance)
            {
                edges |= RegionResizeEdges.Bottom;
            }

            return edges;
        }

        private static System.Windows.Input.Cursor CursorForResizeEdges(RegionResizeEdges edges)
        {
            bool horizontal = (edges & (RegionResizeEdges.Left | RegionResizeEdges.Right)) != 0;
            bool vertical = (edges & (RegionResizeEdges.Top | RegionResizeEdges.Bottom)) != 0;
            if (horizontal && vertical)
            {
                bool sameDirection =
                    ((edges & RegionResizeEdges.Left) != 0) ==
                    ((edges & RegionResizeEdges.Top) != 0);
                return sameDirection
                    ? System.Windows.Input.Cursors.SizeNWSE
                    : System.Windows.Input.Cursors.SizeNESW;
            }

            if (horizontal)
            {
                return System.Windows.Input.Cursors.SizeWE;
            }

            if (vertical)
            {
                return System.Windows.Input.Cursors.SizeNS;
            }

            return System.Windows.Input.Cursors.SizeAll;
        }

        private static OverlayRect MoveRegion(OverlayRect start, int deltaX, int deltaY)
        {
            System.Drawing.Rectangle virtualBounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            int maxX = Math.Max(virtualBounds.Left, virtualBounds.Right - start.Width);
            int maxY = Math.Max(virtualBounds.Top, virtualBounds.Bottom - start.Height);
            int x = Math.Max(virtualBounds.Left, Math.Min(start.X + deltaX, maxX));
            int y = Math.Max(virtualBounds.Top, Math.Min(start.Y + deltaY, maxY));
            return new OverlayRect(x, y, start.Width, start.Height);
        }

        private static OverlayRect ResizeRegion(
            OverlayRect start,
            int deltaX,
            int deltaY,
            RegionResizeEdges edges)
        {
            System.Drawing.Rectangle virtualBounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            int minWidth = Math.Min(12, start.Width);
            int minHeight = Math.Min(12, start.Height);
            int left = start.X;
            int top = start.Y;
            int right = start.X + start.Width;
            int bottom = start.Y + start.Height;

            if ((edges & RegionResizeEdges.Left) != 0)
            {
                left = Math.Max(virtualBounds.Left, Math.Min(start.X + deltaX, right - minWidth));
            }
            if ((edges & RegionResizeEdges.Right) != 0)
            {
                right = Math.Min(virtualBounds.Right, Math.Max(start.X + start.Width + deltaX, left + minWidth));
            }
            if ((edges & RegionResizeEdges.Top) != 0)
            {
                top = Math.Max(virtualBounds.Top, Math.Min(start.Y + deltaY, bottom - minHeight));
            }
            if ((edges & RegionResizeEdges.Bottom) != 0)
            {
                bottom = Math.Min(virtualBounds.Bottom, Math.Max(start.Y + start.Height + deltaY, top + minHeight));
            }

            return new OverlayRect(left, top, right - left, bottom - top);
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
            double targetScale = GetDisplayScale(target);
            return new System.Windows.Rect(
                target.X / targetScale,
                target.Y / targetScale,
                target.Width / targetScale,
                target.Height / targetScale);
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
                        if (_dragHandleDragging)
                        {
                            FinishDragHandle(commit: false);
                        }
                        _overlaySession.StopRecognition();
                        OCRTimer.Stop();
                        UITimer.Stop();
                        UpdateDebugSamplingOverlayVisibility();
                        ApplyPairOverlay();
                        UpdateDragHandle();
                        SystemSounds.Hand.Play();
                        SwitchIcon("mask.ico");
                    }
                    else
                    {
                        _overlaySession.StartRecognition();
                        if (_overlaySession.RecognitionRunning)
                        {
                            UpdateOcrSamplingInterval();
                            UpdateDebugSamplingOverlayVisibility();
                            ApplyPairOverlay();
                            UpdateDragHandle();
                            OCRTimer.Start();
                            UITimer.Start();
                            SystemSounds.Exclamation.Play();
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
                        SystemSounds.Hand.Play();
                    }
                    else
                    {
                        _overlaySession.ShowSubtitles();
                        SystemSounds.Exclamation.Play();
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

        private VoiceAudioSource CreateVoiceAudioSource(
            string audioKey,
            bool logActivity = false,
            Action<bool, string> completion = null,
            string requestLabel = null)
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
                LogActivity = logActivity,
                Completion = completion,
                RequestLabel = requestLabel ?? "voice",
                AudioKey = audioKey
            };
        }

        private void PlayDialogueOptionAudio(string audioKey, bool logActivity = false)
        {
            VoiceAudioSource source = CreateVoiceAudioSource(
                audioKey,
                logActivity,
                requestLabel: "dialogue-option");
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

        private void PlayMainAudio(
            string audioKey,
            bool logActivity = false,
            Action<bool, string> completion = null,
            string requestLabel = null)
        {
            VoiceAudioSource source = CreateVoiceAudioSource(
                audioKey,
                logActivity,
                completion,
                requestLabel ?? "subtitle");
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
            bool logActivity = false,
            string requestLabel = "voice")
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

                        if (args.Exception != null)
                        {
                            if (usingSoundTouch)
                            {
                                if (IsVoiceTestRequest(requestLabel))
                                {
                                    Logger.Log.Warn(
                                        $"[Voice:{requestLabel}] SoundTouch playback failed; " +
                                        $"retrying at normal speed: {args.Exception.Message}");
                                }
                                else
                                {
                                    Logger.Log.Warn(
                                        $"SoundTouch playback failed; retrying at normal speed: " +
                                        args.Exception.Message);
                                }
                                StartAudioPlayback(
                                    filePath,
                                    generation,
                                    allowTempoProcessing: false,
                                    logActivity: logActivity,
                                    requestLabel: requestLabel);
                            }
                            else
                            {
                                if (IsVoiceTestRequest(requestLabel))
                                {
                                    Logger.Log.Error(
                                        $"[Voice:{requestLabel}] playback stopped with error: " +
                                        args.Exception);
                                }
                                DisposeCurrentAudioPlayback();
                                _ = ProcessNextAudioAsync(generation);
                            }
                            return;
                        }

                        if (IsVoiceTestRequest(requestLabel))
                        {
                            Logger.Log.Debug($"[Voice:{requestLabel}] playback stopped normally.");
                        }
                        DisposeCurrentAudioPlayback();
                        _ = ProcessNextAudioAsync(generation);
                    }));
                };
                waveOut.PlaybackStopped += _playbackStoppedHandler;
                waveOut.Init(playbackSource);
                waveOut.Play();
                if (IsVoiceTestRequest(requestLabel))
                {
                    Logger.Log.Info(
                        $"[Voice:{requestLabel}] playback initialized: " +
                        $"format={mediaReader.WaveFormat.SampleRate}Hz/" +
                        $"{mediaReader.WaveFormat.Channels}ch/{mediaReader.WaveFormat.BitsPerSample}bit, " +
                        $"tempoProcessing={usingSoundTouch}");
                }
                if (logActivity)
                {
                    _overlaySession.NoteVoicePlaybackStarted();
                }
            }
            catch (Exception ex) when (usingSoundTouch)
            {
                if (IsVoiceTestRequest(requestLabel))
                {
                    Logger.Log.Warn(
                        $"[Voice:{requestLabel}] SoundTouch initialization failed; " +
                        $"retrying at normal speed: {ex.Message}");
                }
                else
                {
                    Logger.Log.Warn(
                        $"SoundTouch initialization failed; retrying at normal speed: " +
                        ex.Message);
                }
                DisposeCurrentAudioPlayback();
                StartAudioPlayback(
                    filePath,
                    generation,
                    allowTempoProcessing: false,
                    logActivity: logActivity,
                    requestLabel: requestLabel);
            }
            catch (Exception ex)
            {
                if (IsVoiceTestRequest(requestLabel))
                {
                    Logger.Log.Error(
                        $"[Voice:{requestLabel}] playback initialization failed: {ex}");
                }
                DisposeCurrentAudioPlayback();
                throw;
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
                        if (IsVoiceTestRequest(source.RequestLabel))
                        {
                            Logger.Log.Info(
                                $"[Voice:{source.RequestLabel}] local audio selected: " +
                                $"bytes={new FileInfo(source.LocalFilePath).Length}");
                        }
                        await Dispatcher.InvokeAsync(() =>
                        {
                            lock (_audioPlaybackQueueLock)
                            {
                                if (generation != _audioPlaybackGeneration)
                                {
                                    if (IsVoiceTestRequest(source.RequestLabel))
                                    {
                                        Logger.Log.Warn(
                                            $"[Voice:{source.RequestLabel}] request superseded before local playback.");
                                    }
                                    source.Completion?.Invoke(false, "superseded");
                                    return;
                                }
                            }

                            tempFilePath = source.LocalFilePath;
                            StartAudioPlayback(
                                source.LocalFilePath,
                                generation,
                                logActivity: source.LogActivity,
                                requestLabel: source.RequestLabel);
                            source.Completion?.Invoke(true, null);
                        });
                        return;
                    }

                    Logger.Log.Warn(
                        "Local voice file has an unsupported format; falling back to server.");
                }

                string tempFile = Path.GetTempFileName();
                try
                {
                    if (IsVoiceTestRequest(source.RequestLabel))
                    {
                        Logger.Log.Info(
                            $"[Voice:{source.RequestLabel}] downloading audio: " +
                            $"md5={source.AudioKey}");
                    }
                    using (var webClient = new WebClient())
                    {
                        webClient.Headers[HttpRequestHeader.UserAgent] = "GI-Subtitles/1.0";
                        await webClient.DownloadFileTaskAsync(new Uri(source.RemoteUrl), tempFile);
                    }

                    long downloadedBytes = new FileInfo(tempFile).Length;
                    if (!IsAudioTempFile(tempFile))
                    {
                        string details = IsVoiceTestRequest(source.RequestLabel)
                            ? $"bytes={downloadedBytes}, header={ReadAudioHeader(tempFile)}"
                            : "unsupported audio format";
                        throw new InvalidDataException(
                            "Downloaded voice file has an unsupported format: " + details + ".");
                    }

                    if (IsVoiceTestRequest(source.RequestLabel))
                    {
                        Logger.Log.Info(
                            $"[Voice:{source.RequestLabel}] download validated: " +
                            $"bytes={downloadedBytes}, header={ReadAudioHeader(tempFile)}");
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        lock (_audioPlaybackQueueLock)
                        {
                            if (generation != _audioPlaybackGeneration)
                            {
                                if (IsVoiceTestRequest(source.RequestLabel))
                                {
                                    Logger.Log.Warn(
                                        $"[Voice:{source.RequestLabel}] request superseded before downloaded playback.");
                                }
                                TryDeleteAudioTempFile(tempFile);
                                source.Completion?.Invoke(false, "superseded");
                                return;
                            }
                        }

                        tempFilePath = tempFile;
                        StartAudioPlayback(
                            tempFile,
                            generation,
                            logActivity: source.LogActivity,
                            requestLabel: source.RequestLabel);
                        source.Completion?.Invoke(true, null);
                    });
                    return;
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse response &&
                                              response.StatusCode == HttpStatusCode.NotFound)
                {
                    if (IsVoiceTestRequest(source.RequestLabel))
                    {
                        Logger.Log.Warn(
                            $"[Voice:{source.RequestLabel}] server returned HTTP 404; " +
                            $"audio is unavailable for md5={source.AudioKey}.");
                    }
                    source.Completion?.Invoke(false, "not-found");
                }
                catch (Exception ex)
                {
                    if (IsVoiceTestRequest(source.RequestLabel))
                    {
                        Logger.Log.Warn(
                            $"[Voice:{source.RequestLabel}] preparation failed: {ex}");
                    }
                    else
                    {
                        Logger.Log.Warn($"Voice playback preparation failed: {ex.Message}");
                    }
                    source.Completion?.Invoke(false, ex.Message);
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
                StartAudioPlayback(tempFilePath, generation, requestLabel: "subtitle");
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

        public void PlayVoiceTest(Action<bool, string> completion = null)
        {
            const string testAudioMd5 = "6f3ea6152a7864d324404f8d93a70a1a";
            Logger.Log.Info($"[VoiceTest] requested: md5={testAudioMd5}");
            PlayMainAudio(
                testAudioMd5,
                completion: completion,
                requestLabel: "test");
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
