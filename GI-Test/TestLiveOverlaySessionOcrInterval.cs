using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestLiveOverlaySessionOcrInterval
    {
        [TestMethod]
        public void InRangeEdit_AppliesToConfigAndEngineOnNextGate()
        {
            var store = new MemoryOcrIntervalStore { Stored = 400 };
            var session = new LiveOverlaySession(store);

            var view = session.OpenOcrIntervalSettings();
            Assert.AreEqual("400", view.BoxText);
            Assert.IsFalse(view.IsOutOfRange);

            view.BoxText = "500";
            view.Commit();

            Assert.AreEqual("500", view.BoxText);
            Assert.IsFalse(view.IsOutOfRange);
            Assert.AreEqual(500, store.Stored);
            Assert.AreEqual(1, store.WriteCount);
            Assert.AreEqual(500, session.EngineOcrIntervalMs);
        }

        [TestMethod]
        public void MissingJson_OpensAtDefaultWithoutWriting()
        {
            var store = new MemoryOcrIntervalStore();
            var session = new LiveOverlaySession(store);

            var view = session.OpenOcrIntervalSettings();

            Assert.AreEqual("400", view.BoxText);
            Assert.IsFalse(view.IsOutOfRange);
            Assert.IsNull(store.Stored);
            Assert.AreEqual(0, store.WriteCount);
            Assert.AreEqual(400, session.EngineOcrIntervalMs);
        }

        [TestMethod]
        public void InRangeJson_DisplaysAsIs()
        {
            var store = new MemoryOcrIntervalStore { Stored = 200 };
            var session = new LiveOverlaySession(store);
            var view = session.OpenOcrIntervalSettings();

            Assert.AreEqual("200", view.BoxText);
            Assert.IsFalse(view.IsOutOfRange);

            store.Stored = 1000;
            session = new LiveOverlaySession(store);
            view = session.OpenOcrIntervalSettings();

            Assert.AreEqual("1000", view.BoxText);
            Assert.IsFalse(view.IsOutOfRange);
            Assert.AreEqual(0, store.WriteCount);
        }

        [TestMethod]
        public void OutOfRangeJson_IsShownAndNotWrittenBackOnOpen()
        {
            var store = new MemoryOcrIntervalStore { Stored = 50 };
            var session = new LiveOverlaySession(store);

            var view = session.OpenOcrIntervalSettings();

            Assert.AreEqual("50", view.BoxText);
            Assert.IsTrue(view.IsOutOfRange);
            Assert.AreEqual(50, store.Stored);
            Assert.AreEqual(0, store.WriteCount);
            Assert.AreEqual(50, session.EngineOcrIntervalMs);
        }

        [TestMethod]
        public void LaterEdit_ClampsOutOfRangeValueIntoUiRange()
        {
            var store = new MemoryOcrIntervalStore { Stored = 50 };
            var session = new LiveOverlaySession(store);
            var view = session.OpenOcrIntervalSettings();

            view.BoxText = "50";
            view.Commit();

            Assert.AreEqual("200", view.BoxText);
            Assert.IsFalse(view.IsOutOfRange);
            Assert.AreEqual(200, store.Stored);
            Assert.AreEqual(1, store.WriteCount);
            Assert.AreEqual(200, session.EngineOcrIntervalMs);
        }

        [TestMethod]
        public void EngineFloor_AllowsHandEditedValueBelowUiMinimumUntilEdit()
        {
            var store = new MemoryOcrIntervalStore { Stored = 0 };
            var session = new LiveOverlaySession(store);
            var view = session.OpenOcrIntervalSettings();

            Assert.AreEqual("0", view.BoxText);
            Assert.IsTrue(view.IsOutOfRange);
            Assert.AreEqual(1, session.EngineOcrIntervalMs);
            Assert.AreEqual(0, store.WriteCount);

            view.BoxText = "0";
            view.Commit();

            Assert.AreEqual("200", view.BoxText);
            Assert.AreEqual(200, session.EngineOcrIntervalMs);
            Assert.AreEqual(200, store.Stored);
        }

        [TestMethod]
        public void AboveUiMaximum_KeepsRawEngineValueUntilEditClamps()
        {
            var store = new MemoryOcrIntervalStore { Stored = 1500 };
            var session = new LiveOverlaySession(store);
            var view = session.OpenOcrIntervalSettings();

            Assert.AreEqual("1500", view.BoxText);
            Assert.IsTrue(view.IsOutOfRange);
            Assert.AreEqual(1500, session.EngineOcrIntervalMs);
            Assert.AreEqual(0, store.WriteCount);

            view.BoxText = "1500";
            view.Commit();

            Assert.AreEqual("1000", view.BoxText);
            Assert.IsFalse(view.IsOutOfRange);
            Assert.AreEqual(1000, session.EngineOcrIntervalMs);
            Assert.AreEqual(1000, store.Stored);
        }

        [TestMethod]
        public void UnparseableEdit_RevertsToValueBeforeTheEdit()
        {
            var store = new MemoryOcrIntervalStore { Stored = 50 };
            var session = new LiveOverlaySession(store);
            var view = session.OpenOcrIntervalSettings();

            view.BoxText = "abc";
            view.Commit();

            Assert.AreEqual("50", view.BoxText);
            Assert.IsTrue(view.IsOutOfRange);
            Assert.AreEqual(50, store.Stored);
            Assert.AreEqual(0, store.WriteCount);
            Assert.AreEqual(50, session.EngineOcrIntervalMs);
        }

        private sealed class MemoryOcrIntervalStore : IOcrIntervalStore
        {
            public int? Stored;
            public int WriteCount;

            public int Read(int defaultValue)
            {
                return Stored ?? defaultValue;
            }

            public void Write(int milliseconds)
            {
                Stored = milliseconds;
                WriteCount++;
            }
        }
    }
}
