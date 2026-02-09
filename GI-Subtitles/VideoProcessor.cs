using OpenCvSharp;
using PaddleOCRSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace GI_Subtitles
{
    /// <summary>
    /// Progress information class
    /// </summary>
    public class ProgressInfo
    {
        public double CurrentTime { get; set; }
        public double TotalTime { get; set; }
        public double SpeedRatio { get; set; }
        public SrtEntry LatestSubtitle { get; set; }
        public bool IsFinished { get; set; }
    }

    internal class VideoProcessor : IDisposable
    {
        private readonly string _videoPath;
        private readonly OpenCvSharp.Rect _ocrRegion;
        private readonly int _detectionInterval;
        private readonly int _minDurationMs;
        private readonly bool _limitToFirstMinute;

        private const double SimilarityThreshold = 0.995;

        // Subtitles are usually bright. If your subtitles are yellow or white, a threshold of 180–200 usually filters out most dark backgrounds.
        private const double SubtitleBrightnessThreshold = 220;

        public VideoProcessor(
            string videoPath,
            System.Drawing.Rectangle ocrRegion,
            int detectionFps = 5, // 建议保持 10 FPS 以上
            int minDurationMs = 200,
            bool limitToFirstMinute = false,
            bool debugMode = false)
        {
            _videoPath = videoPath ?? throw new ArgumentNullException(nameof(videoPath));
            _ocrRegion = new OpenCvSharp.Rect(ocrRegion.X, ocrRegion.Y, ocrRegion.Width, ocrRegion.Height);
            _minDurationMs = minDurationMs;
            _limitToFirstMinute = limitToFirstMinute;

            // Sampling interval: higher FPS reduces the chance of missing short subtitles. Recommended to sample every 3–4 frames.
            // For example, for a 30fps video, if detectFps=10, then step=3.
            _detectionInterval = Math.Max(1, 30 / detectionFps);
        }

        // Backward-compatible constructor
        public VideoProcessor(string videoPath, System.Drawing.Rectangle ocrRegion, double intervalSeconds, bool limitToFirstMinute = false)
            : this(videoPath, ocrRegion, (int)(1.0 / intervalSeconds), 100, limitToFirstMinute, false) { }

        public void GenerateSrt(PaddleOCREngine engine, string outputSrtPath, IProgress<ProgressInfo> progress = null)
        {
            if (!File.Exists(_videoPath)) throw new FileNotFoundException("Video file not found.", _videoPath);

            using var capture = new VideoCapture(_videoPath);
            if (!capture.IsOpened()) throw new InvalidOperationException("Cannot open video file.");

            var videoFps = capture.Fps;
            var totalFrames = (long)capture.Get(VideoCaptureProperties.FrameCount);
            var durationSec = totalFrames / videoFps;

            // 重新计算步长，确保不会跳过太多
            int step = (int)Math.Max(1, videoFps / (videoFps / _detectionInterval));
            if (step < 1) step = 1;

            var maxDuration = _limitToFirstMinute ? Math.Min(durationSec, 60.0) : durationSec;
            var maxFrameToProcess = _limitToFirstMinute ? (long)(60 * videoFps) : totalFrames;

            Console.WriteLine($"开始处理: {_videoPath}");
            Console.WriteLine($"视频FPS: {videoFps:F2}, 步长: {step}, 阈值: {SimilarityThreshold:P0}");

            var srtEntries = new List<SrtEntry>();

            // State machine variables
            Mat lastProcessed = null;      // Previous processed frame (binarized)
            Mat pendingStableFrame = null; // Original color frame (for OCR)

            double stableStartTime = -1;   // Time when stability started
            double lastTime = 0;           // Time of previous frame
            int stableFrameCount = 0;      // Counter for consecutive stable frames

            // Pre-allocate memory
            Mat currentFrame = new Mat();
            Mat roiFrame = new Mat();
            Mat currentProcessed = new Mat();
            Mat diffFrame = new Mat();

            int processedCount = 0;
            int ocrCount = 0;

            var stopWatch = Stopwatch.StartNew();
            var startTime = stopWatch.Elapsed.TotalSeconds;

            try
            {
                // Ensure the ROI is valid
                capture.Read(currentFrame);
                var validRoi = new OpenCvSharp.Rect(
                    Math.Max(0, _ocrRegion.X),
                    Math.Max(0, _ocrRegion.Y),
                    Math.Min(_ocrRegion.Width, currentFrame.Width - _ocrRegion.X),
                    Math.Min(_ocrRegion.Height, currentFrame.Height - _ocrRegion.Y)
                );
                // Reset back to the beginning
                capture.Set(VideoCaptureProperties.PosFrames, 0);

                for (long frameIdx = 0; frameIdx < maxFrameToProcess; frameIdx += step)
                {
                    // 1. Read the frame
                    if (frameIdx > 0)
                    {
                        // Skip step-1 frames
                        for (int i = 0; i < step - 1; i++)
                        {
                            capture.Grab(); // Grab 比 Read 快得多，因为它只解压不解码像素
                        }
                    }
                    if (!capture.Read(currentFrame) || currentFrame.Empty()) break;
                    double currentTime = capture.PosMsec / 1000.0;
                    if (currentTime > maxDuration) break;

                    // 2. Crop ROI
                    roiFrame = new Mat(currentFrame, validRoi);

                    // 3. Image preprocessing (key optimization)
                    // Convert the image into a binary form that is easy to compare, filtering out background interference
                    PreProcessFrame(roiFrame, currentProcessed);

                    // 4. Difference detection
                    bool isStable = false;

                    if (lastProcessed != null)
                    {
                        // Compute difference: compare only white pixels in the binarized image (i.e., subtitle region)
                        Cv2.Absdiff(currentProcessed, lastProcessed, diffFrame);

                        // Count differing pixels
                        int nonZero = Cv2.CountNonZero(diffFrame);
                        double changeRatio = (double)nonZero / (validRoi.Width * validRoi.Height);

                        // If the change ratio is small, subtitles are likely unchanged (or a solid black background remains unchanged)
                        if (changeRatio < (1.0 - SimilarityThreshold))
                        {
                            isStable = true;
                        }
                    }
                    else
                    {
                        // First frame: treat as unstable and initialize
                        isStable = false;
                    }

                    // 5. State machine logic
                    if (isStable)
                    {
                        // Detected a still frame (subtitles may be showing)
                        if (stableStartTime < 0)
                        {
                            // Stability just started
                            stableStartTime = lastTime;
                            stableFrameCount = 0;
                            // Backup this frame for OCR (keep color image for better OCR accuracy)
                            pendingStableFrame?.Dispose();
                            pendingStableFrame = roiFrame.Clone();
                        }
                        stableFrameCount++;
                    }
                    else
                    {
                        // Frame changed (subtitle appears, disappears, or switches)

                        // Check whether there was a stable subtitle segment before
                        if (stableStartTime >= 0)
                        {
                            double durationMs = (lastTime - stableStartTime) * 1000;
                            // Only consider it if the stability lasted long enough and the frame contained content (exclude pure-black to pure-black situations)
                            // Simple heuristic: if pendingStableFrame is all black or too dark, it is likely not subtitles
                            if (durationMs >= _minDurationMs && pendingStableFrame != null)
                            {
                                // === Trigger OCR ===
                                string text = PerformOcr(engine, pendingStableFrame);

                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    var newEntry = AddOrMergeSubtitle(srtEntries, text, stableStartTime, lastTime);
                                    ocrCount++;
                                    Console.Write("+"); // Recognition succeeded

                                    // Report progress (including latest subtitle)
                                    if (progress != null)
                                    {
                                        var elapsed = stopWatch.Elapsed.TotalSeconds;
                                        var speedRatio = elapsed > 0 ? currentTime / elapsed : 1.0;
                                        progress.Report(new ProgressInfo
                                        {
                                            CurrentTime = currentTime,
                                            TotalTime = maxDuration,
                                            SpeedRatio = speedRatio,
                                            LatestSubtitle = newEntry,
                                            IsFinished = false
                                        });
                                    }
                                }
                                else
                                {
                                    Console.Write("."); // OCR result empty (may be a misdetected stable segment)
                                }
                        }

                        // Reset state
                            stableStartTime = -1;
                            stableFrameCount = 0;
                            pendingStableFrame?.Dispose();
                            pendingStableFrame = null;
                        }
                    }

                    // Update previous frame
                    if (lastProcessed == null) lastProcessed = new Mat();
                    currentProcessed.CopyTo(lastProcessed);
                    lastTime = currentTime;
                    processedCount++;

                    // Periodically report progress (after processing a certain number of frames or time interval)
                    if (progress != null && processedCount % 10 == 0)
                    {
                        var elapsed = stopWatch.Elapsed.TotalSeconds;
                        var speedRatio = elapsed > 0 ? currentTime / elapsed : 1.0;
                        progress.Report(new ProgressInfo
                        {
                            CurrentTime = currentTime,
                            TotalTime = maxDuration,
                            SpeedRatio = speedRatio,
                            LatestSubtitle = null,
                            IsFinished = false
                        });
                    }
                }

                // Finalization: handle the last subtitle segment
                if (stableStartTime >= 0 && pendingStableFrame != null)
                {
                    double durationMs = (lastTime - stableStartTime) * 1000;
                    if (durationMs >= _minDurationMs)
                    {
                        string text = PerformOcr(engine, pendingStableFrame);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var newEntry = AddOrMergeSubtitle(srtEntries, text, stableStartTime, lastTime);
                            ocrCount++;

                            // Report progress
                            if (progress != null)
                            {
                                var elapsed = stopWatch.Elapsed.TotalSeconds;
                                var speedRatio = elapsed > 0 ? lastTime / elapsed : 1.0;
                                progress.Report(new ProgressInfo
                                {
                                    CurrentTime = lastTime,
                                    TotalTime = maxDuration,
                                    SpeedRatio = speedRatio,
                                    LatestSubtitle = newEntry,
                                    IsFinished = false
                                });
                            }
                        }
                    }
                }
            }
            finally
            {
                lastProcessed?.Dispose();
                pendingStableFrame?.Dispose();
                currentFrame?.Dispose();
                roiFrame?.Dispose();
                currentProcessed?.Dispose();
                diffFrame?.Dispose();
            }

            stopWatch.Stop();
            Console.WriteLine($"\nProcessing completed. Time: {stopWatch.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine($"Scanned frames: {processedCount}, OCR count: {ocrCount}, Subtitle count: {srtEntries.Count}");

            WriteSrtFile(outputSrtPath, srtEntries);

            // Report completion
            if (progress != null)
            {
                var elapsed = stopWatch.Elapsed.TotalSeconds;
                var speedRatio = elapsed > 0 ? maxDuration / elapsed : 1.0;
                progress.Report(new ProgressInfo
                {
                    CurrentTime = maxDuration,
                    TotalTime = maxDuration,
                    SpeedRatio = speedRatio,
                    LatestSubtitle = null,
                    IsFinished = true
                });
            }
        }

        /// <summary>
        /// Preprocess a frame: convert it to a binary image containing only subtitle outlines, masking background interference.
        /// </summary>
        private void PreProcessFrame(Mat src, Mat dst)
        {
            // 1. Convert to grayscale
            if (src.Channels() == 3)
                Cv2.CvtColor(src, dst, ColorConversionCodes.BGR2GRAY);
            else
                src.CopyTo(dst);

            // 2. Binarization (key step)
            // Assume subtitles are white and the background is darker.
            // Set threshold to 180: only pixels with brightness above 180 (subtitles) remain white, others become black.
            // This way, as long as the background brightness does not exceed 180, it becomes black and will not create differences.
            Cv2.Threshold(dst, dst, SubtitleBrightnessThreshold, 255, ThresholdTypes.Binary);

        }

        private string PerformOcr(PaddleOCREngine engine, Mat mat)
        {
            try
            {
                // OCR works best on the original image (color or grayscale); avoid using the binarized image because the OCR engine will handle preprocessing itself.
                var result = engine.DetectTextFromMat(mat);
                return result?.Text?.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private SrtEntry AddOrMergeSubtitle(List<SrtEntry> entries, string text, double start, double end)
        {
            if (entries.Count > 0)
            {
                var last = entries[entries.Count - 1];

                // 1. If text is exactly the same, merge
                if (last.Text == text)
                {
                    last.EndTime = TimeSpan.FromSeconds(end);
                    return last;
                }

                // 2. If text is highly similar and times overlap or are adjacent, merge
                // Calculate time gap
                double gap = start - last.EndTime.TotalSeconds;
                int lastLength = last.Text.Length;
                int currentLength = text.Length;
                // Relax gap threshold to 2.0 seconds, allowing merging of slightly more distant similar texts
                if (gap < 2.0 && CalculateLevenshteinSimilarity(last.Text, text.Substring(0, Math.Min(lastLength, currentLength))) > 0.8)
                {
                    // For similar texts, keep the longer one
                    if (currentLength > lastLength) last.Text = text;
                    last.EndTime = TimeSpan.FromSeconds(end);
                    return last;
                }
            }

            var newEntry = new SrtEntry
            {
                Index = entries.Count + 1,
                StartTime = TimeSpan.FromSeconds(start),
                EndTime = TimeSpan.FromSeconds(end),
                Text = text
            };
            entries.Add(newEntry);
            return newEntry;
        }

        private double CalculateLevenshteinSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0;
            int len1 = s1.Length;
            int len2 = s2.Length;
            var d = new int[len1 + 1, len2 + 1];
            for (int i = 0; i <= len1; i++) d[i, 0] = i;
            for (int j = 0; j <= len2; j++) d[0, j] = j;
            for (int i = 1; i <= len1; i++)
            {
                for (int j = 1; j <= len2; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return 1.0 - (double)d[len1, len2] / Math.Max(len1, len2);
        }

        private void WriteSrtFile(string path, List<SrtEntry> entries)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                writer.WriteLine(i + 1);
                writer.WriteLine($"{entry.StartTime:hh\\:mm\\:ss\\,fff} --> {entry.EndTime:hh\\:mm\\:ss\\,fff}");
                writer.WriteLine(entry.Text);
                writer.WriteLine();
            }
        }

        public void Dispose()
        {
        }
    }

    public class SrtEntry
    {
        public int Index { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Text { get; set; }
    }

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

    /// <summary>
    /// Video processing tool class
    /// </summary>
    public static class VideoProcessorHelper
    {
        /// <summary>
        /// Automatically process demo video for performance evaluation
        /// </summary>
        public static void ProcessDemoVideo(string videoPath, string regionJsonPath, PaddleOCREngine engine, Action onComplete = null)
        {
            try
            {
                Console.WriteLine("=== Start processing demo video ===");
                Console.WriteLine($"Video path: {videoPath}");
                Console.WriteLine($"Region configuration: {regionJsonPath}");

                // Read region information
                string json = File.ReadAllText(regionJsonPath, Encoding.UTF8);
                var regionInfo = JsonConvert.DeserializeObject<RegionInfo>(json);

                if (regionInfo == null)
                {
                    Console.WriteLine("Error: Unable to parse region configuration file");
                    return;
                }

                Console.WriteLine($"Region: X={regionInfo.X}, Y={regionInfo.Y}, W={regionInfo.Width}, H={regionInfo.Height}");
                Console.WriteLine($"Video resolution: {regionInfo.VideoWidth}x{regionInfo.VideoHeight}");

                // Create region rectangle
                var ocrRegion = new System.Drawing.Rectangle(
                    regionInfo.X,
                    regionInfo.Y,
                    regionInfo.Width,
                    regionInfo.Height
                );

                // Read parameters from configuration
                int detectionFps = Config.Get<int>("SubtitleDetectionFps", 5);
                int minDurationMs = Config.Get<int>("SubtitleMinDurationMs", 200);
                bool debugMode = Config.Get<bool>("SubtitleDebugMode", false); // Debug mode: only execute the first stage

                Console.WriteLine($"Detection frequency: {detectionFps} FPS");
                Console.WriteLine($"Minimum duration: {minDurationMs} ms");
                if (debugMode)
                {
                    Console.WriteLine($"Debug mode: enabled (only execute the first stage fast scan)");
                }

                // Generate output file path
                string videoDir = Path.GetDirectoryName(videoPath);
                string videoName = Path.GetFileNameWithoutExtension(videoPath);
                string srtPath = Path.Combine(videoDir, $"{videoName}.srt");

                // Get video information (for performance statistics)
                double videoDurationSeconds = 0;
                try
                {
                    using (var capture = new OpenCvSharp.VideoCapture(videoPath))
                    {
                        if (capture.IsOpened())
                        {
                            var fps = capture.Fps;
                            var totalFrames = (long)capture.Get(OpenCvSharp.VideoCaptureProperties.FrameCount);
                            videoDurationSeconds = totalFrames / fps;
                            Console.WriteLine($"Video duration: {videoDurationSeconds:F2} seconds ({videoDurationSeconds / 60:F2} minutes)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Unable to get video information: {ex.Message}");
                }

                // Create processor
                var processor = new VideoProcessor(
                    videoPath,
                    ocrRegion,
                    detectionFps: detectionFps,
                    minDurationMs: minDurationMs,
                    limitToFirstMinute: false,
                    debugMode: debugMode
                );

                // Start timing
                var stopwatch = Stopwatch.StartNew();
                Console.WriteLine("\nStart extracting subtitles...");

                // Process video
                processor.GenerateSrt(engine, srtPath);

                // Stop timing
                stopwatch.Stop();

                // Output results
                Console.WriteLine("\n=== Processing completed ===");
                Console.WriteLine($"Output file: {srtPath}");
                Console.WriteLine($"Total time: {stopwatch.Elapsed.TotalSeconds:F2} seconds ({stopwatch.ElapsedMilliseconds} milliseconds)");

                if (videoDurationSeconds > 0)
                {
                    double speedRatio = videoDurationSeconds / stopwatch.Elapsed.TotalSeconds;
                    Console.WriteLine($"Processing speed: {speedRatio:F2}x (real-time speed of {speedRatio:F2} times)");
                    Console.WriteLine($"Average speed: {stopwatch.Elapsed.TotalSeconds / (videoDurationSeconds / 60):F2} seconds/minute video");
                }
                Console.WriteLine("==================\n");

                // Check if the output file exists
                if (File.Exists(srtPath))
                {
                    var fileInfo = new FileInfo(srtPath);
                    Console.WriteLine($"SRT file size: {fileInfo.Length} bytes");

                    // Count subtitle entries (SRT format: index, time range, text, empty line)
                    var lines = File.ReadAllLines(srtPath);
                    int entryCount = 0;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        // Check if it is a index line (pure number)
                        if (int.TryParse(lines[i].Trim(), out int index) && index > 0)
                        {
                            entryCount++;
                        }
                    }
                    Console.WriteLine($"Subtitle entries: {entryCount}");
                }

                // Call the completion callback
                onComplete?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                Logger.Log.Error(ex);
                throw; // Rethrow the exception, let the caller handle it
            }
        }
    }
}