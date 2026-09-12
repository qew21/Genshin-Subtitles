using System.Diagnostics;
using GI_Subtitles.Common;

namespace GI_Subtitles.Core.Overlay
{
    /// <summary>
    /// Debug-only diagnostic trace for the region-adjust pipeline:
    /// arm entry, outline rebuild, drag start/end, and persistence — the key
    /// points only. Per-frame movement logging was removed; key-point tracing
    /// remains. Every public method is [Conditional("DEBUG")] and every body
    /// is compiled out unless DEBUG is defined, so Release artifacts carry
    /// none of this code.
    ///
    /// The trace counts element downs and persists while armed and prints a
    /// verdict on disarm that names which link of the chain died: no element
    /// input at all, element input that never persisted, or the healthy path.
    /// </summary>
    public static class RegionAdjustTrace
    {
        [Conditional("DEBUG")]
        public static void ArmEntryRefused(OverlayAdjustTarget target, int pairId, string detail)
        {
#if DEBUG
            Write("arm refused: target=" + target + " pairId=" + pairId + " detail=" + detail);
#endif
        }

        [Conditional("DEBUG")]
        public static void ArmAccepted(OverlayAdjustTarget target, int pairId, int outlineCount)
        {
#if DEBUG
            ResetEvidence();
            Write("arm accepted: target=" + target + " pairId=" + pairId
                + " outlines=" + outlineCount);
#endif
        }

        [Conditional("DEBUG")]
        public static void OutlineBuilt(RegionOutline outline)
        {
#if DEBUG
            if (outline == null || outline.Rect == null)
            {
                Write("outline: <null>");
                return;
            }

            Write("outline: kind=" + outline.Kind + " pairOrdinal=" + outline.PairOrdinal
                + " rect=" + outline.Rect.ToCsv() + " valid=" + outline.Rect.IsValid
                + " isDisplay=" + outline.IsDisplay);
#endif
        }

        [Conditional("DEBUG")]
        public static void ArmDismissed(OverlayAdjustTarget target, int pairId)
        {
#if DEBUG
            Write("arm dismissed: target=" + target + " pairId=" + pairId);
            EmitSummary();
#endif
        }

        [Conditional("DEBUG")]
        public static void CaptureSet(OverlayAdjustTarget target, int pairId, OverlayRect rect)
        {
#if DEBUG
            _persistEvents++;
            if (_persistLogged < MaxRepeatLogs)
            {
                _persistLogged++;
                Write("capture set: target=" + target + " pairId=" + pairId
                    + " rect=" + (rect == null ? "<null>" : rect.ToCsv())
                    + " valid=" + (rect != null && rect.IsValid));
            }
#endif
        }

        [Conditional("DEBUG")]
        public static void DisplaySet(OverlayAdjustTarget target, int pairId, OverlayRect rect)
        {
#if DEBUG
            _persistEvents++;
            if (_persistLogged < MaxRepeatLogs)
            {
                _persistLogged++;
                Write("display set: target=" + target + " pairId=" + pairId
                    + " rect=" + (rect == null ? "<null>" : rect.ToCsv())
                    + " valid=" + (rect != null && rect.IsValid));
            }
#endif
        }

        [Conditional("DEBUG")]
        public static void StoreWrite(string what, string detail)
        {
#if DEBUG
            if (_storeWriteLogged < MaxRepeatLogs)
            {
                _storeWriteLogged++;
                Write("store write: " + what + " " + detail);
            }
#endif
        }

        [Conditional("DEBUG")]
        public static void ElementDown(
            AdjustMouseExit exit,
            OverlayAdjustTarget target,
            int pairIndex,
            OverlayRect startRect)
        {
#if DEBUG
            _elementDownEvents++;
            Write("element mouse-down: " + Describe(exit) + " target=" + target
                + " pairIndex=" + pairIndex
                + " start=" + (startRect == null ? "<null>" : startRect.ToCsv()));
#endif
        }

        [Conditional("DEBUG")]
        public static void ElementUp(AdjustMouseExit exit)
        {
#if DEBUG
            _elementUpEvents++;
            Write("element mouse-up: " + Describe(exit));
#endif
        }

        [Conditional("DEBUG")]
        public static void NoteHitModeResult(bool interactiveExpected, bool transparentBitCleared)
        {
#if DEBUG
            if (interactiveExpected && !transparentBitCleared)
            {
                _hitModeRemovalFailed = true;
            }
#endif
        }

        /// <summary>
        /// Turns the collected evidence into the verdict that names the dead
        /// link of the adjust chain. Pure, so tests pin the vocabulary.
        /// </summary>
#if DEBUG
        internal static string Summarize(
            int elementDownEvents,
            int persistEvents,
            bool hitModeRemovalFailed)
        {
            string verdict;
            if (persistEvents > 0 && elementDownEvents > 0)
            {
                verdict = "element-input-reached-and-persisted";
            }
            else if (elementDownEvents > 0)
            {
                verdict = "element-input-reached-but-not-persisted";
            }
            else
            {
                verdict = "no-element-input";
            }

            if (!hitModeRemovalFailed)
            {
                return verdict;
            }

            return verdict + " [hit-mode-transparent-bit-still-set]";
        }
#endif

#if DEBUG
        private const int MaxRepeatLogs = 3;

        private static int _elementDownEvents;
        private static int _elementUpEvents;
        private static int _persistEvents;
        private static int _persistLogged;
        private static int _storeWriteLogged;
        private static bool _hitModeRemovalFailed;

        private static void ResetEvidence()
        {
            _elementDownEvents = 0;
            _elementUpEvents = 0;
            _persistEvents = 0;
            _persistLogged = 0;
            _storeWriteLogged = 0;
            _hitModeRemovalFailed = false;
        }

        private static void EmitSummary()
        {
            Write("summary: element-down=" + _elementDownEvents
                + " element-up=" + _elementUpEvents
                + " persists=" + _persistEvents
                + " verdict=" + Summarize(
                    _elementDownEvents,
                    _persistEvents,
                    _hitModeRemovalFailed));
        }

        private static string Describe(AdjustMouseExit exit)
        {
            switch (exit)
            {
                case AdjustMouseExit.None:
                    return "proceed";
                case AdjustMouseExit.ClickThrough:
                    return "exit=click-through (session reports click-through)";
                case AdjustMouseExit.ArmedPairIndexMissing:
                    return "exit=armed-pair-index-missing";
                case AdjustMouseExit.StartRectInvalid:
                    return "exit=start-rect-invalid";
                case AdjustMouseExit.SenderNotBox:
                    return "exit=sender-not-box";
                case AdjustMouseExit.NotDragging:
                    return "exit=not-dragging";
                case AdjustMouseExit.ButtonReleased:
                    return "exit=button-released";
                case AdjustMouseExit.NoDragTarget:
                    return "exit=no-drag-target";
                default:
                    return "exit=unknown(" + exit + ")";
            }
        }

        private static void Write(string message)
        {
            Logger.Log.Debug("[AdjustTrace] " + message);
        }
#endif
    }
}
