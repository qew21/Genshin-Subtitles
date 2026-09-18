using System;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestLiveOverlaySessionHint
    {
        [TestMethod]
        public void StartRecognition_WithCaptureRegion_ShowsRecognizingHint()
        {
            LiveOverlaySession session = CreateSession();

            session.StartRecognition(hasCaptureRegion: true);

            Assert.IsTrue(session.HintVisible);
            Assert.AreEqual("Hint_RecognitionRunning", session.HintResourceKey);
        }

        [TestMethod]
        public void StartRecognition_FromHotkeyAndSettings_SharesTheSameHint()
        {
            LiveOverlaySession session = CreateSession();

            session.StartRecognition(hasCaptureRegion: true);
            Assert.AreEqual("Hint_RecognitionRunning", session.HintResourceKey);

            session.StopRecognition();
            Assert.AreEqual("Hint_RecognitionStopped", session.HintResourceKey);

            session.StartRecognition(hasCaptureRegion: true);
            Assert.AreEqual("Hint_RecognitionRunning", session.HintResourceKey);
            Assert.IsTrue(session.RecognitionRunning);
        }

        [TestMethod]
        public void NewHint_ReplacesTheOldAndRestartsTheTwoSecondClock()
        {
            DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(() => now);

            session.StartRecognition(hasCaptureRegion: true);
            Assert.AreEqual("Hint_RecognitionRunning", session.HintResourceKey);

            now = now.AddSeconds(1);
            session.HideSubtitles();
            Assert.AreEqual("Hint_SubtitlesHidden", session.HintResourceKey);
            Assert.IsTrue(session.HintVisible);

            now = now.AddSeconds(1);
            session.Tick();
            Assert.IsTrue(session.HintVisible, "Clock restarted at replace, so 1s later the hint is still live.");

            now = now.AddSeconds(1);
            session.Tick();
            Assert.IsFalse(session.HintVisible);
            Assert.IsNull(session.HintResourceKey);
        }

        [TestMethod]
        public void HideSubtitles_LeavesALiveHintVisible()
        {
            LiveOverlaySession session = CreateSession();

            session.StartRecognition(hasCaptureRegion: true);
            session.HideSubtitles();

            Assert.IsFalse(session.SubtitlesVisible);
            Assert.IsTrue(session.HintVisible);
            Assert.AreEqual("Hint_SubtitlesHidden", session.HintResourceKey);

            session.ShowSubtitles();
            Assert.IsTrue(session.SubtitlesVisible);
            Assert.IsTrue(session.HintVisible);
            Assert.AreEqual("Hint_SubtitlesShown", session.HintResourceKey);
        }

        [TestMethod]
        public void SuccessfulPreview_ProducesNoHint()
        {
            LiveOverlaySession session = CreateSession();

            session.PreviewCaptureRegion(hasCaptureRegion: true);

            Assert.IsFalse(session.HintVisible);
            Assert.IsNull(session.HintResourceKey);
        }

        [TestMethod]
        public void SuccessfulPreview_DoesNotReplaceALiveHint()
        {
            LiveOverlaySession session = CreateSession();

            session.StartRecognition(hasCaptureRegion: true);
            session.PreviewCaptureRegion(hasCaptureRegion: true);

            Assert.AreEqual("Hint_RecognitionRunning", session.HintResourceKey);
            Assert.IsTrue(session.HintVisible);
        }

        [TestMethod]
        public void ActionFailures_HintTheActionJustTaken()
        {
            LiveOverlaySession session = CreateSession();

            session.StartRecognition(hasCaptureRegion: false);
            Assert.AreEqual("Hint_CaptureRegionMissing", session.HintResourceKey);
            Assert.IsFalse(session.RecognitionRunning);

            session.CaptureRegionSelectionCancelled();
            Assert.AreEqual("Hint_CaptureRegionMissing", session.HintResourceKey);

            session.Refresh(hasCaptureRegion: true, foundText: false);
            Assert.AreEqual("Hint_RefreshFoundNoText", session.HintResourceKey);

            session.Refresh(hasCaptureRegion: false, foundText: false);
            Assert.AreEqual("Hint_CaptureRegionMissing", session.HintResourceKey);

            session.PreviewCaptureRegion(hasCaptureRegion: false);
            Assert.AreEqual("Hint_CaptureRegionMissing", session.HintResourceKey);
        }

        [TestMethod]
        public void OcrAndMatchMisses_DoNotHint()
        {
            LiveOverlaySession session = CreateSession();

            session.NoteOcrMiss();
            session.NoteMatchMiss();

            Assert.IsFalse(session.HintVisible);
            Assert.IsNull(session.HintResourceKey);

            session.StartRecognition(hasCaptureRegion: true);
            session.NoteOcrMiss();
            session.NoteMatchMiss();

            Assert.AreEqual("Hint_RecognitionRunning", session.HintResourceKey);
            Assert.IsTrue(session.HintVisible);
        }

        [TestMethod]
        public void OperatorActions_SelectTheMatchingHintKeys()
        {
            LiveOverlaySession session = CreateSession();

            session.CaptureRegionSelected();
            Assert.AreEqual("Hint_CaptureRegionBoxed", session.HintResourceKey);

            session.Refresh(hasCaptureRegion: true, foundText: true);
            Assert.AreEqual("Hint_Refreshed", session.HintResourceKey);

            session.ChangeVoiceSpeed(1.5);
            Assert.AreEqual("Hint_VoiceSpeed", session.HintResourceKey);
            CollectionAssert.AreEqual(new object[] { "1.5" }, session.HintFormatArguments);

            session.ChangeVoiceSpeed(1.25);
            Assert.AreEqual("Hint_VoiceSpeed", session.HintResourceKey);
            CollectionAssert.AreEqual(new object[] { "1.25" }, session.HintFormatArguments);

            session.ChangeVoiceSpeed(2.0);
            Assert.AreEqual("Hint_VoiceSpeed", session.HintResourceKey);
            CollectionAssert.AreEqual(new object[] { "2" }, session.HintFormatArguments);
        }

        [TestMethod]
        public void HintDisplayCandidates_LeadWithTheVoicePrimaryPairDisplay()
        {
            LiveOverlaySession session = CreateSession();
            session.SetCapture(0, new OverlayRect(10, 100, 80, 20));
            session.SetDisplay(0, new OverlayRect(10, 500, 800, 200));
            session.SetCapture(1, new OverlayRect(2100, 100, 80, 20));
            session.SetDisplay(1, new OverlayRect(2100, 500, 800, 200));
            session.SetVoicePrimary(session.Pairs[1].Id);

            System.Collections.Generic.IReadOnlyList<OverlayRect> candidates = session.HintDisplayCandidates;

            Assert.AreEqual(2, candidates.Count);
            Assert.AreEqual(2100, candidates[0].X, "The voice-primary pair's display is the first hint anchor candidate.");
            Assert.AreEqual(10, candidates[1].X);
        }

        [TestMethod]
        public void HintDisplayCandidates_KeepInvalidDisplaysInPlace()
        {
            LiveOverlaySession session = CreateSession();
            session.SetCapture(0, new OverlayRect(10, 100, 80, 20));
            session.SetDisplay(0, OverlayRect.Invalid);
            session.SetCapture(1, new OverlayRect(2100, 100, 80, 20));
            session.SetDisplay(1, new OverlayRect(2100, 500, 800, 200));

            System.Collections.Generic.IReadOnlyList<OverlayRect> candidates = session.HintDisplayCandidates;

            Assert.AreEqual(2, candidates.Count);
            Assert.IsFalse(candidates[0].IsValid);
            Assert.IsTrue(candidates[1].IsValid);
        }

        [TestMethod]
        public void HintDisplayCandidates_NoPairs_IsEmpty()
        {
            LiveOverlaySession session = CreateSession();

            Assert.AreEqual(0, session.HintDisplayCandidates.Count);
        }

        private static LiveOverlaySession CreateSession(Func<DateTime> utcNow = null)
        {
            return new LiveOverlaySession(new MemoryOcrIntervalStore(), utcNow);
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
    }
}
