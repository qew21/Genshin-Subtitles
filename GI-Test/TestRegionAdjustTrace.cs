#if DEBUG
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    /// <summary>
    /// The region-adjust diagnostic summary is Debug-only, so these
    /// tests compile and run only in Debug builds of the test project.
    /// </summary>
    [TestClass]
    public class TestRegionAdjustTrace
    {
        [TestMethod]
        public void Summarize_NoInputAtAll_NoElementInput()
        {
            string verdict = RegionAdjustTrace.Summarize(
                elementDownEvents: 0,
                persistEvents: 0,
                hitModeRemovalFailed: false);

            Assert.AreEqual("no-element-input", verdict);
        }

        [TestMethod]
        public void Summarize_ElementDownWithoutPersist_ReachedElementNotPersisted()
        {
            string verdict = RegionAdjustTrace.Summarize(
                elementDownEvents: 2,
                persistEvents: 0,
                hitModeRemovalFailed: false);

            Assert.AreEqual("element-input-reached-but-not-persisted", verdict);
        }

        [TestMethod]
        public void Summarize_ElementDownAndPersist_Healthy()
        {
            string verdict = RegionAdjustTrace.Summarize(
                elementDownEvents: 1,
                persistEvents: 3,
                hitModeRemovalFailed: false);

            Assert.AreEqual("element-input-reached-and-persisted", verdict);
        }

        [TestMethod]
        public void Summarize_PersistWithoutElementDown_StillNoElementInput()
        {
            // A settings-page edit while armed persists without any element
            // input; that alone must not read as the healthy drag path.
            string verdict = RegionAdjustTrace.Summarize(
                elementDownEvents: 0,
                persistEvents: 2,
                hitModeRemovalFailed: false);

            Assert.AreEqual("no-element-input", verdict);
        }

        [TestMethod]
        public void Summarize_HitModeFailure_AppendsNote()
        {
            string verdict = RegionAdjustTrace.Summarize(
                elementDownEvents: 0,
                persistEvents: 0,
                hitModeRemovalFailed: true);

            Assert.AreEqual(
                "no-element-input [hit-mode-transparent-bit-still-set]",
                verdict);
        }

        [TestMethod]
        public void Summarize_HitModeFailure_AppendsNoteToHealthyVerdictToo()
        {
            string verdict = RegionAdjustTrace.Summarize(
                elementDownEvents: 1,
                persistEvents: 1,
                hitModeRemovalFailed: true);

            Assert.AreEqual(
                "element-input-reached-and-persisted [hit-mode-transparent-bit-still-set]",
                verdict);
        }
    }
}
#endif
