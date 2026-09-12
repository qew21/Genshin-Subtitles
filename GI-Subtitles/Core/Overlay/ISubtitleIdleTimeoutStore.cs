namespace GI_Subtitles.Core.Overlay
{
    public interface ISubtitleIdleTimeoutStore
    {
        int Read(int defaultValue);

        void Write(int seconds);
    }
}
