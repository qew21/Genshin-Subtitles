using System;
using System.Collections.Generic;
using System.Drawing;

namespace PaddleOCRSharp
{
    /// <summary>
    /// OCR recognition result
    /// </summary>
    public class OCRResult
    {
        /// <summary>
        /// List of recognized text blocks
        /// </summary>
        public List<TextBlock> TextBlocks { get; set; }

        /// <summary>
        /// Merged text
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Number of text regions found by the detector before any caller-specific filtering.
        /// </summary>
        public int DetectedTextRegionCount { get; set; }

        /// <summary>
        /// Number of text regions sent to the recognizer.
        /// </summary>
        public int RecognizedTextRegionCount { get; set; }

        /// <summary>
        /// Time spent finding text boxes in the image, in milliseconds.
        /// </summary>
        public double TextDetectionElapsedMilliseconds { get; set; }

        /// <summary>
        /// Time spent selecting subtitle boxes, in milliseconds.
        /// </summary>
        public double SubtitleSelectionElapsedMilliseconds { get; set; }

        /// <summary>
        /// Time spent cropping and recognizing selected text boxes, in milliseconds.
        /// </summary>
        public double TextRecognitionElapsedMilliseconds { get; set; }

        /// <summary>
        /// JSON format result
        /// </summary>
        public string JsonText { get; set; }

        public OCRResult()
        {
            TextBlocks = new List<TextBlock>();
            Text = "";
        }
    }

    /// <summary>
    /// Text block
    /// </summary>
    public class TextBlock
    {
        /// <summary>
        /// Recognized text
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Confidence
        /// </summary>
        public float Score { get; set; }

        /// <summary>
        /// Four corners of text box
        /// </summary>
        public PointF[] BoxPoints { get; set; }

        public TextBlock()
        {
            BoxPoints = new PointF[4];
        }
    }
}
