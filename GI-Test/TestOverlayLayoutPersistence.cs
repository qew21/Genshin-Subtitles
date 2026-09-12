using System.Collections.Generic;
using GI_Subtitles.Core.Config;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace GI_Test
{
    [TestClass]
    public class TestOverlayLayoutPersistence
    {
        [TestMethod]
        public void GlobalBlob_MigratesOntoSelectedGame_OtherGamesStartEmpty()
        {
            var settings = new MemoryConfigMap();
            var pair = new RegionPairRecord
            {
                Id = 1,
                Capture = new OverlayRect(10, 20, 30, 40),
                Display = new OverlayRect(11, 21, 30, 40)
            };
            settings.Set(ConfigRegionPairStore.PairsConfigKey, new List<RegionPairRecord> { pair });
            settings.Set(ConfigRegionPairStore.VoicePrimaryIdConfigKey, 1);
            settings.Set(ConfigRegionPairStore.NextPairIdConfigKey, 2);
            settings.Set(ConfigRegionPairStore.DarkScreenDisplayConfigKey, new OverlayRect(5, 6, 70, 18));
            settings.Set(ConfigRegionPairStore.DialogueOptionDisplayConfigKey, new OverlayRect(8, 9, 120, 30));
            settings.Set("RecognizeDarkScreenSubtitles", false);
            settings.Set("RecognizeDialogueOptions", true);

            var genshin = new ConfigRegionPairStore(settings, "Genshin");
            IReadOnlyList<RegionPairRecord> genshinPairs = genshin.ReadPairs();
            Assert.AreEqual(1, genshinPairs.Count);
            Assert.AreEqual(10, genshinPairs[0].Capture.X);
            Assert.AreEqual(1, genshin.ReadVoicePrimaryId());
            Assert.AreEqual(2, genshin.ReadNextPairId());
            Assert.AreEqual(5, genshin.ReadDarkScreenDisplay().X);
            Assert.AreEqual(8, genshin.ReadDialogueOptionDisplay().X);
            Assert.IsFalse(genshin.ReadDarkScreenScan());
            Assert.IsTrue(genshin.ReadDialogueOptionScan());

            var starRail = new ConfigRegionPairStore(settings, "StarRail");
            Assert.AreEqual(0, starRail.ReadPairs().Count);
            Assert.AreEqual(0, starRail.ReadVoicePrimaryId());
            Assert.IsFalse(starRail.ReadDarkScreenDisplay().IsValid);
            Assert.IsFalse(starRail.ReadDialogueOptionDisplay().IsValid);
            Assert.IsTrue(starRail.ReadDarkScreenScan());
            Assert.IsFalse(starRail.ReadDialogueOptionScan());
        }

        [TestMethod]
        public void Migration_DoesNotRunAgain_WhenLayoutsAlreadyExist()
        {
            var settings = new MemoryConfigMap();
            settings.Set(ConfigRegionPairStore.PairsConfigKey, new List<RegionPairRecord>
            {
                new RegionPairRecord
                {
                    Id = 1,
                    Capture = new OverlayRect(10, 20, 30, 40),
                    Display = new OverlayRect(11, 21, 30, 40)
                }
            });

            var first = new ConfigRegionPairStore(settings, "Genshin");
            Assert.AreEqual(1, first.ReadPairs().Count);

            settings.Set(ConfigRegionPairStore.PairsConfigKey, new List<RegionPairRecord>
            {
                new RegionPairRecord
                {
                    Id = 9,
                    Capture = new OverlayRect(1, 2, 3, 4),
                    Display = OverlayRect.Invalid
                }
            });

            var second = new ConfigRegionPairStore(settings, "Genshin");
            IReadOnlyList<RegionPairRecord> pairs = second.ReadPairs();
            Assert.AreEqual(1, pairs.Count);
            Assert.AreEqual(10, pairs[0].Capture.X);
            Assert.AreEqual(1, pairs[0].Id);
        }

        [TestMethod]
        public void BoxingOnGameA_ThenRestartOnB_LeavesBEmpty_AndRestoresA()
        {
            var settings = new MemoryConfigMap();
            var gameA = new ConfigRegionPairStore(settings, "Genshin");
            gameA.WritePairs(new[]
            {
                new RegionPairRecord
                {
                    Id = 1,
                    Capture = new OverlayRect(10, 20, 30, 40),
                    Display = new OverlayRect(11, 50, 30, 40)
                }
            });
            gameA.WriteVoicePrimaryId(1);
            gameA.WriteNextPairId(2);
            gameA.WriteDarkScreenDisplay(new OverlayRect(5, 6, 70, 18));
            gameA.WriteDialogueOptionDisplay(new OverlayRect(8, 9, 120, 30));
            gameA.WriteDarkScreenScan(false);
            gameA.WriteDialogueOptionScan(true);

            var gameB = new ConfigRegionPairStore(settings, "StarRail");
            Assert.AreEqual(0, gameB.ReadPairs().Count);
            Assert.IsFalse(gameB.ReadDarkScreenDisplay().IsValid);
            Assert.IsFalse(gameB.ReadDialogueOptionDisplay().IsValid);
            Assert.IsTrue(gameB.ReadDarkScreenScan());
            Assert.IsFalse(gameB.ReadDialogueOptionScan());

            var restoredA = new ConfigRegionPairStore(settings, "Genshin");
            Assert.AreEqual(1, restoredA.ReadPairs().Count);
            Assert.AreEqual(10, restoredA.ReadPairs()[0].Capture.X);
            Assert.AreEqual(1, restoredA.ReadVoicePrimaryId());
            Assert.AreEqual(5, restoredA.ReadDarkScreenDisplay().X);
            Assert.AreEqual(8, restoredA.ReadDialogueOptionDisplay().X);
            Assert.IsFalse(restoredA.ReadDarkScreenScan());
            Assert.IsTrue(restoredA.ReadDialogueOptionScan());
        }

        [TestMethod]
        public void CustomGameName_GetsItsOwnOverlayLayout()
        {
            var settings = new MemoryConfigMap();
            var custom = new ConfigRegionPairStore(settings, "MyPack");
            custom.WritePairs(new[]
            {
                new RegionPairRecord
                {
                    Id = 1,
                    Capture = new OverlayRect(40, 50, 60, 20),
                    Display = new OverlayRect(40, 80, 60, 20)
                }
            });
            custom.WriteVoicePrimaryId(1);

            var builtIn = new ConfigRegionPairStore(settings, "Genshin");
            Assert.AreEqual(0, builtIn.ReadPairs().Count);

            var reloaded = new ConfigRegionPairStore(settings, "MyPack");
            Assert.AreEqual(1, reloaded.ReadPairs().Count);
            Assert.AreEqual(40, reloaded.ReadPairs()[0].Capture.X);
            Assert.AreEqual(1, reloaded.ReadVoicePrimaryId());
        }

        [TestMethod]
        public void UnconfiguredGame_DarkScreenScanDefaultsOn_DisplaysUnset()
        {
            var settings = new MemoryConfigMap();
            var store = new ConfigRegionPairStore(settings, "Zenless");
            Assert.AreEqual(0, store.ReadPairs().Count);
            Assert.IsTrue(store.ReadDarkScreenScan());
            Assert.IsFalse(store.ReadDialogueOptionScan());
            Assert.IsFalse(store.ReadDarkScreenDisplay().IsValid);
            Assert.IsFalse(store.ReadDialogueOptionDisplay().IsValid);
        }

        [TestMethod]
        public void Migration_LeavesGlobalPreferencesInPlace()
        {
            var settings = new MemoryConfigMap();
            settings.Set(ConfigRegionPairStore.PairsConfigKey, new List<RegionPairRecord>
            {
                new RegionPairRecord
                {
                    Id = 1,
                    Capture = new OverlayRect(10, 20, 30, 40),
                    Display = OverlayRect.Invalid
                }
            });
            settings.Set(LiveOverlaySession.OcrIntervalConfigKey, 400);
            settings.Set("Size", 20);
            settings.Set("PlayVoice", true);
            settings.Set("UILang", "zh-CN");
            settings.Set("Input", "CHS");
            settings.Set("Output", "EN");

            var store = new ConfigRegionPairStore(settings, "Genshin");
            Assert.AreEqual(1, store.ReadPairs().Count);
            Assert.AreEqual(400, settings.Get(LiveOverlaySession.OcrIntervalConfigKey, 0));
            Assert.AreEqual(20, settings.Get("Size", 0));
            Assert.IsTrue(settings.Get("PlayVoice", false));
            Assert.AreEqual("zh-CN", settings.Get("UILang", string.Empty));
            Assert.AreEqual("CHS", settings.Get("Input", string.Empty));
            Assert.AreEqual("EN", settings.Get("Output", string.Empty));
            Assert.IsFalse(settings.Contains(ConfigRegionPairStore.PairsConfigKey));
        }

        [TestMethod]
        public void LegacyCapture_MigratesOntoSelectedGameOnly()
        {
            var settings = new MemoryConfigMap();
            settings.Set("Region", "100,200,800,80");
            settings.Set("Region2", "50,60,400,50");
            settings.Set("Pad", new[] { 86, 10 });

            var genshin = new ConfigRegionPairStore(settings, "Genshin");
            LegacyRegionSlots genshinLegacy = genshin.ReadLegacy();
            Assert.AreEqual("100,200,800,80", genshinLegacy.Region);
            Assert.AreEqual("50,60,400,50", genshinLegacy.Region2);
            Assert.AreEqual(86, genshinLegacy.PadVertical);
            Assert.AreEqual(10, genshinLegacy.PadHorizontal);

            var starRail = new ConfigRegionPairStore(settings, "StarRail");
            LegacyRegionSlots starRailLegacy = starRail.ReadLegacy();
            Assert.AreEqual(string.Empty, starRailLegacy.Region);
            Assert.AreEqual(string.Empty, starRailLegacy.Region2);
            Assert.AreEqual(0, starRailLegacy.PadVertical);
            Assert.AreEqual(0, starRailLegacy.PadHorizontal);
        }

        [TestMethod]
        public void NonGenshinGame_IgnoresDialogueOptionDisplayAndScan()
        {
            var settings = new MemoryConfigMap();
            settings.Set(ConfigRegionPairStore.DialogueOptionDisplayConfigKey, new OverlayRect(8, 9, 120, 30));
            settings.Set(ConfigRegionPairStore.DialogueOptionScanConfigKey, true);

            var starRail = new ConfigRegionPairStore(settings, "StarRail");
            Assert.IsFalse(starRail.ReadDialogueOptionDisplay().IsValid);
            Assert.IsFalse(starRail.ReadDialogueOptionScan());
        }

        private sealed class MemoryConfigMap : IConfigMap
        {
            private readonly Dictionary<string, JToken> _settings = new Dictionary<string, JToken>();

            public bool Contains(string key)
            {
                return _settings.ContainsKey(key);
            }

            public T Get<T>(string key, T defaultValue)
            {
                if (_settings.TryGetValue(key, out JToken token))
                {
                    try
                    {
                        return token.ToObject<T>();
                    }
                    catch
                    {
                    }
                }

                return defaultValue;
            }

            public void Set<T>(string key, T value)
            {
                _settings[key] = value == null ? JValue.CreateNull() : JToken.FromObject(value);
            }

            public void Remove(string key)
            {
                _settings.Remove(key);
            }
        }
    }
}
