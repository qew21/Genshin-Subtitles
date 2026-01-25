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
    /// MainWindow.xaml 的交互逻辑
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
        // 使用 LRU 缓存限制内存占用，限制为100个条目
        readonly LRUCache<string, string> resDict = new LRUCache<string, string>(100);
        public System.Windows.Threading.DispatcherTimer OCRTimer = new System.Windows.Threading.DispatcherTimer();
        public System.Windows.Threading.DispatcherTimer UITimer = new System.Windows.Threading.DispatcherTimer();
        readonly bool debug = Config.Get<bool>("Debug", false);
        readonly string server = Config.Get<string>("Server");
        readonly string token = Config.Get<string>("Token");
        readonly int distant = Config.Get<int>("Distant", 3);
        int Pad = Config.Get<int>("Pad");
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int y, int Width, int Height, int flags);
        [DllImport("User32.dll")]
        private static extern int GetDpiForSystem();
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID_1 = 9000; // 自定义热键ID
        private const int HOTKEY_ID_2 = 9001; // 自定义热键ID
        private const int HOTKEY_ID_3 = 9002; // 自定义热键ID
        private const int HOTKEY_ID_4 = 9003;
        private const uint MOD_CTRL = 0x0002; // Ctrl键
        private const uint MOD_SHIFT = 0x0004; // Shift键
        private const uint VK_S = 0x53; // S键的虚拟键码
        private const uint VK_R = 0x52; // R键的虚拟键码
        private const uint VK_H = 0x48; // H键的虚拟键码
        private const uint VK_D = 0x44;
        private double Scale = GetDpiForSystem() / 96f;
        // 使用 LRU 缓存限制内存占用，限制为30个条目（图像哈希到OCR文本的映射）
        LRUCache<string, string> BitmapDict = new LRUCache<string, string>(30);
        List<string> AudioList = new List<string>();
        string InputLanguage = Config.Get<string>("Input");
        string OutputLanguage = Config.Get<string>("Output");
        bool Multi = Config.Get<bool>("Multiline", false);
        string Game = Config.Get<string>("Game");
        string Update = Config.Get<string>("Update");
        string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
        INotifyIcon notify;
        SettingsWindow data;
        Dictionary<string, string> VoiceMap = new Dictionary<string, string>();
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
            Loaded += MainWindow_Loaded;
            CheckAndUpdate(Update);
        }


        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 获取窗口句柄
            IntPtr handle = new WindowInteropHelper(this).Handle;
            // 监听窗口消息
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
                                    notifyIcon.ShowBalloonTip(3000, "语言包更新通知", $"仓库更新时间{repoDate}，本地修改时间{inputDate}", ToolTipIcon.Info);
                                    string originalTitle = data.Title;
                                    data.Title = $"【语言包更新】{originalTitle}";
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
                VoiceMap = VoiceContentHelper.LoadAudioMap(server, Path.Combine(dataDir, Game));
            }
            if (notify.Region[1] == "0")
            {
                data.Show();
            }


            data.LoadEngine();

            OCRTimer.Interval = new TimeSpan(0, 0, 0, 0, 500);
            OCRTimer.Tick += GetOCR;    //委托，要执行的方法


            UITimer.Interval = new TimeSpan(0, 0, 0, 0, 500);
            UITimer.Tick += UpdateText;    //委托，要执行的方法

            SetWindowPos(new WindowInteropHelper(this).Handle, -1, 0, 0, 0, 0, 1 | 2);
            this.Width = screenBounds.Width;
            this.Top = screenBounds.Bottom / Scale - this.Height;
            this.Left = screenBounds.Left / Scale;
            this.LocationChanged += MainWindow_LocationChanged;

            if (Directory.Exists("Images"))
            {
                OCRSummary.ProcessFolder("Images", data.engine);
                notifyIcon.Dispose();
                notifyIcon = null;
                data.UnregisterAllHotkeys();
                data.RealClose();
                System.Environment.Exit(0);
            }
            else if (Directory.Exists("Videos"))
            {
                string demoVideoPath = Path.Combine("Videos", "demo.mp4");
                string demoRegionPath = Path.Combine("Videos", "demo_region.json");

                if (File.Exists(demoVideoPath) && File.Exists(demoRegionPath))
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            VideoProcessorHelper.ProcessDemoVideo(demoVideoPath, demoRegionPath, data.engine, () =>
                            {
                                // 清理并退出（需要在UI线程上执行）
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    notifyIcon?.Dispose();
                                    notifyIcon = null;
                                    data?.UnregisterAllHotkeys();
                                    data?.RealClose();
                                    System.Environment.Exit(0);
                                });
                            });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"处理失败: {ex.Message}");
                            Logger.Log.Error(ex);
                            System.Windows.Application.Current.Dispatcher.Invoke(() => System.Environment.Exit(1));
                        }
                    });
                    return;
                }
                else
                {
                    var video = new Video(data.engine);
                    video.ShowDialog();
                }
            }
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

                    // 使用 LRU 缓存查找
                    if (BitmapDict.TryGetValue(bitStr, out string cachedOcrText))
                    {
                        ocrText = cachedOcrText;
                    }
                    else
                    {
                        string matchedImageHash = ImageProcessor.FindSimilarImageHash(bitStr, BitmapDict, maxDistance: distant);
                        if (matchedImageHash != null)
                        {
                            int distance = ImageProcessor.CalculateHammingDistance(bitStr, matchedImageHash);
                            ocrText = BitmapDict[matchedImageHash];
                            BitmapDict[bitStr] = ocrText; // LRU 缓存会自动管理大小
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

                            double top = Convert.ToInt16(notify.Region[1]) / Scale + Config.Get<int>("Pad");
                            foreach (var screen in Screen.AllScreens)
                            {
                                if (screen.WorkingArea.Contains(new System.Drawing.Point(Convert.ToInt16(notify.Region[0]), Convert.ToInt16(notify.Region[1]))))
                                {
                                    double scale = GetScaleForScreen(screen);
                                    double left = screen.Bounds.Left / scale;
                                    this.Top = top;
                                    double width = Convert.ToInt16(notify.Region[2]) / scale + 200;
                                    this.Left = left + (screen.Bounds.Width / scale - width) / 2;
                                    this.Width = width;
                                }
                            }

                            this.Height = 100;
                            BitmapDict[bitStr] = ocrText; // LRU 缓存会自动管理大小，无需手动检查
                        }

                    }

                    // 在 SetImage 之前设置图像（SetImage 会保存引用，所以不在这里释放）
                    if (data.IsVisible)
                    {
                        data.SetImage(target);
                    }
                    else
                    {
                        // 如果不需要显示，立即释放资源
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
                        if (Multi)
                        {
                            // 使用新的分离方法
                            var matchResult = data.Matcher.FindMatchWithHeaderSeparated(ocrText, out key);
                            header = matchResult.Header ?? "";
                            content = matchResult.Content ?? "";
                            res = string.IsNullOrEmpty(header) ? content : (header + " " + content);

                            Logger.Log.Debug($"Convert ocrResult for {ocrText}: header={header}, content={content}, key={key}");

                            // 缓存仍然使用拼接后的结果用于兼容性
                            if (!resDict.ContainsKey(ocrText))
                            {
                                resDict[ocrText] = res;
                                resDict[res] = key;
                            }
                        }
                        else
                        {
                            // 使用 LRU 缓存查找
                            if (resDict.TryGetValue(ocrText, out string cachedRes))
                            {
                                res = cachedRes;
                                // 查找对应的 key
                                if (resDict.TryGetValue(res, out string cachedKey))
                                {
                                    key = cachedKey;
                                }
                            }
                            else
                            {
                                res = data.Matcher.FindClosestMatch(ocrText, out key);
                                Logger.Log.Debug($"Convert ocrResult for {ocrText}: {res},{key}");
                                // LRU 缓存会自动管理大小，无需手动检查
                                resDict[ocrText] = res;
                                resDict[res] = key;
                            }
                            content = res;
                            header = "";
                        }
                    }

                    // 检查内容是否有变化（主要检查content，因为它是主要内容）
                    bool contentChanged = content != lastContent;
                    bool headerChanged = header != lastHeader;

                    if (contentChanged || headerChanged)
                    {
                        if (Multi)
                        {
                            // 分别设置header和content
                            if (headerChanged)
                            {
                                lastHeader = header;
                                if (!string.IsNullOrEmpty(header))
                                {
                                    HeaderText.Text = header;
                                    HeaderText.Visibility = Visibility.Visible;
                                    // 计算content的字体大小，动态调整header的上移距离
                                    int fontSize = Config.Get<int>("Size");
                                    // header上移距离 = content字体大小的一半 + header字体大小的一半 + 间距
                                    var transform = (System.Windows.Media.TranslateTransform)HeaderText.RenderTransform;
                                    transform.Y = -(fontSize / 2.0 + 7 + 4); // 7是header字体14的一半，4是间距
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
                                // 如果header可见，更新header的上移距离，使其显示在content上方
                                if (HeaderText.Visibility == Visibility.Visible && !string.IsNullOrEmpty(lastHeader))
                                {
                                    var transform = (System.Windows.Media.TranslateTransform)HeaderText.RenderTransform;
                                    // 上移距离 = content字体大小的一半 + header字体大小的一半 + 间距
                                    transform.Y = -(fontSize / 2.0 + 7 + 4); // 7是header字体14的一半，4是间距
                                }
                            }
                        }
                        else
                        {
                            // 非Multi模式，保持原有逻辑
                            if (res != lastRes)
                            {
                                lastRes = res;
                                lastContent = content;
                                SubtitleText.Text = res;
                                SubtitleText.FontSize = Config.Get<int>("Size");
                                // 确保header在非Multi模式下始终隐藏
                                HeaderText.Visibility = Visibility.Collapsed;
                            }
                        }

                        // 播放音频（只在content变化时播放，避免重复播放）
                        if (contentChanged && !AudioList.Contains(key))
                        {
                            string text = key;
                            if (VoiceMap.ContainsKey(text))
                            {
                                var audioPath = VoiceMap[text];
                                if (string.IsNullOrEmpty(server))
                                {
                                    PlayAudio(audioPath);
                                }
                                else
                                {
                                    PlayAudioFromUrl($"{server}?md5={audioPath}&token={token}");
                                }
                            }
                            Logger.Log.Debug($"key: {key}, contains: {VoiceMap.ContainsKey(text)}");
                            AudioList.Add(key);
                        }
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
        /// 捕获屏幕区域，修复内存泄漏问题
        /// 优化：直接返回 Bitmap，由调用者负责释放，避免 Clone() 导致的内存问题
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

            // 验证 width 和 height 必须大于 0
            if (width <= 0 || height <= 0)
            {
                Logger.Log.Error($"Invalid region dimensions: width={width}, height={height}");
                throw new ArgumentException($"Region dimensions must be positive: width={width}, height={height}");
            }

            // 验证坐标是否在屏幕范围内（可选，但有助于调试）
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
                return bitmap; // 直接返回，由调用者负责释放
            }
            catch (Exception ex)
            {
                // 如果出错，确保释放资源
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
            Pad = Convert.ToInt16(this.Top - Convert.ToInt16(notify.Region[1]) / Scale);
            Config.Set("Pad", Pad);
        }


        public void SwitchIcon(string iconName)
        {
            Uri iconUri = new Uri($"pack://application:,,,/Resources/{iconName}");
            Stream iconStream = System.Windows.Application.GetResourceStream(iconUri).Stream;

            // 创建新的Icon对象
            Icon newIcon = new Icon(iconStream);

            // 更新NotifyIcon的图标
            notifyIcon.Icon = newIcon;
        }

        // 处理窗口消息
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

                // 下载文件到临时文件
                using (var webClient = new WebClient())
                {
                    string tempFile = Path.GetTempFileName();
                    Console.WriteLine($"{tempFile}, {tempFilePath}");
                    if (tempFile != tempFilePath)
                    {
                        webClient.DownloadFile(url, tempFile);
                        StopAudio();
                        tempFilePath = tempFile;

                        // 使用 MediaFoundationReader 从文件读取
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
            // 获取屏幕的工作区域中心点
            System.Drawing.Point screenCenter = new System.Drawing.Point(
                screen.Bounds.Left + screen.Bounds.Width / 2,
                screen.Bounds.Top + screen.Bounds.Height / 2
            );

            // 获取屏幕的句柄
            IntPtr monitorHandle = NativeMethods.MonitorFromPoint(screenCenter, 2); // MONITOR_DEFAULTTONEAREST

            // 获取 DPI 值
            uint dpiX, dpiY;
            NativeMethods.GetDpiForMonitor(monitorHandle, NativeMethods.MonitorDpiType.EffectiveDpi, out dpiX, out dpiY);

            // 计算缩放比例（基准 DPI 为 96）
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
                System.Windows.Forms.DialogResult dr = System.Windows.Forms.MessageBox.Show($"发现新版本 {remote["version"]}，是否更新？\n更新日期：{remote["date"]}\n更新内容：\n{remote["info"]}",
                                                  "更新提示", System.Windows.Forms.MessageBoxButtons.YesNo);
                if (dr == System.Windows.Forms.DialogResult.Yes)
                {
                    try
                    {
                        var msi = Path.GetTempFileName() + ".msi";
                        new WebClient().DownloadFile(remote["url"], msi);

                        var startInfo = new ProcessStartInfo
                        {
                            FileName = "msiexec.exe",
                            // 使用 /i 安装新版本，/quiet 静默，/norestart 防止自动重启
                            Arguments = $"/i \"{msi}\" /quiet /norestart",
                            UseShellExecute = true,
                            Verb = "runas"  // 请求管理员权限（如果 MSI 需要）
                        };

                        Process updaterProcess = Process.Start(startInfo);
                        Logger.Log.Debug($"启动安装: msiexec {startInfo.Arguments}");
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
