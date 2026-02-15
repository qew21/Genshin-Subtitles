using System.Collections.Generic;

namespace GI_Subtitles.Models
{
    /// <summary>
    /// Subtitle item model for SRT processing
    /// </summary>
    public class SubtitleItem
    {
        public int Index { get; set; }
        public string TimeRange { get; set; }
        public List<string> Lines { get; set; } = new List<string>();
        public double StartTimeSeconds { get; set; }
        public double EndTimeSeconds { get; set; }

        public override string ToString()
        {
            return $"{Index}\r\n{TimeRange}\r\n{string.Join("\r\n", Lines)}\r\n";
        }
    }
}

