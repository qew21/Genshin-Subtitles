using GI_Subtitles.Properties;
using Microsoft.Win32;
using Screenshot;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using GI_Subtitles.Common;
using GI_Subtitles.Core.Overlay;
using System.Drawing;

namespace GI_Subtitles.Core.UI
{
    /// <summary>
    /// System tray notification icon management
    /// </summary>
    public class INotifyIcon
    {
        System.Windows.Forms.ContextMenuStrip contextMenuStrip;
        ToolStripMenuItem fontSizeSelector;
        ToolStripMenuItem languageSelector;
        ToolStripMenuItem settingItem;
        ToolStripMenuItem activityLogItem;
        ToolStripMenuItem exitItem;
        ToolStripMenuItem availableUpdateItem;
        private EventHandler availableUpdateClick;
        private string availableUpdateVersion;
        private string availableUpdateTextResource = "Tray_UpdateAvailable";
        private string availableUpdateTextFallback = "New version {0}";
        private object[] availableUpdateTextArguments = Array.Empty<object>();
        private int Size = Config.Config.Get<int>("Size");
        private bool AutoStart = Config.Config.Get("AutoStart", false);
        private LiveOverlaySession _overlaySession;
        string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        public bool isContextMenuOpen = false;
        private Views.SettingsWindow data;
        private Action _openActivityLog;

        public NotifyIcon InitializeNotifyIcon(double scale)
        {
            NotifyIcon notifyIcon;
            contextMenuStrip = new ContextMenuStrip();
            // Localized tray menu texts (fallback to Chinese)
            string trayFontSize = GetLocalizedString("Tray_FontSize", "字号选择");
            string trayLanguage = GetLocalizedString("Tray_Language", "Language");
            string trayActivityLog = GetLocalizedString("Tray_ActivityLog", "活动日志");
            string traySettings = GetLocalizedString("Tray_Settings", "程序设定");
            string trayExit = GetLocalizedString("Tray_Exit", "退出程序");

            fontSizeSelector = new ToolStripMenuItem(trayFontSize);
            fontSizeSelector.DropDownItems.Add(CreateSizeItem("14"));
            fontSizeSelector.DropDownItems.Add(CreateSizeItem("16"));
            fontSizeSelector.DropDownItems.Add(CreateSizeItem("18"));
            fontSizeSelector.DropDownItems.Add(CreateSizeItem("20"));
            fontSizeSelector.DropDownItems.Add(CreateSizeItem("22"));
            fontSizeSelector.DropDownItems.Add(CreateSizeItem("24"));

            // Language selector submenu in tray area
            languageSelector = new ToolStripMenuItem(trayLanguage);
            languageSelector.DropDownItems.Add(CreateLanguageItem("简体中文", "zh-CN"));
            languageSelector.DropDownItems.Add(CreateLanguageItem("English", "en-US"));
            languageSelector.DropDownItems.Add(CreateLanguageItem("日本語", "ja-JP"));

            activityLogItem = new ToolStripMenuItem(trayActivityLog);
            settingItem = new ToolStripMenuItem(traySettings);
            exitItem = new ToolStripMenuItem(trayExit);
            ToolStripMenuItem versionItem = new ToolStripMenuItem(version)
            {
                Enabled = false
            };
            availableUpdateItem = new ToolStripMenuItem
            {
                Visible = false
            };
            availableUpdateItem.Click += (sender, e) => availableUpdateClick?.Invoke(sender, e);
            activityLogItem.Click += (sender, e) => _openActivityLog?.Invoke();
            settingItem.Click += (sender, e) =>
            {
                data.ShowDialog();
            };
            exitItem.Click += (sender, e) => { System.Windows.Application.Current.Shutdown(); };
            contextMenuStrip.Items.Add(versionItem);
            contextMenuStrip.Items.Add(availableUpdateItem);
            contextMenuStrip.Items.Add(new ToolStripSeparator());
            contextMenuStrip.Items.Add(languageSelector);
            contextMenuStrip.Items.Add(fontSizeSelector);
            contextMenuStrip.Items.Add(activityLogItem);
            contextMenuStrip.Items.Add(settingItem);
            contextMenuStrip.Items.Add(exitItem);
            contextMenuStrip.Opening += ContextMenuStrip_Opening; // The menu is opened before triggering
            contextMenuStrip.Closed += ContextMenuStrip_Closed;   // The menu is closed after triggering
            Uri iconUri = new Uri("pack://application:,,,/Resources/mask.ico");
            Stream iconStream = System.Windows.Application.GetResourceStream(iconUri).Stream;
            notifyIcon = new NotifyIcon
            {
                Icon = new Icon(iconStream),
                Visible = true,
                ContextMenuStrip = contextMenuStrip
            };
            SetAutoStart(AutoStart);
            return notifyIcon;
        }

