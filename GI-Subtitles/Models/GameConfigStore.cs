using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace GI_Subtitles.Models
{
    /// <summary>
    /// Stateless persistence for per-game configuration files.
    /// </summary>
    public static class GameConfigStore
    {
        public const string WutheringRepoUrl =
            "https://github.com/Arikatsu/WutheringWaves_Data/commits.atom";
        public const string WutheringTextMapUrlTemplate =
            "https://raw.githubusercontent.com/Arikatsu/WutheringWaves_Data/HEAD/Textmaps/{Language}/multi_text/MultiText.json";
        public const string EndfieldRepoUrl =
            "https://github.com/cmyyx/cep/commits/main/public/game-i18n.atom";
        public const string EndfieldTextMapUrlTemplate =
            "https://raw.githubusercontent.com/cmyyx/cep/main/public/game-i18n/{Language}/000.json";

        public static GameConfig LoadOrCreate(
            string configPath,
            Func<GameConfig> createDefault,
            Action<Exception> onReadError = null)
        {
            GameConfig config = null;
            if (File.Exists(configPath))
            {
                try
                {
                    config = JsonConvert.DeserializeObject<GameConfig>(File.ReadAllText(configPath));
                }
                catch (Exception ex)
                {
                    onReadError?.Invoke(ex);
                }
            }

            if (config != null)
            {
                return config;
            }

            config = createDefault();
            File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
            return config;
        }

        /// <summary>
        /// Replaces only known Dimbreath WutheringData URLs, preserving custom repositories.
        /// </summary>
        public static bool MigrateWutheringRepository(GameConfig config)
        {
            if (config == null) return false;

            bool changed = false;
            if (IsLegacyWutheringUrl(config.RepoUrl))
            {
                config.RepoUrl = WutheringRepoUrl;
                config.RepoType = "GitHubAtom";
                changed = true;
            }
            if (IsLegacyWutheringUrl(config.InputUrlTemplate))
            {
                config.InputUrlTemplate = WutheringTextMapUrlTemplate;
                changed = true;
            }
            if (IsLegacyWutheringUrl(config.OutputUrlTemplate))
            {
                config.OutputUrlTemplate = WutheringTextMapUrlTemplate;
                changed = true;
            }

            return changed;
        }

        public static bool MigrateEndfieldRepository(GameConfig config)
        {
            if (config == null) return false;

            bool changed = false;
            bool migrateLanguageMapping = false;
            if (IsLegacyEndfieldUrl(config.RepoUrl))
            {
                config.RepoUrl = EndfieldRepoUrl;
                config.RepoType = "GitHubAtom";
                changed = true;
            }
            if (IsLegacyEndfieldUrl(config.InputUrlTemplate))
            {
                config.InputUrlTemplate = EndfieldTextMapUrlTemplate;
                changed = true;
                migrateLanguageMapping = true;
            }
            if (IsLegacyEndfieldUrl(config.OutputUrlTemplate))
            {
                config.OutputUrlTemplate = EndfieldTextMapUrlTemplate;
                changed = true;
                migrateLanguageMapping = true;
            }

            if (migrateLanguageMapping)
            {
                config.LanguageMapping = CreateEndfieldLanguageMapping();
            }

            return changed;
        }

        public static Dictionary<string, string> CreateEndfieldLanguageMapping()
        {
            return new Dictionary<string, string>
            {
                ["CHS"] = "zh-CN",
                ["CHT"] = "zh-TW",
                ["EN"] = "en",
                ["JP"] = "ja",
                ["KR"] = "ko",
                ["FR"] = "fr",
                ["DE"] = "de",
                ["ES"] = "es-MX",
                ["PT"] = "pt-BR",
                ["RU"] = "ru",
                ["TH"] = "th",
                ["ID"] = "id",
                ["VI"] = "vi"
            };
        }

        private static bool IsLegacyWutheringUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri)) return false;
            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            const string repositoryPath = "/Dimbreath/WutheringData";
            return string.Equals(uri.AbsolutePath, repositoryPath, StringComparison.OrdinalIgnoreCase) ||
                   uri.AbsolutePath.StartsWith(repositoryPath + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLegacyEndfieldUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri)) return false;
            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            const string repositoryPath = "/XiaBei-cy/EndfieldData";
            return string.Equals(uri.AbsolutePath, repositoryPath, StringComparison.OrdinalIgnoreCase) ||
                   uri.AbsolutePath.StartsWith(repositoryPath + "/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
