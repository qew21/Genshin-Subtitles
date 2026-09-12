using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    /// <summary>
    /// Subtitle idle timeout: each region-pair and dark-screen surface clears its body
    /// and result fold after N whole seconds with no newly applied recognition result
    /// on that surface alone. Zero means off.
    /// </summary>
    [TestClass]
    public class TestLiveOverlaySessionSubtitleIdleTimeout
    {
        [TestMethod]
        public void DefaultTimeout_Zero_LeavesBodyUntilReplaced()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            Assert.AreEqual(0, session.SubtitleIdleTimeoutSeconds);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");

            now = now.AddHours(1);
            session.Tick();
            Assert.AreEqual("line", session.PairBodies[0].Content);
        }

        [TestMethod]
        public void TimeoutExpires_ClearsPairBodyAndResultFold_NoLogRow()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            session.SetSubtitleIdleTimeoutSeconds(2);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.AreEqual("line", session.PairBodies[0].Content);
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();
            int logCountAfterApply = session.ActivityLog.Count;

            now = now.AddSeconds(1);
            session.Tick();
            Assert.AreEqual("line", session.PairBodies[0].Content, "Idle clock has not reached the timeout yet.");

            now = now.AddSeconds(1);
            session.Tick();
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content);
            Assert.AreEqual(
                logCountAfterApply,
                session.ActivityLog.Count,
                "Expiry writes no activity log row.");

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.AreEqual("line", session.PairBodies[0].Content, "Cleared fold lets the same line re-apply.");
            Assert.IsNotNull(session.TakeVoicePlayRequest(), "Cleared fold lets the same line speak again.");
        }

        [TestMethod]
        public void EachSurface_TimesOutOnItsOwnClock()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 10, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(2, () => now);
            session.SetSubtitleIdleTimeoutSeconds(3);
            OverlayRect band = new OverlayRect(40, 80, 400, 60);

            session.Beat(PairFrameSample.ChangedAndStable(), PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, content: "pair-a", ocrText: "pair-a", original: "A");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();

            now = now.AddSeconds(1);
            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.Unchanged(), PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "pair-b", ocrText: "pair-b", original: "B");

            now = now.AddSeconds(1);
            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged(),
                PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, content: "cutscene", ocrText: "cutscene", original: "C");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();

            // pair-a applied at t0; pair-b at t0+1s; dark at t0+2s. Timeout 3s.
            now = now.AddSeconds(1); // t0+3s: pair-a due
            session.Tick();
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content);
            Assert.AreEqual("pair-b", session.PairBodies[1].Content);
            Assert.AreEqual("cutscene", session.DarkScreenBody.Content);

            now = now.AddSeconds(1); // t0+4s: pair-b due
            session.Tick();
            Assert.AreEqual(string.Empty, session.PairBodies[1].Content);
            Assert.AreEqual("cutscene", session.DarkScreenBody.Content);

            now = now.AddSeconds(1); // t0+5s: dark due
            session.Tick();
            Assert.AreEqual(string.Empty, session.DarkScreenBody.Content);
        }

        [TestMethod]
        public void FoldAndMissKeep_DoNotResetIdleClock_ForceRefreshDoes()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 20, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            session.SetSubtitleIdleTimeoutSeconds(5);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();

            now = now.AddSeconds(2);
            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(
                miss: false,
                content: "line",
                ocrText: "line drifted",
                original: "orig"); // same result -> fold
            Assert.AreEqual("line", session.PairBodies[0].Content);
            Assert.IsNull(session.TakeVoicePlayRequest());

            now = now.AddSeconds(1);
            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "", ocrText: "partial", matchMiss: true);

            now = now.AddSeconds(1);
            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: true);

            // 2+1+1 = 4s since apply; fold/miss must not have restarted the clock.
            now = now.AddSeconds(1);
            session.Tick();
            Assert.AreEqual(
                string.Empty,
                session.PairBodies[0].Content,
                "Fold and miss-keep must not reset the idle clock.");

            // Re-apply, then force-refresh the same line near expiry to restart the clock.
            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();

            now = now.AddSeconds(4);
            session.ApplyPairResult(
                0,
                miss: false,
                content: "line",
                header: null,
                ocrText: "line",
                original: "orig",
                matchMiss: false,
                force: true);
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();

            now = now.AddSeconds(4);
            session.Tick();
            Assert.AreEqual(
                "line",
                session.PairBodies[0].Content,
                "Force refresh resets the idle clock from the force-apply moment.");

            now = now.AddSeconds(1);
            session.Tick();
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content);
        }

        [TestMethod]
        public void Expiry_DoesNotStopVoice()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 30, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            session.SetSubtitleIdleTimeoutSeconds(1);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.IsTrue(session.VoicePlaybackActive);

            now = now.AddSeconds(1);
            session.Tick();
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content);
            Assert.IsTrue(session.VoicePlaybackActive, "Expiry must not stop voice.");
        }

        [TestMethod]
        public void ChangingTimeout_RecalculatesFromLastApply_ZeroDisarms()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 40, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            session.SetSubtitleIdleTimeoutSeconds(10);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();

            now = now.AddSeconds(4);
            session.SetSubtitleIdleTimeoutSeconds(3);
            Assert.AreEqual(
                string.Empty,
                session.PairBodies[0].Content,
                "Shortening below elapsed idle clears on the applying Tick.");

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "again", ocrText: "again", original: "A");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();

            now = now.AddSeconds(2);
            session.SetSubtitleIdleTimeoutSeconds(0);
            Assert.AreEqual("again", session.PairBodies[0].Content, "Zero disarms without clearing.");

            now = now.AddHours(1);
            session.Tick();
            Assert.AreEqual("again", session.PairBodies[0].Content);
        }

        [TestMethod]
        public void EnablingTimeout_ClearsAlreadyStaleBodyOnNextTick()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 45, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            Assert.AreEqual(0, session.SubtitleIdleTimeoutSeconds);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "stale", ocrText: "stale", original: "S");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();

            now = now.AddSeconds(5);
            session.SetSubtitleIdleTimeoutSeconds(3);
            Assert.AreEqual(
                string.Empty,
                session.PairBodies[0].Content,
                "Enabling a timeout shorter than elapsed idle clears on the applying Tick.");
        }

        [TestMethod]
        public void HintAndPreview_StayIndependentOfIdleTimeout()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 55, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            session.SetSubtitleIdleTimeoutSeconds(2);

            session.StartRecognition(hasCaptureRegion: true);
            Assert.AreEqual("Hint_RecognitionRunning", session.HintResourceKey);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();

            session.PreviewCaptureRegion(hasCaptureRegion: true);
            Assert.IsTrue(session.PreviewOutlines.Count > 0);
            Assert.AreEqual(
                "Hint_RecognitionRunning",
                session.HintResourceKey,
                "Successful preview must not replace a live hint.");

            now = now.AddSeconds(2);
            session.Tick();
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content);
            Assert.IsFalse(session.HintVisible, "Hint still uses its own two-second clock.");
            Assert.IsTrue(
                session.PreviewOutlines.Count > 0,
                "Preview still uses its own ten-second clock.");
        }

        [TestMethod]
        public void IdleClock_KeepsRunningAfterRecognitionStopped()
        {
            DateTime now = new DateTime(2026, 9, 10, 12, 50, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            session.SetSubtitleIdleTimeoutSeconds(2);

            session.StartRecognition(hasCaptureRegion: true);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();

            session.StopRecognition();
            Assert.IsFalse(session.RecognitionRunning);

            now = now.AddSeconds(2);
            session.Tick();
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content);
        }

        [TestMethod]
        public void DarkScreenSameResult_DoesNotResetIdleClock()
        {
            DateTime now = new DateTime(2026, 9, 10, 13, 10, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            session.SetSubtitleIdleTimeoutSeconds(3);
            OverlayRect band = new OverlayRect(40, 80, 400, 60);

            session.Beat(ExtraPathSample.DarkScreenCandidate(band, needsOcr: true));
            session.CompleteOcr(miss: false, content: "cutscene", ocrText: "cutscene", original: "C");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();

            now = now.AddSeconds(2);
            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(ExtraPathSample.DarkScreenCandidate(band, needsOcr: true));
            session.CompleteOcr(miss: false, content: "cutscene", ocrText: "cutscene", original: "C");
            Assert.AreEqual("cutscene", session.DarkScreenBody.Content);

            now = now.AddSeconds(1);
            session.Tick();
            Assert.AreEqual(
                string.Empty,
                session.DarkScreenBody.Content,
                "A same dark-screen result must not restart the idle clock.");
        }

        [TestMethod]
        public void HideShowAndDialogueChoiceEcho_AreUnchangedByIdleTimeout()
        {
            DateTime now = new DateTime(2026, 9, 10, 13, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);
            session.SetSubtitleIdleTimeoutSeconds(2);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "line", ocrText: "line", original: "orig");
            Assert.IsNotNull(session.TakeVoicePlayRequest());
            session.NoteVoicePlaybackEnded();

            session.Beat(ExtraPathSample.DialogueChoice("跳过"));
            Assert.IsTrue(session.DialogueChoiceEcho.Visible);
            Assert.AreEqual("◆ 跳过", session.DialogueChoiceEcho.Content);

            session.HideSubtitles();
            Assert.IsFalse(session.SubtitlesVisible);
            Assert.IsFalse(session.PairBodies[0].Visible);
            Assert.AreEqual("line", session.PairBodies[0].Content, "Hide mutes visibility, not body.");

            now = now.AddSeconds(1);
            session.Tick();
            Assert.AreEqual("line", session.PairBodies[0].Content);
            Assert.IsTrue(session.DialogueChoiceEcho.Content.Length > 0);

            // Echo keeps its fixed 3s duration; at t+1s after choice it is still live.
            Assert.IsFalse(
                session.DialogueChoiceEcho.Visible,
                "Hide mutes the echo surface too while content remains until echo expiry.");
            Assert.AreEqual("◆ 跳过", session.DialogueChoiceEcho.Content);

            now = now.AddSeconds(1);
            session.Tick();
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content, "Idle clock was not paused by hide.");
            Assert.AreEqual("◆ 跳过", session.DialogueChoiceEcho.Content, "Echo is not under idle timeout.");

            session.ShowSubtitles();
            Assert.IsTrue(session.SubtitlesVisible);

            now = now.AddSeconds(1);
            session.Tick();
            Assert.AreEqual(
                string.Empty,
                session.DialogueChoiceEcho.Content,
                "Echo still expires on its own fixed duration.");
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
