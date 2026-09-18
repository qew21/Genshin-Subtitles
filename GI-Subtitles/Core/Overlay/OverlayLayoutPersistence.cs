using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Config;
using Newtonsoft.Json.Linq;

namespace GI_Subtitles.Core.Overlay
{
    public static class OverlayLayoutPersistence
    {
        public const string LayoutsConfigKey = "OverlayLayouts";

        public static bool TryMigrate(IConfigMap config, string selectedGame)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.Contains(LayoutsConfigKey))
            {
                return false;
            }

            var layouts = new Dictionary<string, OverlayLayoutRecord>(StringComparer.Ordinal)
            {
                [NormalizeGame(selectedGame)] = CaptureGlobal(config)
            };

            // Set the new root in one atomic Config.Save operation. Keep the legacy
            // keys as a recoverable backup; they are ignored once OverlayLayouts exists
            // and can be removed by a later cleanup migration after the new layout has
            // been verified in production.
            config.Set(LayoutsConfigKey, layouts);
            return true;
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
