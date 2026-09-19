using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    /// <summary>
    /// Extra-path OCR slots (dialogue options, dark screen) mark same-result reruns as
    /// repeat rows, matching region-pair result fold so log de-noise can hide them.
    /// </summary>
    [TestClass]
    public class TestLiveOverlaySessionExtraPathRepeat
    {
        [TestMethod]
        public void SameDialogueOptionsOcrText_SecondRun_MarksRepeatRow()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            const string ocrText = "继续 / 再想想";

            session.Beat(ExtraPathSample.DialogueOptionsReady(), PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, ocrText: ocrText);

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(ExtraPathSample.DialogueOptionsReady(), PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, ocrText: ocrText);

            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.AreEqual(ActivityLogScope.DialogueOptions, session.ActivityLog[0].Scope);
            Assert.AreEqual(ActivityLogScope.DialogueOptions, session.ActivityLog[1].Scope);
            Assert.AreEqual(ocrText, session.ActivityLog[0].OcrText);
            Assert.AreEqual(ocrText, session.ActivityLog[1].OcrText);
            Assert.IsFalse(session.ActivityLog[0].IsRepeat);
            Assert.IsTrue(session.ActivityLog[1].IsRepeat);
        }

        [TestMethod]
        public void DifferentDialogueOptionsOcrText_IsNotRepeatRow()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 1, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(ExtraPathSample.DialogueOptionsReady(), PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, ocrText: "继续 / 再想想");

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(ExtraPathSample.DialogueOptionsReady(), PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, ocrText: "离开 / 留下");

            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.IsFalse(session.ActivityLog[0].IsRepeat);
            Assert.IsFalse(session.ActivityLog[1].IsRepeat);
        }

        [TestMethod]
        public void SameDarkScreenResult_SecondRun_MarksRepeatRow()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 5, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            OverlayRect band = new OverlayRect(40, 80, 400, 60);

            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged());
            session.CompleteOcr(
                miss: false,
                content: "cutscene",
                ocrText: "cutscene",
                original: "cutscene-orig");

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged());
            session.CompleteOcr(
                miss: false,
                content: "cutscene",
                ocrText: "cutscene",
                original: "cutscene-orig");

            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.AreEqual(ActivityLogScope.DarkScreen, session.ActivityLog[1].Scope);
            Assert.IsFalse(session.ActivityLog[0].IsRepeat);
            Assert.IsTrue(session.ActivityLog[1].IsRepeat);
        }

        [TestMethod]
        public void AfterDialogueOptionsEnded_SameOcrText_IsFreshRow()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 10, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            const string ocrText = "继续 / 再想想";

            session.Beat(ExtraPathSample.DialogueOptionsReady(), PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, ocrText: ocrText);

            session.Beat(ExtraPathSample.DialogueOptionsEnded(), PairFrameSample.Unchanged());

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(ExtraPathSample.DialogueOptionsReady(), PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, ocrText: ocrText);

            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.IsFalse(session.ActivityLog[0].IsRepeat);
            Assert.IsFalse(
                session.ActivityLog[1].IsRepeat,
                "Dismiss without choice must clear last-result so the next menu is a fresh row.");
        }

        [TestMethod]
        public void AfterDialogueChoice_SameOcrText_IsFreshRow()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 11, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            const string ocrText = "离开 / 留下";

            session.Beat(ExtraPathSample.DialogueOptionsReady(), PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, ocrText: ocrText);

            session.Beat(ExtraPathSample.DialogueChoice("离开"), PairFrameSample.Unchanged());

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(ExtraPathSample.DialogueOptionsReady(), PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, ocrText: ocrText);

            ActivityLogRow latestOptions = null;
            for (int i = session.ActivityLog.Count - 1; i >= 0; i--)
            {
                if (session.ActivityLog[i].Scope == ActivityLogScope.DialogueOptions
                    && session.ActivityLog[i].Jobs.Count > 1)
                {
                    latestOptions = session.ActivityLog[i];
                    break;
                }
            }

            Assert.IsNotNull(latestOptions);
            Assert.AreEqual(ocrText, latestOptions.OcrText);
            Assert.IsFalse(latestOptions.IsRepeat);
        }

        [TestMethod]
        public void AfterDarkScreenEnded_SameResult_IsFreshRow()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 12, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            OverlayRect band = new OverlayRect(40, 80, 400, 60);

            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged());
            session.CompleteOcr(
                miss: false,
                content: "cutscene",
                ocrText: "cutscene",
                original: "cutscene-orig");

            session.Beat(ExtraPathSample.DarkScreenEnded(), PairFrameSample.Unchanged());

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged());
            session.CompleteOcr(
                miss: false,
                content: "cutscene",
                ocrText: "cutscene",
                original: "cutscene-orig");

            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.IsFalse(session.ActivityLog[1].IsRepeat);
        }

        [TestMethod]
        public void InFlightDarkScreenComplete_AfterEnded_DoesNotSeedRepeatForNextCard()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 13, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            OverlayRect band = new OverlayRect(40, 80, 400, 60);

            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged());
            Assert.AreEqual(LiveOverlaySession.DarkScreenOcrSlot, session.BusyOcrSlot);

            session.Beat(ExtraPathSample.DarkScreenEnded(), PairFrameSample.Unchanged());
            session.CompleteOcr(
                miss: false,
                content: "cutscene",
                ocrText: "cutscene",
                original: "cutscene-orig");

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged());
            session.CompleteOcr(
                miss: false,
                content: "cutscene",
                ocrText: "cutscene",
                original: "cutscene-orig");

            Assert.IsFalse(
                session.ActivityLog[session.ActivityLog.Count - 1].IsRepeat,
                "CompleteOcr after dark-screen end must not re-seed last-result.");
        }

        [TestMethod]
        public void AfterApplyGame_SameDialogueOptionsOcrText_IsFreshRow()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 14, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            const string ocrText = "继续 / 再想想";

            session.Beat(ExtraPathSample.DialogueOptionsReady(), PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, ocrText: ocrText);

            session.ApplyGame("StarRail");
            session.ApplyGame("Genshin");

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(ExtraPathSample.DialogueOptionsReady(), PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, ocrText: ocrText);

            Assert.IsFalse(session.ActivityLog[session.ActivityLog.Count - 1].IsRepeat);
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
                    NextPairId = pairCount + 1,
                    DialogueOptionScan = true
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
