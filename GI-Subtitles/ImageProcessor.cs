using Emgu.CV.Structure;
using Emgu.CV;
using System.Drawing;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;
using System.Text;
using System;
using System.Collections.Generic;
using PaddleOCRSharp;

namespace GI_Subtitles
{
    public class ImageProcessor
    {
        public static Bitmap EnhanceTextInImage(Bitmap inputImage)
        {
            Image<Bgr, byte> img = new Image<Bgr, byte>(inputImage);

            // 转换为灰度图像
            Image<Gray, byte> gray = img.Convert<Gray, byte>();

            // 应用二值化
            double thresholdValue = 240; // 阈值（0 - 255）
            double maxValue = 245;       // 最大值
            gray = gray.ThresholdBinary(new Gray(thresholdValue), new Gray(maxValue));

            // 将处理后的Emgu CV Image转换回Bitmap
            Bitmap processedImage = gray.Bitmap;

            return processedImage;
        }

        public static string ComputeDHash(Bitmap bmp)
        {
            var resized = new Bitmap(9, 8);
            var g = Graphics.FromImage(resized);
            g.DrawImage(bmp, 0, 0, 9, 8);

            var hash = new StringBuilder();
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var left = resized.GetPixel(x, y).GetBrightness();
                    var right = resized.GetPixel(x + 1, y).GetBrightness();
                    hash.Append(left > right ? '1' : '0');
                }
            }
            resized.Dispose();
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
        /// </summary>
        /// <param name="targetHash">目标哈希</param>
        /// <param name="hashDict">哈希字典</param>
        /// <param name="maxDistance">最大允许的汉明距离（默认5，约6%的差异）</param>
        /// <returns>最相似的哈希键，如果未找到则返回null</returns>
        public static string FindSimilarImageHash(string targetHash, Dictionary<string, string> hashDict, int maxDistance = 5)
        {
            if (string.IsNullOrEmpty(targetHash) || hashDict == null || hashDict.Count == 0)
                return null;

            string bestMatch = null;
            int minDistance = int.MaxValue;

            foreach (var kvp in hashDict)
            {
                int distance = CalculateHammingDistance(targetHash, kvp.Key);
                if (distance < minDistance && distance <= maxDistance)
                {
                    minDistance = distance;
                    bestMatch = kvp.Key;
                }
            }

            return bestMatch;
        }

    }

}

