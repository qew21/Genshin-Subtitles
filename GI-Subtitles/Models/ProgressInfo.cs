namespace GI_Subtitles.Models
{
    /// <summary>
    /// Progress information class
    /// </summary>
    public class ProgressInfo
    {
        public double CurrentTime { get; set; }
        public double TotalTime { get; set; }
        public double SpeedRatio { get; set; }
        public Models.SrtEntry LatestSubtitle { get; set; }
        public bool IsFinished { get; set; }
    }
}

