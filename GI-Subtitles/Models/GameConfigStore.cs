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
    }
}
