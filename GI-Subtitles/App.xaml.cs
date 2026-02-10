using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Globalization;

namespace GI_Subtitles
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static Mutex _mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            string appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            Directory.SetCurrentDirectory(appDir);
            const string appName = "GI-Subtitles";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                Process current = Process.GetCurrentProcess();
                foreach (var process in Process.GetProcessesByName(current.ProcessName).Where(p => p.Id != current.Id))
                {
                    process.Kill();
                }
            }

            // Load UI language resources before MainWindow is created
            LoadUILanguageResources();

            base.OnStartup(e);
        }

        /// <summary>
        /// Load UI language resources based on Config["UILang"] before any UI components are created
        /// </summary>
        private void LoadUILanguageResources()
        {
            try
            {
                string uiLang = Config.Get("UILang", "zh-CN");
                var culture = new CultureInfo(uiLang);
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;

                // Load the language resource dictionary
                var rd = new ResourceDictionary();
                switch (uiLang)
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
                this.Resources.MergedDictionaries.Add(rd);
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Failed to load UI language resources: {ex.Message}");
                try
                {
                    var rd = new ResourceDictionary();
                    rd.Source = new Uri("Resources/Strings.zh-CN.xaml", UriKind.Relative);
                    this.Resources.MergedDictionaries.Add(rd);
                }
                catch
                {
                    Logger.Log.Error($"Failed to load fallback UI language resources: {ex.Message}");
                }
            }
        }
    }
}
