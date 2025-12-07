using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
using System.Net.Http;
using Newtonsoft.Json;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.Reflection;
using Emgu.CV.CvEnum;
using PaddleOCRSharp;
using System.Drawing;
using System.Runtime.Remoting.Contexts;
using System.Threading;
using System.Windows.Markup;
using System.Collections;
using System.Globalization;
using System.Web.UI.WebControls;
using System.Xml;
using System.ServiceModel.Syndication;
using System.Runtime.InteropServices;

namespace GI_Subtitles
{
    /// <summary>
    /// SettingsWindow.xaml 的交互逻辑
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public string repoUrl = "https://gitlab.com/Dimbreath/AnimeGameData/-/refs/master/logs_tree/TextMap?format=json&offset=0&ref_type=heads";
        string Game = Config.Get<string>("Game");
        string InputLanguage = Config.Get<string>("Input");
        string OutputLanguage = Config.Get<string>("Output");
        private const int MaxRetries = 1; // 最大重试次数
        private static readonly HttpClient client = new HttpClient();
        public Dictionary<string, string> contentDict = new Dictionary<string, string>();
        readonly Dictionary<string, string> OutputLanguages = new Dictionary<string, string>() { { "简体中文", "CHS" }, { "English", "EN" }, { "日本語", "JP" }, { "繁體中文", "CHT" }, { "Deutsch", "DE" }, { "Español", "ES" }, { "Français", "FR" }, { "Bahasa Indonesia", "ID" }, { "한국어", "KR" }, { "Português", "PT" }, { "Русский", "RU" }, { "ไทย", "TH" }, { "Tiếng Việt", "VI" } };
        readonly Dictionary<string, string> InputLanguages = new Dictionary<string, string>()
            {
                { "简体中文", "CHS"},
                { "English", "EN"},
                { "日本語", "JP"}
            };
        readonly Dictionary<string, string> GameDict = new Dictionary<string, string>
        {
            ["原神"] = "Genshin",
            ["星穹铁道"] = "StarRail",
            ["绝区零"] = "Zenless",
            ["鸣潮"] = "Wuthering",
        };
        readonly Stopwatch sw = new Stopwatch();
        readonly bool mtuliline = Config.Get<bool>("Multiline", false);
        readonly static string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
        readonly string outpath = Path.Combine(dataDir, "out");
        public PaddleOCREngine engine;
        private Bitmap bitmap;
        double Scale = 1;
        INotifyIcon notifyIcon;
        // Windows API 函数用于注册和注销热键
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // 热键常量
        private const int HOTKEY_ID_1 = 9000;
        private const int HOTKEY_ID_2 = 9001;
        private const int HOTKEY_ID_3 = 9002;
        private const int HOTKEY_ID_4 = 9003;

