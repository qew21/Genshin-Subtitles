namespace GI_Subtitles.Core.Overlay
{
    /// <summary>
    /// Follow-tail policy for the activity log window: pin to the newest visible
    /// content only while the viewport is already at the bottom, including after
    /// the operator jumps there with 有新记录. Hidden repeat rows are not visible
    /// content — the window simply does not call <see cref="VisibleContentAdded"/>
    /// for them. Layout ScrollChanged must not call
    /// <see cref="OperatorViewportAtBottom"/>; only the operator moving the
    /// viewport does.
    /// </summary>
    public sealed class ActivityLogFollowTail
    {
        // Pixel virtualization and wrapped result rows leave the offset a few
        // pixels short of ScrollableHeight after ScrollToEnd; treat that slack
        // as still at the bottom so follow is not disarmed by layout.
        private const double BottomSlack = 8.0;

        public ActivityLogFollowTail()
        {
            IsFollowing = true;
        }

        public bool IsFollowing { get; private set; }

        public bool ShowNewRecords { get; private set; }

        public void VisibleContentAdded()
        {
            ShowNewRecords = !IsFollowing;
        }

        public void JumpToNewest()
        {
            IsFollowing = true;
            ShowNewRecords = false;
        }

        public void OperatorViewportAtBottom(bool atBottom)
        {
            IsFollowing = atBottom;
            if (atBottom)
            {
                ShowNewRecords = false;
            }
        }

        public static bool IsAtBottom(double verticalOffset, double scrollableHeight)
        {
            return scrollableHeight <= 0.0 || verticalOffset >= scrollableHeight - BottomSlack;
        }
    }
}
