namespace GI_Subtitles.Core.Overlay
{
    public sealed class RegionPair
    {
        public RegionPair(int id, OverlayRect capture, OverlayRect display)
        {
            Id = id;
            Capture = capture ?? OverlayRect.Invalid;
            Display = display ?? OverlayRect.Invalid;
        }

        public int Id { get; }

        public OverlayRect Capture { get; }

        public OverlayRect Display { get; }
    }
}
