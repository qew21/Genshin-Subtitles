using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;
using GI_Subtitles.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestRegionPairSettings
    {
        [TestMethod]
        public void EmptyList_HasNoCards_AndHotkeyDoesNotAdd()
        {
            LiveOverlaySession session = CreateSession();
            var page = new RegionPairSettings(session);

            Assert.IsTrue(page.IsEmpty);
            Assert.AreEqual(0, page.Cards.Count);
            Assert.IsTrue(page.CanAdd);
            Assert.AreEqual(0, page.VoicePrimaryOrdinal);
            Assert.IsFalse(page.TryGetHotkeyTarget(out _, out _, out _));
            Assert.IsFalse(page.TryBoxHotkeyPair(
                new OverlayRect(10, 20, 30, 40),
                new OverlayRect(11, 50, 30, 40)));
            Assert.AreEqual(0, session.Pairs.Count);
        }

        [TestMethod]
        public void AbortAdd_OnEitherStep_WritesNothing()
        {
            var store = new MemoryRegionPairStore();
            LiveOverlaySession session = CreateSession(store);
            var page = new RegionPairSettings(session);

            Assert.IsTrue(page.TryStartAdd());
            page.SetAddCapture(new OverlayRect(10, 20, 30, 40));
            page.AbortAdd();

            Assert.AreEqual(0, session.Pairs.Count);
            Assert.AreEqual(0, store.WriteCount);
            Assert.IsFalse(session.AddInProgress);
            Assert.IsTrue(page.IsEmpty);

            Assert.IsTrue(page.TryStartAdd());
            page.SetAddCapture(new OverlayRect(10, 20, 30, 40));
            page.SetAddDisplay(new OverlayRect(11, 50, 30, 40));
            page.AbortAdd();

            Assert.AreEqual(0, session.Pairs.Count);
            Assert.AreEqual(0, store.WriteCount);
            Assert.IsFalse(session.AddInProgress);
        }

        [TestMethod]
        public void CommitAdd_RequiresBothRectangles_ThenShowsCardAsVoicePrimary()
        {
            var store = new MemoryRegionPairStore();
            LiveOverlaySession session = CreateSession(store);
            var page = new RegionPairSettings(session);

            Assert.IsTrue(page.TryStartAdd());
            Assert.AreEqual(1, page.NextAddOrdinal);
            page.SetAddCapture(new OverlayRect(10, 20, 30, 40));
            Assert.IsFalse(page.TryCommitAdd());
            Assert.AreEqual(0, session.Pairs.Count);
            Assert.AreEqual(0, store.WriteCount);
            Assert.IsFalse(session.AddInProgress);

            Assert.IsTrue(page.TryStartAdd());
            page.SetAddCapture(new OverlayRect(10, 20, 30, 40));
            page.SetAddDisplay(new OverlayRect(11, 50, 30, 40));
            Assert.IsTrue(page.TryCommitAdd());

            Assert.AreEqual(1, session.Pairs.Count);
            Assert.AreEqual(1, page.Cards.Count);
            Assert.IsFalse(page.IsEmpty);
            Assert.AreEqual(1, page.Cards[0].Ordinal);
            Assert.AreEqual(1, page.Cards[0].Id);
            Assert.IsTrue(page.Cards[0].IsVoicePrimary);
            Assert.IsFalse(page.Cards[0].IsOverAddCap);
            Assert.AreEqual(1, page.VoicePrimaryOrdinal);
            Assert.AreEqual(10, page.Cards[0].Capture.X);
            Assert.AreEqual(11, page.Cards[0].Display.X);
        }

        [TestMethod]
        public void ReboxCaptureOrDisplayAlone_WritesThatRectangle()
        {
            LiveOverlaySession session = CreateSessionWithPairs(1);
            var page = new RegionPairSettings(session);
            int id = session.Pairs[0].Id;

            Assert.IsTrue(page.TrySetCapture(id, new OverlayRect(1, 2, 3, 4)));
            Assert.AreEqual(1, session.Pairs[0].Capture.X);
            Assert.AreEqual(40, session.Pairs[0].Display.Y);

            Assert.IsTrue(page.TrySetDisplay(id, new OverlayRect(5, 6, 7, 8)));
            Assert.AreEqual(1, session.Pairs[0].Capture.X);
            Assert.AreEqual(5, session.Pairs[0].Display.X);
            Assert.AreEqual(6, session.Pairs[0].Display.Y);
        }

        [TestMethod]
        public void ReboxCaptureAlone_DoesNotInventDisplay()
        {
            var store = new MemoryRegionPairStore();
            LiveOverlaySession session = CreateSession(store);
            session.SetCapture(0, new OverlayRect(100, 200, 800, 80));
            var page = new RegionPairSettings(session);
            int id = session.Pairs[0].Id;

            Assert.IsFalse(session.Pairs[0].Display.IsValid);
            Assert.IsTrue(page.TrySetCapture(id, new OverlayRect(1, 2, 3, 4)));
            Assert.AreEqual(1, session.Pairs[0].Capture.X);
            Assert.IsFalse(session.Pairs[0].Display.IsValid);
        }

        [TestMethod]
        public void Hotkey_BoxesSelectedPair_OrPair1_AndDoesNotAdd()
        {
            LiveOverlaySession session = CreateSessionWithPairs(3);
            var page = new RegionPairSettings(session);

            Assert.IsTrue(page.TryGetHotkeyTarget(out int index, out int id, out int ordinal));
            Assert.AreEqual(0, index);
            Assert.AreEqual(1, id);
            Assert.AreEqual(1, ordinal);

            page.Select(3);
            Assert.IsTrue(page.TryGetHotkeyTarget(out index, out id, out ordinal));
            Assert.AreEqual(2, index);
            Assert.AreEqual(3, id);
            Assert.AreEqual(3, ordinal);

            OverlayRect capture = new OverlayRect(9, 8, 7, 6);
            OverlayRect display = new OverlayRect(5, 4, 3, 2);
            Assert.IsTrue(page.TryBoxHotkeyPair(capture, display));
            Assert.AreEqual(3, session.Pairs.Count);
            Assert.AreEqual(9, session.Pairs[2].Capture.X);
            Assert.AreEqual(5, session.Pairs[2].Display.X);
            Assert.AreEqual(0, session.Pairs[0].Capture.X);
        }

        [TestMethod]
        public void LoadedPairsPastFour_AreTruncated_AndAddStops()
        {
            LiveOverlaySession session = CreateSessionWithPairs(6);
            var page = new RegionPairSettings(session);

            Assert.AreEqual(LiveOverlaySession.SettingsPairCap, page.Cards.Count);
            Assert.IsFalse(page.CanAdd);
            Assert.IsFalse(page.TryStartAdd());
            Assert.IsFalse(page.Cards[3].IsOverAddCap);
            Assert.IsFalse(page.Cards[3].IsOverAddCap);
        }

        [TestMethod]
        public void VoicePrimaryButton_CannotClickOff_OthersDesignate()
        {
            LiveOverlaySession session = CreateSessionWithPairs(2);
            var page = new RegionPairSettings(session);

            Assert.AreEqual(1, session.VoicePrimaryId);
            Assert.IsFalse(page.TryDesignate(1));
            Assert.AreEqual(1, session.VoicePrimaryId);
            Assert.IsTrue(page.Cards[0].IsVoicePrimary);
            Assert.IsFalse(page.Cards[1].IsVoicePrimary);

            Assert.IsTrue(page.TryDesignate(2));
            Assert.AreEqual(2, session.VoicePrimaryId);
            Assert.IsFalse(page.Cards[0].IsVoicePrimary);
            Assert.IsTrue(page.Cards[1].IsVoicePrimary);
            Assert.AreEqual(2, page.VoicePrimaryOrdinal);
            Assert.IsFalse(page.TryDesignate(2));
            Assert.AreEqual(2, session.VoicePrimaryId);
        }

        [TestMethod]
        public void AdjustRegion_ArmsSelectedCard_UntilToggledOff()
        {
            LiveOverlaySession session = CreateSessionWithPairs(2);
            var page = new RegionPairSettings(session);
            int firstId = session.Pairs[0].Id;
            int secondId = session.Pairs[1].Id;

            Assert.IsTrue(page.Cards[0].CanAdjustRegion);
            Assert.IsTrue(page.TryToggleRegionAdjust(firstId));
            Assert.IsTrue(page.Cards[0].IsAdjustArmed);
            Assert.IsFalse(page.Cards[1].IsAdjustArmed);
            Assert.IsFalse(session.IsClickThrough);

            Assert.IsTrue(page.TryToggleRegionAdjust(secondId));
            Assert.IsFalse(page.Cards[0].IsAdjustArmed);
            Assert.IsTrue(page.Cards[1].IsAdjustArmed);

            page.CancelRegionAdjust();
            Assert.IsFalse(page.Cards[0].IsAdjustArmed);
            Assert.IsFalse(page.Cards[1].IsAdjustArmed);
            Assert.IsTrue(session.IsClickThrough);
        }

        [TestMethod]
        public void CaptureOnlyCard_CanAdjustRegion()
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
            LiveOverlaySession session = CreateSession(store);
            var page = new RegionPairSettings(session);

            Assert.IsFalse(page.Cards[0].Display.IsValid);
            Assert.IsTrue(page.Cards[0].CanAdjustRegion);
        }

        [TestMethod]
        public void FourPairs_RefuseAdd_ThreeAllowCommit()
        {
            LiveOverlaySession session = CreateSessionWithPairs(4);
            var page = new RegionPairSettings(session);
            Assert.IsFalse(page.CanAdd);
            Assert.IsFalse(page.TryStartAdd());

            session = CreateSessionWithPairs(3);
            page = new RegionPairSettings(session);
            Assert.IsTrue(page.CanAdd);
            Assert.IsTrue(page.TryStartAdd());
            page.SetAddCapture(new OverlayRect(1, 2, 3, 4));
            page.SetAddDisplay(new OverlayRect(5, 6, 7, 8));
            Assert.IsTrue(page.TryCommitAdd());
            Assert.AreEqual(4, page.Cards.Count);
            Assert.IsFalse(page.CanAdd);
        }

        private static LiveOverlaySession CreateSession(MemoryRegionPairStore store = null)
        {
            return new LiveOverlaySession(
                new MemoryOcrIntervalStore(),
                store ?? new MemoryRegionPairStore());
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

            return new LiveOverlaySession(
                new MemoryOcrIntervalStore(),
                new MemoryRegionPairStore
                {
                    StoredPairs = records,
                    VoicePrimaryId = pairCount > 0 ? 1 : 0,
                    NextPairId = pairCount + 1
                });
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
            public int WriteCount;
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
