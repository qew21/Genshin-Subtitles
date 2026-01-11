using System;
using System.Collections.Generic;
using System.Drawing;

namespace PaddleOCRSharp
{
    /// <summary>
    /// OCR识别结果
    /// </summary>
    public class OCRResult
    {
        /// <summary>
        /// 识别的文本块列表
        /// </summary>
        public List<TextBlock> TextBlocks { get; set; }

        /// <summary>
        /// 合并后的文本
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// JSON格式的结果
        /// </summary>
        public string JsonText { get; set; }

        public OCRResult()
        {
            TextBlocks = new List<TextBlock>();
            Text = "";
        }
    }

    /// <summary>
    /// 文本块
    /// </summary>
    public class TextBlock
    {
        /// <summary>
        /// 识别的文本
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// 置信度
        /// </summary>
        public float Score { get; set; }

        /// <summary>
        /// 文本框的四个角点
        /// </summary>
        public PointF[] BoxPoints { get; set; }

        public TextBlock()
        {
            BoxPoints = new PointF[4];
        }
    }
}