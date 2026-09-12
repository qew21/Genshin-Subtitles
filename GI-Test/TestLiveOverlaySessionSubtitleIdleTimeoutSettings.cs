using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Config;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace GI_Test
{
    /// <summary>
    /// Settings seam for subtitle idle timeout: Config persistence (0–60) and live
    /// session apply through the OCR-interval-style settings view.
    /// </summary>
    [TestClass]
    public class TestLiveOverlaySessionSubtitleIdleTimeoutSettings
    {
        [TestMethod]
        public void InRangeEdit_AppliesToConfigAndSessionImmediately()
        {
            var store = new MemorySubtitleIdleTimeoutStore { Stored = 0 };
            var session = new LiveOverlaySession(
                new MemoryOcrIntervalStore(),
                idleTimeoutStore: store);

            var view = session.OpenSubtitleIdleTimeoutSettings();
            Assert.AreEqual("0", view.BoxText);

            view.BoxText = "5";
            view.Commit();

            Assert.AreEqual("5", view.BoxText);
            Assert.AreEqual(5, store.Stored);
            Assert.AreEqual(1, store.WriteCount);
            Assert.AreEqual(5, session.SubtitleIdleTimeoutSeconds);
        }

        [TestMethod]
        public void MissingConfig_OpensAtDefaultWithoutWriting()
        {
            var store = new MemorySubtitleIdleTimeoutStore();
            var session = new LiveOverlaySession(
                new MemoryOcrIntervalStore(),
                idleTimeoutStore: store);

            var view = session.OpenSubtitleIdleTimeoutSettings();

            Assert.AreEqual("0", view.BoxText);
            Assert.IsNull(store.Stored);
            Assert.AreEqual(0, store.WriteCount);
            Assert.AreEqual(0, session.SubtitleIdleTimeoutSeconds);
        }

        [TestMethod]
        public void OutOfRangeEdit_ClampsToZeroThroughSixty()
        {
            var store = new MemorySubtitleIdleTimeoutStore { Stored = 5 };
            var session = new LiveOverlaySession(
                new MemoryOcrIntervalStore(),
                idleTimeoutStore: store);
            var view = session.OpenSubtitleIdleTimeoutSettings();

            view.BoxText = "-3";
            view.Commit();
            Assert.AreEqual("0", view.BoxText);
            Assert.AreEqual(0, store.Stored);
            Assert.AreEqual(0, session.SubtitleIdleTimeoutSeconds);

            view.BoxText = "90";
            view.Commit();
            Assert.AreEqual("60", view.BoxText);
            Assert.AreEqual(60, store.Stored);
            Assert.AreEqual(60, session.SubtitleIdleTimeoutSeconds);
        }

        [TestMethod]
        public void UnparseableEdit_RevertsToValueBeforeTheEdit()
        {
            var store = new MemorySubtitleIdleTimeoutStore { Stored = 8 };
            var session = new LiveOverlaySession(
                new MemoryOcrIntervalStore(),
                idleTimeoutStore: store);
            var view = session.OpenSubtitleIdleTimeoutSettings();

            view.BoxText = "abc";
            view.Commit();

            Assert.AreEqual("8", view.BoxText);
            Assert.AreEqual(8, store.Stored);
            Assert.AreEqual(0, store.WriteCount);
            Assert.AreEqual(8, session.SubtitleIdleTimeoutSeconds);
        }

        [TestMethod]
        public void ConfigStore_PersistsAcrossReload()
        {
            var config = new MemoryConfigMap();
            var store = new ConfigSubtitleIdleTimeoutStore(config);
            Assert.AreEqual(0, store.Read(LiveOverlaySession.DefaultSubtitleIdleTimeoutSeconds));

            store.Write(12);
            Assert.AreEqual(12, config.Get(LiveOverlaySession.SubtitleIdleTimeoutConfigKey, -1));

            var reloaded = new ConfigSubtitleIdleTimeoutStore(config);
            Assert.AreEqual(12, reloaded.Read(LiveOverlaySession.DefaultSubtitleIdleTimeoutSeconds));
        }

        [TestMethod]
        public void LiveCommit_ShorteningTimeoutClearsAlreadyStaleBody()
        {
            DateTime now = new DateTime(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc);
            var idleStore = new MemorySubtitleIdleTimeoutStore { Stored = 10 };
            var records = new List<RegionPairRecord>
            {
                new RegionPairRecord
                {
                    Id = 1,
                    Capture = new OverlayRect(0, 10, 80, 20),
                    Display = new OverlayRect(0, 40, 80, 20)
                }
            };
            var session = new LiveOverlaySession(
                new MemoryOcrIntervalStore(),
                new MemoryRegionPairStore
                {
                    StoredPairs = records,
                    VoicePrimaryId = 1,
                    NextPairId = 2
                },
                () => now,
                appliedGame: null,
                idleTimeoutStore: idleStore);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "stale", ocrText: "stale", original: "S");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();
            Assert.AreEqual("stale", session.PairBodies[0].Content);

            now = now.AddSeconds(5);
            var view = session.OpenSubtitleIdleTimeoutSettings();
            view.BoxText = "3";
            view.Commit();

            Assert.AreEqual(3, session.SubtitleIdleTimeoutSeconds);
            Assert.AreEqual(3, idleStore.Stored);
            Assert.AreEqual(
                string.Empty,
                session.PairBodies[0].Content,
                "Committing a shorter timeout recalculates deadlines on the applying Tick.");
        }

        private sealed class MemoryOcrIntervalStore : IOcrIntervalStore
        {
            public int Read(int defaultValue)
            {
                return defaultValue;
            }

            public void Write(int milliseconds)
            {
            }
        }

        private sealed class MemorySubtitleIdleTimeoutStore : ISubtitleIdleTimeoutStore
        {
            public int? Stored;
            public int WriteCount;

            public int Read(int defaultValue)
            {
                return Stored ?? defaultValue;
            }

            public void Write(int seconds)
            {
                Stored = seconds;
                WriteCount++;
            }
        }

        private sealed class MemoryRegionPairStore : IRegionPairStore
        {
            public LegacyRegionSlots Legacy = new LegacyRegionSlots();
            public List<RegionPairRecord> StoredPairs = new List<RegionPairRecord>();
            public int VoicePrimaryId;
            public int NextPairId = 1;
            public OverlayRect DarkScreenDisplay = OverlayRect.Invalid;
            public OverlayRect DialogueOptionDisplay = OverlayRect.Invalid;
            public bool DarkScreenScan = true;
            public bool DialogueOptionScan;

            public IReadOnlyList<RegionPairRecord> ReadPairs()
            {
                return StoredPairs;
            }

            public LegacyRegionSlots ReadLegacy()
            {
                return Legacy;
            }

            public void WritePairs(IReadOnlyList<RegionPairRecord> pairs)
            {
                StoredPairs = new List<RegionPairRecord>(pairs);
            }

            public int ReadVoicePrimaryId()
            {
                return VoicePrimaryId;
            }

            public void WriteVoicePrimaryId(int id)
            {
                VoicePrimaryId = id;
            }

            public int ReadNextPairId()
            {
                return NextPairId;
            }

            public void WriteNextPairId(int id)
            {
                NextPairId = id;
            }

            public OverlayRect ReadDarkScreenDisplay()
            {
                return DarkScreenDisplay ?? OverlayRect.Invalid;
            }

            public void WriteDarkScreenDisplay(OverlayRect display)
            {
                DarkScreenDisplay = display ?? OverlayRect.Invalid;
            }

            public OverlayRect ReadDialogueOptionDisplay()
            {
                return DialogueOptionDisplay ?? OverlayRect.Invalid;
            }

            public void WriteDialogueOptionDisplay(OverlayRect display)
            {
                DialogueOptionDisplay = display ?? OverlayRect.Invalid;
            }

            public bool ReadDarkScreenScan()
            {
                return DarkScreenScan;
            }

            public void WriteDarkScreenScan(bool enabled)
            {
                DarkScreenScan = enabled;
            }

            public bool ReadDialogueOptionScan()
            {
                return DialogueOptionScan;
            }

            public void WriteDialogueOptionScan(bool enabled)
            {
                DialogueOptionScan = enabled;
            }

            public void SwitchGame(string gameName)
            {
            }
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
