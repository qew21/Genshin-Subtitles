using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GI_Subtitles.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenCvSharp;
using PaddleOCRSharp;

namespace GI_Test
{
    /// <summary>
    /// Replays real debug captures so OCR and subtitle-candidate changes can be
    /// measured without asking someone to reproduce a particular screen state.
    /// </summary>
    [TestClass]
    [TestCategory("DebugCaptureReplay")]
    [DoNotParallelize]
    public class TestDebugCaptureReplay
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void SavedGameSubtitleCaptures_PreserveSubtitleTextAndMeasureOcr()
        {
            string appDir = Path.GetDirectoryName(typeof(TestDebugCaptureReplay).Assembly.Location);
            var setup = new AppDomainSetup
            {
                ApplicationBase = appDir,
                ConfigurationFile = Path.Combine(appDir, "GI-Test.dll.config"),
                ShadowCopyFiles = "false"
            };

            AppDomain replayDomain = AppDomain.CreateDomain(
                "GI-Test OCR screenshot replay",
                null,
                setup);
            try
            {
                var runner = (DebugCaptureReplayRunner)replayDomain.CreateInstanceAndUnwrap(
                    typeof(TestDebugCaptureReplay).Assembly.FullName,
                    typeof(DebugCaptureReplayRunner).FullName);
                foreach (string line in runner.Replay())
                {
                    TestContext.WriteLine(line);
                }
            }
            catch (Exception exception)
            {
                Assert.Fail("Screenshot replay failed in its isolated .NET Framework domain: " + exception);
            }
            finally
            {
                AppDomain.Unload(replayDomain);
            }
        }

        /// <summary>
        /// MSTest's host loads an older System.Memory from its own directory. Running
        /// inference in a fresh domain makes the test use GI-Test.dll.config and the
        /// same local dependency versions as the application.
        /// </summary>
        public sealed class DebugCaptureReplayRunner : MarshalByRefObject
        {
            private static readonly DebugCapture[] Captures =
            {
                new DebugCapture("genshin-long-dialogue.png"),
                new DebugCapture("genshin-speaker-and-dialogue.png"),
                new DebugCapture("genshin-dialogue.png")
            };

            public string[] Replay()
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string captureDirectory = Path.Combine(appDir, "DebugCaptureSamples");
                string[] paths = Captures
                    .Select(capture => Path.Combine(captureDirectory, capture.FileName))
                    .ToArray();
                foreach (string path in paths)
                {
                    if (!File.Exists(path))
                    {
                        throw new FileNotFoundException("Missing replay capture.", path);
                    }
                }

