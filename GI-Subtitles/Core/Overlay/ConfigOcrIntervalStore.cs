using AppConfig = GI_Subtitles.Core.Config.Config;

namespace GI_Subtitles.Core.Overlay
{
    public sealed class ConfigOcrIntervalStore : IOcrIntervalStore
    {
        public int Read(int defaultValue)
        {
            return AppConfig.Get(LiveOverlaySession.OcrIntervalConfigKey, defaultValue);
        }

        public void Write(int milliseconds)
        {
            AppConfig.Set(LiveOverlaySession.OcrIntervalConfigKey, milliseconds);
        }
    }
}
