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
    /// 进度信息类
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

        // 字幕通常是高亮的。如果你的字幕是黄色的或白色的，180-200的阈值通常能过滤掉大部分深色背景
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

            // 采样间隔：FPS越高，漏掉短字幕的概率越低。建议每3-4帧采一次。
            // 例如30fps视频，detectFps=10，则step=3。
            _detectionInterval = Math.Max(1, 30 / detectionFps);
        }

        // 向后兼容构造函数
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

            // 状态机变量
            Mat lastProcessed = null;      // 上一帧处理后的图像（二值化后）
            Mat pendingStableFrame = null; // 原始彩图（用于OCR）

            double stableStartTime = -1;   // 稳定开始时间
            double lastTime = 0;           // 上一帧时间
            int stableFrameCount = 0;      // 连续稳定帧数计数

            // 预分配内存
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
                // 确保 ROI 有效
                capture.Read(currentFrame);
                var validRoi = new OpenCvSharp.Rect(
                    Math.Max(0, _ocrRegion.X),
                    Math.Max(0, _ocrRegion.Y),
                    Math.Min(_ocrRegion.Width, currentFrame.Width - _ocrRegion.X),
                    Math.Min(_ocrRegion.Height, currentFrame.Height - _ocrRegion.Y)
                );
                // 重置回去
                capture.Set(VideoCaptureProperties.PosFrames, 0);

                for (long frameIdx = 0; frameIdx < maxFrameToProcess; frameIdx += step)
                {
                    // 1. 读取
                    if (frameIdx > 0)
                    {
                        // 跳过 step-1 帧
                        for (int i = 0; i < step - 1; i++)
                        {
                            capture.Grab(); // Grab 比 Read 快得多，因为它只解压不解码像素
                        }
                    }
                    if (!capture.Read(currentFrame) || currentFrame.Empty()) break;
                    double currentTime = capture.PosMsec / 1000.0;
                    if (currentTime > maxDuration) break;

                    // 2. 裁剪 ROI
                    roiFrame = new Mat(currentFrame, validRoi);

                    // 3. 图像预处理 (核心优化点)
                    // 将图像转换为"易于比较"的二值形态，过滤背景干扰
                    PreProcessFrame(roiFrame, currentProcessed);

                    // 4. 差异检测
                    bool isStable = false;

                    if (lastProcessed != null)
                    {
                        // 计算差异：只比较二值化后的白色像素（即字幕部分）
                        Cv2.Absdiff(currentProcessed, lastProcessed, diffFrame);

                        // 统计差异像素
                        int nonZero = Cv2.CountNonZero(diffFrame);
                        double changeRatio = (double)nonZero / (validRoi.Width * validRoi.Height);

                        // 如果变化率很小，说明字幕没变（或者背景全黑没变）
                        if (changeRatio < (1.0 - SimilarityThreshold))
                        {
                            isStable = true;
                        }
                    }
                    else
                    {
                        // 第一帧，默认不稳定，先初始化
                        isStable = false;
                    }

                    // 5. 状态机逻辑
                    if (isStable)
                    {
                        // 检测到画面静止（字幕可能正在显示）
                        if (stableStartTime < 0)
                        {
                            // 刚开始稳定
                            stableStartTime = lastTime;
                            stableFrameCount = 0;
                            // 备份这一帧用于OCR（存彩图，OCR识别率高）
                            pendingStableFrame?.Dispose();
                            pendingStableFrame = roiFrame.Clone();
                        }
                        stableFrameCount++;
                    }
                    else
                    {
                        // 画面发生变化（字幕出现、消失或切换）

                        // 检查之前是否有一段稳定的字幕
                        if (stableStartTime >= 0)
                        {
                            double durationMs = (lastTime - stableStartTime) * 1000;
                            // 只有稳定持续了一定时间，且这期间画面是有内容的（排除纯黑转纯黑的情况）
                            // 简单的判断：如果 pendingStableFrame 全黑或太暗，可能不是字幕
                            if (durationMs >= _minDurationMs && pendingStableFrame != null)
                            {
                                // === 触发 OCR ===
                                string text = PerformOcr(engine, pendingStableFrame);

                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    var newEntry = AddOrMergeSubtitle(srtEntries, text, stableStartTime, lastTime);
                                    ocrCount++;
                                    Console.Write("+"); // 成功识别

                                    // 报告进度（包含最新字幕）
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
                                    Console.Write("."); // 识别为空（可能是误判的稳定期）
                                }
                            }

                            // 重置状态
                            stableStartTime = -1;
                            stableFrameCount = 0;
                            pendingStableFrame?.Dispose();
                            pendingStableFrame = null;
                        }
                    }

                    // 更新上一帧
                    if (lastProcessed == null) lastProcessed = new Mat();
                    currentProcessed.CopyTo(lastProcessed);
                    lastTime = currentTime;
                    processedCount++;

                    // 定期报告进度（每处理一定帧数或时间间隔）
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

                // 收尾：处理最后一段
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

                            // 报告进度
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
            Console.WriteLine($"\n处理完成。耗时: {stopWatch.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine($"扫描帧数: {processedCount}, OCR次数: {ocrCount}, 字幕条数: {srtEntries.Count}");

            WriteSrtFile(outputSrtPath, srtEntries);

            // 报告完成
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
        /// 预处理：将图像转换为只包含字幕轮廓的二值图，屏蔽背景干扰
        /// </summary>
        private void PreProcessFrame(Mat src, Mat dst)
        {
            // 1. 转灰度
            if (src.Channels() == 3)
                Cv2.CvtColor(src, dst, ColorConversionCodes.BGR2GRAY);
            else
                src.CopyTo(dst);

            // 2. 二值化 (关键步骤)
            // 假设字幕是白色的，背景较暗。
            // 阈值设为 180，只有亮度大于180的像素（字幕）会被保留为白色，其余变黑。
            // 这样背景怎么动（只要亮度不超过180）都变成黑色，不会产生差异。
            Cv2.Threshold(dst, dst, SubtitleBrightnessThreshold, 255, ThresholdTypes.Binary);

        }

        private string PerformOcr(PaddleOCREngine engine, Mat mat)
        {
            try
            {
                // OCR 最好还是用原图（彩图或灰度），不要用二值化后的图，因为OCR引擎自己会处理
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

                // 1. 文本完全相同，合并
                if (last.Text == text)
                {
                    last.EndTime = TimeSpan.FromSeconds(end);
                    return last;
                }

                // 2. 文本相似度高，且时间重叠或紧邻，合并
                // 计算时间间隔
                double gap = start - last.EndTime.TotalSeconds;
                int lastLength = last.Text.Length;
                int currentLength = text.Length;
                // 放宽gap阈值到2.0秒，允许合并间隔稍长的相似文本
                if (gap < 2.0 && CalculateLevenshteinSimilarity(last.Text, text.Substring(0, Math.Min(lastLength, currentLength))) > 0.8)
                {
                    // 相似合并，取较长的一个
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
    /// 选区信息类（用于JSON序列化/反序列化）
    /// </summary>
    public class RegionInfo
    {
        public string VideoPath { get; set; }
        public string TimeCode { get; set; } // 格式: HH:MM:SS 或 MM:SS
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int VideoWidth { get; set; }
        public int VideoHeight { get; set; }
    }

    /// <summary>
    /// 视频处理工具类
    /// </summary>
    public static class VideoProcessorHelper
    {
        /// <summary>
        /// 自动处理demo视频，用于性能评估
        /// </summary>
        public static void ProcessDemoVideo(string videoPath, string regionJsonPath, PaddleOCREngine engine, Action onComplete = null)
        {
            try
            {
                Console.WriteLine("=== 开始处理Demo视频 ===");
                Console.WriteLine($"视频路径: {videoPath}");
                Console.WriteLine($"选区配置: {regionJsonPath}");

                // 读取选区信息
                string json = File.ReadAllText(regionJsonPath, Encoding.UTF8);
                var regionInfo = JsonConvert.DeserializeObject<RegionInfo>(json);

                if (regionInfo == null)
                {
                    Console.WriteLine("错误: 无法解析选区配置文件");
                    return;
                }

                Console.WriteLine($"选区: X={regionInfo.X}, Y={regionInfo.Y}, W={regionInfo.Width}, H={regionInfo.Height}");
                Console.WriteLine($"视频分辨率: {regionInfo.VideoWidth}x{regionInfo.VideoHeight}");

                // 创建选区矩形
                var ocrRegion = new System.Drawing.Rectangle(
                    regionInfo.X,
                    regionInfo.Y,
                    regionInfo.Width,
                    regionInfo.Height
                );

                // 从配置读取参数
                int detectionFps = Config.Get<int>("SubtitleDetectionFps", 5);
                int minDurationMs = Config.Get<int>("SubtitleMinDurationMs", 200);
                bool debugMode = Config.Get<bool>("SubtitleDebugMode", false); // 调试模式：只执行第一阶段

                Console.WriteLine($"检测频率: {detectionFps} FPS");
                Console.WriteLine($"最短持续时间: {minDurationMs} ms");
                if (debugMode)
                {
                    Console.WriteLine($"调试模式: 开启（只执行第一阶段快速扫描）");
                }

                // 生成输出文件路径
                string videoDir = Path.GetDirectoryName(videoPath);
                string videoName = Path.GetFileNameWithoutExtension(videoPath);
                string srtPath = Path.Combine(videoDir, $"{videoName}.srt");

                // 获取视频信息（用于性能统计）
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
                            Console.WriteLine($"视频时长: {videoDurationSeconds:F2} 秒 ({videoDurationSeconds / 60:F2} 分钟)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"警告: 无法获取视频信息: {ex.Message}");
                }

                // 创建处理器
                var processor = new VideoProcessor(
                    videoPath,
                    ocrRegion,
                    detectionFps: detectionFps,
                    minDurationMs: minDurationMs,
                    limitToFirstMinute: false,
                    debugMode: debugMode
                );

                // 开始计时
                var stopwatch = Stopwatch.StartNew();
                Console.WriteLine("\n开始提取字幕...");

                // 处理视频
                processor.GenerateSrt(engine, srtPath);

                // 停止计时
                stopwatch.Stop();

                // 输出结果
                Console.WriteLine("\n=== 处理完成 ===");
                Console.WriteLine($"输出文件: {srtPath}");
                Console.WriteLine($"总耗时: {stopwatch.Elapsed.TotalSeconds:F2} 秒 ({stopwatch.ElapsedMilliseconds} 毫秒)");

                if (videoDurationSeconds > 0)
                {
                    double speedRatio = videoDurationSeconds / stopwatch.Elapsed.TotalSeconds;
                    Console.WriteLine($"处理速度: {speedRatio:F2}x (实时速度的 {speedRatio:F2} 倍)");
                    Console.WriteLine($"平均速度: {stopwatch.Elapsed.TotalSeconds / (videoDurationSeconds / 60):F2} 秒/分钟视频");
                }
                Console.WriteLine("==================\n");

                // 检查输出文件是否存在
                if (File.Exists(srtPath))
                {
                    var fileInfo = new FileInfo(srtPath);
                    Console.WriteLine($"SRT文件大小: {fileInfo.Length} 字节");

                    // 统计字幕条目数（SRT格式：序号、时间轴、文本、空行）
                    var lines = File.ReadAllLines(srtPath);
                    int entryCount = 0;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        // 检查是否是序号行（纯数字）
                        if (int.TryParse(lines[i].Trim(), out int index) && index > 0)
                        {
                            entryCount++;
                        }
                    }
                    Console.WriteLine($"字幕条目数: {entryCount}");
                }

                // 调用完成回调
                onComplete?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n错误: {ex.Message}");
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                Logger.Log.Error(ex);
                throw; // 重新抛出异常，让调用者处理
            }
        }
    }
}