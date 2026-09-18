using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestLiveOverlaySessionPreviewAndAdjust
    {
        [TestMethod]
        public void SuccessfulPreview_OutlinesEveryPairForTenSeconds_WithoutAHint()
        {
            DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(2, () => now);

            session.PreviewCaptureRegion(hasCaptureRegion: true);

            Assert.IsFalse(session.HintVisible);
            Assert.IsNull(session.HintResourceKey);
            Assert.IsTrue(session.IsClickThrough);
            Assert.AreEqual(0, session.ArmedPairId);
            Assert.AreEqual(4, session.PreviewOutlines.Count);
            AssertOutline(session.PreviewOutlines[0], 1, 0, 10, false);
            AssertOutline(session.PreviewOutlines[1], 1, 0, 40, true);
            AssertOutline(session.PreviewOutlines[2], 2, 100, 10, false);
            AssertOutline(session.PreviewOutlines[3], 2, 100, 40, true);

            now = now.AddMilliseconds(LiveOverlaySession.PreviewDurationMs - 1);
            session.Tick();
            Assert.AreEqual(4, session.PreviewOutlines.Count);

            now = now.AddMilliseconds(2);
            session.Tick();
            Assert.AreEqual(0, session.PreviewOutlines.Count);
        }

        [TestMethod]
        public void Preview_PairWithNoDisplay_OutlinesCaptureOnly()
        {
            var store = new MemoryRegionPairStore
            {
                StoredPairs =
                {
                    new RegionPairRecord
                    {
                        Id = 1,
                        Capture = new OverlayRect(10, 20, 30, 40),
                        Display = OverlayRect.Invalid
                    },
                    new RegionPairRecord
                    {
                        Id = 2,
                        Capture = new OverlayRect(50, 60, 70, 80),
                        Display = new OverlayRect(51, 90, 70, 80)
                    }
                },
                VoicePrimaryId = 1,
                NextPairId = 3
            };
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), store);

            session.PreviewCaptureRegion(hasCaptureRegion: true);

            Assert.AreEqual(3, session.PreviewOutlines.Count);
            AssertOutline(session.PreviewOutlines[0], 1, 10, 20, false);
            AssertOutline(session.PreviewOutlines[1], 2, 50, 60, false);
            AssertOutline(session.PreviewOutlines[2], 2, 51, 90, true);
            Assert.IsFalse(session.HintVisible);
        }

        [TestMethod]
        public void HideSubtitles_DoesNotHidePreview()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);

            session.PreviewCaptureRegion(hasCaptureRegion: true);
            session.HideSubtitles();

            Assert.IsFalse(session.SubtitlesVisible);
            Assert.AreEqual(2, session.PreviewOutlines.Count);
            Assert.AreEqual("Hint_SubtitlesHidden", session.HintResourceKey);
        }

        [TestMethod]
        public void FailedPreview_HintsAndClearsOutlines()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);

            session.PreviewCaptureRegion(hasCaptureRegion: true);
            Assert.AreEqual(2, session.PreviewOutlines.Count);

            session.PreviewCaptureRegion(hasCaptureRegion: false);

            Assert.AreEqual(0, session.PreviewOutlines.Count);
            Assert.AreEqual("Hint_CaptureRegionMissing", session.HintResourceKey);
        }

        [TestMethod]
        public void DefaultOverlay_IsClickThrough_WithNoArmedTarget()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);

            Assert.IsTrue(session.IsClickThrough);
            Assert.AreEqual(0, session.ArmedPairId);
            Assert.AreEqual(0, session.AdjustOutlines.Count);
        }

        [TestMethod]
        public void ToggleRegionAdjust_ArmsOnePair_UntilButtonOrEsc()
        {
            LiveOverlaySession session = CreateSessionWithPairs(2);
            int firstId = session.Pairs[0].Id;
            int secondId = session.Pairs[1].Id;

            Assert.IsTrue(session.TryToggleRegionAdjust(firstId));
            Assert.IsFalse(session.IsClickThrough);
            Assert.AreEqual(firstId, session.ArmedPairId);
            Assert.AreEqual(2, session.AdjustOutlines.Count);
            AssertOutline(session.AdjustOutlines[0], 1, 0, 10, false);
            AssertOutline(session.AdjustOutlines[1], 1, 0, 40, true);

            Assert.IsTrue(session.TryToggleRegionAdjust(secondId));
            Assert.AreEqual(secondId, session.ArmedPairId);
            Assert.AreEqual(2, session.AdjustOutlines.Count);
            AssertOutline(session.AdjustOutlines[0], 2, 100, 10, false);
            AssertOutline(session.AdjustOutlines[1], 2, 100, 40, true);

            Assert.IsTrue(session.TryToggleRegionAdjust(secondId));
            Assert.IsTrue(session.IsClickThrough);
            Assert.AreEqual(0, session.ArmedPairId);
            Assert.AreEqual(0, session.AdjustOutlines.Count);

            Assert.IsTrue(session.TryToggleRegionAdjust(firstId));
            session.CancelRegionAdjust();
            Assert.IsTrue(session.IsClickThrough);
            Assert.AreEqual(0, session.ArmedPairId);
            Assert.AreEqual(0, session.AdjustOutlines.Count);
        }

        [TestMethod]
        public void ToggleRegionAdjust_CaptureOnlyPair_ArmsCaptureFrameAlone()
        {
            var store = new MemoryRegionPairStore
            {
                StoredPairs =
                {
                    new RegionPairRecord
                    {
                        Id = 1,
                        Capture = new OverlayRect(10, 20, 30, 40),
                        Display = OverlayRect.Invalid
                    }
                },
                VoicePrimaryId = 1,
                NextPairId = 2
            };
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), store);

            Assert.IsTrue(session.TryToggleRegionAdjust(1));
            Assert.IsFalse(session.IsClickThrough);
            Assert.AreEqual(1, session.ArmedPairId);
            Assert.AreEqual(1, session.AdjustOutlines.Count);
            AssertOutline(session.AdjustOutlines[0], 1, 10, 20, false);

            session.SetCapture(0, new OverlayRect(70, 80, 30, 40));
            Assert.AreEqual(1, session.AdjustOutlines.Count);
            AssertOutline(session.AdjustOutlines[0], 1, 70, 80, false);
        }

        [TestMethod]
        public void ToggleRegionAdjust_DisplayOnlyPair_ArmsDisplayFrameAlone()
        {
            var store = new MemoryRegionPairStore
            {
                StoredPairs =
                {
                    new RegionPairRecord
                    {
                        Id = 1,
                        Capture = OverlayRect.Invalid,
                        Display = new OverlayRect(10, 40, 30, 40)
                    }
                },
                VoicePrimaryId = 1,
                NextPairId = 2
            };
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), store);

            Assert.IsTrue(session.TryToggleRegionAdjust(1));
            Assert.IsFalse(session.IsClickThrough);
            Assert.AreEqual(1, session.ArmedPairId);
            Assert.AreEqual(1, session.AdjustOutlines.Count);
            AssertOutline(session.AdjustOutlines[0], 1, 10, 40, true);
        }

        [TestMethod]
        public void ToggleRegionAdjust_PairWithNeitherRect_DoesNotArm()
        {
            var store = new MemoryRegionPairStore
            {
                StoredPairs =
                {
                    new RegionPairRecord
                    {
                        Id = 1,
                        Capture = OverlayRect.Invalid,
                        Display = OverlayRect.Invalid
                    }
                },
                VoicePrimaryId = 0,
                NextPairId = 2
            };
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), store);

            Assert.IsFalse(session.TryToggleRegionAdjust(1));
            Assert.IsTrue(session.IsClickThrough);
            Assert.AreEqual(0, session.ArmedPairId);
            Assert.AreEqual(0, session.AdjustOutlines.Count);
        }

        [TestMethod]
        public void DeleteArmedPair_ClearsArming()
        {
            LiveOverlaySession session = CreateSessionWithPairs(2);
            int firstId = session.Pairs[0].Id;

            Assert.IsTrue(session.TryToggleRegionAdjust(firstId));
            session.DeletePair(firstId);

            Assert.IsTrue(session.IsClickThrough);
            Assert.AreEqual(0, session.ArmedPairId);
            Assert.AreEqual(0, session.AdjustOutlines.Count);
        }

        [TestMethod]
        public void HideSubtitles_DoesNotHideAdjustOutlines()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);

            Assert.IsTrue(session.TryToggleRegionAdjust(session.Pairs[0].Id));
            session.HideSubtitles();

            Assert.IsFalse(session.SubtitlesVisible);
            Assert.AreEqual(2, session.AdjustOutlines.Count);
            Assert.IsFalse(session.IsClickThrough);
        }

        private static void AssertOutline(
            RegionOutline outline,
            int pairOrdinal,
            int x,
            int y,
            bool isDisplay)
        {
            Assert.AreEqual(pairOrdinal, outline.PairOrdinal);
            Assert.AreEqual(x, outline.Rect.X);
            Assert.AreEqual(y, outline.Rect.Y);
            Assert.AreEqual(isDisplay, outline.IsDisplay);
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
