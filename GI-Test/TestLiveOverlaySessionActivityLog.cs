using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestLiveOverlaySessionActivityLog
    {
        [TestMethod]
        public void NewSession_HasNoActivityLogRows()
        {
            LiveOverlaySession session = CreateSession();

            Assert.AreEqual(0, session.ActivityLog.Count);
        }

        [TestMethod]
        public void StartRecognition_WithCapture_AppendsGlobalRunningRow()
        {
            DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(() => now);

            session.StartRecognition(hasCaptureRegion: true);

            Assert.AreEqual(1, session.ActivityLog.Count);
            ActivityLogRow row = session.ActivityLog[0];
            Assert.AreEqual(now, row.UtcTimestamp);
            Assert.AreEqual(OperatorJob.StartRecognition, row.Job);
            Assert.IsNull(row.PairOrdinal);
            Assert.AreEqual("Hint_RecognitionRunning", row.ResultResourceKey);
            Assert.IsNull(row.ResultFormatArguments);
        }

        [TestMethod]
        public void SharedMissingResult_IsDistinguishedByJob()
        {
            LiveOverlaySession session = CreateSession();

            session.StartRecognition(hasCaptureRegion: false);
            session.CaptureRegionSelectionCancelled();
            session.PreviewCaptureRegion(hasCaptureRegion: false);
            session.Refresh(hasCaptureRegion: false, foundText: false);

            Assert.AreEqual(4, session.ActivityLog.Count);
            Assert.AreEqual(OperatorJob.StartRecognition, session.ActivityLog[0].Job);
            Assert.AreEqual(OperatorJob.BoxCapture, session.ActivityLog[1].Job);
            Assert.AreEqual(OperatorJob.Preview, session.ActivityLog[2].Job);
            Assert.AreEqual(OperatorJob.Refresh, session.ActivityLog[3].Job);
            foreach (ActivityLogRow row in session.ActivityLog)
            {
                Assert.AreEqual("Hint_CaptureRegionMissing", row.ResultResourceKey);
                Assert.IsNull(row.PairOrdinal);
            }
        }

        [TestMethod]
        public void CaptureRegionSelected_SnapshotsOrdinalAtWriteTime()
        {
            LiveOverlaySession session = CreateSessionWithPairs(2);

            session.CaptureRegionSelected(session.Pairs[1].Id);

            Assert.AreEqual(1, session.ActivityLog.Count);
            Assert.AreEqual(OperatorJob.BoxCapture, session.ActivityLog[0].Job);
            Assert.AreEqual(2, session.ActivityLog[0].PairOrdinal);
            Assert.AreEqual("Hint_CaptureRegionBoxed", session.ActivityLog[0].ResultResourceKey);

            session.DeletePair(session.Pairs[0].Id);

            Assert.AreEqual(2, session.ActivityLog[0].PairOrdinal);
            Assert.AreEqual(1, session.Pairs.Count);
        }

        [TestMethod]
        public void CaptureRegionSelected_MissingId_IsGlobalRow()
        {
            LiveOverlaySession session = CreateSession();

            session.CaptureRegionSelected();

            Assert.AreEqual(1, session.ActivityLog.Count);
            Assert.AreEqual(OperatorJob.BoxCapture, session.ActivityLog[0].Job);
            Assert.IsNull(session.ActivityLog[0].PairOrdinal);
            Assert.AreEqual("Hint_CaptureRegionBoxed", session.ActivityLog[0].ResultResourceKey);
        }

        [TestMethod]
        public void StopHideShowRefreshAndSpeed_AreGlobalRows()
        {
            LiveOverlaySession session = CreateSession();

            session.StopRecognition();
            session.HideSubtitles();
            session.ShowSubtitles();
            session.Refresh(hasCaptureRegion: true, foundText: true);
            session.Refresh(hasCaptureRegion: true, foundText: false);
            session.ChangeVoiceSpeed(1.5);

            Assert.AreEqual(6, session.ActivityLog.Count);
            Assert.AreEqual(OperatorJob.StopRecognition, session.ActivityLog[0].Job);
            Assert.AreEqual("Hint_RecognitionStopped", session.ActivityLog[0].ResultResourceKey);
            Assert.AreEqual(OperatorJob.HideSubtitles, session.ActivityLog[1].Job);
            Assert.AreEqual("Hint_SubtitlesHidden", session.ActivityLog[1].ResultResourceKey);
            Assert.AreEqual(OperatorJob.ShowSubtitles, session.ActivityLog[2].Job);
            Assert.AreEqual("Hint_SubtitlesShown", session.ActivityLog[2].ResultResourceKey);
            Assert.AreEqual(OperatorJob.Refresh, session.ActivityLog[3].Job);
            Assert.AreEqual("Hint_Refreshed", session.ActivityLog[3].ResultResourceKey);
            Assert.AreEqual(OperatorJob.Refresh, session.ActivityLog[4].Job);
            Assert.AreEqual("Hint_RefreshFoundNoText", session.ActivityLog[4].ResultResourceKey);
            Assert.AreEqual(OperatorJob.VoiceSpeed, session.ActivityLog[5].Job);
            Assert.AreEqual("Hint_VoiceSpeed", session.ActivityLog[5].ResultResourceKey);
            CollectionAssert.AreEqual(new object[] { "1.5" }, session.ActivityLog[5].ResultFormatArguments);
            foreach (ActivityLogRow row in session.ActivityLog)
            {
                Assert.IsNull(row.PairOrdinal);
            }
        }

        [TestMethod]
        public void SuccessfulPreview_ProducesNoRowAndLeavesExistingRows()
        {
            LiveOverlaySession session = CreateSession();
            session.StartRecognition(hasCaptureRegion: true);

            session.PreviewCaptureRegion(hasCaptureRegion: true);

            Assert.AreEqual(1, session.ActivityLog.Count);
            Assert.AreEqual(OperatorJob.StartRecognition, session.ActivityLog[0].Job);
            Assert.AreEqual("Hint_RecognitionRunning", session.HintResourceKey);
        }

        [TestMethod]
        public void HintExpiry_DoesNotAppendOrRaiseActivityLogChanged()
        {
            DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(() => now);
            int notifications = 0;
            session.ActivityLogChanged += (sender, args) => notifications++;

            session.StartRecognition(hasCaptureRegion: true);
            Assert.AreEqual(1, notifications);
            Assert.AreEqual(1, session.ActivityLog.Count);

            now = now.AddSeconds(3);
            session.Tick();

            Assert.IsFalse(session.HintVisible);
            Assert.AreEqual(1, notifications);
            Assert.AreEqual(1, session.ActivityLog.Count);
        }

        [TestMethod]
        public void WindowClosedStillRecords_RowsKeepAccumulatingOnTheSession()
        {
            LiveOverlaySession session = CreateSession();
            session.StartRecognition(hasCaptureRegion: true);
            session.StopRecognition();

            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.AreEqual(OperatorJob.StartRecognition, session.ActivityLog[0].Job);
            Assert.AreEqual(OperatorJob.StopRecognition, session.ActivityLog[1].Job);
        }

        [TestMethod]
        public void OcrMissesAndSettingsBoxing_DoNotEnterTheFunnel()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);
            session.NoteOcrMiss();
            session.NoteMatchMiss();
            session.SetCapture(0, new OverlayRect(10, 20, 30, 40));

            Assert.AreEqual(0, session.ActivityLog.Count);
        }

        private static LiveOverlaySession CreateSession(Func<DateTime> utcNow = null)
        {
            return new LiveOverlaySession(new MemoryOcrIntervalStore(), utcNow);
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

            var pairs = new MemoryRegionPairStore
            {
                StoredPairs = records,
                VoicePrimaryId = pairCount > 0 ? 1 : 0,
                NextPairId = pairCount + 1
            };
            return new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);
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
