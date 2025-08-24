using Emgu.CV.Dnn;
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
    public partial class MainWindow : Window
    {
        private static int OCR_TIMER = 0;
        private static int UI_TIMER = 0;
        string ocrText = null;
        private NotifyIcon notifyIcon;
        string lastRes = null;
        readonly Dictionary<string, string> resDict = new Dictionary<string, string>();
        public System.Windows.Threading.DispatcherTimer OCRTimer = new System.Windows.Threading.DispatcherTimer();
        public System.Windows.Threading.DispatcherTimer UITimer = new System.Windows.Threading.DispatcherTimer();
        readonly bool debug = Config.Get<bool>("Debug", false);
        readonly string server = Config.Get<string>("Server");
        readonly string token = Config.Get<string>("Token");
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
        Dictionary<string, string> BitmapDict = new Dictionary<string, string>();
        List<string> AudioList = new List<string>();
        string InputLanguage = Config.Get<string>("Input");
        string OutputLanguage = Config.Get<string>("Output");
        string Game = Config.Get<string>("Game");
        string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        INotifyIcon notify;
        Data data;
        Dictionary<string, string> VoiceMap = new Dictionary<string, string>();
        SoundPlayer player = new SoundPlayer();
        private System.Drawing.Rectangle screenBounds = Screen.PrimaryScreen.Bounds;
        bool ShowText = true;
        bool ChooseRegion = false;
        private IWavePlayer waveOut;
        private MediaFoundationReader mediaReader;
        private string tempFilePath;



        public MainWindow()
        {
            Logger.Log.Debug("Start App");
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }


        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 获取窗口句柄
            IntPtr handle = new WindowInteropHelper(this).Handle;
            RegisterHotKey(handle, HOTKEY_ID_1, MOD_CTRL | MOD_SHIFT, VK_S);
            RegisterHotKey(handle, HOTKEY_ID_2, MOD_CTRL | MOD_SHIFT, VK_R);
            RegisterHotKey(handle, HOTKEY_ID_3, MOD_CTRL | MOD_SHIFT, VK_H);
            RegisterHotKey(handle, HOTKEY_ID_4, MOD_CTRL | MOD_SHIFT, VK_D);

            // 监听窗口消息
            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(WndProc);
            data = new Data(version, Scale);
            notify = new INotifyIcon();
            notifyIcon = notify.InitializeNotifyIcon(Scale);
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
                                    data.ShowDialog();
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
                VoiceMap = VoiceContentHelper.LoadAudioMap(server, Game);
            }
            if (notify.Region[1] == "0")
            {
                About about = new About(version);
                about.Show();
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
        }

        public void GetOCR(object sender, EventArgs e)
        {
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
                    target = CaptureRegion(Convert.ToInt16(notify.Region[0]), Convert.ToInt16(notify.Region[1]), Convert.ToInt16(notify.Region[2]), Convert.ToInt16(notify.Region[3]));
                    if (data.IsVisible)
                    {
                        data.SetImage(target);
                    }
                    Bitmap enhanced = target;
                    if (Game != "Wuthering")
                    {
                        enhanced = ImageProcessor.EnhanceTextInImage(target);
                    }

                    string bitStr = Bitmap2String(enhanced);
                    if (BitmapDict.ContainsKey(bitStr))
                    {
                        ocrText = BitmapDict[bitStr];
                    }
                    else
                    {
                        OCRResult ocrResult = data.engine.DetectText(enhanced);
                        ocrText = ocrResult.Text;
                        if (debug)
                        {
                            Logger.Log.Debug(DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss_ffffff") + ".png");
                            target.Save(DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss_ffffff") + ".png");
                            Logger.Log.Debug(ocrText);
                        }
                        var maxY = 0;
                        foreach (var i in ocrResult.TextBlocks)
                        {
                            foreach (var j in i.BoxPoints)
                            {
                                if (j.Y > maxY)
                                {
                                    maxY = j.Y;
                                }
                            }
                        }

                        if (Math.Abs(Pad) > 500)
                        {
                            Pad = 100;
                        }
                        double top = Convert.ToInt16(notify.Region[1]) / Scale + Pad;
                        foreach (var screen in Screen.AllScreens)
                        {
                            if (screen.WorkingArea.Contains(new System.Drawing.Point(Convert.ToInt16(notify.Region[0]), Convert.ToInt16(notify.Region[1]))))
                            {
                                double scale = GetScaleForScreen(screen);
                                double left = screen.Bounds.Left / scale;
                                if (top > screen.Bounds.Bottom / scale - 20 || notify.Region[1] == "0")
                                {
                                    top = screen.Bounds.Bottom / scale - 20;
                                }
                                if (top < screen.Bounds.Top / scale + 20)
                                {
                                    top = screen.Bounds.Top / scale + 20;
                                }
                                this.Top = top;
                                double width = Convert.ToInt16(notify.Region[2]) / scale + 200;
                                if (width > screen.Bounds.Width / scale)
                                {
                                    width = screen.Bounds.Width / scale;
                                }
                                this.Left = left + (screen.Bounds.Width / scale - width) / 2;
                                this.Width = width;
                            }
                        }

                        this.Height = 100;
                        BitmapDict.Add(bitStr, ocrText);
                        if (BitmapDict.Count > 10)
                        {
                            BitmapDict.Remove(BitmapDict.ElementAt(0).Key);
                        }
                    }
                    Logger.Log.Debug($"OCR Content: {ocrText}");

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
                    if (ocrText.Length > 1)
                    {
                        if (resDict.ContainsKey(ocrText))
                        {
                            res = resDict[ocrText];
                            key = resDict[res];
                        }
                        else
                        {
                            DateTime dateTime = DateTime.Now;
                            res = VoiceContentHelper.FindClosestMatch(ocrText, data.contentDict, out key);
                            Logger.Log.Debug($"Convert ocrResult: {res}");
                            resDict[ocrText] = res;
                            resDict[res] = key;
                            if (BitmapDict.Count > 20)
                            {
                                BitmapDict.Remove(BitmapDict.ElementAt(0).Key);
                            }
                        }
                    }
                    if (res != lastRes)
                    {
                        lastRes = res;
                        SubtitleText.Text = res;
                        SubtitleText.FontSize = Config.Get<int>("Size");
                        if (!AudioList.Contains(key))
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


        public static string Bitmap2String(Bitmap bmp)
        {
            MemoryStream ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            byte[] arr = new byte[ms.Length];
            ms.Position = 0;
            ms.Read(arr, 0, (int)ms.Length);
            ms.Close();
            return Convert.ToBase64String(arr);
        }


        public static Bitmap CaptureRegion(int x, int y, int width, int height)
        {
            using (Bitmap bitmap = new Bitmap(width, height))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
                }
                return (Bitmap)bitmap.Clone();
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
            IntPtr handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HOTKEY_ID_1);
            UnregisterHotKey(handle, HOTKEY_ID_2);
            UnregisterHotKey(handle, HOTKEY_ID_3);
            UnregisterHotKey(handle, HOTKEY_ID_4);
        }

        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            Pad = Convert.ToInt16(this.Top - Convert.ToInt16(notify.Region[1]) / Scale);
            if (Pad < 500)
            {
                Config.Set("Pad", Pad);
            }
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
                    ShowRegionOverlay();
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

        private void ShowRegionOverlay()
        {
            if (notify.Region[1] == "0") return;
            int x = Convert.ToInt16(int.Parse(notify.Region[0]) / Scale);
            int y = Convert.ToInt16(int.Parse(notify.Region[1]) / Scale);
            int w = Convert.ToInt16(int.Parse(notify.Region[2]) / Scale);
            int h = Convert.ToInt16(int.Parse(notify.Region[3]) / Scale);
            Logger.Log.Debug($"x {x} y {y} w {w} h {h}");

            var overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Width = SystemParameters.VirtualScreenWidth,
                Height = SystemParameters.VirtualScreenHeight,
                Left = 0,
                Top = 0
            };

            var canvas = new Canvas();
            var rect = new System.Windows.Shapes.Rectangle
            {
                Stroke = System.Windows.Media.Brushes.LimeGreen,
                StrokeThickness = 10,
                Width = w,
                Height = h,
                IsHitTestVisible = true // 确保可以捕获鼠标事件
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            canvas.Children.Add(rect);
            overlay.Content = canvas;

            overlay.Show();

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            timer.Tick += (_, __) =>
                        {
                            timer.Stop();
                            overlay.Close();
                        };
            timer.Start();
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
