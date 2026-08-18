using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace GI_Subtitles.Services.OCR
{
    /// <summary>
    /// Finds wide, low-saturation text bands on a predominantly dark game frame.
    /// The caller is expected to pass only the central portion of the game screen.
    /// </summary>
    internal static class DarkScreenSubtitleDetector
    {
        private const double MinimumDarkRatio = 0.78;
        private const double MinimumBrightRatio = 0.0005;
        private const double MaximumBrightRatio = 0.25;
        private const int MaximumAnalysisWidth = 960;
        private const int MaximumTextLines = 10;

        public static bool TryFindSubtitleRegion(
            Mat screen,
            out Rect subtitleRegion,
            out bool isDarkScreen,
            out double darkRatio,
            out double brightRatio)
        {
            subtitleRegion = default;
            isDarkScreen = false;
            darkRatio = 0;
            brightRatio = 0;

            if (screen == null || screen.Empty() || screen.Width < 320 || screen.Height < 160)
            {
                return false;
            }

            using var bgr = EnsureBgr(screen);
            using var analysisFrame = new Mat();
            using var gray = new Mat();
            using var hsv = new Mat();
            using var darkMask = new Mat();
            using var brightMask = new Mat();

            double analysisScale = Math.Min(1.0, MaximumAnalysisWidth / (double)bgr.Width);
            if (analysisScale < 1.0)
            {
                Cv2.Resize(
                    bgr,
                    analysisFrame,
                    new Size(),
                    analysisScale,
                    analysisScale,
                    InterpolationFlags.Area);
            }
            else
            {
                bgr.CopyTo(analysisFrame);
            }

            Cv2.CvtColor(analysisFrame, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(analysisFrame, hsv, ColorConversionCodes.BGR2HSV);
            Cv2.InRange(gray, Scalar.All(0), Scalar.All(48), darkMask);
            // White and light-gray subtitle pixels have high value and relatively low saturation.
            // This excludes the common yellow "continue" prompt even if it enters the search area.
            Cv2.InRange(hsv, new Scalar(0, 0, 185), new Scalar(180, 90, 255), brightMask);

            double totalPixels = analysisFrame.Width * (double)analysisFrame.Height;
            darkRatio = Cv2.CountNonZero(darkMask) / totalPixels;
            brightRatio = Cv2.CountNonZero(brightMask) / totalPixels;
            isDarkScreen = darkRatio >= MinimumDarkRatio;
            if (!isDarkScreen)
            {
                return false;
            }

            if (brightRatio < MinimumBrightRatio || brightRatio > MaximumBrightRatio)
            {
                return false;
            }

            List<RowBand> lines = FindTextLines(brightMask);
            if (lines.Count == 0)
            {
                return false;
            }

            Candidate best = default;
            bool found = false;
            for (int start = 0; start < lines.Count; start++)
            {
                Rect combined = lines[start].Bounds;
                // Story summaries and cut-scene cards can contain substantially more
                // than three rows. Keep the whole contiguous central text block so OCR
                // does not fall back to a lower "continue" prompt after missing it.
                for (int count = 1;
                     count <= MaximumTextLines && start + count <= lines.Count;
                     count++)
                {
                    if (count > 1)
                    {
                        RowBand previous = lines[start + count - 2];
                        RowBand next = lines[start + count - 1];
                        int allowedGap = Math.Max(12, Math.Max(previous.Bounds.Height, next.Bounds.Height));
                        if (next.Bounds.Top - previous.Bounds.Bottom > allowedGap)
                        {
                            break;
                        }
                        combined = Union(combined, next.Bounds);
                    }

                    double widthRatio = combined.Width / (double)analysisFrame.Width;
                    double centerRatio = (combined.Top + combined.Height / 2.0) / analysisFrame.Height;
                    if (widthRatio < 0.20 || centerRatio < 0.12 || centerRatio > 0.88)
                    {
                        continue;
                    }

                    double centerPenalty = Math.Abs(centerRatio - 0.55);
                    double score = widthRatio * 3.0 + Math.Min(count, 6) * 0.15 - centerPenalty * 1.5;
                    if (!found || score > best.Score)
                    {
                        best = new Candidate(combined, score);
                        found = true;
                    }
                }
            }

            if (!found)
            {
                return false;
            }

            int horizontalPadding = Math.Min(36, Math.Max(8, analysisFrame.Width / 100));
            int verticalPadding = Math.Min(24, Math.Max(6, best.Bounds.Height / 4));
            Rect analysisRegion = InflateWithin(
                best.Bounds,
                horizontalPadding,
                verticalPadding,
                analysisFrame.Size());
            subtitleRegion = ScaleWithin(analysisRegion, 1.0 / analysisScale, screen.Size());
            return subtitleRegion.Width > 0 && subtitleRegion.Height > 0;
        }

        private static Mat EnsureBgr(Mat source)
        {
            if (source.Channels() == 3)
            {
                return source.Clone();
            }

            var converted = new Mat();
            Cv2.CvtColor(
                source,
                converted,
                source.Channels() == 4
                    ? ColorConversionCodes.BGRA2BGR
                    : ColorConversionCodes.GRAY2BGR);
            return converted;
        }

        private static unsafe List<RowBand> FindTextLines(Mat mask)
        {
            var rowCounts = new int[mask.Height];
            byte* basePointer = (byte*)mask.DataPointer;
            long step = mask.Step();
            for (int y = 0; y < mask.Height; y++)
            {
                byte* row = basePointer + y * step;
                int count = 0;
                for (int x = 0; x < mask.Width; x++)
                {
                    if (row[x] != 0)
                    {
                        count++;
                    }
                }
                rowCounts[y] = count;
            }

            int minimumPixelsPerRow = Math.Max(10, mask.Width / 250);
            int maximumGap = Math.Max(2, mask.Height / 300);
            var lines = new List<RowBand>();
            int start = -1;
            int lastActive = -1;
            for (int y = 0; y <= mask.Height; y++)
            {
                bool active = y < mask.Height && rowCounts[y] >= minimumPixelsPerRow;
                if (active)
                {
                    if (start < 0)
                    {
                        start = y;
                    }
                    lastActive = y;
                    continue;
                }

                if (start < 0 || y - lastActive <= maximumGap)
                {
                    continue;
                }

                Rect bounds = FindBandBounds(mask, start, lastActive);
                int minimumHeight = Math.Max(5, mask.Height / 180);
                if (bounds.Height >= minimumHeight &&
                    bounds.Height <= mask.Height * 0.14 &&
                    bounds.Width >= mask.Width * 0.12)
                {
                    lines.Add(new RowBand(bounds));
                }
                start = -1;
                lastActive = -1;
            }

            return lines;
        }

        private static unsafe Rect FindBandBounds(Mat mask, int top, int bottom)
        {
            int left = mask.Width;
            int right = -1;
            byte* basePointer = (byte*)mask.DataPointer;
            long step = mask.Step();
            for (int y = top; y <= bottom; y++)
            {
                byte* row = basePointer + y * step;
                for (int x = 0; x < mask.Width; x++)
                {
                    if (row[x] == 0)
                    {
                        continue;
                    }
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }
            }

            return right >= left
                ? Rect.FromLTRB(left, top, right + 1, bottom + 1)
                : default;
        }

        private static Rect Union(Rect left, Rect right)
        {
            return Rect.FromLTRB(
                Math.Min(left.Left, right.Left),
                Math.Min(left.Top, right.Top),
                Math.Max(left.Right, right.Right),
                Math.Max(left.Bottom, right.Bottom));
        }

        private static Rect InflateWithin(Rect source, int horizontal, int vertical, Size bounds)
        {
            return Rect.FromLTRB(
                Math.Max(0, source.Left - horizontal),
                Math.Max(0, source.Top - vertical),
                Math.Min(bounds.Width, source.Right + horizontal),
                Math.Min(bounds.Height, source.Bottom + vertical));
        }

        private static Rect ScaleWithin(Rect source, double scale, Size bounds)
        {
            return Rect.FromLTRB(
                Math.Max(0, (int)Math.Floor(source.Left * scale)),
                Math.Max(0, (int)Math.Floor(source.Top * scale)),
                Math.Min(bounds.Width, (int)Math.Ceiling(source.Right * scale)),
                Math.Min(bounds.Height, (int)Math.Ceiling(source.Bottom * scale)));
        }

        private readonly struct RowBand
        {
            public RowBand(Rect bounds)
            {
                Bounds = bounds;
            }

            public Rect Bounds { get; }
        }

        private readonly struct Candidate
        {
            public Candidate(Rect bounds, double score)
            {
                Bounds = bounds;
                Score = score;
            }

            public Rect Bounds { get; }
            public double Score { get; }
        }
    }
}
