using System.Collections.Generic;

namespace GI_Subtitles.Core.Overlay
{
    /// <summary>
    /// The activity log window's projection state: which rows of the bounded
    /// record the view shows, and how far it has consumed that record. Log
    /// de-noise hides repeat rows from the view; hidden rows are still consumed
    /// so they are never reconsidered, and the record itself is never modified.
    /// </summary>
    public sealed class ActivityLogRowFilter
    {
        private readonly bool _hideRepeats;
        private int _consumedCount;

        public ActivityLogRowFilter(bool hideRepeats)
        {
            _hideRepeats = hideRepeats;
        }

        public bool HideRepeats
        {
            get { return _hideRepeats; }
        }

        public int ConsumedCount
        {
            get { return _consumedCount; }
        }

        public bool Shows(ActivityLogRow row)
        {
            return !_hideRepeats || !row.IsRepeat;
        }

        /// <summary>
        /// The session may discard rows from the front when its bounded log
        /// reaches capacity. Move the source cursor with that eviction before
        /// consuming the newly appended tail.
        /// </summary>
        public void RemoveFromFront(int count)
        {
            if (count <= 0)
            {
                return;
            }

            _consumedCount = _consumedCount > count
                ? _consumedCount - count
                : 0;
        }

        /// <summary>
        /// Consumes every row past <see cref="ConsumedCount"/> and returns the
        /// rows the view should append now. Hidden rows advance the consumed
        /// count without being returned. Call <see cref="RemoveFromFront"/>
        /// before this method when the bounded source evicts old rows.
        /// </summary>
        public IReadOnlyList<ActivityLogRow> Consume(IReadOnlyList<ActivityLogRow> rows)
        {
            List<ActivityLogRow> shown = null;
            while (_consumedCount < rows.Count)
            {
                ActivityLogRow row = rows[_consumedCount];
                if (Shows(row))
                {
                    if (shown == null)
                    {
                        shown = new List<ActivityLogRow>();
                    }

                    shown.Add(row);
                }

                _consumedCount++;
            }

            return shown ?? (IReadOnlyList<ActivityLogRow>)new ActivityLogRow[0];
        }
    }
}
