using System;

namespace GI_Subtitles.Models
{
    /// <summary>
    /// SRT entry model for video processing
    /// </summary>
    public class SrtEntry
    {
        public int Index { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Text { get; set; }
    }
}

