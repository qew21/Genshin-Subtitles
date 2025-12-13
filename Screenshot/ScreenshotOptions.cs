using System.Windows.Media;
using System.IO;
using System.Text;
using System;

namespace Screenshot
{


    public static class DebugLogger
    {
        public static void Log(string message)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                string filePath = Path.Combine(folder, "screenshot_log.txt");
                string logLine = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";

                File.AppendAllText(filePath, logLine, Encoding.UTF8);
            }
            catch { /* 忽略日志错误，防止崩溃 */ }
        }
    }
    public class ScreenshotOptions
    {
        public ScreenshotOptions()
        {
            BackgroundOpacity = 0.5;
            SelectionRectangleBorderBrush = Brushes.Red;
        }

        /// <summary>
        /// Background opacity when selecting region to capture.
        /// </summary>
        public double BackgroundOpacity { get; set; }

        /// <summary>
        /// Brush used to draw border of selection rectangle.
        /// </summary>
        public Brush SelectionRectangleBorderBrush { get; set; }
    }
}