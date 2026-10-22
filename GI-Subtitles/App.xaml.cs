using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Globalization;
using GI_Subtitles.Core.Config;
using GI_Subtitles.Common;
using static GI_Subtitles.Core.Config.Config;

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
            string configuredFont = Config.Get("Font", "Arial");
            string resolvedFont = ResolveSubtitleFontName(configuredFont);
            if (!string.Equals(configuredFont, resolvedFont, StringComparison.Ordinal))
            {
                Config.Set("Font", resolvedFont);
            }
            ApplySubtitleFont(resolvedFont);

            bool hasSubtitleColor = Config.Contains("SubtitleColor");
            string configuredSubtitleColor = "#FFFFFF";
            bool hasStringSubtitleColor = !hasSubtitleColor ||
                Config.TryGetString("SubtitleColor", out configuredSubtitleColor);
            if (!hasSubtitleColor)
            {
                // Backfill installations whose packaged defaults predate this setting.
                Config.Set("SubtitleColor", configuredSubtitleColor);
            }

            Color subtitleColor;
            if (!hasStringSubtitleColor ||
                !TryParseSubtitleColor(configuredSubtitleColor, out subtitleColor))
            {
                string messageTemplate = Current.TryFindResource("SubtitleColor_InvalidSaved") as string
                    ?? "The saved subtitle color '{0}' is invalid. It has been reset to white; choose a new color in Settings.";
                string title = Current.TryFindResource("SubtitleColor_InvalidSavedTitle") as string
                    ?? "Subtitle color reset";
                string invalidValue = hasStringSubtitleColor ? configuredSubtitleColor : "<non-string value>";
                Logger.Log.Error($"Invalid saved subtitle color '{invalidValue}'. Resetting it to #FFFFFF.");
                MessageBox.Show(
                    string.Format(CultureInfo.CurrentCulture, messageTemplate, invalidValue),
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Config.Set("SubtitleColor", "#FFFFFF");
                subtitleColor = Colors.White;
            }
            ApplySubtitleColor(subtitleColor);

            base.OnStartup(e);
        }

        public static void ApplySubtitleFont(string fontName)
        {
            Current.Resources["SubtitleFontFamily"] = new FontFamily(ResolveSubtitleFontName(fontName));
        }

        public static void ApplySubtitleColor(Color color)
        {
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }
            Current.Resources["SubtitleForegroundBrush"] = brush;
        }

        internal static bool TryParseSubtitleColor(string value, out Color color)
        {
            color = Colors.White;
            if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[0] != '#')
            {
                return false;
            }

            if (!byte.TryParse(value.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) ||
                !byte.TryParse(value.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) ||
                !byte.TryParse(value.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue))
            {
                return false;
            }

            color = Color.FromRgb(red, green, blue);
            return true;
        }

        public static string ResolveSubtitleFontName(string fontName)
        {
            FontFamily selectedFont = Fonts.SystemFontFamilies.FirstOrDefault(
                font => string.Equals(font.Source, fontName, StringComparison.OrdinalIgnoreCase));
            if (selectedFont != null)
            {
                return selectedFont.Source;
            }

            FontFamily arial = Fonts.SystemFontFamilies.FirstOrDefault(
                font => string.Equals(font.Source, "Arial", StringComparison.OrdinalIgnoreCase));
            return arial?.Source ?? SystemFonts.MessageFontFamily.Source;
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

                // Remove the default resource dictionary from App.xaml if it exists
                var defaultRd = this.Resources.MergedDictionaries
                    .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Strings.zh-CN.xaml"));
                if (defaultRd != null)
                {
                    this.Resources.MergedDictionaries.Remove(defaultRd);
                }

                // Load the language resource dictionary
                var rd = new ResourceDictionary();
                switch (uiLang)
                {
                    case "en-US":
                        rd.Source = new Uri("pack://application:,,,/Resources/Strings.en-US.xaml", UriKind.Absolute);
                        break;
                    case "ja-JP":
                        rd.Source = new Uri("pack://application:,,,/Resources/Strings.ja-JP.xaml", UriKind.Absolute);
                        break;
                    default:
                        rd.Source = new Uri("pack://application:,,,/Resources/Strings.zh-CN.xaml", UriKind.Absolute);
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
                    rd.Source = new Uri("pack://application:,,,/Resources/Strings.zh-CN.xaml", UriKind.Absolute);
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
