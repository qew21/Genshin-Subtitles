using System;
using System.Collections.Generic;
using GI_Subtitles.Common;
using GI_Subtitles.Core.Config;
using Newtonsoft.Json.Linq;

namespace GI_Subtitles.Core.Overlay
{
    public static class OverlayLayoutPersistence
    {
        public const string LayoutsConfigKey = "OverlayLayouts";
        public const string MigrationVersionConfigKey = "OverlayLayoutMigrationVersion";
        private const int CurrentMigrationVersion = 1;

        public static bool TryMigrate(IConfigMap config, string selectedGame)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (!config.Contains(LayoutsConfigKey))
            {
                var captured = CaptureGlobal(config);
                var layouts = new Dictionary<string, OverlayLayoutRecord>(StringComparer.Ordinal)
                {
                    [NormalizeGame(selectedGame)] = captured
                };

                // Keep the legacy keys as a recoverable backup. The migration
                // version lets us make one repair attempt for layouts created by
                // the first implementation without resurrecting a deliberate
                // deletion on every subsequent launch.
                config.Set(LayoutsConfigKey, layouts);
                config.Set(MigrationVersionConfigKey, CurrentMigrationVersion);
                Logger.Log.Info(
                    "[OverlayLayout] migrated global settings: game="
                    + NormalizeGame(selectedGame)
                    + " pairs=" + captured.RegionPairs.Count
                    + " legacyPrimary=" + IsValidLegacyRegion(captured.Legacy?.Region)
                    + " legacySecond=" + IsValidLegacyRegion(captured.Legacy?.Region2));
                return true;
            }

            if (config.Get(MigrationVersionConfigKey, 0) >= CurrentMigrationVersion)
            {
                return false;
            }

            var existingLayouts = config.Get<Dictionary<string, OverlayLayoutRecord>>(
                LayoutsConfigKey,
                null) ?? new Dictionary<string, OverlayLayoutRecord>(StringComparer.Ordinal);
            string game = NormalizeGame(selectedGame);
            existingLayouts.TryGetValue(game, out OverlayLayoutRecord existing);
            OverlayLayoutRecord globalBackup = CaptureGlobal(config);
            bool recovered = existing != null
                && !HasMeaningfulLayout(existing)
                && HasMeaningfulLayout(globalBackup);
            if (recovered)
            {
                existingLayouts[game] = globalBackup;
                config.Set(LayoutsConfigKey, existingLayouts);
                Logger.Log.Warn(
                    "[OverlayLayout] recovered an empty legacy layout: game="
                    + game + " pairs=" + globalBackup.RegionPairs.Count);
            }

            config.Set(MigrationVersionConfigKey, CurrentMigrationVersion);
            return recovered;
        }

        public static OverlayLayoutRecord Read(IConfigMap config, string gameName)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Dictionary<string, OverlayLayoutRecord> layouts =
                config.Get<Dictionary<string, OverlayLayoutRecord>>(LayoutsConfigKey, null);
            string game = NormalizeGame(gameName);
            if (layouts != null && layouts.TryGetValue(game, out OverlayLayoutRecord layout) && layout != null)
            {
                return layout;
            }

            return Unconfigured();
        }

        public static void Write(IConfigMap config, string gameName, OverlayLayoutRecord layout)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Dictionary<string, OverlayLayoutRecord> layouts =
                config.Get<Dictionary<string, OverlayLayoutRecord>>(LayoutsConfigKey, null)
                ?? new Dictionary<string, OverlayLayoutRecord>(StringComparer.Ordinal);
            layouts[NormalizeGame(gameName)] = layout ?? Unconfigured();
            config.Set(LayoutsConfigKey, layouts);
        }

        public static OverlayLayoutRecord Unconfigured()
        {
            return new OverlayLayoutRecord
            {
                RegionPairs = new List<RegionPairRecord>(),
                RecognizeDarkScreenSubtitles = true,
                RecognizeDialogueOptions = false,
                DarkScreenDisplay = OverlayRect.Invalid,
                DialogueOptionDisplay = OverlayRect.Invalid,
                Legacy = new LegacyRegionSlots
                {
                    Region = string.Empty,
                    Region2 = string.Empty
                }
            };
        }

        public static string NormalizeGame(string gameName)
        {
            return string.IsNullOrWhiteSpace(gameName) ? "Genshin" : gameName;
        }

        private static OverlayLayoutRecord CaptureGlobal(IConfigMap config)
        {
            ReadPad(config, out int padVertical, out int padHorizontal);
            return new OverlayLayoutRecord
            {
                RegionPairs = config.Get<List<RegionPairRecord>>(ConfigRegionPairStore.PairsConfigKey, null)
                    ?? new List<RegionPairRecord>(),
                VoicePrimaryId = config.Get(ConfigRegionPairStore.VoicePrimaryIdConfigKey, 0),
                NextPairId = config.Get(ConfigRegionPairStore.NextPairIdConfigKey, 0),
                DarkScreenDisplay = config.Get<OverlayRect>(ConfigRegionPairStore.DarkScreenDisplayConfigKey, null)
                    ?? OverlayRect.Invalid,
                DialogueOptionDisplay = config.Get<OverlayRect>(ConfigRegionPairStore.DialogueOptionDisplayConfigKey, null)
                    ?? OverlayRect.Invalid,
                RecognizeDarkScreenSubtitles = config.Get(ConfigRegionPairStore.DarkScreenScanConfigKey, true),
                RecognizeDialogueOptions = config.Get(ConfigRegionPairStore.DialogueOptionScanConfigKey, false),
                Legacy = new LegacyRegionSlots
                {
                    Region = config.Get("Region", string.Empty),
                    Region2 = config.Get("Region2", string.Empty),
                    PadVertical = padVertical,
                    PadHorizontal = padHorizontal
                }
            };
        }

        private static bool HasMeaningfulLayout(OverlayLayoutRecord layout)
        {
            if (layout == null)
            {
                return false;
            }

            return (layout.RegionPairs != null && layout.RegionPairs.Count > 0)
                || layout.VoicePrimaryId > 0
                || layout.NextPairId > 0
                || (layout.DarkScreenDisplay != null && layout.DarkScreenDisplay.IsValid)
                || (layout.DialogueOptionDisplay != null && layout.DialogueOptionDisplay.IsValid)
                || IsValidLegacyRegion(layout.Legacy?.Region)
                || IsValidLegacyRegion(layout.Legacy?.Region2);
        }

        private static bool IsValidLegacyRegion(string csv)
        {
            return OverlayRect.TryParse(csv, out _);
        }

        private static void ReadPad(IConfigMap config, out int vertical, out int horizontal)
        {
            vertical = 0;
            horizontal = 0;
            JToken token = config.Get<JToken>("Pad", null);
            if (token == null)
            {
                return;
            }

            if (token.Type == JTokenType.Array)
            {
                int[] padArray = token.ToObject<int[]>();
                if (padArray == null)
                {
                    return;
                }

                if (padArray.Length > 0)
                {
                    vertical = padArray[0];
                }

                if (padArray.Length > 1)
                {
                    horizontal = padArray[1];
                }

                return;
            }

            vertical = token.ToObject<int>();
        }
    }
}
