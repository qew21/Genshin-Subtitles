namespace GI_Subtitles.Core.Overlay
{
    public sealed class ExtraPathBody
    {
        public ExtraPathBody(
            OverlayRect display,
            string header,
            string content,
            bool visible,
            int recognitionOrder,
            bool followsVoicePrimary = false)
        {
            Display = display ?? OverlayRect.Invalid;
            Header = header ?? string.Empty;
            Content = content ?? string.Empty;
            Visible = visible;
            RecognitionOrder = recognitionOrder;
            FollowsVoicePrimary = followsVoicePrimary;
        }

        public OverlayRect Display { get; }

        public string Header { get; }

        public string Content { get; }

        public bool Visible { get; }

        public int RecognitionOrder { get; }

        public bool FollowsVoicePrimary { get; }
    }
}
