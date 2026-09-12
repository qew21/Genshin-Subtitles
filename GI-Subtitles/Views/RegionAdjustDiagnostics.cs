using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GI_Subtitles.Core.Overlay;

namespace GI_Subtitles.Views
{
    /// <summary>
    /// Debug-only Win32 diagnostics for the region-adjust pipeline:
    /// the exStyle switch around WS_EX_TRANSPARENT and the WS_DISABLED clear
    /// that must accompany it — the key points only. The per-frame movement
    /// probes (window input, cursor/render sampling, thread hooks) were
    /// removed. Every public method is [Conditional("DEBUG")] and every body
    /// — including the Win32 declarations — is compiled out unless DEBUG is
    /// defined, so Release artifacts contain none of this code.
    /// </summary>
    public static class RegionAdjustDiagnostics
    {
        [Conditional("DEBUG")]
        public static void HitModeApplied(
            IntPtr hwnd,
            bool interactive,
            int beforeExStyle,
            int newExStyle,
            int setResult,
            int lastError)
        {
#if DEBUG
            if (hwnd == IntPtr.Zero)
            {
                Write("hit-mode: skipped, hwnd is zero");
                return;
            }

            int afterExStyle = GetWindowLong(hwnd, GwlExStyle);
            bool transparentBefore = HasFlag(beforeExStyle);
            bool transparentAfter = HasFlag(afterExStyle);
            bool setFailed = setResult == 0 && lastError != 0;

            string verdict;
            if (setFailed)
            {
                verdict = "SetWindowLong FAILED";
            }
            else if (interactive && transparentAfter)
            {
                verdict = "TRANSPARENT-BIT-STILL-SET";
            }
            else if (!interactive && !transparentAfter)
            {
                verdict = "TRANSPARENT-BIT-NOT-RESTORED";
            }
            else if (afterExStyle != newExStyle)
            {
                verdict = "STYLE-MISMATCH-AFTER-SET";
            }
            else
            {
                verdict = "ok";
            }

            Write("hit-mode interactive=" + interactive
                + " exStyleBefore=0x" + beforeExStyle.ToString("X")
                + " exStyleAfter=0x" + afterExStyle.ToString("X")
                + " requested=0x" + newExStyle.ToString("X")
                + " setReturn=0x" + setResult.ToString("X")
                + " lastError=" + lastError
                + " transparentBefore=" + transparentBefore
                + " transparentAfter=" + transparentAfter
                + " verdict=" + verdict);

            RegionAdjustTrace.NoteHitModeResult(
                interactiveExpected: interactive,
                transparentBitCleared: !transparentAfter);
#endif
        }

        [Conditional("DEBUG")]
        public static void DisabledBitCleared(
            IntPtr hwnd,
            int beforeStyle,
            int newStyle,
            int setResult,
            int lastError)
        {
#if DEBUG
            if (hwnd == IntPtr.Zero)
            {
                Write("disabled-bit: skipped, hwnd is zero");
                return;
            }

            int afterStyle = GetWindowLong(hwnd, GwlStyle);
            bool setFailed = setResult == 0 && lastError != 0;

            string verdict;
            if (setFailed)
            {
                verdict = "SetWindowLong FAILED";
            }
            else if ((afterStyle & WsDisabled) != 0)
            {
                verdict = "DISABLED-BIT-STILL-SET";
            }
            else if (afterStyle != newStyle)
            {
                verdict = "STYLE-MISMATCH-AFTER-SET";
            }
            else
            {
                verdict = "ok";
            }

            Write("disabled-bit cleared: styleBefore=0x" + beforeStyle.ToString("X")
                + " styleAfter=0x" + afterStyle.ToString("X")
                + " requested=0x" + newStyle.ToString("X")
                + " setReturn=0x" + setResult.ToString("X")
                + " lastError=" + lastError
                + " verdict=" + verdict);
#endif
        }

#if DEBUG
        private const int GwlExStyle = -20;
        private const int GwlStyle = -16;
        private const int WsExTransparent = 0x00000020;
        private const int WsDisabled = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private static bool HasFlag(int exStyle)
        {
            return (exStyle & WsExTransparent) != 0;
        }

        private static void Write(string message)
        {
            Common.Logger.Log.Debug("[AdjustTrace] " + message);
        }
#endif
    }
}