        private const uint MOD_NONE = 0x0000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CTRL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        private IntPtr _windowHandle;
        private ObservableCollection<HotkeyViewModel> _hotkeys;
        public SettingsWindow(string version, INotifyIcon notify, double scale = 1)
        {
            InitializeComponent();
            Scale = scale;
            ApplyLanguage("zh-CN");
            this.Title += $"({version})";
            GameSelector.SelectionChanged += OnGameSelectorChanged;
            InputSelector.SelectionChanged += OnInputSelectorChanged;
            OutputSelector.SelectionChanged += OnOutputSelectorChanged;
            Dictionary<string, string> GameNames = GameDict.ToDictionary(x => x.Value, x => x.Key);
            Dictionary<string, string> InputNames = InputLanguages.ToDictionary(x => x.Value, x => x.Key);
            Dictionary<string, string> OutputNames = OutputLanguages.ToDictionary(x => x.Value, x => x.Key);
            var item = GameSelector.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Content.ToString() == GameNames[Game]);
            if (item != null)
            {
                GameSelector.SelectedItem = item;
            }
            item = InputSelector.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Content.ToString() == InputNames[InputLanguage]);
            if (item != null)
            {
                InputSelector.SelectedItem = item;
            }
            item = OutputSelector.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Content.ToString() == OutputNames[OutputLanguage]);
            if (item != null)
            {
                OutputSelector.SelectedItem = item;
            }
            DisplayLocalFileDates();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
            if (contentDict.Count > 100)
            {
                Status.Content = $"Loaded {contentDict.Count} key-values";
            }
            if (!Directory.Exists(Path.Combine(dataDir, Game)))
            {
                Directory.CreateDirectory(Path.Combine(dataDir, Game));
            }
            notifyIcon = notify;
            DataContext = this;

            // 初始化热键列表
            InitializeHotkeys();
            Console.WriteLine("InitializeHotkeys");
            // 绑定按钮事件
            saveButton.Click += SaveButton_Click;
            resetButton.Click += ResetButton_Click;
            // Pad
            PadTextBox.Text = Config.Get("Pad", 86).ToString();

            // Region: 解析字符串 "x,y,w,h"
            var regionStr = Config.Get("Region", "763,1797,2226,110");
            var parts = regionStr.Split(',');
            if (parts.Length == 4)
            {
                RegionX.Text = parts[0];
                RegionY.Text = parts[1];
                RegionWidth.Text = parts[2];
                RegionHeight.Text = parts[3];
            }

            // Boolean flags
            MultilineCheckBox.IsChecked = Config.Get("Multiline", false);
            AutoStartCheckBox.IsChecked = Config.Get("AutoStart", false);
        }

        private void ResetLocation_Click(object sender, RoutedEventArgs e)
        {
            Config.Set("Pad", 86);
        }

        private void SecondRegion_Click(object sender, RoutedEventArgs e)
        {
            notifyIcon.ChooseRegion2();
        }

        private void UILangSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UILangSelector.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ApplyLanguage(tag);
            }
        }

        private void ApplyLanguage(string cultureTag)
        {
            // 可选：设置线程文化（如果你在其他地方需要）
            try
            {
                var culture = new CultureInfo(cultureTag);
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
            catch { /* 忽略非法 culture */ }

            // 先移除旧的语言资源
            var oldLangs = System.Windows.Application.Current.Resources.MergedDictionaries
                .Where(d => d.Source != null && d.Source.OriginalString.Contains("Resources/Strings"))
                .ToList();
            foreach (var d in oldLangs)
                System.Windows.Application.Current.Resources.MergedDictionaries.Remove(d);

            // 合并新的语言资源
            var rd = new ResourceDictionary();
            switch (cultureTag)
            {
                case "en-US":
                    rd.Source = new Uri("Resources/Strings.en-US.xaml", UriKind.Relative);
                    break;
                case "ja-JP":
                    rd.Source = new Uri("Resources/Strings.ja-JP.xaml", UriKind.Relative);
                    break;
                default:
                    rd.Source = new Uri("Resources/Strings.zh-CN.xaml", UriKind.Relative);
                    break;
            }
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(rd);

            // 强制刷新窗口上的绑定
            this.InvalidateVisual();
        }

        public async Task Load()
        {
            await CheckDataAsync();
        }

        public void RefreshUrl()
        {
            InputLangDownloadUrl.Text = $"https://gitlab.com/Dimbreath/AnimeGameData/-/raw/master/TextMap/TextMap{InputLanguage}.json?inline=false";
            OutputLangDownloadUrl.Text = $"https://gitlab.com/Dimbreath/AnimeGameData/-/raw/master/TextMap/TextMap{OutputLanguage}.json?inline=false";
            if (Game == "StarRail")
            {
                repoUrl = "https://gitlab.com/Dimbreath/turnbasedgamedata/-/refs/main/logs_tree/?format=json&offset=0&ref_type=HEADS";
                InputLangDownloadUrl.Text = $"https://gitlab.com/Dimbreath/turnbasedgamedata/-/raw/main/TextMap/TextMap{InputLanguage}.json?inline=false";
                OutputLangDownloadUrl.Text = $"https://gitlab.com/Dimbreath/turnbasedgamedata/-/raw/main/TextMap/TextMap{OutputLanguage}.json?inline=false";
            }
            else if (Game == "Zenless")
            {
                repoUrl = "https://git.mero.moe/dimbreath/ZenlessData";
                InputLangDownloadUrl.Text = ZenlessUrl(InputLanguage);
                OutputLangDownloadUrl.Text = ZenlessUrl(OutputLanguage);
            }
            else if (Game == "Wuthering")
            {
                repoUrl = "https://github.com/Dimbreath/WutheringData/commits/master.atom";
                InputLangDownloadUrl.Text = WutheringUrl(InputLanguage);
                OutputLangDownloadUrl.Text = WutheringUrl(OutputLanguage);
            }
        }

        private string ZenlessUrl(string language)
        {
            string url = "https://git.mero.moe/dimbreath/ZenlessData/raw/branch/master/TextMap/TextMapTemplateTb.json";
            if (language != "CHS")
            {
                if (language == "JP")
                {
                    language = "JA";
                }
                url = $"https://git.mero.moe/dimbreath/ZenlessData/raw/branch/master/TextMap/TextMap_{language}TemplateTb.json";
            }
            return url;
        }

        private string WutheringUrl(string language)
        {
            string url = "https://raw.githubusercontent.com/Dimbreath/WutheringData/refs/heads/master/TextMap/zh-Hans/MultiText.json";
            if (language != "CHS")
            {
                if (language == "JP")
                {
                    language = "JA";
                }
                url = $"https://raw.githubusercontent.com/Dimbreath/WutheringData/refs/heads/master/TextMap/{language.ToLower()}/MultiText.json";
            }
            return url;
        }

        private async void OnGameSelectorChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is System.Windows.Controls.ComboBox comboBox))
            {
                return;
            }

            if (comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string newValue = GameDict[selectedItem.Content.ToString()];
                if (Game != newValue)
                {
                    Game = newValue;
                    if (!Directory.Exists(Path.Combine(dataDir, Game)))
                    {
                        Directory.CreateDirectory(Path.Combine(dataDir, Game));
                    }
                    DisplayLocalFileDates();
                    Config.Set("Game", newValue);
                    await CheckDataAsync(true);
                }
            }
        }

        private async void OnInputSelectorChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is System.Windows.Controls.ComboBox comboBox))
            {
                return;
            }

            if (comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string newValue = InputLanguages[selectedItem.Content.ToString()];
                if (InputLanguage != newValue)
                {
                    InputLanguage = newValue;
                    DisplayLocalFileDates();
                    Config.Set("Input", InputLanguage);
                    await CheckDataAsync(true);
                }
            }
        }

        private async void OnOutputSelectorChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is System.Windows.Controls.ComboBox comboBox))
            {
                return;
            }

            if (comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string newValue = OutputLanguages[selectedItem.Content.ToString()];
                if (OutputLanguage != newValue)
                {
                    OutputLanguage = newValue;
                    DisplayLocalFileDates();
                    Config.Set("Output", OutputLanguage);
                    await CheckDataAsync(true);
                }
            }
        }
        public bool FileExists()
        {
            return File.Exists($"{Path.Combine(dataDir, Game)}\\TextMap{InputLanguage}_TextMap{OutputLanguage}.json") || (File.Exists($"{Path.Combine(dataDir, Game)}\\TextMap{InputLanguage}.json") &&
                              File.Exists($"{Path.Combine(dataDir, Game)}\\TextMap{OutputLanguage}.json"));
        }

        public async Task CheckDataAsync(bool renew = false)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Status.Content = "Data loading......";
                Logger.Log.Debug(Status.Content);
                contentDict.Clear();
            });
            string userName = (OutputLanguage == "CHS") ? "旅行者" : "Traveler";

            if (FileExists())
            {
                string inputFilePath = $"{Path.Combine(dataDir, Game)}\\TextMap{InputLanguage}.json";
                string outputFilePath = $"{Path.Combine(dataDir, Game)}\\TextMap{OutputLanguage}.json";
                var jsonFilePath = Path.Combine(Path.GetDirectoryName(inputFilePath),
                    $"{Path.GetFileNameWithoutExtension(inputFilePath)}_{Path.GetFileNameWithoutExtension(outputFilePath)}.json");
                if (renew && File.Exists(jsonFilePath))
                {
                    File.Delete(jsonFilePath);
                }
                contentDict = await Task.Run(() =>
                    VoiceContentHelper.CreateVoiceContentDictionary(inputFilePath, outputFilePath, userName));
            }
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Status.Content = $"Loaded {contentDict.Count} key-values，{InputLanguage} -> {OutputLanguage}";
                Logger.Log.Debug(Status.Content);
                Logger.Log.Debug(12333);
            });
            DisplayLocalFileDates();
        }

        private void DisplayLocalFileDates()
        {
            RefreshUrl();
            string inputFilePath = $"{Path.Combine(dataDir, Game)}\\TextMap{InputLanguage}.json";
            string outputFilePath = $"{Path.Combine(dataDir, Game)}\\TextMap{OutputLanguage}.json";
            if (File.Exists(inputFilePath))
            {
                DateTime modDate1 = File.GetLastWriteTime(inputFilePath);
                inputFilePathDate.Text = $"{inputFilePath} file date {modDate1}";
            }
            else
            {
                inputFilePathDate.Text = $"{inputFilePath} not found";
            }

            if (File.Exists(outputFilePath))
            {
                DateTime modDate2 = File.GetLastWriteTime(outputFilePath);
                outputFilePathDate.Text = $"{outputFilePath} file date {modDate2}";
            }
            else
            {
                outputFilePathDate.Text = $"{outputFilePath} not found";
            }
        }

        public DateTime GetLocalFileDates(string input, string output, string game)
        {
            string inputFilePath = $"{Path.Combine(dataDir, Game)}\\TextMap{input}.json";
            string outputFilePath = $"{Path.Combine(dataDir, Game)}\\TextMap{output}.json";
            if (File.Exists(inputFilePath))
            {
                return File.GetLastWriteTime(inputFilePath);
            }
            else if (File.Exists(outputFilePath))
            {
                return File.GetLastWriteTime(outputFilePath);
            }
            else
            {
                return DateTime.Now.AddYears(-1);
            }
        }


        public async Task GetRepositoryModificationDateAsync()
        {
            try
            {
                Logger.Log.Info($"Load start.");
                HttpResponseMessage response = await client.GetAsync(repoUrl);
                response.EnsureSuccessStatusCode();
                string responseText = await response.Content.ReadAsStringAsync();
                if (Game == "Zenless")
                {
                    string pattern = @"datetime=""([^""]*)""";
                    Match match = Regex.Match(responseText, pattern);

                    if (match.Success)
                    {
                        string dateTimeString = match.Groups[1].Value;
                        try
                        {
                            DateTimeOffset dateTimeOffset = DateTimeOffset.Parse(dateTimeString);
                            DateTime localTime = dateTimeOffset.LocalDateTime; // 自动转换为本地时区
                            RepoModifiedDate.Text = localTime.ToString();
                        }
                        catch (Exception ex)
                        {
                            Logger.Log.Error("Error parsing datetime: " + ex.Message);
                        }
                    }
                    else
                    {
                        Logger.Log.Error("No datetime attribute found in the input string.");
                    }

                }
                else if (Game == "Wuthering")
                {
                    var reader = XmlReader.Create(new System.IO.StringReader(responseText));
                    var feed = SyndicationFeed.Load(reader);
                    var item = feed?.Items?.FirstOrDefault();
                    var dateTime = item?.LastUpdatedTime ?? item?.PublishDate;
                    RepoModifiedDate.Text = dateTime.ToString();
                }
                else
                {
                    JArray jsonArray = JArray.Parse(responseText);
                    if (jsonArray.Count > 0)
                    {
                        List<string> dateList = new List<string>();
                        foreach (var date in jsonArray)
                        {
                            dateList.Add(date["commit"]?["committed_date"]?.ToString());
                        }
                        dateList.Sort();
                        dateList.Reverse();
                        RepoModifiedDate.Text = dateList.Count > 0 ? dateList[0] : "Unable to get committed_date";
                    }
                    else
                    {
                        RepoModifiedDate.Text = "No response";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Error(ex);
                RepoModifiedDate.Text = "Error: " + ex.Message;
            }
        }

        public async Task<string> GetRepositoryModificationDate(string url, string game)
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string responseText = await response.Content.ReadAsStringAsync();
                if (Game == "Zenless")
                {
                    string pattern = @"datetime=""([^""]*)""";
                    Match match = Regex.Match(responseText, pattern);

                    if (match.Success)
                    {
                        string dateTimeString = match.Groups[1].Value;
                        try
                        {
                            DateTimeOffset dateTimeOffset = DateTimeOffset.Parse(dateTimeString);
                            DateTime localTime = dateTimeOffset.LocalDateTime; // 自动转换为本地时区
                            return localTime.ToString();
                        }
                        catch (Exception ex)
                        {
                            Logger.Log.Error("Error parsing datetime: " + ex.Message);
                        }
                    }
                    else
                    {
                        Logger.Log.Error("No datetime attribute found in the input string.");
                    }

                }
                else
                {
                    JArray jsonArray = JArray.Parse(responseText);
                    if (jsonArray.Count > 0)
                    {
                        List<string> dateList = new List<string>();
                        foreach (var date in jsonArray)
                        {
                            dateList.Add(date["commit"]?["committed_date"]?.ToString());
                        }
                        dateList.Sort();
                        dateList.Reverse();
                        return dateList[0];
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Error(ex);

            }
            return "";
        }

        private async void SyncButton_Click(object sender, RoutedEventArgs e)
        {
            await GetRepositoryModificationDateAsync();
        }

        private async void DownloadButton1_Click(object sender, RoutedEventArgs e)
        {
            string inputFilePath = $"{Path.Combine(dataDir, Game)}\\TextMap{InputLanguage}.json";
            await DownloadFileAsync(InputLangDownloadUrl.Text, inputFilePath);
        }

        private async void DownloadButton2_Click(object sender, RoutedEventArgs e)
        {
            string outputFilePath = $"{Path.Combine(dataDir, Game)}\\TextMap{OutputLanguage}.json";
            await DownloadFileAsync(OutputLangDownloadUrl.Text, outputFilePath);
            await CheckDataAsync(true);
        }

        private async Task DownloadFileAsync(string url, string fileName)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                System.Windows.MessageBox.Show($"Invalid URL: {url}");
                return;
            }
            fileName = Path.Combine(dataDir, fileName);
            int attempt = 0;
            bool success = false;
            long existingLength = 0;
            string tmpFileName = fileName.Replace("json", "jsontmp");


            while (attempt < MaxRetries && !success)
            {
                try
                {
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri))
                    {

                        sw.Start();
                        using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                        {
                            response.EnsureSuccessStatusCode();

                            // 获取总大小
                            long totalBytes = response.Content.Headers.ContentLength.Value;

                            using (Stream contentStream = await response.Content.ReadAsStreamAsync(),
                                          fileStream = new FileStream(tmpFileName, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                            {
                                byte[] buffer = new byte[8192];
                                int bytesRead;
                                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                                    existingLength += bytesRead;

                                    // 更新进度
                                    double progressPercentage = (double)existingLength / totalBytes * 100;
                                    DownloadProgressBar.Value = progressPercentage;

                                    // 计算下载速度
                                    double speed = existingLength / 1024d / sw.Elapsed.TotalSeconds;
                                    DownloadSpeedText.Text = $"{speed:0.00} KB/s";
                                }
                            }
                        }
                    }

                    sw.Reset();
                    if (File.Exists(tmpFileName))
                    {
                        if (File.Exists(fileName))
                        {
                            File.Delete(fileName);
                        }
                        File.Move(tmpFileName, fileName);
                        string directoryPath = Path.GetDirectoryName(fileName);

                        string baseFileName = Path.GetFileNameWithoutExtension(fileName);
                        string[] matchingFiles = Directory.GetFiles(directoryPath);
                        foreach (string file in matchingFiles)
                        {
                            try
                            {
                                string baseName = Path.GetFileNameWithoutExtension(file);
                                if (baseName.Contains(baseFileName) && baseName.Contains("_"))
                                {
                                    File.Delete(file);
                                    Logger.Log.Info($"Deleted: {file}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Log.Error($"Failed to delete {file}: {ex.Message}");
                            }
                        }
                    }

                    DisplayLocalFileDates(); // 更新本地文件日期
                    success = true;
                }
                catch (Exception ex)
                {
                    sw.Reset();
                    attempt++;
                    if (attempt >= MaxRetries)
                    {
                        System.Windows.MessageBox.Show($"Error: {ex.Message}");
                    }
                    else
                    {
                        await Task.Delay(2000);
                    }
                }
                finally
                {
                    DownloadProgressBar.Value = 0;
                    DownloadSpeedText.Text = "";
                }
            }
        }

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            string executablePath = Assembly.GetEntryAssembly().Location;
            Process.Start(executablePath, "Restart");
            Environment.Exit(0);
        }

        public void LoadEngine(bool enableMultiLine = false)
        {
            if (engine != null)
            {
                engine.Dispose();
            }

            if (!Directory.Exists(outpath))
            { Directory.CreateDirectory(outpath); }

            OCRModelConfig config = null;
            OCRParameter oCRParameter = new OCRParameter
            {
                cpu_math_library_num_threads = 5,//预测并发线程数
                enable_mkldnn = true,//web部署该值建议设置为0,否则出错，内存如果使用很大，建议该值也设置为0.
                cls = false, //是否执行文字方向分类；默认false
                det = false,//是否开启方向检测，用于检测识别180旋转
                use_angle_cls = false,//是否开启方向检测，用于检测识别180旋转
                det_db_score_mode = false,//是否使用多段线，即文字区域是用多段线还是用矩形，
                max_side_len = 1560
            };
            oCRParameter.cls = mtuliline | enableMultiLine;
            oCRParameter.det = mtuliline | enableMultiLine;

            if (InputLanguage == "JP")
            {
                config = new OCRModelConfig();
                string root = System.IO.Path.GetDirectoryName(typeof(OCRModelConfig).Assembly.Location);
                string modelPathroot = root + @"\inference";
                config.det_infer = modelPathroot + @"\ch_PP-OCRv3_det_infer";
                config.cls_infer = modelPathroot + @"\ch_ppocr_mobile_v2.0_cls_infer";
                config.rec_infer = modelPathroot + @"\japan_PP-OCRv3_rec_infer";
                config.keys = modelPathroot + @"\japan_dict.txt";
                oCRParameter.max_side_len = 1560;
            }


            //初始化OCR引擎
            engine = new PaddleOCREngine(config, oCRParameter);
        }
        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            LoadEngine();
            string testFile = InputLanguage + ".jpg";
            if (Game == "Wuthering")
            {
                testFile = "Wuthering.png";
            }
            string report = "";
            try
            {
                while (contentDict.Count < 10)
                {
                    Thread.Sleep(1000);
                    Console.WriteLine("Sleeping ...");
                }
                DateTime dateTime = DateTime.Now;
                Bitmap target;
                if (bitmap == null)
                {
                    target = (Bitmap)Bitmap.FromFile(testFile);
                }
                else
                {
                    target = bitmap;
                }
                var enhanced = ImageProcessor.EnhanceTextInImage(target);
                OCRResult ocrResult = engine.DetectText(target);
                string ocrText = ocrResult.Text;
                dateTime = DateTime.Now;
                string res = VoiceContentHelper.FindClosestMatch(ocrText, contentDict, out string key);
                report = $"OCR: {ocrText}\nMatch: {key}\nTranslate: {res}";
            }
            catch (Exception ex)
            {
                report = ex.Message;
            }
            System.Windows.MessageBox.Show(report);
        }

        public void SetImage(Bitmap bitmap)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                this.bitmap = bitmap;
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;

                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.UriSource = null;
                bitmapImage.StreamSource = ms;
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // 冻结，使其可以在多个线程中使用

                // 设置 Image 控件的 Source 属性
                Capture.Source = bitmapImage;
            }
        }

        private void RegionButton_Click(object sender, RoutedEventArgs e)
        {
            LoadEngine(true);

            int idx = 0;
            string configRegion = Config.Get<string>("Region");
            Logger.Log.Debug($"Config Region: {configRegion}");
            foreach (var screen in Screen.AllScreens)
            {
                Logger.Log.Debug($"Capturing screen {idx}: {screen.DeviceName}");
                Logger.Log.Debug($"Bounds: {screen.Bounds.Width}x{screen.Bounds.Height} at {screen.Bounds.Location}");

                // Use 'using' to ensure resources are properly disposed
                using (Bitmap bitmap = new Bitmap(screen.Bounds.Width, screen.Bounds.Height))
                {
                    // Create a Graphics object from the bitmap
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        // Copy the screen contents to the bitmap
                        g.CopyFromScreen(screen.Bounds.Location, System.Drawing.Point.Empty, screen.Bounds.Size);
                    }

                    // Now save the bitmap, which contains the screenshot
                    bitmap.Save($"{idx}.png", System.Drawing.Imaging.ImageFormat.Png);
                    if (bitmap == null)
                    {
                        continue;
                    }
                    var res = engine.DetectStructure(bitmap);
                    foreach (var i in res.Cells)
                    {
                        if (i.TextBlocks.Count > 0)
                        {
                            Logger.Log.Debug(i.TextBlocks);
                            List<OCRPoint> point = i.TextBlocks[0].BoxPoints;
                            Logger.Log.Debug($"Region:\"{point[0].X - 400},{point[0].Y - 20},{point[1].X - point[0].X + 800},{point[2].Y - point[0].Y + 40}\"");
                        }
                    }
                }
                idx++;
            }
            System.Windows.MessageBox.Show("Finished");
        }

        private void OpenAppDataFolder_Click(object sender, RoutedEventArgs e)
        {
            string dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GI-Subtitles");

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // 直接打开资源管理器并定位到该目录
            Process.Start("explorer.exe", dir);
        }

        private void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            string srtFolder = Path.Combine(dataDir, "srt");
            if (Directory.Exists(srtFolder))
            {
                var processor = new SrtProcessor(this.contentDict);
                foreach (var file in Directory.GetFiles(srtFolder))
                {
                    if (file.EndsWith(".convert.srt"))
                    {
                        continue;
                    }
                    var subtitles = processor.ReadSrtFile(file);
                    var processedSubtitles = processor.ProcessSubtitles(subtitles);
                    string outputPath = file.Replace(".srt", ".convert.srt");
                    processor.WriteSrtFile(outputPath, processedSubtitles);
                }
                System.Windows.MessageBox.Show($"Convert finished.");
            }
            else
            {
                System.Windows.MessageBox.Show($"{srtFolder} folder is empty.");
            }
        }

        // 修改InitializeHotkeys方法
        public void InitializeHotkeys()
        {
            // 从设置加载热键
            var settings = HotkeySettingsManager.LoadSettings();

            // 创建可用按键列表 (A-Z)
            var availableKeys = Enumerable.Range(65, 26).Select(c => (char)c).ToList();

            // 初始化热键集合
            _hotkeys = new ObservableCollection<HotkeyViewModel>(
                settings.Hotkeys.Select(h => new HotkeyViewModel
                {
                    Id = h.Id,
                    Description = h.Description,
                    IsCtrl = h.IsCtrl,
                    IsShift = h.IsShift,
                    SelectedKey = h.SelectedKey,
                    AvailableKeys = availableKeys
                })
            );

            hotkeyListView.ItemsSource = _hotkeys;
        }

        // 修改SaveButton_Click方法，添加保存到文件的功能
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // 验证每个热键：必须包含 Ctrl 或 Shift
            foreach (var hotkey in _hotkeys)
            {
                if (!hotkey.IsCtrl && !hotkey.IsShift)
                {
                    System.Windows.MessageBox.Show($"快捷键 \"{hotkey.Description}\" 必须包含 Ctrl 或 Shift。",
                                    "无效快捷键", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 确保选中的是 A-Z（防御性检查）
                if (!char.IsLetter(hotkey.SelectedKey) || hotkey.SelectedKey < 'A' || hotkey.SelectedKey > 'Z')
                {
                    System.Windows.MessageBox.Show($"快捷键 \"{hotkey.Description}\" 必须选择 A-Z 之间的字母。",
                                    "无效按键", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // 检查重复
            var hotkeyTexts = _hotkeys.Select(h => h.GetHotkeyText()).ToList();
            if (hotkeyTexts.GroupBy(t => t).Any(g => g.Count() > 1))
            {
                System.Windows.MessageBox.Show("发现重复的快捷键组合，请修改后再保存。",
                                "重复的快捷键", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 保存设置
            var settings = new HotkeySettings
            {
                Hotkeys = _hotkeys.Select(h => new HotkeyData
                {
                    Id = h.Id,
                    Description = h.Description,
                    IsCtrl = h.IsCtrl,
                    IsShift = h.IsShift,
                    SelectedKey = h.SelectedKey
                }).ToList()
            };

            HotkeySettingsManager.SaveSettings(settings);
            RegisterAllHotkeys();

            System.Windows.MessageBox.Show("快捷键设置已保存。", "保存成功",
                            MessageBoxButton.OK, MessageBoxImage.Information);

            foreach (var hotkey in _hotkeys)
            {
                hotkey.IsEditing = false;
            }
        }
        public void InitializeKey(IntPtr handle)
        {
            Console.WriteLine("OnSourceInitialized");
            _windowHandle = handle;
            RegisterAllHotkeys();
        }


        private void HandleHotkeyPress(int hotkeyId)
        {
            var hotkey = _hotkeys.FirstOrDefault(h => h.Id == hotkeyId);
            if (hotkey != null)
            {
                System.Windows.MessageBox.Show($"触发快捷键: {hotkey.Description}\n组合键: {hotkey.GetHotkeyText()}",
                                "快捷键触发", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RegisterAllHotkeys()
        {
            // 先注销所有热键
            UnregisterAllHotkeys();

            // 注册所有热键
            foreach (var hotkey in _hotkeys)
            {
                RegisterHotkey(hotkey);
            }
        }

        // 添加这个辅助方法到你的类中
        private uint GetVirtualKeyFromChar(char c)
        {
            // 对于字母字符，直接转换为对应的虚拟键码
            if (char.IsLetter(c))
            {
                // 字母的虚拟键码是ASCII码值 (A=65, B=66, ..., Z=90)
                return (uint)char.ToUpper(c);
            }

            return 0;
        }

        private void RegisterHotkey(HotkeyViewModel hotkey)
        {
            uint modifiers = 0;
            if (hotkey.IsCtrl) modifiers |= MOD_CTRL;
            if (hotkey.IsShift) modifiers |= MOD_SHIFT;

            // 使用这个自定义转换方法
            uint virtualKey = GetVirtualKeyFromChar(hotkey.SelectedKey);



            if (!RegisterHotKey(_windowHandle, hotkey.Id, modifiers, virtualKey))
            {
                // 注册失败，可能是因为热键冲突
                System.Windows.MessageBox.Show($"无法注册快捷键 {hotkey.GetHotkeyText()}\n可能与其他应用程序冲突。",
                                "注册失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void UnregisterAllHotkeys()
        {
            foreach (var hotkey in _hotkeys)
            {
                UnregisterHotKey(_windowHandle, hotkey.Id);
            }
        }


        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show("确定要恢复默认快捷键设置吗？", "确认恢复默认",
                               MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                InitializeHotkeys();
                RegisterAllHotkeys();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            UnregisterAllHotkeys();
        }

        private void PreviewRegion_Click(object sender, RoutedEventArgs e)
        {
            notifyIcon.ShowRegionOverlay();
        }

        private void PadTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PadTextBox.Text, out int pad))
                Config.Set("Pad", pad);
        }

        private void RegionField_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(RegionX.Text, out _) &&
                int.TryParse(RegionY.Text, out _) &&
                int.TryParse(RegionWidth.Text, out _) &&
                int.TryParse(RegionHeight.Text, out _))
            {
                string region = $"{RegionX.Text},{RegionY.Text},{RegionWidth.Text},{RegionHeight.Text}";
                Config.Set("Region", region);
            }
        }

        private void MultilineCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Config.Set("Multiline", MultilineCheckBox.IsChecked == true);
        }

        private void AutoStartCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Config.Set("AutoStart", AutoStartCheckBox.IsChecked == true);
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true // 必须为 true 才能用默认浏览器打开
                });
            }
            catch (Exception ex)
            {
                Logger.Log.Error(ex);
            }
            e.Handled = true;
        }
    }
}
