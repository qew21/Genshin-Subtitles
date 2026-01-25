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
        /// 使用 LockBits 优化图像哈希计算
        /// 优化：避免使用 GetPixel() 方法，直接访问内存数据
        /// </summary>
        public static string ComputeDHash(Bitmap bmp)
        {
            // 创建 9x8 的缩放图像
            var resized = new Bitmap(9, 8);
            try
            {
                using (Graphics g = Graphics.FromImage(resized))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                    g.DrawImage(bmp, 0, 0, 9, 8);
                }

                var hash = new StringBuilder(64); // 预分配容量

                // 使用 LockBits 直接访问像素数据，比 GetPixel 快 3-5 倍
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
                                // 计算两个相邻像素的偏移量
                                int offset1 = y * stride + x * 3;
                                int offset2 = y * stride + (x + 1) * 3;

                                // 使用灰度公式计算亮度 (0.299*R + 0.587*G + 0.114*B)
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

            // 1. 转灰度
            using var gray = new OpenCvSharp.Mat();
            if (srcMat.Channels() == 3 || srcMat.Channels() == 4)
                Cv2.CvtColor(srcMat, gray, ColorConversionCodes.BGR2GRAY);
            else
                srcMat.CopyTo(gray);

            // 2. 关键步骤：二值化 (Threshold)
            using var bin = new OpenCvSharp.Mat();
            Cv2.Threshold(gray, bin, 200, 255, ThresholdTypes.Binary);

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
                // 全黑图像，直接返回全0哈希，或者处理为空
                return new string('0', 64);
            }

            // 裁剪出只有字的区域
            using var cropped = new OpenCvSharp.Mat(bin, roi);
            using var resized = new OpenCvSharp.Mat();
            Cv2.Resize(cropped, resized, new OpenCvSharp.Size(9, 8), 0, 0, InterpolationFlags.Area);

            // 4. 计算哈希 (此时 resized 虽然源自二值图，但因 Area 插值变成了灰度图)
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
                        // 比较相邻块的“文字密度”
                        hash.Append(row[x] > row[x + 1] ? '1' : '0');
                    }
                }
            }

            return hash.ToString();
        }

        /// <summary>
        /// 计算两个哈希字符串的汉明距离（不同位的数量）
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
        /// 查找最相似的图像哈希（基于汉明距离的模糊匹配）
        /// 支持 Dictionary 和 LRUCache
        /// </summary>
        /// <param name="targetHash">目标哈希</param>
        /// <param name="hashDict">哈希字典（支持 Dictionary 或 LRUCache）</param>
        /// <param name="maxDistance">最大允许的汉明距离（默认5，约6%的差异）</param>
        /// <returns>最相似的哈希键，如果未找到则返回null</returns>
        public static string FindSimilarImageHash(string targetHash, object hashDict, int maxDistance = 5)
        {
            if (string.IsNullOrEmpty(targetHash) || hashDict == null)
                return null;

            string bestMatch = null;
            int minDistance = int.MaxValue;

            // 支持 Dictionary<string, string> 和 LRUCache<string, string>
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


