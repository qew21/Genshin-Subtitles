using GI_Subtitles.Services.OCR;
using GI_Subtitles.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using OpenCvSharp;
using PaddleOCRSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;

namespace GI_Test
{
    [TestClass]
    public class SubtitleRecognitionPipelineTests
    {
        [TestMethod]
        public void EpochTrackerLocksAcceptedSubtitleUntilVisualChange()
        {
            using var tracker = new SubtitleEpochTracker();
            using var first = CreateSubtitleFrame("first");
            SubtitleFrameBatch firstBatch = FeedUntilBatch(tracker, first);
            Assert.IsNotNull(firstBatch);
            long firstGeneration = firstBatch.Generation;
            firstBatch.Dispose();
            Assert.IsTrue(tracker.Complete(firstGeneration, accepted: true));

            for (int i = 0; i < 8; i++)
            {
                using SubtitleVisualAnalysis analysis = SubtitleVisualAnalyzer.Analyze(first);
                Assert.IsNull(tracker.Process(first, analysis, DialogueUiPresence.Present));
            }

            using var second = CreateSubtitleFrame("second line");
            SubtitleFrameBatch secondBatch = FeedUntilBatch(tracker, second);
            Assert.IsNotNull(secondBatch);
            Assert.IsTrue(secondBatch.Generation > firstGeneration);
            secondBatch.Dispose();
        }

        [TestMethod]
        public void DialogueUiStateUsesHysteresis()
        {
            var state = new DialogueUiStateMachine();
            Assert.AreEqual(DialogueUiPresence.Unknown, state.Update(true));
            Assert.AreEqual(DialogueUiPresence.Present, state.Update(true));
            Assert.AreEqual(DialogueUiPresence.Present, state.Update(false));
            Assert.AreEqual(DialogueUiPresence.Present, state.Update(false));
            Assert.AreEqual(DialogueUiPresence.Present, state.Update(false));
            Assert.AreEqual(DialogueUiPresence.Absent, state.Update(false));
        }

        [TestMethod]
        public void ConsensusRemovesStableShortNoiseBlocks()
        {
            var results = Enumerable.Range(0, 3)
                .Select(_ => new OCRResult
                {
                    Text = "这是一条足够长的主字幕\nPR2",
                    TextBlocks = new List<TextBlock>
                    {
                        new TextBlock { Text = "这是一条足够长的主字幕", Score = 0.93f },
                        new TextBlock { Text = "PR2", Score = 0.88f }
                    }
                })
                .ToList();

            SubtitleConsensusResult consensus = SubtitleConsensusSelector.Select(results, matcher: null);
            Assert.AreEqual("这是一条足够长的主字幕", consensus.Text);
            Assert.AreEqual(3, consensus.AgreementCount);
        }

