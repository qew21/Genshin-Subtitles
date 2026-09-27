using System.Collections.Generic;

namespace GI_Subtitles.Core.Overlay
{
    public interface IRegionPairStore
    {
        IReadOnlyList<RegionPairRecord> ReadPairs();

        LegacyRegionSlots ReadLegacy();

        void WritePairs(IReadOnlyList<RegionPairRecord> pairs);

        int ReadVoicePrimaryId();

        void WriteVoicePrimaryId(int id);

        int ReadNextPairId();

        void WriteNextPairId(int id);

        OverlayRect ReadDarkScreenDisplay();

        void WriteDarkScreenDisplay(OverlayRect display);

        OverlayRect ReadDialogueOptionDisplay();

        void WriteDialogueOptionDisplay(OverlayRect display);

        bool ReadDarkScreenScan();

        void WriteDarkScreenScan(bool enabled);

        bool ReadDialogueOptionScan();

        void WriteDialogueOptionScan(bool enabled);

        void SwitchGame(string gameName);
    }

    /// <summary>
    /// Optional persistence hook for the one-time legacy Region2 review notice.
    /// Keeping this separate preserves the lightweight test/store implementations.
    /// </summary>
    public interface ILegacyRegion2ReviewStore
    {
        bool ReadLegacyRegion2ReviewPending();

        void WriteLegacyRegion2ReviewPending(bool pending);
    }

    public sealed class RegionPairRecord
    {
        public int Id { get; set; }

        public OverlayRect Capture { get; set; }

        public OverlayRect Display { get; set; }
    }

    public sealed class LegacyRegionSlots
    {
        public string Region { get; set; }

        public string Region2 { get; set; }

        public int PadVertical { get; set; }

        public int PadHorizontal { get; set; }
    }
}
