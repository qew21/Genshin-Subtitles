using System;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestLiveOverlaySessionRegionPairs
    {
        [TestMethod]
        public void LegacyCaptures_MigrateIntoPairList_WithPair1DisplayFromPad()
        {
            var pairs = new MemoryRegionPairStore
            {
                Legacy = new LegacyRegionSlots
                {
                    Region = "100,200,800,80",
                    Region2 = "50,60,400,50",
                    PadVertical = 86,
                    PadHorizontal = 10
                }
            };

            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);

            Assert.AreEqual(2, session.Pairs.Count);

            OverlayRect capture1 = session.Pairs[0].Capture;
            Assert.AreEqual(100, capture1.X);
            Assert.AreEqual(200, capture1.Y);
            Assert.AreEqual(800, capture1.Width);
            Assert.AreEqual(80, capture1.Height);

            OverlayRect display1 = session.Pairs[0].Display;
            Assert.AreEqual(110, display1.X);
            Assert.AreEqual(286, display1.Y);
            Assert.AreEqual(800, display1.Width);
            Assert.AreEqual(80, display1.Height);

            OverlayRect capture2 = session.Pairs[1].Capture;
            Assert.AreEqual(50, capture2.X);
            Assert.AreEqual(60, capture2.Y);
            Assert.AreEqual(400, capture2.Width);
            Assert.AreEqual(50, capture2.Height);
            Assert.IsFalse(session.Pairs[1].Display.IsValid);

            Assert.AreEqual(1, pairs.WriteCount);
        }

        [TestMethod]
        public void StoredPairList_IsUsedAsIs_WithoutReadingLegacy()
        {
            var pairs = new MemoryRegionPairStore
            {
                Legacy = new LegacyRegionSlots
                {
                    Region = "1,2,3,4",
                    Region2 = "5,6,7,8",
                    PadVertical = 99
                },
                VoicePrimaryId = 1,
                NextPairId = 2,
                StoredPairs =
                {
                    new RegionPairRecord
                    {
                        Id = 1,
                        Capture = new OverlayRect(10, 20, 30, 40),
                        Display = new OverlayRect(11, 21, 30, 40)
                    }
                }
            };

            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);

            Assert.AreEqual(1, session.Pairs.Count);
            Assert.AreEqual(10, session.Pairs[0].Capture.X);
            Assert.AreEqual(11, session.Pairs[0].Display.X);
            Assert.AreEqual(1, session.Pairs[0].Id);
            Assert.AreEqual(1, session.VoicePrimaryId);
            Assert.AreEqual(0, pairs.WriteCount);
        }

        [TestMethod]
        public void SettingsCaptureRows_WritePair1AndPair2()
        {
            var pairs = new MemoryRegionPairStore();
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);

            session.SetCapture(0, new OverlayRect(100, 200, 800, 80));
            session.SetCapture(1, new OverlayRect(50, 60, 400, 50));

            Assert.AreEqual(2, session.Pairs.Count);
            Assert.AreEqual(100, session.Pairs[0].Capture.X);
            Assert.IsFalse(session.Pairs[0].Display.IsValid);
            Assert.AreEqual(50, session.Pairs[1].Capture.X);
            Assert.IsFalse(session.Pairs[1].Display.IsValid);

            session.ClearCapture(1);
            Assert.IsFalse(session.Pairs[1].Capture.IsValid);
            Assert.AreEqual(3, pairs.WriteCount);
        }

        [TestMethod]
        public void AllValidCaptures_EnqueueOnSharedBeat_InListOrder()
        {
            LiveOverlaySession session = CreateSessionWithPairs(2);

            session.Beat(PairFrameSample.ChangedAndStable(), PairFrameSample.ChangedAndStable());

            Assert.AreEqual(0, session.BusyOcrPairIndex);
            CollectionAssert.AreEqual(new[] { 1 }, System.Linq.Enumerable.ToArray(session.OcrQueue));
        }

        [TestMethod]
        public void SerialQueue_DoesNotDropOrParallelizeLaterPairs()
        {
            DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(2, () => now);

            session.Beat(PairFrameSample.ChangedAndStable(), PairFrameSample.ChangedAndStable());
            Assert.AreEqual(0, session.BusyOcrPairIndex);
            Assert.AreEqual(1, session.OcrQueue.Count);

            session.CompleteOcr(miss: false, content: "first");
            Assert.AreEqual("first", session.PairBodies[0].Content);
            Assert.IsNull(session.BusyOcrPairIndex);
            CollectionAssert.AreEqual(new[] { 1 }, System.Linq.Enumerable.ToArray(session.OcrQueue));

            now = now.AddMilliseconds(400);
            session.Tick();
            Assert.AreEqual(1, session.BusyOcrPairIndex);
            Assert.AreEqual(0, session.OcrQueue.Count);

            session.CompleteOcr(miss: false, content: "second");
            Assert.AreEqual("second", session.PairBodies[1].Content);
            Assert.IsTrue(session.PairBodies[1].RecognitionOrder > session.PairBodies[0].RecognitionOrder);
        }

        [TestMethod]
        public void OcrMiss_KeepsThatPair_StableNoText_ClearsThatPairOnly()
        {
            DateTime now = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(2, () => now);
            session.Beat(PairFrameSample.ChangedAndStable(), PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "one");
            now = now.AddMilliseconds(400);
            session.Tick();
            session.CompleteOcr(miss: false, content: "two");

            now = now.AddMilliseconds(400);
            session.Beat(PairFrameSample.ChangedAndStable(), PairFrameSample.Unchanged());
            session.CompleteOcr(miss: true);
            Assert.AreEqual("one", session.PairBodies[0].Content);
            Assert.AreEqual("two", session.PairBodies[1].Content);

            session.Beat(PairFrameSample.StableNoText(), PairFrameSample.Unchanged());
            Assert.AreEqual(string.Empty, session.PairBodies[0].Content);
            Assert.AreEqual("two", session.PairBodies[1].Content);
        }

        [TestMethod]
        public void PairStoreTruncatesPairsBeyondUnifiedCap()
        {
            var records = new System.Collections.Generic.List<RegionPairRecord>();
            for (int i = 0; i < 9; i++)
            {
                records.Add(new RegionPairRecord
                {
                    Capture = new OverlayRect(i * 10, 0, 40, 20),
                    Display = new OverlayRect(i * 10, 30, 40, 20)
                });
            }

            var pairs = new MemoryRegionPairStore { StoredPairs = records };
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);

            Assert.AreEqual(LiveOverlaySession.SettingsPairCap, session.Pairs.Count);

            var samples = new PairFrameSample[LiveOverlaySession.SettingsPairCap];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = PairFrameSample.ChangedAndStable();
            }

            session.Beat(samples);
            Assert.AreEqual(0, session.BusyOcrPairIndex);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, System.Linq.Enumerable.ToArray(session.OcrQueue));
        }

        [TestMethod]
        public void CaptureWithoutDisplay_StillRunsOcr_ButNothingToDraw()
        {
            var pairs = new MemoryRegionPairStore
            {
                StoredPairs =
                {
                    new RegionPairRecord
                    {
                        Capture = new OverlayRect(10, 20, 30, 40),
                        Display = OverlayRect.Invalid
                    }
                }
            };
            var session = new LiveOverlaySession(new MemoryOcrIntervalStore(), pairs);

            session.Beat(PairFrameSample.ChangedAndStable());
            Assert.AreEqual(0, session.BusyOcrPairIndex);
            session.CompleteOcr(miss: false, content: "ghost");
            Assert.AreEqual("ghost", session.PairBodies[0].Content);
            Assert.IsFalse(session.PairBodies[0].Visible);
        }

        [TestMethod]
        public void HideSubtitles_HidesEveryPairBody()
        {
            DateTime now = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSessionWithPairs(2, () => now);
            session.Beat(PairFrameSample.ChangedAndStable(), PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "a");
            now = now.AddMilliseconds(400);
            session.Tick();
            session.CompleteOcr(miss: false, content: "b");

            Assert.IsTrue(session.PairBodies[0].Visible);
            Assert.IsTrue(session.PairBodies[1].Visible);

            session.HideSubtitles();
            Assert.IsFalse(session.PairBodies[0].Visible);
            Assert.IsFalse(session.PairBodies[1].Visible);
            Assert.AreEqual("a", session.PairBodies[0].Content);
            Assert.AreEqual("b", session.PairBodies[1].Content);

            session.ShowSubtitles();
            Assert.IsTrue(session.PairBodies[0].Visible);
            Assert.IsTrue(session.PairBodies[1].Visible);
        }

        [TestMethod]
        public void PairMisses_DoNotStealAnotherPairCapture()
        {
            LiveOverlaySession session = CreateSessionWithPairs(2);
            session.Beat(PairFrameSample.ChangedAndStable(), PairFrameSample.Unchanged());
            for (int i = 0; i < 6; i++)
            {
                session.CompleteOcr(miss: true);
                session.Beat(PairFrameSample.ChangedAndStable(), PairFrameSample.Unchanged());
            }

            Assert.AreNotEqual(1, session.BusyOcrPairIndex);
            foreach (int queued in session.OcrQueue)
            {
                Assert.AreNotEqual(1, queued);
            }
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
            public int? Stored;

            public int Read(int defaultValue)
            {
                return Stored ?? defaultValue;
            }

            public void Write(int milliseconds)
            {
                Stored = milliseconds;
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