        [TestMethod]
        public void AdaptiveOcrStopsAtOneTwoOrThreeCalls()
        {
            using var frame1 = CreateSubtitleFrame("one");
            using var frame2 = CreateSubtitleFrame("two");
            using var frame3 = CreateSubtitleFrame("three");
            var frames = new List<Mat> { frame1, frame2, frame3 };

            int highConfidenceCalls = 0;
            AdaptiveSubtitleOcrResult highConfidence = AdaptiveSubtitleRecognizer.Recognize(
                frames,
                _ =>
                {
                    highConfidenceCalls++;
                    return CreateOcrResult("稳定字幕", 0.96f);
                },
                matcher: null);
            Assert.AreEqual(1, highConfidenceCalls);
            Assert.AreEqual(1, highConfidence.OcrCallCount);

            int agreeingCalls = 0;
            AdaptiveSubtitleOcrResult agreeing = AdaptiveSubtitleRecognizer.Recognize(
                frames,
                _ =>
                {
                    agreeingCalls++;
                    return CreateOcrResult("低置信度但一致", 0.70f);
                },
                matcher: null);
            Assert.AreEqual(2, agreeingCalls);
            Assert.AreEqual(2, agreeing.OcrCallCount);

            string[] differentTexts = { "第三帧", "第二帧", "第一帧" };
            int disagreeingCalls = 0;
            AdaptiveSubtitleOcrResult disagreeing = AdaptiveSubtitleRecognizer.Recognize(
                frames,
                _ => CreateOcrResult(differentTexts[disagreeingCalls++], 0.70f),
                matcher: null);
            Assert.AreEqual(3, disagreeingCalls);
            Assert.AreEqual(3, disagreeing.OcrCallCount);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void BenchmarkDemoWithOnlinePipeline()
        {
            string outputDirectory = Path.GetDirectoryName(typeof(SubtitleRecognitionPipelineTests).Assembly.Location);
            string videoPath = ResolveDemoPath(outputDirectory);
            if (videoPath == null)
            {
                Assert.Inconclusive("demo.mp4 is unavailable.");
            }

            using var engine = SettingsWindow.LoadEngine("CHS");
            using var capture = new VideoCapture(videoPath);
            Assert.IsTrue(capture.IsOpened());

            double fps = capture.Fps;
            int step = Math.Max(1, (int)Math.Round(fps / 10.0));
            var region = new Rect(382, 895, 1113, 70);
            var uiState = new DialogueUiStateMachine();
            using var tracker = new SubtitleEpochTracker();
            using var frame = new Mat();
            var lockedTexts = new List<string>();
            int scannedFrames = 0;
            int ocrCalls = 0;
            int oneCallBatches = 0;
            int twoCallBatches = 0;
            int threeCallBatches = 0;
            int uiPresentSamples = 0;
            int uiSamples = 0;
            double uiConfidenceTotal = 0;
            double uiConfidenceMax = 0;
            var process = Process.GetCurrentProcess();
            TimeSpan cpuStart = process.TotalProcessorTime;
            var stopwatch = Stopwatch.StartNew();

            for (long index = 0; ; index += step)
            {
                if (index > 0)
                {
                    for (int skip = 0; skip < step - 1; skip++)
                    {
                        if (!capture.Grab())
                        {
                            break;
                        }
                    }
                }

                if (!capture.Read(frame) || frame.Empty())
                {
                    break;
                }

                if (scannedFrames % 3 == 0)
                {
                    bool detected = GenshinDialogueUiDetector.TryDetect(
                        frame,
                        frame.Height / 1080.0,
                        out double uiConfidence);
                    uiSamples++;
                    uiConfidenceTotal += uiConfidence;
                    uiConfidenceMax = Math.Max(uiConfidenceMax, uiConfidence);
                    if (uiState.Update(detected) == DialogueUiPresence.Present)
                    {
                        uiPresentSamples++;
                    }
                }

                using var roi = new Mat(frame, region);
                using SubtitleVisualAnalysis analysis = SubtitleVisualAnalyzer.Analyze(roi);
                using SubtitleFrameBatch batch = tracker.Process(roi, analysis, uiState.State);
                if (batch != null)
                {
                    AdaptiveSubtitleOcrResult adaptiveResult = AdaptiveSubtitleRecognizer.Recognize(
                        batch.Frames,
                        sample =>
                        {
                            ocrCalls++;
                            return engine.DetectTextFromMat(sample);
                        },
                        matcher: null);
                    if (adaptiveResult.OcrCallCount == 1) oneCallBatches++;
                    else if (adaptiveResult.OcrCallCount == 2) twoCallBatches++;
                    else if (adaptiveResult.OcrCallCount == 3) threeCallBatches++;
                    SubtitleConsensusResult consensus = adaptiveResult.Consensus;
                    bool accepted = !string.IsNullOrWhiteSpace(consensus.Text) && consensus.Text.Length >= 2;
                    tracker.Complete(batch.Generation, accepted);
                    if (accepted)
                    {
                        lockedTexts.Add(consensus.Text);
                    }
                }

                scannedFrames++;
            }

            stopwatch.Stop();
            process.Refresh();
            var report = new
            {
                Video = Path.GetFileName(videoPath),
                Fps = fps,
                DetectionFps = fps / step,
                ScannedFrames = scannedFrames,
                OcrCalls = ocrCalls,
                OneCallBatches = oneCallBatches,
                TwoCallBatches = twoCallBatches,
                ThreeCallBatches = threeCallBatches,
                LockedSubtitleCount = lockedTexts.Count,
                DialogueUiPresentSamples = uiPresentSamples,
                DialogueUiAverageConfidence = uiSamples > 0 ? uiConfidenceTotal / uiSamples : 0,
                DialogueUiMaximumConfidence = uiConfidenceMax,
                WallTimeMs = stopwatch.ElapsedMilliseconds,
                CpuTimeMs = (process.TotalProcessorTime - cpuStart).TotalMilliseconds,
                Texts = lockedTexts
            };
            string reportPath = Path.Combine(outputDirectory, "demo-online-pipeline-report.json");
            File.WriteAllText(reportPath, JsonConvert.SerializeObject(report, Formatting.Indented));
            Console.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));

            Assert.IsTrue(scannedFrames > 500);
            Assert.IsTrue(lockedTexts.Count > 10);
            Assert.IsTrue(uiPresentSamples > 0);
            Assert.IsTrue(ocrCalls >= lockedTexts.Count);
            Assert.IsTrue(ocrCalls <= lockedTexts.Count * 3 + 9);
        }

        private static SubtitleFrameBatch FeedUntilBatch(SubtitleEpochTracker tracker, Mat frame)
        {
            for (int i = 0; i < 12; i++)
            {
                using SubtitleVisualAnalysis analysis = SubtitleVisualAnalyzer.Analyze(frame);
                SubtitleFrameBatch batch = tracker.Process(frame, analysis, DialogueUiPresence.Present);
                if (batch != null)
                {
                    return batch;
                }
            }
            return null;
        }

        private static Mat CreateSubtitleFrame(string text)
        {
            var frame = new Mat(new OpenCvSharp.Size(900, 90), MatType.CV_8UC3, Scalar.All(20));
            Cv2.PutText(
                frame,
                text,
                new OpenCvSharp.Point(30, 58),
                HersheyFonts.HersheySimplex,
                1.2,
                Scalar.All(245),
                2,
                LineTypes.AntiAlias);
            return frame;
        }

        private static OCRResult CreateOcrResult(string text, float score)
        {
            return new OCRResult
            {
                Text = text,
                TextBlocks = new List<TextBlock>
                {
                    new TextBlock { Text = text, Score = score }
                }
            };
        }

        private static string ResolveDemoPath(string outputDirectory)
        {
            string[] candidates =
            {
                Path.Combine(outputDirectory, "Videos", "demo.mp4"),
                Path.Combine(outputDirectory, "demo.mp4")
            };
            return candidates.FirstOrDefault(File.Exists);
        }
    }
}
