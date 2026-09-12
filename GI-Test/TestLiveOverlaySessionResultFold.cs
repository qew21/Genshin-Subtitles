using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    /// <summary>
    /// Result fold: a run concluding the same recognition result its pair already shows
    /// keeps the display and skips voice replay; the run is still logged, and the
    /// second row of the same result is marked as a repeat row.
    /// </summary>
    [TestClass]
    public class TestLiveOverlaySessionResultFold
    {
        [TestMethod]
        public void SameResult_SecondRun_MarksRepeatRow_KeepsDisplay_SkipsVoiceReplay()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.AreEqual(1, session.ActivityLog.Count);
            Assert.IsFalse(session.ActivityLog[0].IsRepeat);
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            int appliedOrder = session.PairBodies[0].RecognitionOrder;
            Assert.IsTrue(appliedOrder > 0);

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(
                miss: false,
                content: "line",
                ocrText: "line drifted", // different OCR text, same matched subtitle
                original: "orig");

            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.IsTrue(session.ActivityLog[1].IsRepeat);
            Assert.AreEqual("line", session.PairBodies[0].Content);
            Assert.AreEqual(appliedOrder, session.PairBodies[0].RecognitionOrder);
            Assert.IsNull(session.TakeVoicePlayRequest());
        }

        [TestMethod]
        public void DifferentResult_ReplacesDisplay_NoRepeatMark()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "a", ocrText: "a", original: "A");
            int firstOrder = session.PairBodies[0].RecognitionOrder;

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "b", ocrText: "b", original: "B");

            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.IsFalse(session.ActivityLog[1].IsRepeat);
            Assert.AreEqual("b", session.PairBodies[0].Content);
            Assert.IsTrue(session.PairBodies[0].RecognitionOrder > firstOrder);
            Assert.IsNotNull(session.TakeVoicePlayRequest());
        }

        [TestMethod]
        public void EmptyStableReset_SameTextAgain_IsNewRow_NotRepeat()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "a", ocrText: "a", original: "A");

            session.Beat(PairFrameSample.StableNoText());
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content);

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "a", ocrText: "a", original: "A");

            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.IsFalse(session.ActivityLog[1].IsRepeat);
            Assert.AreEqual("a", session.PairBodies[0].Content);
        }

        [TestMethod]
        public void ConsecutiveDetectionMisses_FirstRowNormal_LaterRowsRepeat()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: true);

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: true);

            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.IsFalse(session.ActivityLog[0].IsRepeat);
            Assert.IsTrue(session.ActivityLog[1].IsRepeat);
        }

        [TestMethod]
        public void MatchMissRepeats_OnlyWhenOcrTextIsSame()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "", ocrText: "partial", matchMiss: true);

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "", ocrText: "partial", matchMiss: true);

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "", ocrText: "partial.", matchMiss: true);

            Assert.AreEqual(3, session.ActivityLog.Count);
            Assert.IsFalse(session.ActivityLog[0].IsRepeat);
            Assert.IsTrue(session.ActivityLog[1].IsRepeat);
            Assert.IsFalse(session.ActivityLog[2].IsRepeat);
        }

        [TestMethod]
        public void ForceApply_SameResult_Reapplies_ReplaysVoice_OperatorRowIsNotRepeat()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            int autoOrder = session.PairBodies[0].RecognitionOrder;

            session.ApplyPairResult(
                0,
                miss: false,
                content: "line",
                header: null,
                ocrText: "line",
                original: "orig",
                matchMiss: false,
                force: true);
            Assert.AreEqual(
                1,
                session.ActivityLog.Count,
                "ApplyPairResult itself writes no row; the force path's row is the operator Refresh row.");
            Assert.IsTrue(session.PairBodies[0].RecognitionOrder > autoOrder);
            Assert.IsNotNull(session.TakeVoicePlayRequest());

            session.Refresh(hasCaptureRegion: true, foundText: true);
            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.AreEqual(OperatorJob.Refresh, session.ActivityLog[1].Job);
            Assert.IsFalse(session.ActivityLog[1].IsRepeat);

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.AreEqual(3, session.ActivityLog.Count);
            Assert.IsTrue(session.ActivityLog[2].IsRepeat, "A same-result auto run after the force apply is a repeat row again.");
        }

        [TestMethod]
        public void ForceMiss_KeepsLastResult_NextSameResultAutoRunFolds()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            int appliedOrder = session.PairBodies[0].RecognitionOrder;

            session.ApplyPairResult(0, miss: true, force: true);
            Assert.AreEqual("line", session.PairBodies[0].Content, "A forced miss keeps the current subtitle.");
            Assert.AreEqual(appliedOrder, session.PairBodies[0].RecognitionOrder);
            Assert.IsNull(session.TakeVoicePlayRequest());

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.IsTrue(session.ActivityLog[session.ActivityLog.Count - 1].IsRepeat);
            Assert.AreEqual(appliedOrder, session.PairBodies[0].RecognitionOrder);
            Assert.IsNull(session.TakeVoicePlayRequest());
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
