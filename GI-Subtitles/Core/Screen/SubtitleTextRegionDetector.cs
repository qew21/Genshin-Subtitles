using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OpenCvSharp;

namespace GI_Subtitles.Core.Screen
{
    /// <summary>
    /// Finds a coarse, lower-center text cluster in a game-window frame. The
    /// result is only a proposal; OCR remains responsible for confirming text.
    /// </summary>
    internal static class SubtitleTextRegionDetector
    {
        private sealed class Component
        {
            public int X;
            public int Y;
            public int Width;
            public int Height;
            public int Area;
            public int Right => X + Width;
            public int Bottom => Y + Height;
        }

        private sealed class TextRow
        {
            public Rectangle Bounds;
            public int Components;
            public int MedianHeight;
            public double CenterX => Bounds.Left + Bounds.Width / 2.0;
            public double CenterY => Bounds.Top + Bounds.Height / 2.0;
        }

        public static Rectangle? FindCandidate(Mat capturedFrame)
        {
            if (capturedFrame == null || capturedFrame.Empty() ||
                capturedFrame.Cols < 40 || capturedFrame.Rows < 30)
            {
                return null;
            }

            int left = (int)Math.Round(capturedFrame.Cols * 0.08);
            int top = (int)Math.Round(capturedFrame.Rows * 0.43);
            int width = Math.Max(1, capturedFrame.Cols - left * 2);
            int height = Math.Max(1, capturedFrame.Rows - top - (int)Math.Round(capturedFrame.Rows * 0.015));

            using (var search = new Mat(capturedFrame, new OpenCvSharp.Rect(left, top, width, height)))
            using (var gray = new Mat())
            using (var hsv = new Mat())
            using (var grayThreshold = new Mat())
            using (var whiteMask = new Mat())
            using (var goldMask = new Mat())
            using (var mask = new Mat())
            using (var adaptive = new Mat())
            {
                Cv2.CvtColor(search, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(search, hsv, ColorConversionCodes.BGR2HSV);

                Cv2.Threshold(gray, grayThreshold, 200, 255, ThresholdTypes.Binary);
                Cv2.InRange(hsv, new Scalar(0, 0, 185), new Scalar(180, 90, 255), whiteMask);
                Cv2.BitwiseOr(grayThreshold, whiteMask, mask);

                // Genshin subtitles and speaker labels may use warm/gold text.
                Cv2.InRange(hsv, new Scalar(8, 45, 130), new Scalar(38, 255, 255), goldMask);
                Cv2.BitwiseOr(mask, goldMask, mask);

                // Recover outlined white text over bright character art. Keeping
                // this local to the lower-center search band limits background noise.
                int scaleBasis = Math.Max(1, capturedFrame.Rows);
                int blockSize = Clamp((int)Math.Round(31.0 * scaleBasis / 1600.0), 15, 51);
                if (blockSize % 2 == 0)
                {
                    blockSize++;
                }
                double adaptiveC = Math.Max(3, 7.0 * scaleBasis / 1600.0);
                Cv2.AdaptiveThreshold(
                    gray,
                    adaptive,
                    255,
                    AdaptiveThresholdTypes.GaussianC,
                    ThresholdTypes.BinaryInv,
                    blockSize,
                    adaptiveC);
                Cv2.BitwiseOr(mask, adaptive, mask);

                return FindBestTextCluster(mask, left, top);
            }
        }

        private static Rectangle? FindBestTextCluster(Mat sourceMask, int offsetX, int offsetY)
        {
            using (var mask = sourceMask.Clone())
            using (var closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3)))
            using (var labels = new Mat())
            using (var stats = new Mat())
            using (var centroids = new Mat())
            {
                Cv2.MorphologyEx(mask, mask, MorphTypes.Close, closeKernel);
                int count = Cv2.ConnectedComponentsWithStats(
                    mask,
                    labels,
                    stats,
                    centroids,
                    PixelConnectivity.Connectivity8,
                    MatType.CV_32S);

                int minHeight = Math.Max(2, (int)Math.Round(mask.Rows * 0.005));
                int maxHeight = Math.Max(12, (int)Math.Round(mask.Rows * 0.16));
                int maxWidth = Math.Max(24, (int)Math.Round(mask.Cols * 0.12));
                int maxArea = Math.Max(300, (int)Math.Round(mask.Rows * mask.Cols * 0.0015));
                var components = new List<Component>();

                for (int i = 1; i < count; i++)
                {
                    int x = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
                    int y = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
                    int width = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                    int height = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
                    int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                    if (area < 2 || area > maxArea || height < minHeight || height > maxHeight ||
                        width < 1 || width > maxWidth || (double)width / height > 5.0)
                    {
                        continue;
                    }

                    components.Add(new Component
                    {
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height,
                        Area = area
                    });
                }

                List<TextRow> rows = GroupComponentsIntoRows(components, mask.Cols, mask.Rows);
                if (rows.Count == 0)
                {
                    return null;
                }

                Rectangle? best = null;
                double bestScore = double.MinValue;
                for (int start = 0; start < rows.Count; start++)
                {
                    var group = new List<TextRow>();
                    for (int end = start; end < Math.Min(rows.Count, start + 4); end++)
                    {
                        TextRow row = rows[end];
                        if (group.Count > 0)
                        {
                            TextRow previous = group[group.Count - 1];
                            int verticalGap = row.Bounds.Top - previous.Bounds.Bottom;
                            int maxGap = Math.Max(
                                (int)Math.Round(mask.Rows * 0.055),
                                Math.Max(previous.MedianHeight, row.MedianHeight) * 2);
                            if (verticalGap > maxGap || Math.Abs(row.CenterX - previous.CenterX) > mask.Cols * 0.24)
                            {
                                break;
                            }
                        }

                        group.Add(row);
                        Rectangle union = group.Select(item => item.Bounds).Aggregate(Rectangle.Union);
                        double centerOffset = Math.Abs(union.Left + union.Width / 2.0 - mask.Cols / 2.0) /
                                              Math.Max(1, mask.Cols);
                        if (centerOffset > 0.24 || union.Width < Math.Max(18, group.Max(item => item.MedianHeight) * 1.4))
                        {
                            continue;
                        }

                        int componentCount = group.Sum(item => item.Components);
                        if (componentCount < 2)
                        {
                            continue;
                        }

                        double bottomPreference = (union.Bottom / (double)Math.Max(1, mask.Rows));
                        double widthPreference = Math.Min(1.0, union.Width / Math.Max(1.0, mask.Cols * 0.50));
                        double rowPreference = Math.Min(1.0, group.Count / 3.0);
                        double componentPreference = Math.Min(1.0, componentCount / 14.0);
                        double score = 0.34 * rowPreference +
                                       0.30 * componentPreference +
                                       0.20 * widthPreference +
                                       0.16 * bottomPreference -
                                       centerOffset * 0.8;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = new Rectangle(
                                offsetX + union.X,
                                offsetY + union.Y,
                                union.Width,
                                union.Height);
                        }
                    }
                }

