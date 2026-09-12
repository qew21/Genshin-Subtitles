using System;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestLiveOverlaySessionExtraPaths
    {
        [TestMethod]
        public void ExtraPathOnABeat_DoesNotSkipPairMissKeep()
        {
            DateTime now = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(1, () => now);
            OverlayRect band = new OverlayRect(40, 80, 400, 60);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "keep-me");

            now = now.AddMilliseconds(400);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "cutscene");
            now = now.AddMilliseconds(400);
            session.Tick();
            session.CompleteOcr(miss: true);

            Assert.AreEqual("keep-me", session.PairBodies[0].Content);
            Assert.AreEqual("cutscene", session.DarkScreenBody.Content);
        }

        [TestMethod]
        public void ExtraPathOnABeat_DoesNotSkipPairDiffsOrClears()
        {
            DateTime now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(2, () => now);
            OverlayRect band = new OverlayRect(40, 80, 400, 60);

            session.Beat(PairFrameSample.ChangedAndStable(), PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "pair-one");
            now = now.AddMilliseconds(400);
            session.Tick();
            session.CompleteOcr(miss: false, content: "pair-two");

            now = now.AddMilliseconds(400);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true)
                    .WithDialogueOptionsReady(),
                PairFrameSample.StableNoText(),
                PairFrameSample.ChangedAndStable());

            Assert.AreEqual(string.Empty, session.PairBodies[0].Content);
            Assert.AreEqual("pair-two", session.PairBodies[1].Content);
            Assert.AreEqual(LiveOverlaySession.DarkScreenOcrSlot, session.BusyOcrSlot);
            CollectionAssert.AreEqual(
                new[] { LiveOverlaySession.DialogueOptionsOcrSlot, 1 },
                System.Linq.Enumerable.ToArray(session.OcrQueue));
        }

        [TestMethod]
        public void ContendedBeat_EnqueuesDarkScreenThenDialogueOptionsThenPairs()
        {
            DateTime now = new DateTime(2026, 6, 1, 13, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(2, () => now);
            OverlayRect band = new OverlayRect(10, 20, 300, 40);

            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true)
                    .WithDialogueOptionsReady(),
                PairFrameSample.ChangedAndStable(),
                PairFrameSample.ChangedAndStable());

            Assert.AreEqual(LiveOverlaySession.DarkScreenOcrSlot, session.BusyOcrSlot);
            Assert.IsNull(session.BusyOcrPairIndex);
            CollectionAssert.AreEqual(
                new[] { LiveOverlaySession.DialogueOptionsOcrSlot, 0, 1 },
                System.Linq.Enumerable.ToArray(session.OcrQueue));

            session.CompleteOcr(miss: false, content: "cutscene", header: "narrator");
            Assert.AreEqual("cutscene", session.DarkScreenBody.Content);
            Assert.AreEqual("narrator", session.DarkScreenBody.Header);
            Assert.AreEqual(10, session.DarkScreenBody.Display.X);
            Assert.IsTrue(session.DarkScreenBody.Visible);
            Assert.IsNull(session.BusyOcrSlot);
            CollectionAssert.AreEqual(
                new[] { LiveOverlaySession.DialogueOptionsOcrSlot, 0, 1 },
                System.Linq.Enumerable.ToArray(session.OcrQueue));

            now = now.AddMilliseconds(400);
            session.Tick();
            Assert.AreEqual(LiveOverlaySession.DialogueOptionsOcrSlot, session.BusyOcrSlot);

            session.CompleteOcr(miss: false, content: "option-list");
            Assert.AreEqual("cutscene", session.DarkScreenBody.Content);
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content, "Dialogue-option OCR is not in-place overlay text.");

            now = now.AddMilliseconds(400);
            session.Tick();
            Assert.AreEqual(0, session.BusyOcrPairIndex);
            session.CompleteOcr(miss: false, content: "pair-one");
            Assert.AreEqual("pair-one", session.PairBodies[0].Content);
            Assert.IsTrue(session.PairBodies[0].RecognitionOrder > session.DarkScreenBody.RecognitionOrder);
        }

        [TestMethod]
        public void DialogueChoiceEcho_SitsOnVoicePrimary_AndIsOmittedWhenNone()
        {
            DateTime now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(2, () => now);
            session.SetVoicePrimary(2);

            session.Beat(ExtraPathSample.DialogueChoice("跳过"));

            ExtraPathBody echo = session.DialogueChoiceEcho;
            Assert.IsTrue(echo.Visible);
            Assert.AreEqual("◆ 跳过", echo.Content);
            Assert.AreEqual(session.Pairs[1].Display.X, echo.Display.X);
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content, "Echo must not replace the pair body.");
            Assert.AreEqual(string.Empty, session.PairBodies[1].Content, "Echo must not replace the pair body.");
            Assert.IsFalse(session.HintVisible);

            now = now.AddSeconds(3);
            session.Tick();
            Assert.IsFalse(session.DialogueChoiceEcho.Visible);
            Assert.AreEqual(string.Empty, session.DialogueChoiceEcho.Content);

            session.DeletePair(1);
            session.DeletePair(2);
            Assert.AreEqual(0, session.VoicePrimaryId);

            session.Beat(ExtraPathSample.DialogueChoice("再跳过"));
            Assert.IsFalse(session.DialogueChoiceEcho.Visible);
            Assert.AreEqual(string.Empty, session.DialogueChoiceEcho.Content);
        }

        [TestMethod]
        public void ExtraPathVoice_DoesNotRebindOrInterrupt()
        {
            DateTime now = new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(2, () => now);
            OverlayRect band = new OverlayRect(8, 9, 200, 30);

            session.Beat(PairFrameSample.ChangedAndStable(), PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, content: "primary-line");
            VoicePlayRequest primary = session.TakeVoicePlayRequest();
            Assert.IsNotNull(primary);
            Assert.IsFalse(primary.ExtraPath);
            Assert.AreEqual(1, primary.PairId);
            Assert.AreEqual(1, session.VoicePrimaryId);
            int token = session.VoicePlaybackToken;

            now = now.AddMilliseconds(400);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged(),
                PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, content: "cutscene");
            Assert.IsNull(session.TakeVoicePlayRequest());
            Assert.AreEqual(1, session.VoicePrimaryId);
            Assert.AreEqual(token, session.VoicePlaybackToken);
            Assert.IsTrue(session.VoicePlaybackActive);

            session.NoteVoicePlaybackEnded();
            now = now.AddMilliseconds(400);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged(),
                PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, content: "cutscene");
            VoicePlayRequest darkVoice = session.TakeVoicePlayRequest();
            Assert.IsNotNull(darkVoice);
            Assert.IsTrue(darkVoice.ExtraPath);
            Assert.AreEqual(1, session.VoicePrimaryId);
            Assert.AreEqual("cutscene", darkVoice.Content);

            session.NoteVoicePlaybackEnded();
            now = now.AddMilliseconds(400);
            session.Beat(ExtraPathSample.DialogueChoice("同意"));
            VoicePlayRequest echoVoice = session.TakeVoicePlayRequest();
            Assert.IsNotNull(echoVoice);
            Assert.IsTrue(echoVoice.ExtraPath);
            Assert.AreEqual(1, session.VoicePrimaryId);
            Assert.AreEqual("◆ 同意", echoVoice.Content);
            Assert.IsTrue(session.VoicePlaybackActive);

            now = now.AddMilliseconds(400);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged(),
                PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, content: "later-card");
            Assert.IsNull(session.TakeVoicePlayRequest());
            Assert.AreEqual(1, session.VoicePrimaryId);
        }

        [TestMethod]
        public void DarkScreenEnd_ClearsOnlyThatLine()
        {
            DateTime now = new DateTime(2026, 6, 4, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(2, () => now);
            OverlayRect band = new OverlayRect(12, 24, 180, 36);

            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.ChangedAndStable(),
                PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, content: "card");
            now = now.AddMilliseconds(400);
            session.Tick();
            session.CompleteOcr(miss: false, content: "speaker");
            session.Beat(ExtraPathSample.DialogueChoice("走开"));

            Assert.AreEqual("card", session.DarkScreenBody.Content);
            Assert.AreEqual("speaker", session.PairBodies[0].Content);
            Assert.AreEqual("◆ 走开", session.DialogueChoiceEcho.Content);

            session.Beat(
                ExtraPathSample.DarkScreenEnded(),
                PairFrameSample.Unchanged(),
                PairFrameSample.Unchanged());

            Assert.AreEqual(string.Empty, session.DarkScreenBody.Content);
            Assert.IsFalse(session.DarkScreenBody.Visible);
            Assert.AreEqual("speaker", session.PairBodies[0].Content);
            Assert.AreEqual("◆ 走开", session.DialogueChoiceEcho.Content);

            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged(),
                PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, content: "card-again");
            session.Beat(
                ExtraPathSample.DarkScreenWithoutCandidate(),
                PairFrameSample.Unchanged(),
                PairFrameSample.Unchanged());
            Assert.AreEqual(string.Empty, session.DarkScreenBody.Content);
            Assert.AreEqual("speaker", session.PairBodies[0].Content);
        }

        [TestMethod]
        public void ExtraPathTexts_HideWithSubtitles_AndAreNotHints()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);
            OverlayRect band = new OverlayRect(1, 2, 100, 20);

            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "card");
            session.Beat(ExtraPathSample.DialogueChoice("好的"));

            Assert.IsFalse(session.HintVisible);
            Assert.IsNull(session.HintResourceKey);
            Assert.IsTrue(session.DarkScreenBody.Visible);
            Assert.IsTrue(session.DialogueChoiceEcho.Visible);

            session.HideSubtitles();

            Assert.IsFalse(session.DarkScreenBody.Visible);
            Assert.IsFalse(session.DialogueChoiceEcho.Visible);
            Assert.AreEqual("card", session.DarkScreenBody.Content);
            Assert.AreEqual("◆ 好的", session.DialogueChoiceEcho.Content);
            Assert.IsTrue(session.HintVisible);
            Assert.AreEqual("Hint_SubtitlesHidden", session.HintResourceKey);

            session.ShowSubtitles();
            Assert.IsTrue(session.DarkScreenBody.Visible);
            Assert.IsTrue(session.DialogueChoiceEcho.Visible);
        }

        [TestMethod]
        public void DarkScreen_PinnedDisplay_PlacesBodyOnPin_NotCandidateBand()
        {
            OverlayRect band = new OverlayRect(40, 80, 400, 60);
            OverlayRect pin = new OverlayRect(10, 20, 300, 50);
            LiveOverlaySession session = CreateSessionWithPairs(1);
            session.SetDarkScreenDisplay(pin);

            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, content: "cutscene");

            Assert.AreEqual(10, session.DarkScreenBody.Display.X);
            Assert.AreEqual(20, session.DarkScreenBody.Display.Y);
            Assert.AreEqual(300, session.DarkScreenBody.Display.Width);
            Assert.AreEqual(50, session.DarkScreenBody.Display.Height);
            Assert.AreEqual("cutscene", session.DarkScreenBody.Content);
            Assert.IsTrue(session.DarkScreenDisplay.IsValid);
        }

        [TestMethod]
        public void DarkScreen_ClearDisplay_ReturnsBodyToCandidateBand()
        {
            OverlayRect band = new OverlayRect(40, 80, 400, 60);
            OverlayRect pin = new OverlayRect(10, 20, 300, 50);
            LiveOverlaySession session = CreateSessionWithPairs(1);
            session.SetDarkScreenDisplay(pin);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.Unchanged());
            session.CompleteOcr(miss: false, content: "cutscene");

            session.ClearDarkScreenDisplay();

            Assert.IsFalse(session.DarkScreenDisplay.IsValid);
            Assert.AreEqual(40, session.DarkScreenBody.Display.X);
            Assert.AreEqual(80, session.DarkScreenBody.Display.Y);
            Assert.AreEqual("cutscene", session.DarkScreenBody.Content);
        }

        [TestMethod]
        public void Preview_PinnedDarkScreen_DrawsSolidDisplay_NotCandidate()
        {
            OverlayRect band = new OverlayRect(40, 80, 400, 60);
            OverlayRect pin = new OverlayRect(10, 20, 300, 50);
            LiveOverlaySession session = CreateSessionWithPairs(1);
            session.SetDarkScreenDisplay(pin);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: false),
                PairFrameSample.Unchanged());

            session.PreviewCaptureRegion(hasCaptureRegion: true, darkScreenScanOn: true);

            Assert.AreEqual(3, session.PreviewOutlines.Count);
            Assert.AreEqual(RegionOutlineKind.Pair, session.PreviewOutlines[0].Kind);
            Assert.AreEqual(RegionOutlineKind.Pair, session.PreviewOutlines[1].Kind);
            RegionOutline extra = session.PreviewOutlines[2];
            Assert.AreEqual(RegionOutlineKind.DarkScreenDisplay, extra.Kind);
            Assert.AreEqual(0, extra.PairOrdinal);
            Assert.IsTrue(extra.IsDisplay);
            Assert.IsFalse(extra.Dashed);
            Assert.AreEqual(10, extra.Rect.X);
            Assert.AreEqual(20, extra.Rect.Y);
        }

        [TestMethod]
        public void Preview_LiveCandidate_DrawsDashedBand_OnlyWhenScanOnAndUnpinned()
        {
            OverlayRect band = new OverlayRect(40, 80, 400, 60);
            LiveOverlaySession session = CreateSessionWithPairs(1);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: false),
                PairFrameSample.Unchanged());

            session.PreviewCaptureRegion(hasCaptureRegion: true, darkScreenScanOn: true);
            Assert.AreEqual(3, session.PreviewOutlines.Count);
            RegionOutline candidate = session.PreviewOutlines[2];
            Assert.AreEqual(RegionOutlineKind.DarkScreenCandidate, candidate.Kind);
            Assert.AreEqual(0, candidate.PairOrdinal);
            Assert.IsFalse(candidate.IsDisplay);
            Assert.IsTrue(candidate.Dashed);
            Assert.AreEqual(40, candidate.Rect.X);

            session.PreviewCaptureRegion(hasCaptureRegion: true, darkScreenScanOn: false);
            Assert.AreEqual(2, session.PreviewOutlines.Count);
            Assert.AreEqual(RegionOutlineKind.Pair, session.PreviewOutlines[0].Kind);
            Assert.AreEqual(RegionOutlineKind.Pair, session.PreviewOutlines[1].Kind);

            session.SetDarkScreenDisplay(new OverlayRect(10, 20, 300, 50));
            session.PreviewCaptureRegion(hasCaptureRegion: true, darkScreenScanOn: true);
            Assert.AreEqual(3, session.PreviewOutlines.Count);
            Assert.AreEqual(RegionOutlineKind.DarkScreenDisplay, session.PreviewOutlines[2].Kind);
        }

        [TestMethod]
        public void DialogueChoiceEcho_DetachesToDisplay_WithNoDuplicate()
        {
            OverlayRect pin = new OverlayRect(200, 300, 160, 40);
            LiveOverlaySession session = CreateSessionWithPairs(2);
            session.SetVoicePrimary(2);
            session.SetDialogueOptionDisplay(pin);

            session.Beat(ExtraPathSample.DialogueChoice("跳过"));

            ExtraPathBody echo = session.DialogueChoiceEcho;
            Assert.IsTrue(echo.Visible);
            Assert.AreEqual("◆ 跳过", echo.Content);
            Assert.AreEqual(200, echo.Display.X);
            Assert.AreEqual(300, echo.Display.Y);
            Assert.AreEqual(160, echo.Display.Width);
            Assert.AreNotEqual(session.Pairs[1].Display.X, echo.Display.X);
            Assert.IsFalse(echo.FollowsVoicePrimary);
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content);
            Assert.AreEqual(string.Empty, session.PairBodies[1].Content);
            Assert.IsFalse(session.HintVisible);

            session.ClearDialogueOptionDisplay();
            echo = session.DialogueChoiceEcho;
            Assert.IsTrue(echo.Visible);
            Assert.AreEqual("◆ 跳过", echo.Content);
            Assert.AreEqual(session.Pairs[1].Display.X, echo.Display.X);
            Assert.AreEqual(session.Pairs[1].Display.Y, echo.Display.Y);
            Assert.IsTrue(echo.FollowsVoicePrimary);
        }

        [TestMethod]
        public void DialogueChoiceEcho_Detached_DoesNotNeedVoicePrimaryDisplay()
        {
            OverlayRect pin = new OverlayRect(8, 9, 120, 30);
            var store = new MemoryRegionPairStore
            {
                StoredPairs =
                {
                    new RegionPairRecord
                    {
                        Id = 1,
                        Capture = new OverlayRect(0, 10, 80, 20),
                        Display = OverlayRect.Invalid
                    }
                },
                VoicePrimaryId = 1,
                NextPairId = 2
            };
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), store);
            session.SetDialogueOptionDisplay(pin);
            session.Beat(ExtraPathSample.DialogueChoice("同意"));

            ExtraPathBody echo = session.DialogueChoiceEcho;
            Assert.IsTrue(echo.Visible);
            Assert.AreEqual("◆ 同意", echo.Content);
            Assert.AreEqual(8, echo.Display.X);
        }

        [TestMethod]
        public void ExtraPathAdjust_DisabledUntilBoxed_OutlinesDisplayOnly()
        {
            OverlayRect pin = new OverlayRect(10, 20, 300, 50);
            LiveOverlaySession session = CreateSessionWithPairs(1);

            Assert.IsFalse(session.TryToggleDarkScreenDisplayAdjust());
            Assert.IsFalse(session.TryToggleDialogueOptionDisplayAdjust());
            Assert.IsTrue(session.IsClickThrough);
            Assert.AreEqual(OverlayAdjustTarget.None, session.ArmedTarget);
            Assert.AreEqual(0, session.AdjustOutlines.Count);

            session.SetDarkScreenDisplay(pin);
            Assert.IsTrue(session.TryToggleDarkScreenDisplayAdjust());
            Assert.IsFalse(session.IsClickThrough);
            Assert.AreEqual(OverlayAdjustTarget.DarkScreenDisplay, session.ArmedTarget);
            Assert.AreEqual(0, session.ArmedPairId);
            Assert.AreEqual(1, session.AdjustOutlines.Count);
            Assert.AreEqual(RegionOutlineKind.DarkScreenDisplay, session.AdjustOutlines[0].Kind);
            Assert.IsTrue(session.AdjustOutlines[0].IsDisplay);
            Assert.AreEqual(10, session.AdjustOutlines[0].Rect.X);

            Assert.IsTrue(session.TryToggleDarkScreenDisplayAdjust());
            Assert.IsTrue(session.IsClickThrough);
            Assert.AreEqual(OverlayAdjustTarget.None, session.ArmedTarget);

            session.SetDialogueOptionDisplay(new OverlayRect(200, 300, 160, 40));
            Assert.IsTrue(session.TryToggleDialogueOptionDisplayAdjust());
            Assert.AreEqual(OverlayAdjustTarget.DialogueOptionDisplay, session.ArmedTarget);
            Assert.AreEqual(1, session.AdjustOutlines.Count);
            Assert.AreEqual(RegionOutlineKind.DialogueOptionDisplay, session.AdjustOutlines[0].Kind);
            Assert.IsTrue(session.AdjustOutlines[0].IsDisplay);
            Assert.AreEqual(200, session.AdjustOutlines[0].Rect.X);

            session.CancelRegionAdjust();
            Assert.IsTrue(session.IsClickThrough);
            Assert.AreEqual(0, session.AdjustOutlines.Count);
        }

        [TestMethod]
        public void ExtraPathAdjust_SwitchingFromPair_OutlinesOnlyTheExtraDisplay()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);
            session.SetDarkScreenDisplay(new OverlayRect(10, 20, 300, 50));

            Assert.IsTrue(session.TryToggleRegionAdjust(session.Pairs[0].Id));
            Assert.AreEqual(2, session.AdjustOutlines.Count);

            Assert.IsTrue(session.TryToggleDarkScreenDisplayAdjust());
            Assert.AreEqual(OverlayAdjustTarget.DarkScreenDisplay, session.ArmedTarget);
            Assert.AreEqual(0, session.ArmedPairId);
            Assert.AreEqual(1, session.AdjustOutlines.Count);
            Assert.AreEqual(RegionOutlineKind.DarkScreenDisplay, session.AdjustOutlines[0].Kind);
        }

        [TestMethod]
        public void Preview_DialogueOptionDisplay_DrawsPurpleOutline()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);
            session.SetDialogueOptionDisplay(new OverlayRect(200, 300, 160, 40));

            session.PreviewCaptureRegion(hasCaptureRegion: true);

            Assert.AreEqual(3, session.PreviewOutlines.Count);
            RegionOutline extra = session.PreviewOutlines[2];
            Assert.AreEqual(RegionOutlineKind.DialogueOptionDisplay, extra.Kind);
            Assert.AreEqual(0, extra.PairOrdinal);
            Assert.IsTrue(extra.IsDisplay);
            Assert.IsFalse(extra.Dashed);
            Assert.AreEqual(200, extra.Rect.X);
        }

        [TestMethod]
        public void DarkScreen_ScanOff_KeepsBoxedDisplay()
        {
            OverlayRect pin = new OverlayRect(10, 20, 300, 50);
            OverlayRect band = new OverlayRect(40, 80, 400, 60);
            LiveOverlaySession session = CreateSessionWithPairs(1);
            session.SetDarkScreenDisplay(pin);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: false),
                PairFrameSample.Unchanged());

            session.PreviewCaptureRegion(hasCaptureRegion: true, darkScreenScanOn: false);

            Assert.IsTrue(session.DarkScreenDisplay.IsValid);
            Assert.AreEqual(10, session.DarkScreenDisplay.X);
            Assert.AreEqual(3, session.PreviewOutlines.Count);
            Assert.AreEqual(RegionOutlineKind.DarkScreenDisplay, session.PreviewOutlines[2].Kind);
            Assert.AreNotEqual(RegionOutlineKind.DarkScreenCandidate, session.PreviewOutlines[2].Kind);
        }

        [TestMethod]
        public void DarkScreen_BoxedDisplay_PersistsAcrossSessions()
        {
            OverlayRect pin = new OverlayRect(12, 24, 180, 36);
            var store = new MemoryRegionPairStore
            {
                StoredPairs =
                {
                    new RegionPairRecord
                    {
                        Id = 1,
                        Capture = new OverlayRect(0, 10, 80, 20),
                        Display = new OverlayRect(0, 40, 80, 20)
                    }
                },
                VoicePrimaryId = 1,
                NextPairId = 2
            };
            var first = new LiveOverlaySession(new MemoryOcrIntervalStore(), store);
            first.SetDarkScreenDisplay(pin);

            Assert.AreEqual(12, store.DarkScreenDisplay.X);
            Assert.AreEqual(24, store.DarkScreenDisplay.Y);

            var reloaded = new LiveOverlaySession(new MemoryOcrIntervalStore(), store);
            Assert.AreEqual(12, reloaded.DarkScreenDisplay.X);
            Assert.AreEqual(36, reloaded.DarkScreenDisplay.Height);
            Assert.IsTrue(reloaded.DarkScreenDisplay.IsValid);
        }

        [TestMethod]
        public void ExtraPathScanToggles_LoadFromStore_AndPersistAcrossSessions()
        {
            var store = new MemoryRegionPairStore
            {
                StoredPairs =
                {
                    new RegionPairRecord
                    {
                        Id = 1,
                        Capture = new OverlayRect(0, 10, 80, 20),
                        Display = new OverlayRect(0, 40, 80, 20)
                    }
                },
                VoicePrimaryId = 1,
                NextPairId = 2,
                DarkScreenScan = false,
                DialogueOptionScan = true
            };

            var first = new LiveOverlaySession(new MemoryOcrIntervalStore(), store);
            Assert.IsFalse(first.DarkScreenScanOn);
            Assert.IsTrue(first.DialogueOptionScanOn);

            first.SetDarkScreenScan(true);
            first.SetDialogueOptionScan(false);
            Assert.IsTrue(store.DarkScreenScan);
            Assert.IsFalse(store.DialogueOptionScan);

            var reloaded = new LiveOverlaySession(new MemoryOcrIntervalStore(), store);
            Assert.IsTrue(reloaded.DarkScreenScanOn);
            Assert.IsFalse(reloaded.DialogueOptionScanOn);
        }

        [TestMethod]
        public void UnconfiguredStore_DarkScreenScanDefaultsOn()
        {
            var store = new MemoryRegionPairStore();
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), store);
            Assert.IsTrue(session.DarkScreenScanOn);
            Assert.IsFalse(session.DialogueOptionScanOn);
        }

        [TestMethod]
        public void NoValidCapture_DoesNotRunExtraPaths()
        {
            var pairs = new MemoryRegionPairStore();
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);
            OverlayRect band = new OverlayRect(10, 20, 30, 40);

            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true)
                    .WithDialogueOptionsReady()
                    .WithDialogueChoice("无主区"));

            Assert.IsNull(session.BusyOcrSlot);
            Assert.AreEqual(0, session.OcrQueue.Count);
            Assert.IsFalse(session.DarkScreenBody.Visible);
            Assert.AreEqual(string.Empty, session.DarkScreenBody.Content);
            Assert.IsFalse(session.DialogueChoiceEcho.Visible);
            Assert.IsNull(session.TakeVoicePlayRequest());
        }

        private static LiveOverlaySession CreateSessionWithPairs(int pairCount, Func<DateTime> utcNow = null)
        {
            var records = new System.Collections.Generic.List<RegionPairRecord>();
            for (int i = 0; i < pairCount; i++)
            {
                records.Add(new RegionPairRecord
                {
                    Capture = new OverlayRect(i * 100, 10, 80, 20),
                    Display = new OverlayRect(i * 100, 40, 80, 20)
                });
            }

            return new LiveOverlaySession(
                new MemoryOcrIntervalStore(),
                new MemoryRegionPairStore { StoredPairs = records },
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
            public System.Collections.Generic.List<RegionPairRecord> StoredPairs =
                new System.Collections.Generic.List<RegionPairRecord>();
            public int VoicePrimaryId;
            public int NextPairId;
            public int WriteCount;
            public OverlayRect DarkScreenDisplay = OverlayRect.Invalid;
            public OverlayRect DialogueOptionDisplay = OverlayRect.Invalid;

            public System.Collections.Generic.IReadOnlyList<RegionPairRecord> ReadPairs()
            {
                return StoredPairs;
            }

            public LegacyRegionSlots ReadLegacy()
            {
                return Legacy;
            }

            public void WritePairs(System.Collections.Generic.IReadOnlyList<RegionPairRecord> pairs)
            {
                StoredPairs = new System.Collections.Generic.List<RegionPairRecord>(pairs);
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