        private string GetLocalizedString(string resourceKey, string fallback)
        {
            try
            {
                var app = System.Windows.Application.Current;
                if (app != null)
                {
                    var value = app.TryFindResource(resourceKey) as string;
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                }
            }
            catch (Exception e)
            {
                // ignore and fallback
                Logger.Log.Error($"Failed {e} to find localized string for {resourceKey}. Falling back to {fallback}.");
            }
            return fallback;
        }

        public void SetData(Views.SettingsWindow data)
        {
            this.data = data;
        }

        public void SetSession(LiveOverlaySession session)
        {
            _overlaySession = session;
        }

        public void SetActivityLogOpener(Action opener)
        {
            _openActivityLog = opener;
        }

        public string[] Region
        {
            get { return ToRegionParts(_overlaySession != null ? _overlaySession.GetCapture(0) : OverlayRect.Invalid); }
        }

        public string[] Region2
        {
            get { return ToRegionParts(_overlaySession != null ? _overlaySession.GetCapture(1) : OverlayRect.Invalid); }
        }

        private static string[] ToRegionParts(OverlayRect rect)
        {
            if (rect == null || !rect.IsValid)
            {
                return new[] { "0", "0", "0", "0" };
            }

            return new[]
            {
                rect.X.ToString(),
                rect.Y.ToString(),
                rect.Width.ToString(),
                rect.Height.ToString()
            };
        }

        public void ShowAvailableUpdate(string updateVersion, EventHandler clickHandler)
        {
            if (availableUpdateItem == null)
                return;

            availableUpdateVersion = updateVersion;
            availableUpdateClick = clickHandler;
            availableUpdateTextResource = "Tray_UpdateAvailable";
            availableUpdateTextFallback = "New version {0}";
            availableUpdateTextArguments = new object[] { updateVersion };
            availableUpdateItem.Text = FormatAvailableUpdateText();
            availableUpdateItem.Enabled = true;
            availableUpdateItem.Visible = true;
        }

        public void ShowUpdateStatus(string resourceKey, string fallback, params object[] arguments)
        {
            if (availableUpdateItem == null)
                return;

            availableUpdateTextResource = resourceKey;
            availableUpdateTextFallback = fallback;
            availableUpdateTextArguments = arguments ?? Array.Empty<object>();
            availableUpdateItem.Text = FormatAvailableUpdateText();
            availableUpdateItem.Enabled = false;
            availableUpdateItem.Visible = true;
        }

        public void RestoreAvailableUpdate()
        {
            if (availableUpdateItem == null || string.IsNullOrWhiteSpace(availableUpdateVersion))
                return;

            availableUpdateTextResource = "Tray_UpdateAvailable";
            availableUpdateTextFallback = "New version {0}";
            availableUpdateTextArguments = new object[] { availableUpdateVersion };
            availableUpdateItem.Text = FormatAvailableUpdateText();
            availableUpdateItem.Enabled = true;
            availableUpdateItem.Visible = true;
        }

        public void HideAvailableUpdate()
        {
            if (availableUpdateItem == null)
                return;

            availableUpdateItem.Visible = false;
            availableUpdateItem.Enabled = true;
            availableUpdateClick = null;
            availableUpdateVersion = null;
            availableUpdateTextArguments = Array.Empty<object>();
        }

