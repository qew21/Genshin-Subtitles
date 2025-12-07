// HotkeySettings.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using System.Xml.Serialization;

namespace GI_Subtitles
{
    [XmlRoot("HotkeySettings")]
    public class HotkeySettings
    {
        [XmlArray("Hotkeys")]
        [XmlArrayItem("Hotkey")]
        public List<HotkeyData> Hotkeys { get; set; } = new List<HotkeyData>();
    }

    public class HotkeyData
    {
        public int Id { get; set; }
        public bool IsCtrl { get; set; }
        public bool IsShift { get; set; }
        public char SelectedKey { get; set; }
        public string Description { get; set; }
    }

    public static class HotkeySettingsManager
    {
        private static string _settingsPath = Path.Combine(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles"), "hotkeySettings.xml");

        public static HotkeySettings LoadSettings()
        {
            if (!File.Exists(_settingsPath))
            {
                // 返回默认设置
                return GetDefaultSettings();
            }

            try
            {
                using (var reader = new StreamReader(_settingsPath))
                {
                    var serializer = new XmlSerializer(typeof(HotkeySettings));
                    return (HotkeySettings)serializer.Deserialize(reader);
                }
            }
            catch
            {
                // 读取失败时返回默认设置
                return GetDefaultSettings();
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
            catch { /* 处理保存错误 */ }
        }

        private static HotkeySettings GetDefaultSettings()
        {
            return new HotkeySettings
            {
                Hotkeys = new List<HotkeyData>
                {
                    new HotkeyData { Id = 9000, IsCtrl = true, IsShift = true, SelectedKey = 'S', Description = "开始/停止识别字幕" },
                    new HotkeyData { Id = 9001, IsCtrl = true, IsShift = true, SelectedKey = 'R', Description = "选择字幕区域（第一行）" },
                    new HotkeyData { Id = 9002, IsCtrl = true, IsShift = true, SelectedKey = 'H', Description = "隐藏双语字幕" },
                    new HotkeyData { Id = 9003, IsCtrl = true, IsShift = true, SelectedKey = 'D', Description = "展示识别区域" }
                }
            };
        }
    }

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

        public string ButtonText => IsEditing ? "取消" : "修改";

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

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public event EventHandler CanExecuteChanged;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => _execute();

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}