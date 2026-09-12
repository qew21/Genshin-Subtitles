using System;
using System.Collections.Generic;
using System.Linq;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestLiveOverlaySessionPipelineLog
    {
        [TestMethod]
        public void UnchangedBeat_DoesNotAppendRow()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);

            session.Beat(PairFrameSample.Unchanged());

            Assert.AreEqual(0, session.ActivityLog.Count);
        }

        [TestMethod]
        public void OcrIntervalNotDue_DoesNotAppendRow()
        {
            DateTime now = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            Assert.AreEqual(0, session.ActivityLog.Count, "Queued OCR is not yet work that ran.");

            now = now.AddMilliseconds(10);
            session.Beat(PairFrameSample.ChangedAndStable());
            Assert.AreEqual(0, session.ActivityLog.Count);
            Assert.AreEqual(0, session.BusyOcrPairIndex);
        }

        [TestMethod]
        public void DetectionMiss_LogsCaptureAndOcr_OnPair()
        {
            DateTime now = new DateTime(2026, 9, 5, 12, 1, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: true);

            Assert.AreEqual(1, session.ActivityLog.Count);
            ActivityLogRow row = session.ActivityLog[0];
            Assert.AreEqual(now, row.UtcTimestamp);
            CollectionAssert.AreEqual(new[] { OperatorJob.Capture, OperatorJob.Ocr }, row.Jobs.ToArray());
            Assert.AreEqual(ActivityLogScope.Pair, row.Scope);
            Assert.AreEqual(1, row.PairOrdinal);
            Assert.IsTrue(row.VoicePrimary);
            Assert.IsTrue(row.DetectionMiss);
            Assert.IsFalse(row.MatchMiss);
            Assert.AreEqual("ActivityLog_Result_DetectionMiss", row.ResultResourceKey);
            Assert.IsNull(row.OcrText);
            Assert.IsNull(row.Original);
            Assert.IsNull(row.Translation);
            Assert.IsFalse(session.HintVisible);
        }

        [TestMethod]
        public void MatchHit_LogsCaptureOcrMatch_WithOriginalAndTranslation()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(
                miss: false,
                content: "旅行者，这边。",
                header: null,
                ocrText: "旅行者，这边。",
                original: "Traveler, over here.");

            Assert.AreEqual(1, session.ActivityLog.Count);
            ActivityLogRow row = session.ActivityLog[0];
            CollectionAssert.AreEqual(
                new[] { OperatorJob.Capture, OperatorJob.Ocr, OperatorJob.Match },
                row.Jobs.ToArray());
            Assert.AreEqual(ActivityLogScope.Pair, row.Scope);
            Assert.AreEqual(1, row.PairOrdinal);
            Assert.IsTrue(row.VoicePrimary);
            Assert.IsFalse(row.DetectionMiss);
            Assert.IsFalse(row.MatchMiss);
            Assert.AreEqual("旅行者，这边。", row.OcrText);
            Assert.AreEqual("Traveler, over here.", row.Original);
            Assert.AreEqual("旅行者，这边。", row.Translation);
            Assert.IsNull(row.ResultResourceKey);
            Assert.IsFalse(session.HintVisible);
        }

        [TestMethod]
        public void MatchMiss_KeepsOcrText_AndOmitsEmptyTranslation()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(
                miss: false,
                content: "",
                header: null,
                ocrText: "????",
                original: null,
                matchMiss: true);

            Assert.AreEqual(1, session.ActivityLog.Count);
            ActivityLogRow row = session.ActivityLog[0];
            CollectionAssert.AreEqual(
                new[] { OperatorJob.Capture, OperatorJob.Ocr, OperatorJob.Match },
                row.Jobs.ToArray());
            Assert.IsFalse(row.DetectionMiss);
            Assert.IsTrue(row.MatchMiss);
            Assert.AreEqual("????", row.OcrText);
            Assert.IsTrue(string.IsNullOrEmpty(row.Original));
            Assert.IsTrue(string.IsNullOrEmpty(row.Translation));
            Assert.IsFalse(session.HintVisible);
        }

        [TestMethod]
        public void VoiceJob_IsRecordedOnlyWhenPlaybackStarts_OnVoicePrimaryRow()
        {
            DateTime now = new DateTime(2026, 9, 5, 12, 2, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(2, () => now);

            session.Beat(PairFrameSample.ChangedAndStable(), PairFrameSample.ChangedAndStable());
            session.CompleteOcr(
                miss: false,
                content: "primary-line",
                ocrText: "primary-line",
                original: "primary-orig");

            ActivityLogRow primary = session.ActivityLog[0];
            CollectionAssert.AreEqual(
                new[] { OperatorJob.Capture, OperatorJob.Ocr, OperatorJob.Match },
                primary.Jobs.ToArray());
            Assert.IsTrue(primary.VoicePrimary);
            Assert.AreEqual(1, primary.PairOrdinal);
            Assert.IsNotNull(session.TakeVoicePlayRequest());

            session.NoteVoicePlaybackStarted();
            CollectionAssert.AreEqual(
                new[] { OperatorJob.Capture, OperatorJob.Ocr, OperatorJob.Match, OperatorJob.Voice },
                session.ActivityLog[0].Jobs.ToArray());

            now = now.AddMilliseconds(400);
            session.Tick();
            session.CompleteOcr(
                miss: false,
                content: "side-line",
                ocrText: "side-line",
                original: "side-orig");

            Assert.AreEqual(2, session.ActivityLog.Count);
            ActivityLogRow side = session.ActivityLog[1];
            Assert.AreEqual(2, side.PairOrdinal);
            Assert.IsFalse(side.VoicePrimary);
            CollectionAssert.AreEqual(
                new[] { OperatorJob.Capture, OperatorJob.Ocr, OperatorJob.Match },
                side.Jobs.ToArray());
            Assert.IsNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackStarted();
            CollectionAssert.AreEqual(
                new[] { OperatorJob.Capture, OperatorJob.Ocr, OperatorJob.Match },
                session.ActivityLog[1].Jobs.ToArray(),
                "Non-primary pairs must not grow a 配音 job or a 未配音 line.");
        }

        [TestMethod]
        public void ExtraPathRows_AreDarkScreenOrDialogueOptions_NotGlobalOrPair()
        {
            DateTime now = new DateTime(2026, 9, 5, 12, 3, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(1, () => now);
            OverlayRect band = new OverlayRect(40, 80, 400, 60);

            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true)
                    .WithDialogueOptionsReady(),
                PairFrameSample.Unchanged());
            session.CompleteOcr(
                miss: false,
                content: "cutscene",
                header: "narrator",
                ocrText: "cutscene",
                original: "cutscene-orig");

            Assert.AreEqual(1, session.ActivityLog.Count);
            ActivityLogRow dark = session.ActivityLog[0];
            Assert.AreEqual(ActivityLogScope.DarkScreen, dark.Scope);
            Assert.IsNull(dark.PairOrdinal);
            Assert.IsFalse(dark.VoicePrimary);
            CollectionAssert.AreEqual(
                new[] { OperatorJob.Capture, OperatorJob.Ocr, OperatorJob.Match },
                dark.Jobs.ToArray());
            Assert.AreEqual("cutscene", dark.OcrText);
            Assert.AreEqual("cutscene-orig", dark.Original);
            Assert.AreEqual("cutscene", dark.Translation);

            now = now.AddMilliseconds(400);
            session.Tick();
            session.CompleteOcr(miss: true);

            Assert.AreEqual(2, session.ActivityLog.Count);
            ActivityLogRow options = session.ActivityLog[1];
            Assert.AreEqual(ActivityLogScope.DialogueOptions, options.Scope);
            Assert.IsNull(options.PairOrdinal);
            CollectionAssert.AreEqual(
                new[] { OperatorJob.Capture, OperatorJob.Ocr },
                options.Jobs.ToArray());
            Assert.IsTrue(options.DetectionMiss);
        }

        [TestMethod]
        public void DialogueChoice_IsLoggedEvenWhenOverlayEchoIsOmitted()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);
            session.DeletePair(session.Pairs[0].Id);
            Assert.AreEqual(0, session.VoicePrimaryId);

            session.Beat(ExtraPathSample.DialogueChoice("跳过"));

            Assert.IsFalse(session.DialogueChoiceEcho.Visible);
            Assert.AreEqual(1, session.ActivityLog.Count);
            ActivityLogRow row = session.ActivityLog[0];
            Assert.AreEqual(ActivityLogScope.DialogueOptions, row.Scope);
            CollectionAssert.AreEqual(new[] { OperatorJob.Match }, row.Jobs.ToArray());
            Assert.AreEqual("◆ 跳过", row.OcrText);
            Assert.IsTrue(string.IsNullOrEmpty(row.Original));
            Assert.IsTrue(string.IsNullOrEmpty(row.Translation));
            Assert.IsFalse(session.HintVisible);
        }

        [TestMethod]
        public void ExtraPathVoice_AddsVoiceJobOnThatPathRow()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);
            OverlayRect band = new OverlayRect(8, 9, 200, 30);

            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged());
            session.CompleteOcr(
                miss: false,
                content: "cutscene",
                ocrText: "cutscene",
                original: "orig");
            Assert.IsNotNull(session.TakeVoicePlayRequest());

            session.NoteVoicePlaybackStarted();

            CollectionAssert.AreEqual(
                new[] { OperatorJob.Capture, OperatorJob.Ocr, OperatorJob.Match, OperatorJob.Voice },
                session.ActivityLog[0].Jobs.ToArray());
            Assert.AreEqual(ActivityLogScope.DarkScreen, session.ActivityLog[0].Scope);
        }

        [TestMethod]
        public void LanguagePackLoadAndDownload_AreGlobalRows()
        {
            DateTime now = new DateTime(2026, 9, 5, 12, 4, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(1, () => now);

            session.NoteLanguagePackLoadStarted("JP");
            session.NoteLanguagePackLoadFinished("JP", true);
            session.NoteLanguagePackDownloadStarted("EN");
            session.NoteLanguagePackDownloadFinished("EN", false);

            Assert.AreEqual(4, session.ActivityLog.Count);
            foreach (ActivityLogRow row in session.ActivityLog)
            {
                Assert.AreEqual(ActivityLogScope.Global, row.Scope);
                Assert.IsNull(row.PairOrdinal);
                Assert.IsFalse(row.VoicePrimary);
                Assert.IsFalse(session.HintVisible);
            }

            Assert.AreEqual(OperatorJob.LanguagePackLoad, session.ActivityLog[0].Job);
            Assert.AreEqual("ActivityLog_Result_LanguagePackStart", session.ActivityLog[0].ResultResourceKey);
            CollectionAssert.AreEqual(new object[] { "JP" }, session.ActivityLog[0].ResultFormatArguments);

            Assert.AreEqual(OperatorJob.LanguagePackLoad, session.ActivityLog[1].Job);
            Assert.AreEqual("ActivityLog_Result_LanguagePackDone", session.ActivityLog[1].ResultResourceKey);
            CollectionAssert.AreEqual(new object[] { "JP" }, session.ActivityLog[1].ResultFormatArguments);

            Assert.AreEqual(OperatorJob.LanguagePackDownload, session.ActivityLog[2].Job);
            Assert.AreEqual("ActivityLog_Result_LanguagePackStart", session.ActivityLog[2].ResultResourceKey);
            CollectionAssert.AreEqual(new object[] { "EN" }, session.ActivityLog[2].ResultFormatArguments);

            Assert.AreEqual(OperatorJob.LanguagePackDownload, session.ActivityLog[3].Job);
            Assert.AreEqual("ActivityLog_Result_LanguagePackFailed", session.ActivityLog[3].ResultResourceKey);
            CollectionAssert.AreEqual(new object[] { "EN" }, session.ActivityLog[3].ResultFormatArguments);
            Assert.IsFalse(session.HintVisible);
        }

        [TestMethod]
        public void VoicePrimaryBoxing_SnapshotsMarker_WithoutNotVoicedJob()
        {
            LiveOverlaySession session = CreateSessionWithPairs(2);

            session.CaptureRegionSelected(session.Pairs[0].Id);
            session.CaptureRegionSelected(session.Pairs[1].Id);

            Assert.IsTrue(session.ActivityLog[0].VoicePrimary);
            Assert.AreEqual(ActivityLogScope.Pair, session.ActivityLog[0].Scope);
            Assert.AreEqual(1, session.ActivityLog[0].PairOrdinal);
            Assert.AreEqual(OperatorJob.BoxCapture, session.ActivityLog[0].Job);

            Assert.IsFalse(session.ActivityLog[1].VoicePrimary);
            Assert.AreEqual(2, session.ActivityLog[1].PairOrdinal);
            CollectionAssert.DoesNotContain(
                session.ActivityLog[1].Jobs.ToArray(),
                OperatorJob.Voice);
        }

        [TestMethod]
        public void StartStopAndLanguagePack_StayGlobal_WhileDarkScreenDoesNot()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);

            session.StartRecognition(hasCaptureRegion: true);
            session.HideSubtitles();
            session.NoteLanguagePackLoadStarted("CHS");
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(new OverlayRect(1, 2, 3, 4), needsOcr: true),
                PairFrameSample.Unchanged());
            session.CompleteOcr(miss: true);

            Assert.AreEqual(ActivityLogScope.Global, session.ActivityLog[0].Scope);
            Assert.AreEqual(ActivityLogScope.Global, session.ActivityLog[1].Scope);
            Assert.AreEqual(ActivityLogScope.Global, session.ActivityLog[2].Scope);
            Assert.AreEqual(ActivityLogScope.DarkScreen, session.ActivityLog[3].Scope);
        }

        private static LiveOverlaySession CreateSessionWithPairs(int pairCount, Func<DateTime> utcNow = null)
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
