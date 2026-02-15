using System.Collections.Generic;

namespace GI_Subtitles.Models
{
    /// <summary>
    /// OCR test result model
    /// </summary>
    public class OCRTestResult
    {
        public string FileName { get; set; }
        public string OCRText { get; set; }
        public double DurationMs { get; set; }
    }

    /// <summary>
    /// Summary model for OCR test results
    /// </summary>
    public class Summary
    {
        public List<OCRTestResult> Results { get; set; } = new List<OCRTestResult>();
        public double AverageDurationMs { get; set; }
    }
}

