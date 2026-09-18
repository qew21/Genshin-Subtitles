namespace GI_Subtitles.Core.Overlay
{
    public sealed class PairFrameSample
    {
        public PairFrameSample(bool changed, bool stable, bool empty)
        {
            Changed = changed;
            Stable = stable;
            Empty = empty;
        }

        public bool Changed { get; }

        public bool Stable { get; }

        public bool Empty { get; }

        public static PairFrameSample ChangedAndStable()
        {
            return new PairFrameSample(true, true, false);
        }

        public static PairFrameSample Unchanged()
        {
            return new PairFrameSample(false, true, false);
        }

        public static PairFrameSample StableNoText()
        {
            return new PairFrameSample(false, true, true);
        }
    }
}
