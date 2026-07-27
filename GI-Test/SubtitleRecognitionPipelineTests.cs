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
        public void LegacyFrameChangePolicyTriggersOncePerStableSubtitle()
        {
            using var detector = new SubtitleFrameChangeDetector(0.01);
            using var first = CreateSubtitleFrame("first");
            using var second = CreateSubtitleFrame("second line");

            Assert.IsFalse(detector.Evaluate(first).ShouldRunOcr);
            Assert.IsFalse(detector.Evaluate(first).ShouldRunOcr);
            SubtitleFrameDecision initial = detector.Evaluate(first);
            Assert.IsTrue(initial.ShouldRunOcr);
            detector.CommitCurrentFrame();

            Assert.IsFalse(detector.Evaluate(first).ShouldRunOcr);

            SubtitleFrameDecision transition = detector.Evaluate(second);
            Assert.IsFalse(transition.ShouldRunOcr, "A changing frame must not trigger OCR.");

            Assert.IsFalse(detector.Evaluate(second).ShouldRunOcr);
            Assert.IsFalse(detector.Evaluate(second).ShouldRunOcr);
            SubtitleFrameDecision settled = detector.Evaluate(second);
            Assert.IsTrue(settled.ShouldRunOcr, "The changed subtitle should trigger after it settles.");
            detector.CommitCurrentFrame();

            Assert.IsFalse(detector.Evaluate(second).ShouldRunOcr);
        }

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
        public void MeasureLegacyTriggerSettleWindows()
        {
            string outputDirectory = Path.GetDirectoryName(typeof(SubtitleRecognitionPipelineTests).Assembly.Location);
            string videoPath = ResolveDemoPath(outputDirectory);
            if (videoPath == null)
            {
                Assert.Inconclusive("demo.mp4 is unavailable.");
            }

            int[] settleWindows = { 1, 2, 3, 3, 3, 4, 5, 6 };
            int[] minimumIntervals = { 4, 4, 4, 5, 6, 6, 6, 6 };
            var detectors = settleWindows
                .Select(_ => new SubtitleFrameChangeDetector(0.01, requiredCandidateSamples: 1))
                .ToArray();
            var consecutiveCandidates = new int[settleWindows.Length];
            var lastTriggers = Enumerable.Repeat(-100, settleWindows.Length).ToArray();
            var triggerCounts = new int[settleWindows.Length];
            using var capture = new VideoCapture(videoPath);
            using var frame = new Mat();
            double fps = capture.Fps;
            int step = Math.Max(1, (int)Math.Round(fps / 10.0));
            int scannedFrames = 0;

            try
            {
                for (long index = 0; ; index += step)
                {
                    if (index > 0)
                    {
                        for (int skip = 0; skip < step - 1; skip++)
                        {
                            if (!capture.Grab()) break;
                        }
                    }

                    if (!capture.Read(frame) || frame.Empty()) break;
                    using var roi = new Mat(frame, new Rect(382, 895, 1113, 70));
                    for (int variant = 0; variant < detectors.Length; variant++)
                    {
                        SubtitleFrameDecision decision = detectors[variant].Evaluate(roi);
                        consecutiveCandidates[variant] = decision.ShouldRunOcr
                            ? consecutiveCandidates[variant] + 1
                            : 0;
                        if (consecutiveCandidates[variant] >= settleWindows[variant] &&
                            scannedFrames - lastTriggers[variant] >= minimumIntervals[variant])
                        {
                            detectors[variant].CommitCurrentFrame();
                            lastTriggers[variant] = scannedFrames;
                            triggerCounts[variant]++;
                            consecutiveCandidates[variant] = 0;
                        }
                    }
                    scannedFrames++;
                }
            }
            finally
            {
                foreach (SubtitleFrameChangeDetector detector in detectors) detector.Dispose();
            }

            for (int i = 0; i < settleWindows.Length; i++)
            {
                Console.WriteLine(
                    $"settleFrames={settleWindows[i]}, minIntervalFrames={minimumIntervals[i]}, " +
                    $"triggers={triggerCounts[i]}");
            }
            Assert.IsTrue(triggerCounts[0] > triggerCounts[triggerCounts.Length - 1]);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void BenchmarkDemoWithLegacyTriggerAndUiHints()
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
            using var changeDetector = new SubtitleFrameChangeDetector(0.01);
            using var frame = new Mat();
            var lockedTexts = new List<string>();
            int scannedFrames = 0;
            int ocrCalls = 0;
            int triggerCount = 0;
            int lastTriggerFrame = -100;
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
                SubtitleFrameDecision decision = changeDetector.Evaluate(roi);
                // The production default is 400 ms. At the benchmark's 10 Hz sample rate,
                // that is four sampled frames between automatic OCR calls.
                if (decision.ShouldRunOcr && scannedFrames - lastTriggerFrame >= 4)
                {
                    changeDetector.CommitCurrentFrame();
                    lastTriggerFrame = scannedFrames;
                    triggerCount++;
                    OCRResult result = engine.DetectTextFromMat(roi);
                    ocrCalls++;
                    SubtitleConsensusResult consensus = SubtitleConsensusSelector.Select(
                        new List<OCRResult> { result },
                        matcher: null);
                    bool accepted = !string.IsNullOrWhiteSpace(consensus.Text) && consensus.Text.Length >= 2;
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
                TriggerPolicy = "legacy-full-frame-1pct-3-samples-400ms",
                TriggerCount = triggerCount,
                OcrCalls = ocrCalls,
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
            Assert.AreEqual(triggerCount, ocrCalls);
            Assert.IsTrue(ocrCalls < 50);
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
