using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestLiveOverlaySessionRegressions
    {
        [TestMethod]
        public void StableEmpty_InvalidatesBusyPairResult()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);

            session.Beat(PairFrameSample.ChangedAndStable());
            Assert.AreEqual(0, session.BusyOcrPairIndex);

            session.Beat(PairFrameSample.StableNoText());
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content);

            // A result from the frame that preceded the stable empty frame is stale.
            session.CompleteOcr(miss: false, content: "stale");

            Assert.AreEqual(string.Empty, session.PairBodies[0].Content);
        }

        [TestMethod]
        public void StableEmpty_RemovesQueuedPairWork()
        {
            LiveOverlaySession session = CreateSessionWithPairs(2);

            session.Beat(
                PairFrameSample.ChangedAndStable(),
                PairFrameSample.ChangedAndStable());
            CollectionAssert.Contains(new List<int>(session.OcrQueue), 1);

            session.Beat(
                PairFrameSample.StableNoText(),
                PairFrameSample.StableNoText());

            CollectionAssert.DoesNotContain(new List<int>(session.OcrQueue), 1);
        }

        [TestMethod]
        public void DisablingDarkScreenScan_ClearsActiveBody()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);
            session.SetDarkScreenDisplay(new OverlayRect(500, 500, 400, 100));

            session.Beat(
                ExtraPathSample.DarkScreenCandidate(
                    new OverlayRect(100, 100, 400, 100),
                    needsOcr: true),
                PairFrameSample.Unchanged());
            Assert.AreEqual(LiveOverlaySession.DarkScreenOcrSlot, session.BusyOcrSlot);

            session.CompleteOcr(miss: false, content: "dark subtitle");
            Assert.AreEqual("dark subtitle", session.DarkScreenBody.Content);

            session.SetDarkScreenScan(false);

            Assert.IsFalse(session.DarkScreenBody.Visible);
            Assert.AreEqual(string.Empty, session.DarkScreenBody.Content);
            Assert.AreNotEqual(LiveOverlaySession.DarkScreenOcrSlot, session.BusyOcrSlot);
            CollectionAssert.DoesNotContain(
                new List<int>(session.OcrQueue),
                LiveOverlaySession.DarkScreenOcrSlot);
        }

        [TestMethod]
        public void DisablingDialogueOptionScan_StaleResultIsIgnored()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);

            session.SetDialogueOptionScan(true);
            session.Beat(
                ExtraPathSample.DialogueOptionsReady(),
                PairFrameSample.Unchanged());
            Assert.AreEqual(LiveOverlaySession.DialogueOptionsOcrSlot, session.BusyOcrSlot);

            session.SetDialogueOptionScan(false);
            session.CompleteOcr(miss: false, ocrText: "stale option");

            Assert.AreNotEqual(LiveOverlaySession.DialogueOptionsOcrSlot, session.BusyOcrSlot);
            CollectionAssert.DoesNotContain(
                new List<int>(session.OcrQueue),
                LiveOverlaySession.DialogueOptionsOcrSlot);
        }

        private static LiveOverlaySession CreateSessionWithPairs(int pairCount)
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

            return new LiveOverlaySession(
                new MemoryOcrIntervalStore(),
                new MemoryRegionPairStore
                {
                    StoredPairs = records,
                    VoicePrimaryId = pairCount > 0 ? 1 : 0,
                    NextPairId = pairCount + 1
                });
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
            public List<RegionPairRecord> StoredPairs = new List<RegionPairRecord>();
            public int VoicePrimaryId;
            public int NextPairId;

            public IReadOnlyList<RegionPairRecord> ReadPairs()
            {
                return StoredPairs;
            }

            public LegacyRegionSlots ReadLegacy()
            {
                return new LegacyRegionSlots();
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
                return OverlayRect.Invalid;
            }

            public void WriteDarkScreenDisplay(OverlayRect display)
            {
            }

            public OverlayRect ReadDialogueOptionDisplay()
            {
                return OverlayRect.Invalid;
            }

            public void WriteDialogueOptionDisplay(OverlayRect display)
            {
            }

            public bool ReadDarkScreenScan()
            {
                return true;
            }

            public void WriteDarkScreenScan(bool enabled)
            {
            }

            public bool ReadDialogueOptionScan()
            {
                return false;
            }

            public void WriteDialogueOptionScan(bool enabled)
            {
            }

            public void SwitchGame(string gameName)
            {
            }
        }
    }
}
