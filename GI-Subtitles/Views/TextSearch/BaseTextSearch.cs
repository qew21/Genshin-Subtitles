using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GI_Subtitles.Core.Config;
using GI_Subtitles.Models;
using Newtonsoft.Json;

namespace GI_Subtitles.Views.TextSearch
{
    public class BaseTextSearch : Window
    {
        protected readonly Dictionary<string, string> SourceLangs = new Dictionary<string, string>
        {
            { "简体中文", "CHS" },
            { "English", "EN" },
            { "日本語", "JP" }
        };

        protected readonly Dictionary<string, string> TargetLangs = new Dictionary<string, string>
        {
            { "简体中文", "CHS" },
            { "English", "EN" },
            { "日本語", "JP" },
            { "繁體中文", "CHT" },
            { "Deutsch", "DE" },
            { "Español", "ES" },
            { "Français", "FR" },
            { "Bahasa Indonesia", "ID" },
            { "한국어", "KR" },
            { "Português", "PT" },
            { "Русский", "RU" },
            { "ไทย", "TH" },
            { "Tiếng Việt", "VI" }
        };

        private List<GameMetadata> _supportedGames = new List<GameMetadata>();

        static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GI-Subtitles");
        
        protected ComboBox LoadSupportedGames(ComboBox gameSelector)
        {
            string gamesListPath = Path.Combine(DataDir, "Games.json");
            if (File.Exists(gamesListPath))
            {
                try
                {
                    _supportedGames =
                        JsonConvert.DeserializeObject<List<GameMetadata>>(File.ReadAllText(gamesListPath));
                }
                catch (Exception ex)
                {
                    Logger.Log.Error($"Failed to load Games.json: {ex.Message}");
                }
            }

            if (_supportedGames == null || _supportedGames.Count == 0)
            {
                // Default games with internal ID and display metadata
                _supportedGames = new List<GameMetadata>
                {
                    new GameMetadata
                    {
                        Name = "Genshin",
                        DisplayNames = new Dictionary<string, string>
                            { { "zh-CN", "原神" }, { "en-US", "Genshin Impact" }, { "ja-JP", "原神" } }
                    },
                    new GameMetadata
                    {
                        Name = "StarRail",
                        DisplayNames = new Dictionary<string, string>
                            { { "zh-CN", "崩坏：星穹铁道" }, { "en-US", "Honkai: Star Rail" }, { "ja-JP", "崩壊：スターレイル" } }
                    },
                    new GameMetadata
                    {
                        Name = "Zenless",
                        DisplayNames = new Dictionary<string, string>
                            { { "zh-CN", "绝区零" }, { "en-US", "Zenless Zone Zero" }, { "ja-JP", "ゼンレスゾーンゼロ" } }
                    },
                    new GameMetadata
                    {
                        Name = "Wuthering",
                        DisplayNames = new Dictionary<string, string>
                            { { "zh-CN", "鸣潮" }, { "en-US", "Wuthering Waves" }, { "ja-JP", "鳴潮" } }
                    },
                    new GameMetadata
                    {
                        Name = "Endfield",
                        DisplayNames = new Dictionary<string, string>
                        {
                            { "zh-CN", "明日方舟：终末地" },
                            { "en-US", "Arknights: Endfield" },
                            { "ja-JP", "アークナイツ：エンドフィール" }
                        }
                    },
                    new GameMetadata
                    {
                        Name = "BH3",
                        DisplayNames = new Dictionary<string, string>
                        {
                            { "zh-CN", "崩坏3" },
                            { "en-US", "Honkai Impact 3rd" },
                            { "ja-JP", "崩壊3rd" }
                        }
                    }
                };
                File.WriteAllText(gamesListPath, JsonConvert.SerializeObject(_supportedGames, Formatting.Indented));
            }

            // Current UI culture tag (zh-CN, en-US, ja-JP)
            var uiLang = Config.Get("UILang", "zh-CN");

            // Build a list of simple objects for the ComboBox to avoid binding errors
            // Each object has a Display property and the original Name (Internal ID)
            var displayList = _supportedGames.Select(g => new
            {
                Display = g.DisplayNames != null && g.DisplayNames.TryGetValue(uiLang, out var localizedName)
                    ? localizedName
                    : g.Name,
                g.Name
            }).ToList();

            gameSelector.ItemsSource = displayList;
            gameSelector.DisplayMemberPath = "Display";
            gameSelector.SelectedValuePath = "Name";

            return gameSelector;
        }
    }
}