        /// <summary>
        /// Refresh tray menu texts based on current language resources
        /// </summary>
        public void RefreshMenuTexts()
        {
            if (contextMenuStrip == null || fontSizeSelector == null || settingItem == null || exitItem == null)
                return;

            try
            {
                // Update font size selector text
                string trayFontSize = GetLocalizedString("Tray_FontSize", "字号选择");
                fontSizeSelector.Text = trayFontSize;

                // Update language selector text
                if (languageSelector != null)
                {
                    string trayLanguage = GetLocalizedString("Tray_Language", "Language");
                    languageSelector.Text = trayLanguage;
                }

                if (activityLogItem != null)
                {
                    activityLogItem.Text = GetLocalizedString("Tray_ActivityLog", "活动日志");
                }

                // Update settings menu item text
                string traySettings = GetLocalizedString("Tray_Settings", "程序设定");
                settingItem.Text = traySettings;

                // Update exit menu item text
                string trayExit = GetLocalizedString("Tray_Exit", "退出程序");
                exitItem.Text = trayExit;

                if (availableUpdateItem != null && availableUpdateItem.Visible &&
                    !string.IsNullOrWhiteSpace(availableUpdateVersion))
                {
                    availableUpdateItem.Text = FormatAvailableUpdateText();
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Error refreshing tray menu texts: {ex.Message}");
            }
        }

        private string FormatAvailableUpdateText()
        {
            var format = GetLocalizedString(availableUpdateTextResource, availableUpdateTextFallback);
            try
            {
                return string.Format(format, availableUpdateTextArguments);
            }
            catch (FormatException)
            {
                return availableUpdateTextFallback;
            }
        }

        private void DateUpdate()
        {
            data.ShowDialog();
        }

        public bool ChooseRegion()
        {
            int pairId;
            return ChooseRegion(out pairId);
        }

        public bool ChooseRegion(out int pairId)
        {
            pairId = 0;
            Views.RegionPairSettings settings = data != null ? data.PairSettings : null;
            if (settings == null || !settings.TryGetHotkeyTarget(out _, out pairId, out int ordinal))
            {
                pairId = 0;
                return false;
            }

            OverlayRect capture = PromptRect("RegionPair_BoxCaptureMask", "框选识别区（对 {0}）", ordinal);
            if (!capture.IsValid)
            {
                return false;
            }

            OverlayRect display = PromptRect("RegionPair_BoxDisplayMask", "框选显示区（对 {0}）", ordinal);
            if (!display.IsValid)
            {
                return false;
            }

            bool boxed = settings.TryBoxHotkeyPair(capture, display);
            data.RefreshPairPage();
            return boxed;
        }

        public bool AddRegionPair()
        {
            Views.RegionPairSettings settings = data != null ? data.PairSettings : null;
            if (settings == null || !settings.TryStartAdd())
            {
                return false;
            }

            int ordinal = settings.NextAddOrdinal;
            OverlayRect capture = PromptRect("RegionPair_BoxCaptureMask", "框选识别区（对 {0}）", ordinal);
            if (!capture.IsValid)
            {
                settings.AbortAdd();
                return false;
            }

            settings.SetAddCapture(capture);
            OverlayRect display = PromptRect("RegionPair_BoxDisplayMask", "框选显示区（对 {0}）", ordinal);
            if (!display.IsValid)
            {
                settings.AbortAdd();
                return false;
            }

            settings.SetAddDisplay(display);
            return settings.TryCommitAdd();
        }

        public bool BoxCapture(int pairId)
        {
            Views.RegionPairSettings settings = data != null ? data.PairSettings : null;
            if (settings == null)
            {
                return false;
            }

            int ordinal = settings.OrdinalOf(pairId);
            if (ordinal <= 0)
            {
                return false;
            }

            OverlayRect capture = PromptRect("RegionPair_BoxCaptureMask", "框选识别区（对 {0}）", ordinal);
            return capture.IsValid && settings.TrySetCapture(pairId, capture);
        }

        public bool BoxDisplay(int pairId)
        {
            Views.RegionPairSettings settings = data != null ? data.PairSettings : null;
            if (settings == null)
            {
                return false;
            }

            int ordinal = settings.OrdinalOf(pairId);
            if (ordinal <= 0)
            {
                return false;
            }

            OverlayRect display = PromptRect("RegionPair_BoxDisplayMask", "框选显示区（对 {0}）", ordinal);
            return display.IsValid && settings.TrySetDisplay(pairId, display);
        }

        public bool BoxDarkScreenDisplay()
        {
            if (_overlaySession == null)
            {
                return false;
            }

            OverlayRect display = PromptRect("ExtraPath_BoxDarkScreenMask", "框选暗屏显示区");
            if (!display.IsValid)
            {
                return false;
            }

            _overlaySession.SetDarkScreenDisplay(display);
            data?.RefreshExtraPathDisplayRows();
            return true;
        }

        public bool BoxDialogueOptionDisplay()
        {
            if (_overlaySession == null)
            {
                return false;
            }

            OverlayRect display = PromptRect("ExtraPath_BoxDialogueOptionMask", "框选对话选项显示区");
            if (!display.IsValid)
            {
                return false;
            }

            _overlaySession.SetDialogueOptionDisplay(display);
            data?.RefreshExtraPathDisplayRows();
            return true;
        }

        private OverlayRect PromptRect(string resourceKey, string fallback)
        {
            return PromptRect(resourceKey, fallback, 0);
        }

        private OverlayRect PromptRect(string resourceKey, string fallback, int ordinal)
        {
            try
            {
                string format = GetLocalizedString(resourceKey, fallback);
                string prompt = ordinal > 0 ? string.Format(format, ordinal) : format;
                var rect = Screenshot.Screenshot.GetRegion(prompt);
                if (Convert.ToInt32(rect.Width) > 0 && Convert.ToInt32(rect.Height) > 0)
                {
                    return new OverlayRect(
                        Convert.ToInt32(rect.TopLeft.X),
                        Convert.ToInt32(rect.TopLeft.Y),
                        Convert.ToInt32(rect.Width),
                        Convert.ToInt32(rect.Height));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            return OverlayRect.Invalid;
        }

        private ToolStripMenuItem CreateSizeItem(string code)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(code)
            {
                Tag = code,
                CheckOnClick = true
            };
            item.CheckedChanged += SizeItem_CheckedChanged;
            if (Size == Convert.ToInt32(code))
            {
                item.Checked = true;
            }
            return item;
        }

        /// <summary>
        /// Create a language menu item for the tray language selector.
        /// </summary>
        /// <param name="displayName">Display text of the language.</param>
        /// <param name="cultureTag">Culture tag such as zh-CN / en-US / ja-JP.</param>
        /// <returns>Configured ToolStripMenuItem.</returns>
        private ToolStripMenuItem CreateLanguageItem(string displayName, string cultureTag)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(displayName)
            {
                Tag = cultureTag,
                CheckOnClick = true
            };
            item.CheckedChanged += LanguageItem_CheckedChanged;

            // Initialize checked state from config
            string currentLang = Config.Config.Get("UILang", "zh-CN");
            if (string.Equals(currentLang, cultureTag, StringComparison.OrdinalIgnoreCase))
            {
                item.Checked = true;
            }

            return item;
        }

        /// <summary>
        /// Handle language selection from the tray submenu.
        /// Ensures only one language is selected and propagates change to SettingsWindow.
        /// </summary>
        private void LanguageItem_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem selectedItem && selectedItem.Checked)
            {
                string cultureTag = selectedItem.Tag as string;
                if (string.IsNullOrEmpty(cultureTag))
                {
                    return;
                }

                // Uncheck other language items in the same submenu
                foreach (ToolStripMenuItem langItem in languageSelector.DropDownItems)
                {
                    if (!ReferenceEquals(langItem, selectedItem))
                    {
                        langItem.Checked = false;
                    }
                }

                // Persist to config
                Config.Config.Set("UILang", cultureTag);

                // Apply to settings window (which will update resources, tray texts, hotkeys, etc.)
                if (data != null)
                {
                    data.SetUILanguage(cultureTag);
                }
            }
        }

