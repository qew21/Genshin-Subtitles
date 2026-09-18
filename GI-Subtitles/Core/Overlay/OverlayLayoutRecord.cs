using System.Collections.Generic;

namespace GI_Subtitles.Core.Overlay
{
    public sealed class OverlayLayoutRecord
    {
        public List<RegionPairRecord> RegionPairs { get; set; }

        public int VoicePrimaryId { get; set; }

        public int NextPairId { get; set; }

        public OverlayRect DarkScreenDisplay { get; set; }

        public OverlayRect DialogueOptionDisplay { get; set; }

        public bool RecognizeDarkScreenSubtitles { get; set; } = true;

        public bool RecognizeDialogueOptions { get; set; }

        public LegacyRegionSlots Legacy { get; set; }
    }
}
