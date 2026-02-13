using Emgu.CV.Structure;
using Emgu.CV;
using System.Drawing;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;
using System.Text;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using PaddleOCRSharp;
using OpenCvSharp;
using System.Windows.Input;
using System.Diagnostics;

namespace GI_Subtitles
{
    public class ImageProcessor
    {
        /// <summary>
        /// Use LockBits to optimize image hash calculation.
        /// Optimization: avoid using GetPixel(), and access memory data directly.
        /// </summary>
        public static string ComputeDHash(Bitmap bmp)
        {
            // Create a scaled 9x8 image
            var resized = new Bitmap(9, 8);
            try
            {
                using (Graphics g = Graphics.FromImage(resized))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                    g.DrawImage(bmp, 0, 0, 9, 8);
                }

                var hash = new StringBuilder(64); // Pre-allocate capacity

                // Use LockBits to directly access pixel data, 3–5 times faster than GetPixel
                var data = resized.LockBits(
                    new Rectangle(0, 0, 9, 8),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb);

                try
                {
                    unsafe
                    {
                        byte* ptr = (byte*)data.Scan0;
                        int stride = data.Stride;

                        for (int y = 0; y < 8; y++)
                        {
                            for (int x = 0; x < 8; x++)
                            {
                                // Calculate offsets of two adjacent pixels
                                int offset1 = y * stride + x * 3;
                                int offset2 = y * stride + (x + 1) * 3;

                                // Compute brightness using grayscale formula (0.299*R + 0.587*G + 0.114*B)
                                int brightness1 = (int)(ptr[offset1] * 0.299 + ptr[offset1 + 1] * 0.587 + ptr[offset1 + 2] * 0.114);
                                int brightness2 = (int)(ptr[offset2] * 0.299 + ptr[offset2 + 1] * 0.587 + ptr[offset2 + 2] * 0.114);

                                hash.Append(brightness1 > brightness2 ? '1' : '0');
                            }
                        }
                    }
                }
                finally
                {
                    resized.UnlockBits(data);
                }

                return hash.ToString();
            }
            finally
            {
                resized.Dispose();
            }
        }

        public static string ComputeRobustHash(OpenCvSharp.Mat srcMat)
        {
            if (srcMat == null) return string.Empty;

            // 1. Convert to grayscale
            using var gray = new OpenCvSharp.Mat();
            if (srcMat.Channels() == 3 || srcMat.Channels() == 4)
                Cv2.CvtColor(srcMat, gray, ColorConversionCodes.BGR2GRAY);
            else
                srcMat.CopyTo(gray);

            // 2. Key step: binarization (thresholding)
            using var bin = new OpenCvSharp.Mat();
            Cv2.Threshold(gray, bin, 245, 255, ThresholdTypes.Binary);

            using var points = new OpenCvSharp.Mat();
            Cv2.FindNonZero(bin, points);

            Rect roi;
            if (points.Total() > 0)
            {
                roi = Cv2.BoundingRect(points);

                int padding = 2;
                roi.X = Math.Max(0, roi.X - padding);
                roi.Y = Math.Max(0, roi.Y - padding);
                roi.Width = Math.Min(bin.Width - roi.X, roi.Width + padding * 2);
                roi.Height = Math.Min(bin.Height - roi.Y, roi.Height + padding * 2);
            }
            else
            {
                // All-black image: directly return an all-zero hash, or treat as empty
                return new string('0', 64);
            }

            // Crop out the region that only contains text
            using var cropped = new OpenCvSharp.Mat(bin, roi);
            using var resized = new OpenCvSharp.Mat();
            Cv2.Resize(cropped, resized, new OpenCvSharp.Size(9, 8), 0, 0, InterpolationFlags.Area);

            // 4. Compute hash (resized is derived from a binary image but becomes grayscale due to Area interpolation)
            var hash = new StringBuilder(64);

            unsafe
            {
                byte* ptr = (byte*)resized.DataPointer;
                int step = (int)resized.Step();

                for (int y = 0; y < 8; y++)
                {
                    byte* row = ptr + (y * step);
                    for (int x = 0; x < 8; x++)
                    {
                        // Compare "text density" of adjacent blocks
                        hash.Append(row[x] > row[x + 1] ? '1' : '0');
                    }
                }
            }

            return hash.ToString();
        }

        /// <summary>
        /// Calculate the Hamming distance between two hash strings (number of different bits)
        /// </summary>
        public static int CalculateHammingDistance(string hash1, string hash2)
        {
            if (hash1 == null || hash2 == null || hash1.Length != hash2.Length)
                return int.MaxValue;

            int distance = 0;
            for (int i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i])
                    distance++;
            }
            return distance;
        }

        /// <summary>
        /// Find the most similar image hash (fuzzy matching based on Hamming distance).
        /// Supports both Dictionary and LRUCache.
        /// </summary>
        /// <param name="targetHash">Target hash</param>
        /// <param name="hashDict">Hash dictionary (supports Dictionary or LRUCache)</param>
        /// <param name="maxDistance">Maximum allowed Hamming distance (default 5, about 6% difference)</param>
        /// <returns>The most similar hash key, or null if none is found</returns>
        public static string FindSimilarImageHash(string targetHash, object hashDict, int maxDistance = 5)
        {
            if (string.IsNullOrEmpty(targetHash) || hashDict == null)
                return null;

            string bestMatch = null;
            int minDistance = int.MaxValue;

            // Support Dictionary<string, string> and LRUCache<string, string>
            IEnumerable<KeyValuePair<string, string>> items = null;

            if (hashDict is Dictionary<string, string> dict)
            {
                items = dict;
            }
            else if (hashDict is LRUCache<string, string> lruCache)
            {
                IEnumerable<string> keysCollection = lruCache.Keys;

                foreach (var key in keysCollection)
                {
                    int distance = CalculateHammingDistance(targetHash, key);

                    if (distance == 0) return key;

                    if (distance < minDistance && distance <= maxDistance)
                    {
                        minDistance = distance;
                        bestMatch = key;
                    }
                }
            }

            return bestMatch;
        }

    }

}


