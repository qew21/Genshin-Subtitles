namespace GI_Subtitles.Core.Overlay
{
    public interface IOcrIntervalStore
    {
        int Read(int defaultValue);
        void Write(int milliseconds);
    }
}
