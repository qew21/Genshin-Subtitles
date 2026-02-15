namespace GI_Subtitles.Models
{
    /// <summary>
    /// Region information class (for JSON serialization/deserialization)
    /// </summary>
    public class RegionInfo
    {
        public string VideoPath { get; set; }
        public string TimeCode { get; set; } // Format: HH:MM:SS or MM:SS
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int VideoWidth { get; set; }
        public int VideoHeight { get; set; }
    }
}

