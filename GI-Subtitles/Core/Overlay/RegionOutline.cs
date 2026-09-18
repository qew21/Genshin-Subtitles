namespace GI_Subtitles.Core.Overlay
{
    public enum RegionOutlineKind
    {
        Pair = 0,
        DarkScreenDisplay = 1,
        DialogueOptionDisplay = 2,
        DarkScreenCandidate = 3
    }

    public enum OverlayAdjustTarget
    {
        None = 0,
        Pair = 1,
        DarkScreenDisplay = 2,
        DialogueOptionDisplay = 3
    }

    public sealed class RegionOutline
    {
        public RegionOutline(int pairOrdinal, OverlayRect rect, bool isDisplay)
            : this(pairOrdinal, rect, isDisplay, RegionOutlineKind.Pair)
        {
        }

        public RegionOutline(int pairOrdinal, OverlayRect rect, bool isDisplay, RegionOutlineKind kind)
        {
            PairOrdinal = pairOrdinal;
            Rect = rect ?? OverlayRect.Invalid;
            IsDisplay = isDisplay;
            Kind = kind;
        }

        public int PairOrdinal { get; }

        public OverlayRect Rect { get; }

        public bool IsDisplay { get; }

        public RegionOutlineKind Kind { get; }

        public bool Dashed
        {
            get { return Kind == RegionOutlineKind.DarkScreenCandidate; }
        }
    }
}
