using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using GI_Subtitles;

namespace GI_Test
{
    public static class Config
    {
        private static readonly string SettingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "Config.json");
        private static readonly Dictionary<string, JToken> _settings = new Dictionary<string, JToken>();

        static Config()
        {
            Load(("Config.json"));
            Load(SettingsFile);
        }

        private static void Load(string file)
        {
            if (!Directory.Exists(SettingsFolder))
                Directory.CreateDirectory(SettingsFolder);

            if (!File.Exists(file))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(file);
                var jo = JObject.Parse(json);
                if (jo.Count > 0)
                {
                    foreach (var prop in jo.Properties())
                    {
                        _settings[prop.Name] = prop.Value;
                    }
                }
                else
                {
                    Save();
                }

            }
            catch (Exception ex)
            {
                Logger.Log.Error(ex);
            }
        }

        public static void Save()
        {
            var jo = new JObject();
            foreach (var kv in _settings)
            {
                jo[kv.Key] = kv.Value;
            }
            File.WriteAllText(SettingsFile, jo.ToString(Formatting.Indented));
        }

        public static T Get<T>(string key, T defaultValue = default)
        {
            if (_settings.TryGetValue(key, out var token))
            {
                try { return token.ToObject<T>(); }
                catch { }
            }
            return defaultValue;
        }

        public static void Set<T>(string key, T value)
        {
            _settings[key] = JToken.FromObject(value);
            Save();
        }
    }
}
