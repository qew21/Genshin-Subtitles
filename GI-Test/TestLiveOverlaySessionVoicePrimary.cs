using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestLiveOverlaySessionVoicePrimary
    {
        [TestMethod]
        public void StoredPairsWithoutIds_MigrateToStableIdsAndDefaultPrimary()
        {
            var pairs = new MemoryRegionPairStore
            {
                StoredPairs =
                {
                    new RegionPairRecord
                    {
                        Capture = new OverlayRect(10, 20, 30, 40),
                        Display = new OverlayRect(11, 21, 30, 40)
                    },
                    new RegionPairRecord
                    {
                        Capture = new OverlayRect(50, 60, 70, 80),
                        Display = new OverlayRect(51, 61, 70, 80)
                    }
                }
            };

            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);

            Assert.AreEqual(2, session.Pairs.Count);
            Assert.AreEqual(1, session.Pairs[0].Id);
            Assert.AreEqual(2, session.Pairs[1].Id);
            Assert.AreEqual(1, session.VoicePrimaryId);
            Assert.AreEqual(1, pairs.WriteCount);
            Assert.AreEqual(1, pairs.VoicePrimaryId);
            Assert.AreEqual(3, pairs.NextPairId);
            Assert.AreEqual(1, pairs.StoredPairs[0].Id);
            Assert.AreEqual(2, pairs.StoredPairs[1].Id);
        }

        [TestMethod]
        public void EmptyList_HasNoVoicePrimary()
        {
            var pairs = new MemoryRegionPairStore();
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);

            Assert.AreEqual(0, session.Pairs.Count);
            Assert.AreEqual(0, session.VoicePrimaryId);
            Assert.IsFalse(session.TryGetVoicePrimaryCapture(out _, out OverlayRect capture));
            Assert.IsFalse(capture.IsValid);
        }

        [TestMethod]
        public void FirstCommittedAdd_OnEmptyList_BecomesVoicePrimary()
        {
            var pairs = new MemoryRegionPairStore();
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);
            OverlayRect capture = new OverlayRect(10, 20, 30, 40);
            OverlayRect display = new OverlayRect(11, 50, 30, 40);

            Assert.IsTrue(session.TryStartAdd());
            session.SetAddCapture(capture);
            session.SetAddDisplay(display);
            Assert.AreEqual(0, pairs.WriteCount);
            Assert.AreEqual(0, session.Pairs.Count);

            Assert.IsTrue(session.TryCommitAdd());

            Assert.AreEqual(1, session.Pairs.Count);
            Assert.AreEqual(1, session.Pairs[0].Id);
            Assert.AreEqual(1, session.VoicePrimaryId);
            Assert.AreEqual(10, session.Pairs[0].Capture.X);
            Assert.AreEqual(11, session.Pairs[0].Display.X);
            Assert.AreEqual(1, pairs.WriteCount);
            Assert.IsFalse(session.AddInProgress);
        }

        [TestMethod]
        public void AbortAdd_OnEitherStep_WritesNothing()
        {
            var pairs = new MemoryRegionPairStore();
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);

            Assert.IsTrue(session.TryStartAdd());
            session.SetAddCapture(new OverlayRect(10, 20, 30, 40));
            session.AbortAdd();

            Assert.AreEqual(0, session.Pairs.Count);
            Assert.AreEqual(0, pairs.WriteCount);
            Assert.IsFalse(session.AddInProgress);
            Assert.AreEqual(0, session.VoicePrimaryId);

            Assert.IsTrue(session.TryStartAdd());
            session.SetAddCapture(new OverlayRect(10, 20, 30, 40));
            session.SetAddDisplay(new OverlayRect(11, 50, 30, 40));
            session.AbortAdd();

            Assert.AreEqual(0, session.Pairs.Count);
            Assert.AreEqual(0, pairs.WriteCount);
            Assert.IsFalse(session.AddInProgress);
        }

        [TestMethod]
        public void CommitAdd_DoesNotCopyCaptureIntoDisplay()
        {
            var pairs = new MemoryRegionPairStore();
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);

            Assert.IsTrue(session.TryStartAdd());
            session.SetAddCapture(new OverlayRect(10, 20, 30, 40));
            Assert.IsTrue(session.TryCommitAdd());

            Assert.IsTrue(session.Pairs[0].Capture.IsValid);
            Assert.IsFalse(session.Pairs[0].Display.IsValid);
        }

        [TestMethod]
        public void SetCaptureOnPairZero_DoesNotCopyDisplayWhenInvalid()
        {
            var pairs = new MemoryRegionPairStore();
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);

            session.SetCapture(0, new OverlayRect(100, 200, 800, 80));

            Assert.AreEqual(1, session.Pairs[0].Id);
            Assert.AreEqual(1, session.VoicePrimaryId);
            Assert.IsTrue(session.Pairs[0].Capture.IsValid);
            Assert.IsFalse(session.Pairs[0].Display.IsValid);
        }

        [TestMethod]
        public void SetDisplay_DoesNotInventARectangleFromCapture()
        {
            var pairs = new MemoryRegionPairStore();
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);
            session.SetCapture(0, new OverlayRect(100, 200, 800, 80));
            session.SetDisplay(0, OverlayRect.Invalid);

            Assert.IsTrue(session.Pairs[0].Capture.IsValid);
            Assert.IsFalse(session.Pairs[0].Display.IsValid);

            session.SetDisplay(0, new OverlayRect(5, 6, 7, 8));
            Assert.AreEqual(5, session.Pairs[0].Display.X);
            Assert.AreEqual(100, session.Pairs[0].Capture.X);
        }

        [TestMethod]
        public void UnifiedPairCap_RefusesStartAndCommit_AndTruncatesLoadedExtras()
        {
            var records = new List<RegionPairRecord>();
            for (int i = 0; i < 6; i++)
            {
                records.Add(new RegionPairRecord
                {
                    Id = i + 1,
                    Capture = new OverlayRect(i * 10, 0, 40, 20),
                    Display = new OverlayRect(i * 10, 30, 40, 20)
                });
            }

            var pairs = new MemoryRegionPairStore
            {
                StoredPairs = records,
                VoicePrimaryId = 1,
                NextPairId = 7
            };
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);

            Assert.AreEqual(LiveOverlaySession.SettingsPairCap, session.Pairs.Count);
            Assert.IsFalse(session.TryStartAdd());
            Assert.IsFalse(session.TryCommitAdd());
            Assert.AreEqual(LiveOverlaySession.SettingsPairCap, session.Pairs.Count);
            Assert.AreEqual(1, pairs.WriteCount);

            var samples = new PairFrameSample[LiveOverlaySession.SettingsPairCap];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = PairFrameSample.ChangedAndStable();
            }

            session.Beat(samples);
            Assert.AreEqual(0, session.BusyOcrPairIndex);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, System.Linq.Enumerable.ToArray(session.OcrQueue));
        }

        [TestMethod]
        public void FourPairs_RefuseAnotherAdd_ThreePairsAllowCommit()
        {
            LiveOverlaySession session = CreateSessionWithPairs(4);
            Assert.IsFalse(session.TryStartAdd());

            session = CreateSessionWithPairs(3);
            Assert.IsTrue(session.TryStartAdd());
            session.SetAddCapture(new OverlayRect(1, 2, 3, 4));
            session.SetAddDisplay(new OverlayRect(5, 6, 7, 8));
            Assert.IsTrue(session.TryCommitAdd());
            Assert.AreEqual(4, session.Pairs.Count);
            Assert.AreEqual(4, session.Pairs[3].Id);
            Assert.IsFalse(session.TryStartAdd());
        }

        [TestMethod]
        public void DeletePrimary_RebindsToFirstRemaining_DeleteNonPrimaryKeepsBind()
        {
            LiveOverlaySession session = CreateSessionWithPairs(3);
            Assert.AreEqual(1, session.VoicePrimaryId);

            session.DeletePair(2);
            Assert.AreEqual(2, session.Pairs.Count);
            Assert.AreEqual(1, session.Pairs[0].Id);
            Assert.AreEqual(3, session.Pairs[1].Id);
            Assert.AreEqual(1, session.VoicePrimaryId);

            session.SetVoicePrimary(3);
            Assert.AreEqual(3, session.VoicePrimaryId);

            session.DeletePair(3);
            Assert.AreEqual(1, session.Pairs.Count);
            Assert.AreEqual(1, session.VoicePrimaryId);

            session.DeletePair(1);
            Assert.AreEqual(0, session.Pairs.Count);
            Assert.AreEqual(0, session.VoicePrimaryId);
        }

        [TestMethod]
        public void DeletedPairId_IsNeverReused()
        {
            var pairs = new MemoryRegionPairStore();
            LiveOverlaySession session = CreateSessionWithPairs(2, pairs);
            session.DeletePair(2);

            Assert.IsTrue(session.TryStartAdd());
            session.SetAddCapture(new OverlayRect(1, 2, 3, 4));
            session.SetAddDisplay(new OverlayRect(5, 6, 7, 8));
            Assert.IsTrue(session.TryCommitAdd());

            Assert.AreEqual(2, session.Pairs.Count);
            Assert.AreEqual(1, session.Pairs[0].Id);
            Assert.AreEqual(3, session.Pairs[1].Id);
            Assert.AreEqual(4, pairs.NextPairId);
        }

        [TestMethod]
        public void SetVoicePrimary_DesignatesByIdNotIndex()
        {
            LiveOverlaySession session = CreateSessionWithPairs(3);
            session.SetVoicePrimary(3);
            Assert.AreEqual(3, session.VoicePrimaryId);
            Assert.IsTrue(session.TryGetVoicePrimaryCapture(out int pairIndex, out OverlayRect capture));
            Assert.AreEqual(2, pairIndex);
            Assert.AreEqual(200, capture.X);

            session.SetVoicePrimary(99);
            Assert.AreEqual(3, session.VoicePrimaryId);
        }

        [TestMethod]
        public void MatchOnVoicePrimary_EmitsPlayRequest_NonPrimaryNeverDoes()
        {
            DateTime now = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(2, utcNow: () => now);

            session.Beat(PairFrameSample.ChangedAndStable(), PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "primary", header: "p");
            VoicePlayRequest request = session.TakeVoicePlayRequest();
            Assert.IsNotNull(request);
            Assert.AreEqual(1, request.PairId);
            Assert.AreEqual("primary", request.Content);
            Assert.AreEqual("p", request.Header);
            Assert.IsTrue(session.VoicePlaybackActive);
            Assert.IsNull(session.TakeVoicePlayRequest());

            now = now.AddMilliseconds(400);
            session.Tick();
            session.CompleteOcr(miss: false, content: "side");
            Assert.IsNull(session.TakeVoicePlayRequest());
            Assert.AreEqual("side", session.PairBodies[1].Content);
        }

        [TestMethod]
        public void StableNoTextOnPrimary_DoesNotLetNonPrimarySpeak()
        {
            DateTime now = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(2, utcNow: () => now);
            session.Beat(PairFrameSample.ChangedAndStable(), PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "one");
            session.TakeVoicePlayRequest();
            now = now.AddMilliseconds(400);
            session.Tick();
            session.CompleteOcr(miss: false, content: "two");
            Assert.IsNull(session.TakeVoicePlayRequest());

            session.Beat(PairFrameSample.StableNoText(), PairFrameSample.Unchanged());
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content);
            Assert.AreEqual("two", session.PairBodies[1].Content);
            Assert.IsNull(session.TakeVoicePlayRequest());
        }

        [TestMethod]
        public void ChangingDesignation_DoesNotStopCurrentPlayback()
        {
            LiveOverlaySession session = CreateSessionWithPairs(2);
            session.ApplyPairResult(0, miss: false, content: "hello");
            VoicePlayRequest request = session.TakeVoicePlayRequest();
            Assert.IsNotNull(request);
            int token = session.VoicePlaybackToken;
            Assert.IsTrue(session.VoicePlaybackActive);

            session.SetVoicePrimary(2);

            Assert.AreEqual(2, session.VoicePrimaryId);
            Assert.IsTrue(session.VoicePlaybackActive);
            Assert.AreEqual(token, session.VoicePlaybackToken);
            Assert.IsNull(session.TakeVoicePlayRequest());
        }

        [TestMethod]
        public void RefreshTarget_IsCurrentVoicePrimaryCaptureOnly()
        {
            LiveOverlaySession session = CreateSessionWithPairs(2);
            Assert.IsTrue(session.TryGetVoicePrimaryCapture(out int firstIndex, out OverlayRect firstCapture));
            Assert.AreEqual(0, firstIndex);
            Assert.AreEqual(0, firstCapture.X);

            session.SetVoicePrimary(2);
            Assert.IsTrue(session.TryGetVoicePrimaryCapture(out int secondIndex, out OverlayRect secondCapture));
            Assert.AreEqual(1, secondIndex);
            Assert.AreEqual(100, secondCapture.X);

            session.ClearCapture(1);
            Assert.IsFalse(session.TryGetVoicePrimaryCapture(out _, out OverlayRect missing));
            Assert.IsFalse(missing.IsValid);

            session.DeletePair(2);
            session.DeletePair(1);
            Assert.IsFalse(session.TryGetVoicePrimaryCapture(out _, out _));
        }

        private static LiveOverlaySession CreateSessionWithPairs(
            int pairCount,
            MemoryRegionPairStore pairs = null,
            Func<DateTime> utcNow = null)
        {
            var records = new List<RegionPairRecord>();
            for (int i = 0; i < pairCount; i++)
            {
                records.Add(new RegionPairRecord
                {
                    Id = i + 1,
                    Capture = new OverlayRect(i * 100, 10, 80, 20),
                    Display = new OverlayRect(i * 100, 40, 80, 20)
                });
            }

            if (pairs == null)
            {
                pairs = new MemoryRegionPairStore();
            }

            pairs.StoredPairs = records;
            pairs.VoicePrimaryId = pairCount > 0 ? 1 : 0;
            pairs.NextPairId = pairCount + 1;
            return new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs, utcNow);
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

        private sealed class MemoryRegionPairStore : IRegionPairStore
        {
            public LegacyRegionSlots Legacy = new LegacyRegionSlots();
            public List<RegionPairRecord> StoredPairs = new List<RegionPairRecord>();
            public int VoicePrimaryId;
            public int NextPairId;
            public int WriteCount;
            public OverlayRect DarkScreenDisplay = OverlayRect.Invalid;
            public OverlayRect DialogueOptionDisplay = OverlayRect.Invalid;

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
                WriteCount++;
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

            public bool DarkScreenScan = true;
            public bool DialogueOptionScan;

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
    }
}
