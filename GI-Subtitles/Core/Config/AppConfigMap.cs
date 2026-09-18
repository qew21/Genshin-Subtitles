namespace GI_Subtitles.Core.Config
{
    public sealed class AppConfigMap : IConfigMap
    {
        public bool Contains(string key)
        {
            return Config.Contains(key);
        }

        public T Get<T>(string key, T defaultValue)
        {
            return Config.Get(key, defaultValue);
        }

        public void Set<T>(string key, T value)
        {
            Config.Set(key, value);
        }

        public void Remove(string key)
        {
            Config.Remove(key);
        }
    }
}
