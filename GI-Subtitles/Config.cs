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

namespace GI_Subtitles
{
    public static class Config
    {
        private static readonly string SettingsFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "Config.json");
        private static readonly Dictionary<string, JToken> _settings = new Dictionary<string, JToken>();

        static Config()
        {
            Load();
        }

        private static void Load()
        {
            if (!Directory.Exists(SettingsFolder))
                Directory.CreateDirectory(SettingsFolder);

            if (!File.Exists(SettingsFile))
            {
                Save();
                return;
            }

            try
            {
                var json = File.ReadAllText(SettingsFile);
                var jo = JObject.Parse(json);
                _settings.Clear();
                foreach (var prop in jo.Properties())
                {
                    _settings[prop.Name] = prop.Value;
                }
            }
            catch
            {
                _settings.Clear();
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
