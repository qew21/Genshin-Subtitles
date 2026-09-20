namespace GI_Subtitles.Core.Overlay
{
    /// <summary>
    /// The early-exit verdicts of the adjust-frame mouse handlers, extracted so
    /// the diagnostic trace can name the exact reason a drag never started.
    /// None means the handler should proceed.
    /// </summary>
    public enum AdjustMouseExit
    {
        None = 0,
        ClickThrough,
        ArmedPairIndexMissing,
        StartRectInvalid,
        SenderNotBox,
        NotDragging,
        ButtonReleased,
        NoDragTarget
    }

    /// <summary>
    /// Pure classification of the adjust mouse-handler guard conditions. The
    /// conditions mirror MainWindow's handler flow exactly, in the same order,
    /// so the reason logged for a dropped event is the reason the handler
    /// actually returned.
    /// </summary>
    public static class AdjustMouseGuard
    {
        public static AdjustMouseExit DownExitReason(
            bool clickThrough,
            OverlayAdjustTarget target,
            int armedPairIndex,
            bool startRectValid,
            bool senderIsBox)
        {
            if (clickThrough)
            {
                return AdjustMouseExit.ClickThrough;
            }

            if (target == OverlayAdjustTarget.Pair && armedPairIndex < 0)
            {
                return AdjustMouseExit.ArmedPairIndexMissing;
            }

            if (!startRectValid)
            {
                return AdjustMouseExit.StartRectInvalid;
            }

            if (!senderIsBox)
            {
                return AdjustMouseExit.SenderNotBox;
            }

            return AdjustMouseExit.None;
        }

        public static AdjustMouseExit MoveExitReason(
            bool dragging,
            bool leftButtonPressed,
            OverlayAdjustTarget dragTarget)
        {
            if (!dragging)
            {
                return AdjustMouseExit.NotDragging;
            }

            if (!leftButtonPressed)
            {
                return AdjustMouseExit.ButtonReleased;
            }

            if (dragTarget == OverlayAdjustTarget.None)
            {
                return AdjustMouseExit.NoDragTarget;
            }

            return AdjustMouseExit.None;
        }

        public static AdjustMouseExit UpExitReason(bool dragging)
        {
            return dragging ? AdjustMouseExit.None : AdjustMouseExit.NotDragging;
        }
    }
}
