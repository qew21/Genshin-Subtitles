using OpenCvSharp;
using PaddleOCRSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace GI_Subtitles
{
    internal class VideoProcessor
    {

        private readonly string _videoPath;
        private readonly Rectangle _ocrRegion; // 指定 OCR 区域 (x, y, width, height)
        private readonly double _intervalSeconds; // 截帧间隔（秒）
        private readonly bool _limitToFirstMinute; // 是否只处理前1分钟

        public VideoProcessor(
            string videoPath,
            Rectangle ocrRegion,
            double intervalSeconds = 0.5,
            bool limitToFirstMinute = false)
        {
            _videoPath = videoPath ?? throw new ArgumentNullException(nameof(videoPath));
            _ocrRegion = ocrRegion;
            _intervalSeconds = intervalSeconds > 0 ? intervalSeconds : throw new ArgumentException("Interval must be positive.", nameof(intervalSeconds));
            _limitToFirstMinute = limitToFirstMinute;
        }

        public void GenerateSrt(PaddleOCREngine engine, string outputSrtPath)
        {
            if (!File.Exists(_videoPath))
                throw new FileNotFoundException("Video file not found.", _videoPath);

            using var capture = new VideoCapture(_videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException("Cannot open video file.");

            var fps = capture.Fps;
            var totalFrames = (long)capture.Get(VideoCaptureProperties.FrameCount);
            var totalTimeSeconds = totalFrames / fps;

            var maxDuration = _limitToFirstMinute ? Math.Min(totalTimeSeconds, 60.0) : totalTimeSeconds;
            Logger.Log.Debug($"Total frames: {totalFrames}, FPS: {fps}, Max duration: {maxDuration} seconds");
            var frameInterval = (int)(fps * _intervalSeconds); // 每隔多少帧取一帧

            var srtEntries = new List<SrtEntry>();
            int entryIndex = 1;
            int processedCount = 0;

            for (long frameNumber = 0; frameNumber < totalFrames; frameNumber += frameInterval)
            {
                var currentTime = frameNumber / fps;
                if (currentTime > maxDuration) break;

                capture.Set(VideoCaptureProperties.PosFrames, frameNumber);
                using var mat = new Mat();
                if (!capture.Read(mat) || mat.Empty())
                    break;

                // 裁剪指定区域
                if (_ocrRegion.X + _ocrRegion.Width > mat.Cols ||
                    _ocrRegion.Y + _ocrRegion.Height > mat.Rows)
                {
                    throw new InvalidOperationException("OCR region exceeds video frame size.");
                }
                var regionCv = new OpenCvSharp.Rect(
                    _ocrRegion.X,
                    _ocrRegion.Y,
                    _ocrRegion.Width,
                    _ocrRegion.Height
                );
                var roi = new Mat(mat, regionCv);
                var sw = Stopwatch.StartNew();
                OCRResult result = engine.DetectTextFromMat(roi);
                sw.Stop();

                string text = result?.Text?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    var start = TimeSpan.FromSeconds(currentTime);
                    var end = TimeSpan.FromSeconds(Math.Min(currentTime + _intervalSeconds, maxDuration));
                    Logger.Log.Debug($"OCR result: {text} (time={currentTime}, duration={sw.ElapsedMilliseconds}ms)");

                    srtEntries.Add(new SrtEntry
                    {
                        Index = entryIndex++,
                        StartTime = start,
                        EndTime = end,
                        Text = text
                    });
                }

                processedCount++;
                
                // 每处理一张图暂停0.3秒避免CPU过热
                System.Threading.Thread.Sleep(300);
            }

            Logger.Log.Debug($"Processed {processedCount} frames, generated {srtEntries.Count} subtitle entries.");

            // 写入 SRT 文件
            WriteSrtFile(outputSrtPath, srtEntries);
        }

        private void WriteSrtFile(string path, List<SrtEntry> entries)
        {
            using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
            foreach (var entry in entries)
            {
                writer.WriteLine(entry.Index);
                writer.WriteLine($"{TimeSpanToString(entry.StartTime)} --> {TimeSpanToString(entry.EndTime)}");
                writer.WriteLine(entry.Text);
                writer.WriteLine(); // 空行分隔
            }
        }

        private static string TimeSpanToString(TimeSpan ts)
        {
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
        }
    }

    public class SrtEntry
    {
        public int Index { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Text { get; set; }
    }

}
