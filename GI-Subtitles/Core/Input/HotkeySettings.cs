using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using System.Xml.Serialization;
using GI_Subtitles.Common;
using GI_Subtitles.Models;

namespace GI_Subtitles.Core.Input
{
    /// <summary>
    /// Hotkey settings configuration
    /// </summary>
    [XmlRoot("HotkeySettings")]
    public class HotkeySettings
    {
        [XmlArray("Hotkeys")]
        [XmlArrayItem("Hotkey")]
        public List<HotkeyData> Hotkeys { get; set; } = new List<HotkeyData>();
    }

    /// <summary>
    /// Hotkey settings manager
    /// </summary>
    public static class HotkeySettingsManager
    {
        private static string _settingsPath = Path.Combine(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles"), "hotkeySettings.xml");

        public static HotkeySettings LoadSettings()
        {
            if (!File.Exists(_settingsPath))
            {
                // Return default settings
                return GetDefaultSettings();
            }

            try
            {
                using (var reader = new StreamReader(_settingsPath))
                {
                    var serializer = new XmlSerializer(typeof(HotkeySettings));
                    var settings = (HotkeySettings)serializer.Deserialize(reader);
                    MergeMissingDefaults(settings);
                    return settings;
                }
            }
            catch
            {
                // Return default settings when reading fails
                return GetDefaultSettings();
            }
        }

        private static void MergeMissingDefaults(HotkeySettings settings)
        {
            if (settings == null)
            {
                return;
            }

            if (settings.Hotkeys == null)
            {
                settings.Hotkeys = new List<HotkeyData>();
            }

            foreach (var defaultHotkey in GetDefaultSettings().Hotkeys)
            {
                if (!settings.Hotkeys.Exists(h => h.Id == defaultHotkey.Id))
                {
                    settings.Hotkeys.Add(defaultHotkey);
                }
            }
        }

        public static void SaveSettings(HotkeySettings settings)
        {
            try
            {
                using (var writer = new StreamWriter(_settingsPath))
                {
                    var serializer = new XmlSerializer(typeof(HotkeySettings));
                    serializer.Serialize(writer, settings);
                }
            }
            catch { /* Handle save errors */ }
        }

        private static string GetLocalizedString(string resourceKey, string fallback)
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
            catch
            {
                // ignore and fallback
            }
            return fallback;
        }

        private static HotkeySettings GetDefaultSettings()
        {
            return new HotkeySettings
            {
                Hotkeys = new List<HotkeyData>
                {
                    new HotkeyData
                    {
                        Id = 9000, IsCtrl = true, IsShift = true, SelectedKey = 'S',
                        Description = GetLocalizedString("Hotkey_9000_Description", "开始/停止识别字幕")
                    },
                    new HotkeyData
                    {
                        Id = 9001, IsCtrl = true, IsShift = true, SelectedKey = 'R',
                        Description = GetLocalizedString("Hotkey_9001_Description", "选择字幕区域（第一行）")
                    },
                    new HotkeyData
                    {
                        Id = 9002, IsCtrl = true, IsShift = true, SelectedKey = 'H',
                        Description = GetLocalizedString("Hotkey_9002_Description", "隐藏双语字幕")
                    },
                    new HotkeyData
                    {
                        Id = 9003, IsCtrl = true, IsShift = true, SelectedKey = 'D',
                        Description = GetLocalizedString("Hotkey_9003_Description", "展示识别区域")
                    },
                    new HotkeyData
                    {
                        Id = 9004, IsCtrl = true, IsShift = true, SelectedKey = 'F',
                        Description = GetLocalizedString("Hotkey_9004_Description", "刷新当前字幕并重新播放语音")
                    },
                    new HotkeyData
                    {
                        Id = 9005, IsCtrl = true, IsShift = true, SelectedKey = 'V',
                        Description = GetLocalizedString("Hotkey_9005_Description", "切换语音播放倍速")
                    }
                }
            };
        }
    }

    /// <summary>
    /// Hotkey view model for UI binding
    /// </summary>
    public class HotkeyViewModel : INotifyPropertyChanged
    {
        private int _id;
        private string _description;
        private bool _isCtrl;
        private bool _isShift;
        private char _selectedKey;
        private bool _isEditing;
        private List<char> _availableKeys;

        public event PropertyChangedEventHandler PropertyChanged;

        public int Id
        {
            get => _id;
            set
            {
                _id = value;
                OnPropertyChanged(nameof(Id));
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged(nameof(Description));
            }
        }

        public bool IsCtrl
        {
            get => _isCtrl;
            set
            {
                _isCtrl = value;
                OnPropertyChanged(nameof(IsCtrl));
                OnPropertyChanged(nameof(GetHotkeyText));
            }
        }

        public bool IsShift
        {
            get => _isShift;
            set
            {
                _isShift = value;
                OnPropertyChanged(nameof(IsShift));
                OnPropertyChanged(nameof(GetHotkeyText));
            }
        }

        public char SelectedKey
        {
            get => _selectedKey;
            set
            {
                _selectedKey = value;
                OnPropertyChanged(nameof(SelectedKey));
                OnPropertyChanged(nameof(GetHotkeyText));
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                _isEditing = value;
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(ButtonText));
            }
        }

        public List<char> AvailableKeys
        {
            get => _availableKeys;
            set
            {
                _availableKeys = value;
                OnPropertyChanged(nameof(AvailableKeys));
            }
        }

        public string ButtonText => IsEditing ? "Cancel" : "Edit";

        public ICommand ToggleEditCommand => new RelayCommand(ToggleEdit);

        public string GetHotkeyText()
        {
            var parts = new List<string>();
            if (IsCtrl) parts.Add("Ctrl");
            if (IsShift) parts.Add("Shift");
            parts.Add(SelectedKey.ToString());
            return string.Join("+", parts);
        }

        private void ToggleEdit()
        {
            IsEditing = !IsEditing;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