                var report = new System.Collections.Generic.List<string>();
                using (PaddleOCREngine engine = SettingsWindow.LoadEngine(
                           "CHS",
                           "V6",
                           OCRExecutionProvider.Cpu))
                {
                    // Exclude one-time model initialization from per-frame measurements.
                    engine.DetectText(paths[0]);

                    foreach (DebugCapture capture in Captures)
                    {
                        string path = Path.Combine(captureDirectory, capture.FileName);
                        using (Mat frame = Cv2.ImRead(path, ImreadModes.Color))
                        {
                            if (frame.Empty())
                            {
                                throw new InvalidDataException(
                                    "Could not load replay capture: " + capture.FileName);
                            }

                            engine.DetectTextFromMat(frame);
                            engine.DetectSubtitleTextFromMat(frame);
                            var fullFrameDurations = new double[5];
                            var subtitleDurations = new double[5];
                            var fullDetectionDurations = new double[5];
                            var fullRecognitionDurations = new double[5];
                            var subtitleDetectionDurations = new double[5];
                            var subtitleSelectionDurations = new double[5];
                            var subtitleRecognitionDurations = new double[5];
                            OCRResult fullFrameResult = null;
                            OCRResult subtitleResult = null;
                            for (int round = 0; round < fullFrameDurations.Length; round++)
                            {
                                if (round % 2 == 0)
                                {
                                    fullFrameResult = TimeOcr(
                                        () => engine.DetectTextFromMat(frame),
                                        out fullFrameDurations[round]);
                                    fullDetectionDurations[round] = fullFrameResult.TextDetectionElapsedMilliseconds;
                                    fullRecognitionDurations[round] = fullFrameResult.TextRecognitionElapsedMilliseconds;
                                    subtitleResult = TimeOcr(
                                        () => engine.DetectSubtitleTextFromMat(frame),
                                        out subtitleDurations[round]);
                                    subtitleDetectionDurations[round] = subtitleResult.TextDetectionElapsedMilliseconds;
                                    subtitleSelectionDurations[round] = subtitleResult.SubtitleSelectionElapsedMilliseconds;
                                    subtitleRecognitionDurations[round] = subtitleResult.TextRecognitionElapsedMilliseconds;
                                }
                                else
                                {
                                    subtitleResult = TimeOcr(
                                        () => engine.DetectSubtitleTextFromMat(frame),
                                        out subtitleDurations[round]);
                                    subtitleDetectionDurations[round] = subtitleResult.TextDetectionElapsedMilliseconds;
                                    subtitleSelectionDurations[round] = subtitleResult.SubtitleSelectionElapsedMilliseconds;
                                    subtitleRecognitionDurations[round] = subtitleResult.TextRecognitionElapsedMilliseconds;
                                    fullFrameResult = TimeOcr(
                                        () => engine.DetectTextFromMat(frame),
                                        out fullFrameDurations[round]);
                                    fullDetectionDurations[round] = fullFrameResult.TextDetectionElapsedMilliseconds;
                                    fullRecognitionDurations[round] = fullFrameResult.TextRecognitionElapsedMilliseconds;
                                }
                            }

                            string fullText = fullFrameResult?.Text ?? string.Empty;
                            string subtitleText = subtitleResult?.Text ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(fullText))
                            {
                                throw new InvalidDataException(
                                    "OCR returned no text for replay capture: " + capture.FileName);
                            }
                            if (!fullText.Any(IsRecognizableCharacter))
                            {
                                throw new InvalidDataException(
                                    "OCR returned no recognizable text for game capture: " + capture.FileName);
                            }
                            if (!string.Equals(fullText, subtitleText, StringComparison.Ordinal))
                            {
                                throw new InvalidDataException(
                                    "Subtitle filtering changed recognized game text for " + capture.FileName +
                                    ". Full=" + fullText + "; filtered=" + subtitleText +
                                    "; fullRegions=" + FormatTextBlocks(fullFrameResult) +
                                    "; subtitleRegions=" + FormatTextBlocks(subtitleResult));
                            }

                            report.Add(string.Format(
                                "{0}: frame={1}x{2}; fullOcrAvgMs={3:F1}; fullOcrP95Ms={4:F1}; " +
                                "fullDetectAvgMs={5:F1}; fullRecognizeAvgMs={6:F1}; " +
                                "fullTextLength={7}; detectedBoxes={8}; " +
                                "subtitleOcrAvgMs={9:F1}; subtitleOcrP95Ms={10:F1}; " +
                                "subtitleDetectAvgMs={11:F1}; subtitleSelectAvgMs={12:F2}; " +
                                "subtitleRecognizeAvgMs={13:F1}; subtitleTextLength={14}; " +
                                "recognizedBoxes={15}/{16}",
                                capture.FileName,
                                frame.Width,
                                frame.Height,
                                fullFrameDurations.Average(),
                                Percentile95(fullFrameDurations),
                                fullDetectionDurations.Average(),
                                fullRecognitionDurations.Average(),
                                fullText.Length,
                                fullFrameResult?.DetectedTextRegionCount ?? 0,
                                subtitleDurations.Average(),
                                Percentile95(subtitleDurations),
                                subtitleDetectionDurations.Average(),
                                subtitleSelectionDurations.Average(),
                                subtitleRecognitionDurations.Average(),
                                subtitleText.Length,
                                subtitleResult?.RecognizedTextRegionCount ?? 0,
                                subtitleResult?.DetectedTextRegionCount ?? 0));
                        }
                    }
                }

                return report.ToArray();
            }

            private static bool IsRecognizableCharacter(char value)
            {
                return char.IsLetterOrDigit(value) || (value >= '\u3400' && value <= '\u9fff');
            }

            private static OCRResult TimeOcr(Func<OCRResult> recognize, out double elapsedMs)
            {
                var timer = Stopwatch.StartNew();
                OCRResult result = recognize();
                timer.Stop();
                elapsedMs = timer.Elapsed.TotalMilliseconds;
                return result;
            }

            private static string FormatTextBlocks(OCRResult result)
            {
                return string.Join(" | ", (result?.TextBlocks ?? new System.Collections.Generic.List<TextBlock>())
                    .Select(block => string.Format(
                        "{0}@[{1:F0},{2:F0}-{3:F0},{4:F0}]",
                        block.Text,
                        block.BoxPoints.Min(point => point.X),
                        block.BoxPoints.Min(point => point.Y),
                        block.BoxPoints.Max(point => point.X),
                        block.BoxPoints.Max(point => point.Y))));
            }

            private static double Percentile95(double[] values)
            {
                double[] ordered = values.OrderBy(value => value).ToArray();
                int index = (int)Math.Ceiling(ordered.Length * 0.95) - 1;
                return ordered[Math.Max(0, Math.Min(index, ordered.Length - 1))];
            }

            private sealed class DebugCapture
            {
                public DebugCapture(string fileName)
                {
                    FileName = fileName;
                }

                public string FileName { get; }
            }
        }
    }
}
