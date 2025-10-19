// HotkeySettings.cs
using System;
using System.Collections.Generic;
using System.IO;
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
}