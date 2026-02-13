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
using System.Net;
using Microsoft.Win32;
using System.Diagnostics;
using System.Web;
using System.Runtime.InteropServices.ComTypes;
using Newtonsoft.Json;
using System.Security.Policy;
using System.ServiceModel.PeerResolvers;
using System.Net.Http;


[assembly: log4net.Config.XmlConfigurator(Watch = true)]
namespace GI_Subtitles
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
        string ocrText = null;
        private NotifyIcon notifyIcon;
        string lastRes = null;
        string lastHeader = null;
        string lastContent = null;
        // Use an LRU cache to limit memory usage to 100 entries
        readonly LRUCache<string, string> resDict = new LRUCache<string, string>(100);
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
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID_1 = 9000; // Custom hotkey ID
        private const int HOTKEY_ID_2 = 9001; // Custom hotkey ID
        private const int HOTKEY_ID_3 = 9002; // Custom hotkey ID
        private const int HOTKEY_ID_4 = 9003;
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
        string Update = Config.Get<string>("Update");
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
        private string tempFilePath;
        private int failedCount = 0;
        private bool usingRegion2 = false;


        public MainWindow()
        {
            Logger.Log.Debug("Start App");
            InitializeComponent();
            // Start with the main window fully transparent to avoid showing incomplete UI during heavy startup work.
            // Using Opacity instead of Visibility to ensure Loaded is still raised and initialization runs as usual.
            this.Opacity = 0;
            Loaded += MainWindow_Loaded;
            CheckAndUpdate(Update);
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
            if (!data.FileExists())
            {
                data.ShowDialog();
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
                                    data.ShowDialog();
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

            OCRTimer.Interval = new TimeSpan(0, 0, 0, 0, 500);
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
            if (Interlocked.Exchange(ref OCR_TIMER, 1) == 0)
            {
                Logger.Log.Debug("Start OCR");
                SetWindowPos(new WindowInteropHelper(this).Handle, -1, 0, 0, 0, 0, 1 | 2);
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
                    Mat enhanced = target.ToMat();
                    string bitStr = ImageProcessor.ComputeRobustHash(enhanced);

                    // Use LRU cache lookup
                    if (BitmapDict.TryGetValue(bitStr, out string cachedOcrText))
                    {
                        ocrText = cachedOcrText;
                    }
                    else
                    {
                        string matchedImageHash = ImageProcessor.FindSimilarImageHash(bitStr, BitmapDict, maxDistance: distant);
                        if (matchedImageHash != null)
                        {
                            ocrText = BitmapDict[matchedImageHash];
                            BitmapDict[bitStr] = ocrText; // LRU cache automatically manages its size, no manual checks needed
                        }
                        else
                        {
                            OCRResult ocrResult = data.engine.DetectTextFromMat(enhanced);
                            ocrText = ocrResult.Text;

                            if (debug)
                            {
                                Logger.Log.Debug(DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss_ffffff") + ".png");
                                target.Save(Path.Combine(dataDir, DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss_ffffff") + ".png"));
                                Logger.Log.Debug($"OCR Text: {ocrText}");
                            }

                            UpdateWindowPosition();
                            BitmapDict[bitStr] = ocrText; // LRU cache automatically manages its size, no manual checks needed
                        }

                    }

                    // Set image before calling SetImage (SetImage keeps a reference, so we don't dispose here)
                    if (data.IsVisible)
                    {
                        data.SetImage(target);
                    }
                    else
                    {
                        // If it does not need to be displayed, release resources immediately
                        target?.Dispose();
                    }
                    Logger.Log.Debug($"OCR Content: {ocrText}");
                    if (ocrText.Length < 2)
                    {
                        failedCount++;
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
                    this.Width = width;
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
                    double margin = 50;
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
                    this.Height = desiredHeight;
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
                        // Use the new separation method
                        var matchResult = data.Matcher.FindMatchWithHeaderSeparated(ocrText, out key);
                        header = matchResult.Header ?? "";
                        content = matchResult.Content ?? "";
                        res = string.IsNullOrEmpty(header) ? content : (header + " " + content);

                        Logger.Log.Debug($"Convert ocrResult for {ocrText}: header={header}, content={content}, key={key}");

                        // Cache still uses the concatenated result for compatibility
                        if (!resDict.ContainsKey(ocrText))
                        {
                            resDict[ocrText] = res;
                            resDict[res] = key;
                        }
                    }

                    // Check whether the content has changed (mainly check content, which is the main text)
                    bool contentChanged = content != lastContent;
                    bool headerChanged = header != lastHeader;

                    if (contentChanged || headerChanged)
                    {
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
                        if (Config.Get<bool>("PlayVoice", false) && contentChanged && !AudioList.Contains(key))
                        {
                            string audioKey = VoiceContentHelper.CalculateMd5Hash(key);
                            PlayAudioFromUrl($"{server}?md5={audioKey}&token={token}");
                            AudioList.Add(key);
                        }

                        // Adapt window height and position when text changes
                        UpdateWindowHeightAndTop();
                    }
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
                    if (HeaderText.Visibility != Visibility.Visible || string.IsNullOrEmpty(lastHeader))
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
                    HeaderText.UpdateLayout();
                    double headerHeight = HeaderText.ActualHeight;
                    if (headerHeight <= 0)
                    {
                        headerHeight = 14; // Header font size is 14
                    }

                    // Calculate upward offset: half of content height + half of header height + spacing
                    var transform = (System.Windows.Media.TranslateTransform)HeaderText.RenderTransform;
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

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }


        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            notifyIcon.Dispose();
            notifyIcon = null;
            data.UnregisterAllHotkeys();
            data.RealClose();
        }

        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            int pad = Convert.ToInt16(this.Top - Convert.ToInt16(notify.Region[1]) / Scale);
            int padHorizontal = Config.GetPadHorizontal();
            Config.Set("Pad", new int[] { pad, padHorizontal });
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

        public void PlayAudioFromUrl(string url)
        {
            Console.WriteLine(url);
            try
            {
                if (waveOut == null)
                {
                    waveOut = new WaveOutEvent();
                }

                // Download the file to a temporary file
                using (var webClient = new WebClient())
                {
                    string tempFile = Path.GetTempFileName();
                    Console.WriteLine($"{tempFile}, {tempFilePath}");
                    if (tempFile != tempFilePath)
                    {
                        webClient.DownloadFile(url, tempFile);
                        StopAudio();
                        tempFilePath = tempFile;

                        // Use MediaFoundationReader to read from the file
                        mediaReader = new MediaFoundationReader(tempFile);
                        waveOut.Init(mediaReader);
                        waveOut.Play();
                        //StopAudio();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        public void StopAudio()
        {
            waveOut?.Stop();
            mediaReader?.Dispose();
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


        async void CheckAndUpdate(string url)
        {
            Dictionary<string, string> remote = new Dictionary<string, string>();
            try
            {
                HttpClient client = new HttpClient();
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                string responseText = await response.Content.ReadAsStringAsync();
                remote = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseText);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            if (remote == null || !remote.ContainsKey("version"))
            {
                return;
            }
            if (new Version(remote["version"]) > new Version(version))
            {
                System.Windows.Forms.DialogResult dr = System.Windows.Forms.MessageBox.Show($"New version {remote["version"]} found, update?\nUpdate date: {remote["date"]}\nUpdate content:\n{remote["info"]}",
                                                  "Update notification", System.Windows.Forms.MessageBoxButtons.YesNo);
                if (dr == System.Windows.Forms.DialogResult.Yes)
                {
                    try
                    {
                        var msi = Path.GetTempFileName() + ".msi";
                        new WebClient().DownloadFile(remote["url"], msi);

                        var startInfo = new ProcessStartInfo
                        {
                            FileName = "msiexec.exe",
                            // Use /i to install the new version, /quiet for silent install, /norestart to prevent automatic restart
                            Arguments = $"/i \"{msi}\" /quiet /norestart",
                            UseShellExecute = true,
                            Verb = "runas"  // Request administrator privileges (if MSI requires it)
                        };

                        Process updaterProcess = Process.Start(startInfo);
                        Logger.Log.Debug($"Start installation: msiexec {startInfo.Arguments}");
                        System.Windows.Application.Current.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Error(ex);
                    }
                }
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
