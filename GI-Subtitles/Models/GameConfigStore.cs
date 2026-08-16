using System;
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
    }
}
