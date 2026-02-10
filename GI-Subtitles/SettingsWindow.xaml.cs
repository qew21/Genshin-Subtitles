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
using Microsoft.Win32;

namespace GI_Subtitles
{
    /// <summary>
    /// SettingsWindow.xaml interaction logic
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public string repoUrl = "https://gitlab.com/Dimbreath/AnimeGameData/-/refs/master/logs_tree/TextMap?format=json&offset=0&ref_type=heads";
        string Game = Config.Get<string>("Game");
        string InputLanguage = Config.Get<string>("Input");
        string OutputLanguage = Config.Get<string>("Output");
        private const int MaxRetries = 1; // Maximum number of retries
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
            ["终末地"] = "Endfield",
        };
        readonly Stopwatch sw = new Stopwatch();
        readonly bool mtuliline = Config.Get<bool>("Multiline", false);
        readonly static string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
        readonly string outpath = Path.Combine(dataDir, "out");
        public PaddleOCREngine engine;
        private Bitmap bitmap;
        double Scale = 1;
        INotifyIcon notifyIcon;
        // Windows API functions for registering and unregistering hotkeys
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Hotkey constants
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
        private bool REAL_CLOSE = false;
        public OptimizedMatcher Matcher;
        // Used to suppress initial UILangSelector SelectionChanged events triggered by XAML default selection
        private bool _uiLangInitialized = false;
        public SettingsWindow(string version, INotifyIcon notify, double scale = 1)
        {
            InitializeComponent();
            Scale = scale;
            // Load UI language from config, default to zh-CN
            string uiLang = Config.Get("UILang", "zh-CN");
            ApplyLanguage(uiLang);

            // Sync UI language selector without triggering extra logic
            try
            {
                UILangSelector.SelectionChanged -= UILangSelector_SelectionChanged;
                var uiItem = UILangSelector.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(i => i.Tag is string tag && tag == uiLang);
                if (uiItem != null)
                {
                    // This may raise SelectionChanged again, but we will suppress it via _uiLangInitialized flag
                    UILangSelector.SelectedItem = uiItem;
                }
                UILangSelector.SelectionChanged += UILangSelector_SelectionChanged;
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Failed to sync UI language selector: {ex.Message}");
            }
            // From this point on, UILangSelector_SelectionChanged should start updating config
            _uiLangInitialized = true;
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

            // Initialize hotkey list
            InitializeHotkeys();
            Console.WriteLine("InitializeHotkeys");
            // Bind button events
            saveButton.Click += SaveButton_Click;
            resetButton.Click += ResetButton_Click;
            // Pad
            int pad = Config.GetPad(86);
            int padHorizontal = Config.GetPadHorizontal(0);
            PadTextBox.Text = pad.ToString();
            PadHorizontalTextBox.Text = padHorizontal.ToString();

            // Region: parse the string "x,y,w,h"
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
            PlayVoiceCheckBox.IsChecked = Config.Get("PlayVoice", true);
        }

        private void ResetLocation_Click(object sender, RoutedEventArgs e)
        {
            Config.Set("Pad", new int[] { 86, 0 });
            PadTextBox.Text = "86";
            PadHorizontalTextBox.Text = "0";
            UpdateMainWindowPosition();
        }

        private void SecondRegion_Click(object sender, RoutedEventArgs e)
        {
            notifyIcon.ChooseRegion2();
        }

        private void UILangSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ignore initial SelectionChanged events fired during window construction
            if (!_uiLangInitialized)
            {
                return;
            }

            if (UILangSelector.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ApplyLanguage(tag);
                Config.Set("UILang", tag);

                // Refresh tray menu texts
                if (notifyIcon != null)
                {
                    notifyIcon.RefreshMenuTexts();
                }

                // Re-initialize hotkeys to update descriptions with new language
                InitializeHotkeys();
            }
        }

        private void ApplyLanguage(string cultureTag)
        {
            // Optional: set the thread culture (if you need it elsewhere)
            try
            {
                var culture = new CultureInfo(cultureTag);
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
            catch { /* Ignore invalid culture */ }

            // First remove the old language resources
            var oldLangs = System.Windows.Application.Current.Resources.MergedDictionaries
                .Where(d => d.Source != null && d.Source.OriginalString.Contains("Resources/Strings"))
                .ToList();
            foreach (var d in oldLangs)
                System.Windows.Application.Current.Resources.MergedDictionaries.Remove(d);

            // Merge the new language resources
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

            // Force refresh the bindings on the window
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
            else if (Game == "Endfield")
            {
                repoUrl = "https://github.com/XiaBei-cy/EndfieldData/commits/master.atom";
                InputLangDownloadUrl.Text = EndfieldUrl(InputLanguage);
                OutputLangDownloadUrl.Text = EndfieldUrl(OutputLanguage);
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

        private string EndfieldUrl(string language)
        {
            string url = "https://raw.githubusercontent.com/XiaBei-cy/EndfieldData/refs/heads/master/i18n/I18nTextTable_CN.json";
            if (language != "CHS")
            {
                url = $"https://raw.githubusercontent.com/XiaBei-cy/EndfieldData/refs/heads/master/i18n/I18nTextTable_{language.ToUpper()}.json";
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
                if (Matcher == null)
                {
                    Logger.Log.Debug("Loading OptimizedMatcher...");
                    Matcher = new OptimizedMatcher(contentDict);
                }
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

                            // Get the total size
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

                                    // Update the progress
                                    double progressPercentage = (double)existingLength / totalBytes * 100;
                                    DownloadProgressBar.Value = progressPercentage;

                                    // Calculate the download speed
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

                    DisplayLocalFileDates(); // Update the local file date
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

        public void LoadEngine(bool enableMultiLine = true)
        {
            if (engine != null)
            {
                engine.Dispose();
            }
            engine = LoadEngine(InputLanguage, enableMultiLine);
        }

        public static PaddleOCREngine LoadEngine(string input, bool enableMultiLine = true)
        {
            OCRModelConfig config = null;
            OCRParameter oCRParameter = new OCRParameter
            {
                cpu_math_library_num_threads = 3,//Prediction concurrent thread count
                enable_mkldnn = true,//If you deploy on the web, it is recommended to set this value to 0, otherwise it will error. If the memory is used very large, it is recommended to set this value to 0.
                use_angle_cls = false,//Whether to enable direction detection, used to detect 180 degree rotation
                det_db_score_mode = false,//Whether to use multiple segments, that is, whether the text area is used with multiple segments or with rectangles,
                max_side_len = 960
            };

            if (input == "JP")
            {
                config = new OCRModelConfig();
                string root = System.IO.Path.GetDirectoryName(typeof(OCRModelConfig).Assembly.Location);
                string modelPathroot = root + @"\inference";
                config.det_infer = modelPathroot + @"\Det\V4\PP-OCRv4_mobile_det_infer\slim.onnx";
                config.rec_infer = modelPathroot + @"\Rec\V4\jp_PP-OCRv4_mobile_rec_infer\slim.onnx";
                config.keys = modelPathroot + @"\Rec\V4\jp_PP-OCRv4_mobile_rec_infer\dict.txt";
            }
            else
            {
                config = new OCRModelConfig();
                string root = System.IO.Path.GetDirectoryName(typeof(OCRModelConfig).Assembly.Location);
                string modelPathroot = root + @"\inference";
                config.det_infer = modelPathroot + @"\Det\V4\PP-OCRv4_mobile_det_infer\slim.onnx";
                config.rec_infer = modelPathroot + @"\Rec\V4\PP-OCRv4_mobile_rec_infer\slim.onnx";
                config.keys = modelPathroot + @"\Rec\V4\PP-OCRv4_mobile_rec_infer\dict.txt";
            }
            try
            {
                return new PaddleOCREngine(config, oCRParameter);
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Error loading engine: {ex.Message}");
                throw new Exception("Failed to load engine.");
            }
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
                OCRResult ocrResult = engine.DetectText(target);
                string ocrText = ocrResult.Text;
                dateTime = DateTime.Now;
                string res = Matcher.FindClosestMatch(ocrText, out string key);
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
                bitmapImage.Freeze(); // Freeze, so it can be used in multiple threads

                // Set the Source property of the Image control
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
                    var res = engine.DetectText(bitmap);
                    foreach (var i in res.TextBlocks)
                    {
                        Logger.Log.Debug(i);
                        Logger.Log.Debug($"Region:\"{i.BoxPoints[0].X - 400},{i.BoxPoints[0].Y - 20},{i.BoxPoints[1].X - i.BoxPoints[0].X + 800},{i.BoxPoints[2].Y - i.BoxPoints[0].Y + 40}\"");
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

            // Directly open the explorer and locate to the directory
            Process.Start("explorer.exe", dir);
        }

        private void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Subtitle file|*.srt|All files|*.*",
                Multiselect = true,
                Title = "Select the SRT file to convert"
            };

            if (dialog.ShowDialog() == true)
            {
                var processor = new SrtProcessor(this.contentDict);
                int successCount = 0;
                int failCount = 0;
                var errors = new List<string>();

                foreach (var file in dialog.FileNames)
                {
                    try
                    {
                        // Skip the file that has already been converted
                        if (file.EndsWith(".convert.srt", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var subtitles = processor.ReadSrtFile(file);
                        var processedSubtitles = processor.ProcessSubtitles(Matcher, subtitles);

                        // Output the file to the same directory, add the .convert suffix to the file name
                        string outputPath = Path.Combine(
                            Path.GetDirectoryName(file),
                            Path.GetFileNameWithoutExtension(file) + ".convert.srt"
                        );

                        processor.WriteSrtFile(outputPath, processedSubtitles);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                // Display the conversion result
                string message = $"Conversion completed!\nSuccess: {successCount} files";
                if (failCount > 0)
                {
                    message += $"\nFailed: {failCount} files";
                    if (errors.Count > 0)
                    {
                        message += "\n\nError details:\n" + string.Join("\n", errors.Take(5));
                        if (errors.Count > 5)
                        {
                            message += $"\n... there are {errors.Count - 5} errors";
                        }
                    }
                }
                System.Windows.MessageBox.Show(message, "Conversion result", MessageBoxButton.OK,
                    failCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
        }

        private void VideoButton_Click(object sender, RoutedEventArgs e)
        {
            if (engine == null)
            {
                LoadEngine();
            }
            var video = new Video(engine);
            this.Close();
            video.ShowDialog();
        }

        // Modify the InitializeHotkeys method
        public void InitializeHotkeys()
        {
            // Load the hotkeys from the settings
            var settings = HotkeySettingsManager.LoadSettings();

            // Create the available key list (A-Z)
            var availableKeys = Enumerable.Range(65, 26).Select(c => (char)c).ToList();

            // Initialize the hotkey collection, prefer localized descriptions by Id
            _hotkeys = new ObservableCollection<HotkeyViewModel>(
                settings.Hotkeys.Select(h =>
                {
                    string localizedDescription = null;
                    try
                    {
                        var key = $"Hotkey_{h.Id}_Description";
                        localizedDescription = System.Windows.Application.Current?
                            .TryFindResource(key) as string;
                    }
                    catch
                    {
                        // ignore and fallback
                    }

                    return new HotkeyViewModel
                    {
                        Id = h.Id,
                        Description = string.IsNullOrEmpty(localizedDescription) ? h.Description : localizedDescription,
                        IsCtrl = h.IsCtrl,
                        IsShift = h.IsShift,
                        SelectedKey = h.SelectedKey,
                        AvailableKeys = availableKeys
                    };
                })
            );

            // In some design-time or early-initialization scenarios, the ListView
            // may not yet be created; guard against null to avoid crashes.
            if (hotkeyListView != null)
            {
                hotkeyListView.ItemsSource = _hotkeys;
            }
        }

        // Modify the SaveButton_Click method, add the function to save to the file
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Verify each hotkey: must contain Ctrl or Shift
            foreach (var hotkey in _hotkeys)
            {
                if (!hotkey.IsCtrl && !hotkey.IsShift)
                {
                    System.Windows.MessageBox.Show($"The hotkey \"{hotkey.Description}\" must contain Ctrl or Shift.",
                                    "Invalid hotkey", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Ensure the selected key is A-Z (defensive check)
                if (!char.IsLetter(hotkey.SelectedKey) || hotkey.SelectedKey < 'A' || hotkey.SelectedKey > 'Z')
                {
                    System.Windows.MessageBox.Show($"The hotkey \"{hotkey.Description}\" must select a letter between A-Z.",
                                    "Invalid key", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // Check for duplicates
            var hotkeyTexts = _hotkeys.Select(h => h.GetHotkeyText()).ToList();
            if (hotkeyTexts.GroupBy(t => t).Any(g => g.Count() > 1))
            {
                System.Windows.MessageBox.Show("Duplicate hotkey combinations found, please modify and save again.",
                                "Duplicate hotkeys", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Save the settings
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

            System.Windows.MessageBox.Show("Hotkey settings saved.", "Save successful",
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
                System.Windows.MessageBox.Show($"Triggered hotkey: {hotkey.Description}\nCombination key: {hotkey.GetHotkeyText()}",
                                "Hotkey triggered", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RegisterAllHotkeys()
        {
            // First unregister all hotkeys
            UnregisterAllHotkeys();

            // Register all hotkeys
            foreach (var hotkey in _hotkeys)
            {
                RegisterHotkey(hotkey);
            }
        }

        // Add this helper method to your class
        private uint GetVirtualKeyFromChar(char c)
        {
            // For letter characters, directly convert to the corresponding virtual key code
            if (char.IsLetter(c))
            {
                // The virtual key code for letters is the ASCII value (A=65, B=66, ..., Z=90)
                return (uint)char.ToUpper(c);
            }

            return 0;
        }

        private void RegisterHotkey(HotkeyViewModel hotkey)
        {
            uint modifiers = 0;
            if (hotkey.IsCtrl) modifiers |= MOD_CTRL;
            if (hotkey.IsShift) modifiers |= MOD_SHIFT;

            // Use this custom conversion method
            uint virtualKey = GetVirtualKeyFromChar(hotkey.SelectedKey);



            if (!RegisterHotKey(_windowHandle, hotkey.Id, modifiers, virtualKey))
            {
                // Registration failed, possibly because of hotkey conflict
                System.Windows.MessageBox.Show($"Failed to register hotkey {hotkey.GetHotkeyText()}\nMay be conflicts with other applications.",
                                "Registration failed", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (System.Windows.MessageBox.Show("Are you sure you want to restore the default hotkey settings?", "Confirm restore default",
                               MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                InitializeHotkeys();
                RegisterAllHotkeys();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

        }

        private void PreviewRegion_Click(object sender, RoutedEventArgs e)
        {
            notifyIcon.ShowRegionOverlay();
        }

        private void PadTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PadTextBox.Text, out int pad))
            {
                int padHorizontal = Config.GetPadHorizontal(0);
                Config.Set("Pad", new int[] { pad, padHorizontal });
                UpdateMainWindowPosition();
            }
        }

        private void PadHorizontalTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PadHorizontalTextBox.Text, out int padHorizontal))
            {
                int pad = Config.GetPad(86);
                Config.Set("Pad", new int[] { pad, padHorizontal });
                UpdateMainWindowPosition();
            }
        }

        private void PadVerticalIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PadTextBox.Text, out int pad))
            {
                pad++;
                PadTextBox.Text = pad.ToString();
                int padHorizontal = int.TryParse(PadHorizontalTextBox.Text, out int ph) ? ph : 0;
                Config.Set("Pad", new int[] { pad, padHorizontal });
                UpdateMainWindowPosition();
            }
        }

        private void PadVerticalDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PadTextBox.Text, out int pad))
            {
                pad--;
                PadTextBox.Text = pad.ToString();
                int padHorizontal = int.TryParse(PadHorizontalTextBox.Text, out int ph) ? ph : 0;
                Config.Set("Pad", new int[] { pad, padHorizontal });
                UpdateMainWindowPosition();
            }
        }

        private void PadHorizontalIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PadHorizontalTextBox.Text, out int padHorizontal))
            {
                padHorizontal++;
                PadHorizontalTextBox.Text = padHorizontal.ToString();
                int pad = int.TryParse(PadTextBox.Text, out int p) ? p : 86;
                Config.Set("Pad", new int[] { pad, padHorizontal });
                UpdateMainWindowPosition();
            }
        }

        private void PadHorizontalDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PadHorizontalTextBox.Text, out int padHorizontal))
            {
                padHorizontal--;
                PadHorizontalTextBox.Text = padHorizontal.ToString();
                int pad = int.TryParse(PadTextBox.Text, out int p) ? p : 86;
                Config.Set("Pad", new int[] { pad, padHorizontal });
                UpdateMainWindowPosition();
            }
        }

        private void UpdateMainWindowPosition()
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.UpdateWindowPosition();
            }
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
                    UseShellExecute = true // Must be true to open in the default browser
                });
            }
            catch (Exception ex)
            {
                Logger.Log.Error(ex);
            }
            e.Handled = true;
        }

        // Override the closing event
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!REAL_CLOSE)
            {
                // Cancel the default close behavior
                e.Cancel = true;
                // Change to hide the window
                this.Hide();
            }
            base.OnClosing(e);
        }

        // Provide a method to manually close the window (e.g. when the program exits)
        public void RealClose()
        {
            REAL_CLOSE = true;
            try
            {
                engine.Dispose();
            }
            catch
            {

            }
            this.Close();
        }

        private void PlayVoiceCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!_uiLangInitialized)
            {
                return;
            }
            Config.Set("PlayVoice", PlayVoiceCheckBox.IsChecked == true);
        }
    }
}
