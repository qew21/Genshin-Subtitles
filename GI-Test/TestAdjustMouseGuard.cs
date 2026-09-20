using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestAdjustMouseGuard
    {
        [TestMethod]
        public void Down_ClickThrough_ExitsBeforeAnythingElse()
        {
            AdjustMouseExit exit = AdjustMouseGuard.DownExitReason(
                clickThrough: true,
                target: OverlayAdjustTarget.Pair,
                armedPairIndex: 0,
                startRectValid: true,
                senderIsBox: true);

            Assert.AreEqual(AdjustMouseExit.ClickThrough, exit);
        }

        [TestMethod]
        public void Down_PairTargetWithoutArmedIndex_ExitsForMissingIndex()
        {
            AdjustMouseExit exit = AdjustMouseGuard.DownExitReason(
                clickThrough: false,
                target: OverlayAdjustTarget.Pair,
                armedPairIndex: -1,
                startRectValid: false,
                senderIsBox: true);

            Assert.AreEqual(AdjustMouseExit.ArmedPairIndexMissing, exit);
        }

        [TestMethod]
        public void Down_InvalidStartRect_ExitsForStartRect()
        {
            AdjustMouseExit exit = AdjustMouseGuard.DownExitReason(
                clickThrough: false,
                target: OverlayAdjustTarget.DarkScreenDisplay,
                armedPairIndex: -1,
                startRectValid: false,
                senderIsBox: true);

            Assert.AreEqual(AdjustMouseExit.StartRectInvalid, exit);
        }

        [TestMethod]
        public void Down_NonBoxSender_ExitsForSender()
        {
            AdjustMouseExit exit = AdjustMouseGuard.DownExitReason(
                clickThrough: false,
                target: OverlayAdjustTarget.DialogueOptionDisplay,
                armedPairIndex: -1,
                startRectValid: true,
                senderIsBox: false);

            Assert.AreEqual(AdjustMouseExit.SenderNotBox, exit);
        }

        [TestMethod]
        public void Down_AllConditionsMet_Proceeds()
        {
            AdjustMouseExit exit = AdjustMouseGuard.DownExitReason(
                clickThrough: false,
                target: OverlayAdjustTarget.Pair,
                armedPairIndex: 0,
                startRectValid: true,
                senderIsBox: true);

            Assert.AreEqual(AdjustMouseExit.None, exit);
        }

        [TestMethod]
        public void Move_NotDragging_ExitsFirst()
        {
            AdjustMouseExit exit = AdjustMouseGuard.MoveExitReason(
                dragging: false,
                leftButtonPressed: false,
                dragTarget: OverlayAdjustTarget.None);

            Assert.AreEqual(AdjustMouseExit.NotDragging, exit);
        }

        [TestMethod]
        public void Move_DraggingWithButtonReleased_ExitsForButton()
        {
            AdjustMouseExit exit = AdjustMouseGuard.MoveExitReason(
                dragging: true,
                leftButtonPressed: false,
                dragTarget: OverlayAdjustTarget.Pair);

            Assert.AreEqual(AdjustMouseExit.ButtonReleased, exit);
        }

        [TestMethod]
        public void Move_DraggingWithoutTarget_ExitsForTarget()
        {
            AdjustMouseExit exit = AdjustMouseGuard.MoveExitReason(
                dragging: true,
                leftButtonPressed: true,
                dragTarget: OverlayAdjustTarget.None);

            Assert.AreEqual(AdjustMouseExit.NoDragTarget, exit);
        }

        [TestMethod]
        public void Move_AllConditionsMet_Proceeds()
        {
            AdjustMouseExit exit = AdjustMouseGuard.MoveExitReason(
                dragging: true,
                leftButtonPressed: true,
                dragTarget: OverlayAdjustTarget.Pair);

            Assert.AreEqual(AdjustMouseExit.None, exit);
        }

        [TestMethod]
        public void Up_NotDragging_Exits()
        {
            Assert.AreEqual(
                AdjustMouseExit.NotDragging,
                AdjustMouseGuard.UpExitReason(dragging: false));
        }

        [TestMethod]
        public void Up_Dragging_Proceeds()
        {
            Assert.AreEqual(
                AdjustMouseExit.None,
                AdjustMouseGuard.UpExitReason(dragging: true));
        }
    }
}
