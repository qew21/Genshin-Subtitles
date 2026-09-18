using System;
using System.Collections.Generic;

namespace GI_Subtitles.Core.Overlay
{
    /// <summary>
    /// Picks the one monitor a hint anchors to: the monitor carrying the first valid
    /// display region among the candidates (voice-primary first), else the primary
    /// monitor. Pure over rectangles so tests need no screen APIs.
    /// </summary>
    public static class HintScreenSelection
    {
        public static OverlayRect Select(
            IReadOnlyList<OverlayRect> screens,
            int primaryIndex,
            IReadOnlyList<OverlayRect> displayCandidates)
        {
            if (screens == null || screens.Count == 0)
            {
                return OverlayRect.Invalid;
            }

            int primary = primaryIndex >= 0 && primaryIndex < screens.Count ? primaryIndex : 0;
            OverlayRect anchor = FirstValid(displayCandidates);
            if (!anchor.IsValid)
            {
                return screens[primary];
            }

            int best = BestOverlapIndex(screens, anchor);
            return best < 0 ? screens[primary] : screens[best];
        }

        private static OverlayRect FirstValid(IReadOnlyList<OverlayRect> rects)
        {
            if (rects == null)
            {
                return OverlayRect.Invalid;
            }

            for (int i = 0; i < rects.Count; i++)
            {
                if (rects[i] != null && rects[i].IsValid)
                {
                    return rects[i];
                }
            }

            return OverlayRect.Invalid;
        }

        private static int BestOverlapIndex(IReadOnlyList<OverlayRect> screens, OverlayRect anchor)
        {
            int best = -1;
            long bestArea = 0;
            for (int i = 0; i < screens.Count; i++)
            {
                long area = OverlapArea(screens[i], anchor);
                if (area > bestArea)
                {
                    best = i;
                    bestArea = area;
                }
            }

            return best;
        }

        private static long OverlapArea(OverlayRect screen, OverlayRect anchor)
        {
            if (screen == null || !screen.IsValid)
            {
                return 0;
            }

            long left = Math.Max(screen.X, anchor.X);
            long top = Math.Max(screen.Y, anchor.Y);
            long right = Math.Min((long)screen.X + screen.Width, (long)anchor.X + anchor.Width);
            long bottom = Math.Min((long)screen.Y + screen.Height, (long)anchor.Y + anchor.Height);
            if (right <= left || bottom <= top)
            {
                return 0;
            }

            return (right - left) * (bottom - top);
        }
    }
}
