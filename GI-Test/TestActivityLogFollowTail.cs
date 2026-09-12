using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    /// <summary>
    /// Follow-tail: the activity log viewport pins to the newest visible content
    /// only while it is already at the bottom, including after 有新记录. Layout
    /// is the window's problem; this policy only sees visible-content adds,
    /// the operator jumping to newest, and the operator moving the viewport.
    /// </summary>
    [TestClass]
    public class TestActivityLogFollowTail
    {
        [TestMethod]
        public void DefaultFollowTail_FollowsAndHidesButton()
        {
            var tail = new ActivityLogFollowTail();

            Assert.IsTrue(tail.IsFollowing);
            Assert.IsFalse(tail.ShowNewRecords);
        }

        [TestMethod]
        public void Following_VisibleContentAdded_StaysFollowingAndHidesButton()
        {
            var tail = new ActivityLogFollowTail();

            tail.VisibleContentAdded();

            Assert.IsTrue(tail.IsFollowing);
            Assert.IsFalse(tail.ShowNewRecords);
        }

        [TestMethod]
        public void ScrolledUp_VisibleContentAdded_ShowsButtonAndDoesNotMoveFollow()
        {
            var tail = new ActivityLogFollowTail();
            tail.OperatorViewportAtBottom(false);

            tail.VisibleContentAdded();

            Assert.IsFalse(tail.IsFollowing);
            Assert.IsTrue(tail.ShowNewRecords);
        }

        [TestMethod]
        public void JumpToNewest_HidesButtonAndReArmsFollow()
        {
            var tail = new ActivityLogFollowTail();
            tail.OperatorViewportAtBottom(false);
            tail.VisibleContentAdded();

            tail.JumpToNewest();

            Assert.IsTrue(tail.IsFollowing);
            Assert.IsFalse(tail.ShowNewRecords);
        }

        [TestMethod]
        public void JumpToNewest_ThenVisibleContentAdded_KeepsFollowingAndHidesButton()
        {
            var tail = new ActivityLogFollowTail();
            tail.OperatorViewportAtBottom(false);
            tail.VisibleContentAdded();
            tail.JumpToNewest();

            tail.VisibleContentAdded();

            Assert.IsTrue(tail.IsFollowing);
            Assert.IsFalse(tail.ShowNewRecords);
        }

        [TestMethod]
        public void ScrolledUp_DoesNotShowButton()
        {
            var tail = new ActivityLogFollowTail();

            tail.OperatorViewportAtBottom(false);

            Assert.IsFalse(tail.IsFollowing);
            Assert.IsFalse(tail.ShowNewRecords);
        }

        [TestMethod]
        public void ScrolledBackToBottom_ReArmsAndHidesButton()
        {
            var tail = new ActivityLogFollowTail();
            tail.OperatorViewportAtBottom(false);
            tail.VisibleContentAdded();

            tail.OperatorViewportAtBottom(true);

            Assert.IsTrue(tail.IsFollowing);
            Assert.IsFalse(tail.ShowNewRecords);
        }

        [TestMethod]
        public void HiddenRepeatRows_DoNotCountAsVisibleContent()
        {
            var filter = new ActivityLogRowFilter(hideRepeats: true);
            var tail = new ActivityLogFollowTail();
            var rows = new List<ActivityLogRow> { Row(isRepeat: false) };

            Announce(tail, filter.Consume(rows));
            tail.OperatorViewportAtBottom(false);

            rows.Add(Row(isRepeat: true));
            Announce(tail, filter.Consume(rows));

            Assert.IsFalse(tail.IsFollowing);
            Assert.IsFalse(tail.ShowNewRecords);

            rows.Add(Row(isRepeat: false));
            Announce(tail, filter.Consume(rows));

            Assert.IsFalse(tail.IsFollowing);
            Assert.IsTrue(tail.ShowNewRecords);
        }

        [TestMethod]
        public void IsAtBottom_WhenContentFits_IsTrue()
        {
            Assert.IsTrue(ActivityLogFollowTail.IsAtBottom(0.0, 0.0));
        }

        [TestMethod]
        public void IsAtBottom_AtEnd_IsTrue_AtTop_IsFalse()
        {
            Assert.IsTrue(ActivityLogFollowTail.IsAtBottom(240.0, 240.0));
            Assert.IsTrue(ActivityLogFollowTail.IsAtBottom(239.0, 240.0));
            Assert.IsFalse(ActivityLogFollowTail.IsAtBottom(0.0, 240.0));
        }

        private static void Announce(ActivityLogFollowTail tail, IReadOnlyList<ActivityLogRow> shown)
        {
            if (shown.Count > 0)
            {
                tail.VisibleContentAdded();
            }
        }

        private static ActivityLogRow Row(bool isRepeat)
        {
            return new ActivityLogRow(
                new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc),
                new[] { OperatorJob.Capture, OperatorJob.Ocr, OperatorJob.Match },
                ActivityLogScope.Pair,
                1,
                true,
                null,
                null,
                "ocr text",
                "original",
                "translation",
                false,
                false,
                isRepeat);
        }
    }
}
