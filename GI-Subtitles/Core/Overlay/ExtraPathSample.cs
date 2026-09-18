namespace GI_Subtitles.Core.Overlay
{
    public sealed class ExtraPathSample
    {
        public static ExtraPathSample None { get; } = new ExtraPathSample();

        public bool DarkScreenObserved { get; private set; }

        public bool DarkScreenIsDark { get; private set; }

        public bool DarkScreenHasCandidate { get; private set; }

        public OverlayRect DarkScreenBand { get; private set; }

        public bool DarkScreenNeedsOcr { get; private set; }

        public bool DialogueOptionsNeedOcr { get; private set; }

        public bool DialogueOptionsClosed { get; private set; }

        public bool DialogueChoiceSelected { get; private set; }

        public string DialogueChoiceContent { get; private set; }

        public static ExtraPathSample DarkScreenCandidate(OverlayRect band, bool needsOcr)
        {
            return new ExtraPathSample
            {
                DarkScreenObserved = true,
                DarkScreenIsDark = true,
                DarkScreenHasCandidate = true,
                DarkScreenBand = band ?? OverlayRect.Invalid,
                DarkScreenNeedsOcr = needsOcr
            };
        }

        public static ExtraPathSample DarkScreenEnded()
        {
            return new ExtraPathSample
            {
                DarkScreenObserved = true,
                DarkScreenIsDark = false
            };
        }

        public static ExtraPathSample DarkScreenWithoutCandidate()
        {
            return new ExtraPathSample
            {
                DarkScreenObserved = true,
                DarkScreenIsDark = true,
                DarkScreenHasCandidate = false
            };
        }

        public static ExtraPathSample DialogueOptionsReady()
        {
            return new ExtraPathSample
            {
                DialogueOptionsNeedOcr = true
            };
        }

        public static ExtraPathSample DialogueOptionsEnded()
        {
            return new ExtraPathSample
            {
                DialogueOptionsClosed = true
            };
        }

        public static ExtraPathSample DialogueChoice(string content)
        {
            return new ExtraPathSample
            {
                DialogueChoiceSelected = true,
                DialogueChoiceContent = content ?? string.Empty
            };
        }

        public ExtraPathSample WithDialogueOptionsReady()
        {
            ExtraPathSample copy = Copy();
            copy.DialogueOptionsNeedOcr = true;
            return copy;
        }

        public ExtraPathSample WithDialogueOptionsEnded()
        {
            ExtraPathSample copy = Copy();
            copy.DialogueOptionsClosed = true;
            return copy;
        }

        public ExtraPathSample WithDialogueChoice(string content)
        {
            ExtraPathSample copy = Copy();
            copy.DialogueChoiceSelected = true;
            copy.DialogueChoiceContent = content ?? string.Empty;
            return copy;
        }

        private ExtraPathSample Copy()
        {
            return new ExtraPathSample
            {
                DarkScreenObserved = DarkScreenObserved,
                DarkScreenIsDark = DarkScreenIsDark,
                DarkScreenHasCandidate = DarkScreenHasCandidate,
                DarkScreenBand = DarkScreenBand,
                DarkScreenNeedsOcr = DarkScreenNeedsOcr,
                DialogueOptionsNeedOcr = DialogueOptionsNeedOcr,
                DialogueOptionsClosed = DialogueOptionsClosed,
                DialogueChoiceSelected = DialogueChoiceSelected,
                DialogueChoiceContent = DialogueChoiceContent
            };
        }
    }
}
