using GI_Subtitles.Core.Config;

namespace GI_Subtitles.Core.Overlay
{
    public sealed class ConfigSubtitleIdleTimeoutStore : ISubtitleIdleTimeoutStore
    {
        private readonly IConfigMap _config;

        public ConfigSubtitleIdleTimeoutStore()
            : this(new AppConfigMap())
        {
        }

        public ConfigSubtitleIdleTimeoutStore(IConfigMap config)
        {
            _config = config ?? throw new System.ArgumentNullException(nameof(config));
        }

        public int Read(int defaultValue)
        {
            return _config.Get(LiveOverlaySession.SubtitleIdleTimeoutConfigKey, defaultValue);
        }

        public void Write(int seconds)
        {
            _config.Set(LiveOverlaySession.SubtitleIdleTimeoutConfigKey, seconds);
        }
    }
}
