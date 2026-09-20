using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Config;
using AppConfig = GI_Subtitles.Core.Config.Config;

namespace GI_Subtitles.Core.Overlay
{
    public sealed class ConfigRegionPairStore : IRegionPairStore
    {
        public const string PairsConfigKey = "RegionPairs";
        public const string VoicePrimaryIdConfigKey = "VoicePrimaryId";
        public const string NextPairIdConfigKey = "NextPairId";
        public const string DarkScreenDisplayConfigKey = "DarkScreenDisplay";
        public const string DialogueOptionDisplayConfigKey = "DialogueOptionDisplay";
        public const string DarkScreenScanConfigKey = "RecognizeDarkScreenSubtitles";
        public const string DialogueOptionScanConfigKey = "RecognizeDialogueOptions";

        private readonly IConfigMap _config;
        private string _gameName;

        public ConfigRegionPairStore()
            : this(new AppConfigMap(), AppConfig.Get("Game", "Genshin"))
        {
        }

        public ConfigRegionPairStore(IConfigMap config, string gameName)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            _config = config;
            _gameName = OverlayLayoutPersistence.NormalizeGame(gameName);
            OverlayLayoutPersistence.TryMigrate(_config, _gameName);
        }

        public IReadOnlyList<RegionPairRecord> ReadPairs()
        {
            List<RegionPairRecord> stored = ReadLayout().RegionPairs;
            if (stored == null)
            {
                return Array.Empty<RegionPairRecord>();
            }

            return stored;
        }

        public LegacyRegionSlots ReadLegacy()
        {
            LegacyRegionSlots legacy = ReadLayout().Legacy;
            if (legacy == null)
            {
                return new LegacyRegionSlots
                {
                    Region = string.Empty,
                    Region2 = string.Empty
                };
            }

            return new LegacyRegionSlots
            {
                Region = legacy.Region ?? string.Empty,
                Region2 = legacy.Region2 ?? string.Empty,
                PadVertical = legacy.PadVertical,
                PadHorizontal = legacy.PadHorizontal
            };
        }

        public void WritePairs(IReadOnlyList<RegionPairRecord> pairs)
        {
            OverlayLayoutRecord layout = ReadLayout();
            layout.RegionPairs = pairs == null
                ? new List<RegionPairRecord>()
                : new List<RegionPairRecord>(pairs);
            RegionAdjustTrace.StoreWrite(
                "pairs",
                "count=" + layout.RegionPairs.Count);
            WriteLayout(layout);
        }

        public int ReadVoicePrimaryId()
        {
            return ReadLayout().VoicePrimaryId;
        }

        public void WriteVoicePrimaryId(int id)
        {
            OverlayLayoutRecord layout = ReadLayout();
            layout.VoicePrimaryId = id;
            WriteLayout(layout);
        }

        public int ReadNextPairId()
        {
            return ReadLayout().NextPairId;
        }

        public void WriteNextPairId(int id)
        {
            OverlayLayoutRecord layout = ReadLayout();
            layout.NextPairId = id;
            WriteLayout(layout);
        }

        public OverlayRect ReadDarkScreenDisplay()
        {
            return ReadLayout().DarkScreenDisplay ?? OverlayRect.Invalid;
        }

        public void WriteDarkScreenDisplay(OverlayRect display)
        {
            OverlayLayoutRecord layout = ReadLayout();
            layout.DarkScreenDisplay = display ?? OverlayRect.Invalid;
            RegionAdjustTrace.StoreWrite(
                "darkScreenDisplay",
                "rect=" + layout.DarkScreenDisplay.ToCsv());
            WriteLayout(layout);
        }

        public OverlayRect ReadDialogueOptionDisplay()
        {
            if (!AllowsDialogueOptions)
            {
                return OverlayRect.Invalid;
            }

            return ReadLayout().DialogueOptionDisplay ?? OverlayRect.Invalid;
        }

        public void WriteDialogueOptionDisplay(OverlayRect display)
        {
            OverlayLayoutRecord layout = ReadLayout();
            layout.DialogueOptionDisplay = display ?? OverlayRect.Invalid;
            RegionAdjustTrace.StoreWrite(
                "dialogueOptionDisplay",
                "rect=" + layout.DialogueOptionDisplay.ToCsv());
            WriteLayout(layout);
        }

        public bool ReadDarkScreenScan()
        {
            return ReadLayout().RecognizeDarkScreenSubtitles;
        }

        public void WriteDarkScreenScan(bool enabled)
        {
            OverlayLayoutRecord layout = ReadLayout();
            layout.RecognizeDarkScreenSubtitles = enabled;
            WriteLayout(layout);
        }

        public bool ReadDialogueOptionScan()
        {
            if (!AllowsDialogueOptions)
            {
                return false;
            }

            return ReadLayout().RecognizeDialogueOptions;
        }

        public void WriteDialogueOptionScan(bool enabled)
        {
            OverlayLayoutRecord layout = ReadLayout();
            layout.RecognizeDialogueOptions = enabled;
            WriteLayout(layout);
        }

        public void SwitchGame(string gameName)
        {
            _gameName = OverlayLayoutPersistence.NormalizeGame(gameName);
        }

        private bool AllowsDialogueOptions
        {
            get { return string.Equals(_gameName, "Genshin", StringComparison.Ordinal); }
        }

        private OverlayLayoutRecord ReadLayout()
        {
            return OverlayLayoutPersistence.Read(_config, _gameName);
        }

        private void WriteLayout(OverlayLayoutRecord layout)
        {
            OverlayLayoutPersistence.Write(_config, _gameName, layout);
        }
    }
}

