using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    /// <summary>
    /// Log de-noise: the activity log window's filter decides which session rows
    /// the view shows. Hiding repeat rows is a view choice — the filter consumes
    /// them (so they are never re-considered) but the record keeps them.
    /// </summary>
    [TestClass]
    public class TestActivityLogRowFilter
    {
        [TestMethod]
        public void HideRepeatsOn_Consume_ReturnsOnlyNonRepeatRows()
        {
            var rows = new List<ActivityLogRow>
            {
                Row(isRepeat: false),
                Row(isRepeat: true),
                Row(isRepeat: false),
                Row(isRepeat: true)
            };
            var filter = new ActivityLogRowFilter(hideRepeats: true);

            IReadOnlyList<ActivityLogRow> shown = filter.Consume(rows);

            CollectionAssert.AreEqual(new[] { rows[0], rows[2] }, (System.Collections.ICollection)shown);
            Assert.AreEqual(4, filter.ConsumedCount);
        }

        [TestMethod]
        public void HideRepeatsOff_Consume_ReturnsEveryRow()
        {
            var rows = new List<ActivityLogRow>
            {
                Row(isRepeat: false),
                Row(isRepeat: true),
                Row(isRepeat: true)
            };
            var filter = new ActivityLogRowFilter(hideRepeats: false);

            IReadOnlyList<ActivityLogRow> shown = filter.Consume(rows);

            CollectionAssert.AreEqual(new[] { rows[0], rows[1], rows[2] }, (System.Collections.ICollection)shown);
        }

        [TestMethod]
        public void Consume_SecondCall_ReturnsOnlyNewlyAppendedRows()
        {
            var rows = new List<ActivityLogRow> { Row(isRepeat: false) };
            var filter = new ActivityLogRowFilter(hideRepeats: true);

            CollectionAssert.AreEqual(new[] { rows[0] }, (System.Collections.ICollection)filter.Consume(rows));

            rows.Add(Row(isRepeat: true));
            rows.Add(Row(isRepeat: false));

            IReadOnlyList<ActivityLogRow> shown = filter.Consume(rows);

            CollectionAssert.AreEqual(new[] { rows[2] }, (System.Collections.ICollection)shown);
            Assert.AreEqual(3, filter.ConsumedCount);
        }

        [TestMethod]
        public void Consume_EmptySource_ReturnsNothing()
        {
            var filter = new ActivityLogRowFilter(hideRepeats: true);

            IReadOnlyList<ActivityLogRow> shown = filter.Consume(new List<ActivityLogRow>());

            Assert.AreEqual(0, shown.Count);
            Assert.AreEqual(0, filter.ConsumedCount);
        }

        [TestMethod]
        public void Consume_AfterFrontRemoval_ResumesAtNewTail()
        {
            var rows = new List<ActivityLogRow>
            {
                Row(isRepeat: false),
                Row(isRepeat: true),
                Row(isRepeat: false)
            };
            var filter = new ActivityLogRowFilter(hideRepeats: true);

            CollectionAssert.AreEqual(
                new[] { rows[0], rows[2] },
                (System.Collections.ICollection)filter.Consume(rows));

            ActivityLogRow evicted = rows[0];
            rows.RemoveAt(0);
            filter.RemoveFromFront(1);
            ActivityLogRow appended = Row(isRepeat: false);
            rows.Add(appended);

            IReadOnlyList<ActivityLogRow> shown = filter.Consume(rows);

            CollectionAssert.AreEqual(new[] { appended }, (System.Collections.ICollection)shown);
            Assert.AreEqual(3, filter.ConsumedCount);
            Assert.IsFalse(rows.Contains(evicted));
        }

        [TestMethod]
        public void Shows_RepeatRow_OnlyWhenNotHiding()
        {
            ActivityLogRow repeat = Row(isRepeat: true);
            ActivityLogRow fresh = Row(isRepeat: false);

            Assert.IsFalse(new ActivityLogRowFilter(hideRepeats: true).Shows(repeat));
            Assert.IsTrue(new ActivityLogRowFilter(hideRepeats: false).Shows(repeat));
            Assert.IsTrue(new ActivityLogRowFilter(hideRepeats: true).Shows(fresh));
        }

        private static ActivityLogRow Row(bool isRepeat)
        {
            return new ActivityLogRow(
                new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc),
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