                return best;
            }
        }

        private static List<TextRow> GroupComponentsIntoRows(List<Component> components, int width, int height)
        {
            var groups = new List<List<Component>>();
            foreach (Component component in components.OrderBy(item => item.Y + item.Height / 2.0))
            {
                double centerY = component.Y + component.Height / 2.0;
                List<Component> group = groups
                    .Where(items => Math.Abs(items.Average(item => item.Y + item.Height / 2.0) - centerY) <=
                                    Math.Max(3, Math.Min(MedianHeight(items), component.Height) * 0.75))
                    .OrderBy(items => Math.Abs(items.Average(item => item.Y + item.Height / 2.0) - centerY))
                    .FirstOrDefault();
                if (group == null)
                {
                    group = new List<Component>();
                    groups.Add(group);
                }
                group.Add(component);
            }

            var rows = new List<TextRow>();
            foreach (List<Component> group in groups)
            {
                int medianHeight = MedianHeight(group);
                int maxGap = Math.Max(12, (int)Math.Round(medianHeight * 2.8));
                var run = new List<Component>();
                foreach (Component component in group.OrderBy(item => item.X))
                {
                    if (run.Count > 0 && component.X - run.Max(item => item.Right) > maxGap)
                    {
                        AddRow(run, rows, width, height);
                        run.Clear();
                    }
                    run.Add(component);
                }
                if (run.Count > 0)
                {
                    AddRow(run, rows, width, height);
                }
            }

            return rows.OrderBy(item => item.Bounds.Top).ThenBy(item => item.Bounds.Left).ToList();
        }

        private static void AddRow(List<Component> run, List<TextRow> rows, int width, int height)
        {
            if (run.Count < 2)
            {
                return;
            }

            int medianHeight = MedianHeight(run);
            int left = run.Min(item => item.X);
            int right = run.Max(item => item.Right);
            int top = run.Min(item => item.Y);
            int bottom = run.Max(item => item.Bottom);
            int rowWidth = right - left;
            if (medianHeight < Math.Max(2, (int)Math.Round(height * 0.005)) ||
                rowWidth < Math.Max(18, medianHeight * 1.4))
            {
                return;
            }

            double centerX = left + rowWidth / 2.0;
            if (Math.Abs(centerX - width / 2.0) > width * 0.24)
            {
                return;
            }

            rows.Add(new TextRow
            {
                Bounds = Rectangle.FromLTRB(left, top, right, bottom),
                Components = run.Count,
                MedianHeight = medianHeight
            });
        }

        private static int MedianHeight(List<Component> components)
        {
            int[] heights = components.Select(item => item.Height).OrderBy(item => item).ToArray();
            return heights.Length == 0 ? 0 : heights[heights.Length / 2];
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(value, maximum));
        }
    }
}
