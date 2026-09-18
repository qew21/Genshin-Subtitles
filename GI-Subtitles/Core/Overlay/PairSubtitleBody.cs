namespace GI_Subtitles.Core.Overlay
{
    public sealed class PairSubtitleBody
    {
        public PairSubtitleBody(
            int pairIndex,
            OverlayRect display,
            string header,
            string content,
            bool visible,
            int recognitionOrder)
        {
            PairIndex = pairIndex;
            Display = display ?? OverlayRect.Invalid;
            Header = header ?? string.Empty;
            Content = content ?? string.Empty;
            Visible = visible;
            RecognitionOrder = recognitionOrder;
        }

        public int PairIndex { get; }

        public OverlayRect Display { get; }

        public string Header { get; }

        public string Content { get; }

        public bool Visible { get; }

        public int RecognitionOrder { get; }
    }
}
