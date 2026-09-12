using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    /// <summary>
    /// Match miss keeps the display: OCR read text the pack could not match, so the
    /// pair keeps its current subtitle instead of blanking; the run is still logged
    /// with its OCR text. Only an empty-stable region or a new match changes the display.
    /// </summary>
    [TestClass]
    public class TestLiveOverlaySessionMatchMissKeep
    {
        [TestMethod]
        public void MatchedThenMatchMiss_KeepsDisplay_LogsBothRunsFaithfully()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", header: "speaker", ocrText: "line", original: "orig");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            int appliedOrder = session.PairBodies[0].RecognitionOrder;

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "", ocrText: "partial", matchMiss: true);

            Assert.AreEqual("line", session.PairBodies[0].Content, "A match miss keeps the current subtitle.");
            Assert.AreEqual("speaker", session.PairBodies[0].Header);
            Assert.AreEqual(appliedOrder, session.PairBodies[0].RecognitionOrder);
            Assert.IsNull(session.TakeVoicePlayRequest());
            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.AreEqual("line", session.ActivityLog[0].Translation);
            Assert.IsFalse(session.ActivityLog[1].IsRepeat, "An unmatched result differs from the matched one before it.");
            Assert.IsTrue(session.ActivityLog[1].MatchMiss);
            Assert.AreEqual("partial", session.ActivityLog[1].OcrText);
            Assert.IsNull(session.ActivityLog[1].Translation);
        }

        [TestMethod]
        public void ForcedMatchMiss_KeepsDisplay_NoVoiceReplay()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            int appliedOrder = session.PairBodies[0].RecognitionOrder;

            session.ApplyPairResult(
                0,
                miss: false,
                content: "",
                header: "",
                ocrText: "partial",
                original: "",
                matchMiss: true,
                force: true);

            Assert.AreEqual(
                "line",
                session.PairBodies[0].Content,
                "A forced match miss has no subtitle to re-apply, so the display keeps the current one.");
            Assert.AreEqual(appliedOrder, session.PairBodies[0].RecognitionOrder);
            Assert.IsNull(session.TakeVoicePlayRequest());
            Assert.AreEqual(
                1,
                session.ActivityLog.Count,
                "ApplyPairResult itself writes no row; the force path's row is the operator Refresh row.");
        }

        [TestMethod]
        public void DetectionMiss_AfterMatch_KeepsDisplay()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            int appliedOrder = session.PairBodies[0].RecognitionOrder;

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: true);

            Assert.AreEqual("line", session.PairBodies[0].Content, "A detection miss keeps the current subtitle.");
            Assert.AreEqual(appliedOrder, session.PairBodies[0].RecognitionOrder);
            Assert.IsNull(session.TakeVoicePlayRequest());
            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.IsTrue(session.ActivityLog[1].DetectionMiss);
        }

        [TestMethod]
        public void EmptyStableBeat_AfterMatchMiss_ClearsDisplay()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "", ocrText: "partial", matchMiss: true);
            Assert.AreEqual("line", session.PairBodies[0].Content);

            session.Beat(PairFrameSample.StableNoText());
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content, "An empty-stable region still clears the display.");
        }

        [TestMethod]
        public void NewMatch_AfterMatchMiss_LandsAndReplacesDisplay()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "a", ocrText: "a", original: "A");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            int firstOrder = session.PairBodies[0].RecognitionOrder;

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "", ocrText: "partial", matchMiss: true);
            Assert.AreEqual("a", session.PairBodies[0].Content);

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "b", ocrText: "b", original: "B");

            Assert.AreEqual("b", session.PairBodies[0].Content, "A new match after a match miss replaces the display.");
            Assert.IsTrue(session.PairBodies[0].RecognitionOrder > firstOrder);
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            Assert.IsFalse(session.ActivityLog[2].IsRepeat);
        }

        [TestMethod]
        public void DarkScreenMatchMiss_KeepsCurrentDisplay()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            OverlayRect band = new OverlayRect(40, 80, 400, 60);

            session.Beat(ExtraPathSample.DarkScreenCandidate(band, needsOcr: true));
            session.CompleteOcr(miss: false, content: "cutscene", header: "narrator");
            Assert.AreEqual("cutscene", session.DarkScreenBody.Content);
            Assert.IsNotNull(session.TakeVoicePlayRequest());

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(ExtraPathSample.DarkScreenCandidate(band, needsOcr: true));
            session.CompleteOcr(miss: false, content: "", ocrText: "partial", matchMiss: true);

            Assert.AreEqual("cutscene", session.DarkScreenBody.Content, "A dark-screen match miss keeps the current subtitle.");
            Assert.AreEqual("narrator", session.DarkScreenBody.Header);
            Assert.IsTrue(session.DarkScreenBody.Visible);
            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.IsTrue(session.ActivityLog[1].MatchMiss);
            Assert.AreEqual("partial", session.ActivityLog[1].OcrText);
        }

        private static LiveOverlaySession CreateSession(int pairCount, Func<DateTime> utcNow = null)
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
                },
                utcNow);
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
    }
}