        private void SizeItem_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem selectedSize && selectedSize.Checked)
            {
                int newSize = Convert.ToInt32(selectedSize.Tag.ToString());
                if (Size != newSize)
                {
                    Size = newSize;

                    foreach (ToolStripMenuItem langItem in fontSizeSelector.DropDownItems)
                    {
                        if (langItem != selectedSize)
                        {
                            langItem.Checked = false;
                        }
                    }
                    Config.Config.Set("Size", Size);
                }
            }
        }

        private void SetAutoStart(bool autoStart)
        {
            string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);

            if (key == null)
            {
                Logger.Log.Error("Failed to open registry key");
            }

            string existingValue = (string)key.GetValue(Process.GetCurrentProcess().ProcessName, null);
            if (autoStart)
            {
                if (existingValue != appPath)
                {
                    key.SetValue(Process.GetCurrentProcess().ProcessName, appPath);
                    Logger.Log.Info("Startup item added successfully!");
                }
            }
            else
            {
                if (existingValue != null)
                {
                    key.DeleteValue(Process.GetCurrentProcess().ProcessName, false);
                    Logger.Log.Info("Startup item removed!");
                }
            }
        }
        private void ContextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            isContextMenuOpen = true;
        }

        private void ContextMenuStrip_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            isContextMenuOpen = false;
        }

    }
}
