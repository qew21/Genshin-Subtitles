using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Config;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace GI_Test
{
    [TestClass]
    public class TestLiveOverlaySessionAppliedGame
    {
        [TestMethod]
        public void ApplyToAnotherGame_LoadsThatGamesLayout_WithoutStoppingRecognition()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            var store = CreateGenshinStore();
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), store, () => now);
            session.StartRecognition(hasCaptureRegion: true);

            session.Beat(ExtraPathSample.DialogueOptionsReady(), PairFrameSample.Unchanged());
            Assert.AreEqual(LiveOverlaySession.DialogueOptionsOcrSlot, session.BusyOcrSlot);
            session.CompleteOcr(miss: true);

            session.ApplyGame("StarRail");

            Assert.AreEqual("StarRail", session.AppliedGame);
            Assert.IsTrue(session.RecognitionRunning);
            Assert.AreEqual(0, session.Pairs.Count);
            Assert.IsFalse(session.DialogueOptionDisplay.IsValid);
            Assert.IsFalse(session.DialogueOptionScanOn);
            Assert.IsFalse(session.AllowsDialogueOptionScan);
            Assert.IsTrue(session.DarkScreenScanOn);

            now = now.AddMilliseconds(400);
            session.Beat(ExtraPathSample.DialogueOptionsReady(), PairFrameSample.Unchanged());
            Assert.IsNull(session.BusyOcrSlot);
            Assert.AreEqual(0, session.OcrQueue.Count);
        }

        [TestMethod]
        public void ApplyBackToGenshin_RestoresRegionPairsVoicePrimaryAndExtraPaths()
        {
            DateTime now = new DateTime(2026, 9, 7, 13, 0, 0, DateTimeKind.Utc);
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), CreateGenshinStore(), () => now);

            session.ApplyGame("StarRail");
            Assert.AreEqual(0, session.Pairs.Count);
            Assert.IsFalse(session.AllowsDialogueOptionScan);

            session.ApplyGame("Genshin");

            Assert.AreEqual("Genshin", session.AppliedGame);
            Assert.AreEqual(1, session.Pairs.Count);
            Assert.AreEqual(10, session.Pairs[0].Capture.X);
            Assert.AreEqual(10, session.Pairs[0].Display.X);
            Assert.AreEqual(1, session.VoicePrimaryId);
            Assert.AreEqual(200, session.DialogueOptionDisplay.X);
            Assert.IsTrue(session.DialogueOptionScanOn);
            Assert.IsTrue(session.AllowsDialogueOptionScan);

            now = now.AddMilliseconds(400);
            session.Beat(ExtraPathSample.DialogueOptionsReady(), PairFrameSample.Unchanged());
            Assert.AreEqual(LiveOverlaySession.DialogueOptionsOcrSlot, session.BusyOcrSlot);
        }

        [TestMethod]
        public void ApplyBackToGenshin_LeavesDialogueOptionScanGateClosed_WhenLayoutToggleIsOff()
        {
            var store = CreateGenshinStore();
            store.WriteDialogueOptionScan(false);
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), store);

            session.ApplyGame("StarRail");
            session.ApplyGame("Genshin");

            Assert.IsFalse(session.DialogueOptionScanOn);
            Assert.IsFalse(session.AllowsDialogueOptionScan);
        }

        [TestMethod]
        public void ApplyToAnotherGame_ClosesGenshinLocalVoiceGate_UntilGenshinIsAppliedAgain()
        {
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), CreateGenshinStore());

            Assert.IsTrue(session.AllowsGenshinLocalVoice);

            session.ApplyGame("StarRail");
            Assert.IsFalse(session.AllowsGenshinLocalVoice);

            session.ApplyGame("Genshin");
            Assert.IsTrue(session.AllowsGenshinLocalVoice);
        }

        [TestMethod]
        public void ApplyThatChangesGame_ClearsOcrTranslationMatchCache()
        {
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), CreateGenshinStore());
            session.RememberMatch("ocr-line", "genshin-translation", "voice-key");

            Assert.IsTrue(session.TryGetCachedMatch("ocr-line", out string cached));
            Assert.AreEqual("genshin-translation", cached);
            Assert.AreEqual("voice-key", session.GetCachedMatchKey(cached));

            session.ApplyGame("StarRail");

            Assert.IsFalse(session.TryGetCachedMatch("ocr-line", out _));
        }

        [TestMethod]
        public void ApplyThatDoesNotChangeGame_LeavesOverlayLayoutAndSubtitles()
        {
            DateTime now = new DateTime(2026, 9, 7, 14, 0, 0, DateTimeKind.Utc);
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), CreateGenshinStore(), () => now);
            session.RememberMatch("ocr-line", "genshin-translation", "voice-key");
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "keep-me", header: "speaker");

            session.ApplyGame("Genshin");

            Assert.AreEqual(1, session.Pairs.Count);
            Assert.AreEqual(10, session.Pairs[0].Capture.X);
            Assert.AreEqual("keep-me", session.PairBodies[0].Content);
            Assert.AreEqual("speaker", session.PairBodies[0].Header);
            Assert.IsTrue(session.TryGetCachedMatch("ocr-line", out string cached));
            Assert.AreEqual("genshin-translation", cached);
            Assert.AreEqual("voice-key", session.GetCachedMatchKey(cached));
        }

        [TestMethod]
        public void ApplyThatChangesGame_ClearsPairAndExtraPathSubtitles()
        {
            DateTime now = new DateTime(2026, 9, 7, 15, 0, 0, DateTimeKind.Utc);
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), CreateGenshinStore(), () => now);
            OverlayRect band = new OverlayRect(40, 80, 400, 60);

            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "cutscene", header: "narrator");
            now = now.AddMilliseconds(400);
            session.Tick();
            session.CompleteOcr(miss: false, content: "speaker-line", header: "paimon");
            session.Beat(ExtraPathSample.DialogueChoice("跳过"));

            Assert.AreEqual("cutscene", session.DarkScreenBody.Content);
            Assert.AreEqual("speaker-line", session.PairBodies[0].Content);
            Assert.AreEqual("◆ 跳过", session.DialogueChoiceEcho.Content);

            session.ApplyGame("StarRail");

            Assert.AreEqual(0, session.PairBodies.Count);
            Assert.AreEqual(string.Empty, session.DarkScreenBody.Content);
            Assert.IsFalse(session.DarkScreenBody.Visible);
            Assert.AreEqual(string.Empty, session.DialogueChoiceEcho.Content);
            Assert.IsFalse(session.DialogueChoiceEcho.Visible);
        }

        [TestMethod]
        public void ApplyThatChangesGame_CancelsAdd_AndDoesNotWriteItIntoEitherLayout()
        {
            var store = CreateGenshinStore();
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), store);
            Assert.IsTrue(session.TryStartAdd());
            session.SetAddCapture(new OverlayRect(90, 90, 40, 20));
            session.SetAddDisplay(new OverlayRect(90, 120, 40, 20));

            session.ApplyGame("StarRail");

            Assert.IsFalse(session.AddInProgress);
            Assert.AreEqual(0, session.Pairs.Count);

            session.ApplyGame("Genshin");

            Assert.AreEqual(1, session.Pairs.Count);
            Assert.AreEqual(10, session.Pairs[0].Capture.X);
        }

        [TestMethod]
        public void ApplyThatChangesGame_CancelsRegionAdjustAndDropsPreviewAndOcrQueue()
        {
            DateTime now = new DateTime(2026, 9, 7, 16, 0, 0, DateTimeKind.Utc);
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), CreateGenshinStore(), () => now);
            session.PreviewCaptureRegion(hasCaptureRegion: true, darkScreenScanOn: true);
            Assert.IsTrue(session.PreviewOutlines.Count > 0);
            Assert.IsTrue(session.TryToggleRegionAdjust(1));
            Assert.IsFalse(session.IsClickThrough);

            OverlayRect band = new OverlayRect(40, 80, 400, 60);
            session.Beat(
                ExtraPathSample.DarkScreenCandidate(band, needsOcr: true),
                PairFrameSample.ChangedAndStable());
            Assert.AreEqual(LiveOverlaySession.DarkScreenOcrSlot, session.BusyOcrSlot);
            Assert.AreEqual(1, session.OcrQueue.Count);

            session.ApplyGame("StarRail");

            Assert.IsTrue(session.IsClickThrough);
            Assert.AreEqual(0, session.ArmedPairId);
            Assert.AreEqual(0, session.PreviewOutlines.Count);
            Assert.IsNull(session.BusyOcrSlot);
            Assert.AreEqual(0, session.OcrQueue.Count);
        }

        [TestMethod]
        public void ApplyThatChangesGame_LetsCurrentVoiceLineFinish()
        {
            DateTime now = new DateTime(2026, 9, 7, 17, 0, 0, DateTimeKind.Utc);
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), CreateGenshinStore(), () => now);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "spoken", header: "paimon");

            Assert.IsTrue(session.VoicePlaybackActive);
            int token = session.VoicePlaybackToken;

            session.ApplyGame("StarRail");

            Assert.IsTrue(session.VoicePlaybackActive);
            Assert.AreEqual(token, session.VoicePlaybackToken);
            VoicePlayRequest request = session.TakeVoicePlayRequest();
            Assert.IsNotNull(request);
            Assert.AreEqual("spoken", request.Content);

            session.NoteVoicePlaybackEnded();
            Assert.IsFalse(session.VoicePlaybackActive);
        }

        [TestMethod]
        public void BoxingAfterApply_WritesIncomingLayout_WithoutRewritingOutgoing()
        {
            var settings = new MemoryConfigMap();
            var store = CreateGenshinStore(settings);
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), store);

            session.ApplyGame("StarRail");
            session.SetCapture(0, new OverlayRect(40, 50, 60, 20));
            session.SetDisplay(0, new OverlayRect(40, 80, 60, 20));
            session.SetVoicePrimary(session.Pairs[0].Id);

            session.ApplyGame("Genshin");
            Assert.AreEqual(1, session.Pairs.Count);
            Assert.AreEqual(10, session.Pairs[0].Capture.X);
            Assert.AreEqual(1, session.VoicePrimaryId);

            session.ApplyGame("StarRail");
            Assert.AreEqual(1, session.Pairs.Count);
            Assert.AreEqual(40, session.Pairs[0].Capture.X);
            Assert.AreEqual(40, session.Pairs[0].Display.X);
        }

        private static ConfigRegionPairStore CreateGenshinStore(MemoryConfigMap settings = null)
        {
            settings = settings ?? new MemoryConfigMap();
            var store = new ConfigRegionPairStore(settings, "Genshin");
            store.WritePairs(new[]
            {
                new RegionPairRecord
                {
                    Id = 1,
                    Capture = new OverlayRect(10, 20, 80, 20),
                    Display = new OverlayRect(10, 50, 80, 20)
                }
            });
            store.WriteVoicePrimaryId(1);
            store.WriteNextPairId(2);
            store.WriteDialogueOptionScan(true);
            store.WriteDialogueOptionDisplay(new OverlayRect(200, 300, 160, 40));
            return store;
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

        private sealed class MemoryConfigMap : IConfigMap
        {
            private readonly Dictionary<string, JToken> _settings = new Dictionary<string, JToken>();

            public bool Contains(string key)
            {
                return _settings.ContainsKey(key);
            }

            public T Get<T>(string key, T defaultValue)
            {
                if (_settings.TryGetValue(key, out JToken token))
                {
                    try
                    {
                        return token.ToObject<T>();
                    }
                    catch
                    {
                    }
                }

                return defaultValue;
            }

            public void Set<T>(string key, T value)
            {
                _settings[key] = value == null ? JValue.CreateNull() : JToken.FromObject(value);
            }

            public void Remove(string key)
            {
                _settings.Remove(key);
            }
        }
    }
}
