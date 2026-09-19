using System;
using System.Collections.Generic;
using System.Linq;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenCvSharp;

namespace GI_Test
{
    /// <summary>
    /// Policy documentation for: one visual change → how many activity-log Capture·OCR·Match rows.
    /// One recognition has exactly one row writer (CompleteOcr); repeated Changed beats are real
    /// recomputation (rows recorded, same-result repeats folded by the result fold); pixel drift
    /// above threshold re-triggers OCR — computation as usual, zero added latency.
    /// </summary>
    [TestClass]
    public class TestDuplicatePipelineLogDiagnosis
    {
        private static readonly OperatorJob[] CaptureOcrMatch =
        {
            OperatorJob.Capture,
            OperatorJob.Ocr,
            OperatorJob.Match
        };

        [TestMethod]
        public void OneCompleteOcr_AppendsExactlyOneCaptureOcrMatchRow()
        {
            LiveOverlaySession session = CreateSession(1);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(
                miss: false,
                content: "line",
                ocrText: "line",
                original: "orig");

            Assert.AreEqual(1, session.ActivityLog.Count);
            CollectionAssert.AreEqual(CaptureOcrMatch, session.ActivityLog[0].Jobs.ToArray());
        }

        [TestMethod]
        public void ApplyPairResult_AfterCompleteOcr_MustNotAppendSecondPipelineRow()
        {
            // CompleteOcr is the single writer of queue-run rows; ApplyPairResult only
            // applies (force bypasses the fold but never writes a pipeline row).
            LiveOverlaySession session = CreateSession(1);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(
                miss: false,
                content: "line",
                ocrText: "line",
                original: "orig");
            session.ApplyPairResult(
                0,
                miss: false,
                content: "line",
                header: null,
                ocrText: "line",
                original: "orig",
                matchMiss: false);

            Assert.AreEqual(1, session.ActivityLog.Count);
        }

        [TestMethod]
        public void TwoChangedBeats_EachCompleted_AppendTwoRows_RealRecomputation()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "a", ocrText: "a", original: "A");

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "a", ocrText: "a", original: "A");

            Assert.AreEqual(2, session.ActivityLog.Count);
            Assert.IsTrue(session.ActivityLog.All(row => row.Jobs.SequenceEqual(CaptureOcrMatch)));
        }

        [TestMethod]
        public void UnchangedBeats_AfterMatch_DoNotAppendRows()
        {
            DateTime now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            LiveOverlaySession session = CreateSession(1, () => now);

            session.Beat(PairFrameSample.ChangedAndStable());
            session.CompleteOcr(miss: false, content: "a", ocrText: "a", original: "A");

            now = now.AddMilliseconds(session.EngineOcrIntervalMs);
            session.Beat(PairFrameSample.Unchanged());
            session.Beat(PairFrameSample.Unchanged());

            Assert.AreEqual(1, session.ActivityLog.Count);
        }

        [TestMethod]
        public void PixelDiffPolicy_IdenticalFrames_AfterOcrBaseline_DoNotRetrigger()
        {
            using (Mat baseline = TextLikeBinary(64, 32))
            using (Mat next = baseline.Clone())
            {
                Assert.IsFalse(
                    IsChangedVsLastOcr(next, baseline, threshold: 0.01),
                    "Identical binaries must not look changed after OCR baseline is set.");
                Assert.IsTrue(IsStableVsPrevious(next, baseline, threshold: 0.01));
                Assert.AreEqual(
                    1,
                    CountTriggers(
                        new[] { baseline, next, next, next },
                        threshold: 0.01),
                    "One appear + identical holds must trigger OCR once only.");
            }
        }

        [TestMethod]
        public void PixelDiffPolicy_NoiseAboveThreshold_RetriggersAfterBaseline()
        {
            // Mirrors MainWindow ChangeThreshold default (OCRThreshold 0.01):
            // after OCR starts, a later stable frame that still differs >1% re-enqueues work.
            using (Mat baseline = TextLikeBinary(100, 100))
            using (Mat noisy = baseline.Clone())
            {
                FlipPixels(noisy, count: 200); // 2% of 10_000 pixels
                int triggers = CountTriggers(
                    new[] { baseline, baseline, noisy, noisy },
                    threshold: 0.01);

                // Documents current MainWindow-equivalent policy: drift > OCRThreshold re-triggers
                // (actual recomputation → another Capture·OCR·Match row), not a log-only bug.
                Assert.AreEqual(
                    2,
                    triggers,
                    "Pixel drift after the OCR baseline re-triggers ChangedAndStable. triggers=" + triggers);
            }
        }

        private static int CountTriggers(Mat[] frames, double threshold)
        {
            Mat lastBinary = null;
            Mat lastOcr = null;
            int triggers = 0;
            try
            {
                foreach (Mat frame in frames)
                {
                    bool empty = frame == null || frame.Empty() || Cv2.CountNonZero(frame) == 0;
                    bool stable = IsStableVsPrevious(frame, lastBinary, threshold);
                    bool changed = IsChangedVsLastOcr(frame, lastOcr, threshold);

                    if (!empty && changed && stable)
                    {
                        triggers++;
                        lastOcr?.Dispose();
                        lastOcr = frame.Clone();
                    }

                    lastBinary?.Dispose();
                    lastBinary = frame.Clone();
                }
            }
            finally
            {
                lastBinary?.Dispose();
                lastOcr?.Dispose();
            }

            return triggers;
        }

        private static bool IsStableVsPrevious(Mat current, Mat previous, double threshold)
        {
            if (previous == null || current == null || current.Empty())
            {
                return true;
            }

            if (!SameShape(current, previous))
            {
                return false;
            }

            using (Mat diff = new Mat())
            {
                Cv2.Absdiff(current, previous, diff);
                int nonZero = Cv2.CountNonZero(diff);
                double change = (double)nonZero / (diff.Rows * diff.Cols);
                return change <= threshold;
            }
        }

        private static bool IsChangedVsLastOcr(Mat current, Mat lastOcr, double threshold)
        {
            if (lastOcr == null)
            {
                return true;
            }

            if (current == null || current.Empty() || !SameShape(current, lastOcr))
            {
                return true;
            }

            using (Mat diff = new Mat())
            {
                Cv2.Absdiff(current, lastOcr, diff);
                int nonZero = Cv2.CountNonZero(diff);
                double change = (double)nonZero / (diff.Rows * diff.Cols);
                return change > threshold;
            }
        }

        private static bool SameShape(Mat left, Mat right)
        {
            return left != null && right != null &&
                   left.Size() == right.Size() &&
                   left.Channels() == right.Channels();
        }

        private static Mat TextLikeBinary(int width, int height)
        {
            var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.All(0));
            // Non-empty "subtitle" band so CountNonZero != 0 (empty frames never enqueue OCR).
            for (int y = height / 3; y < (2 * height) / 3; y++)
            {
                for (int x = width / 8; x < (7 * width) / 8; x++)
                {
                    mat.Set(y, x, (byte)255);
                }
            }

            return mat;
        }

        private static void FlipPixels(Mat binary, int count)
        {
            int flipped = 0;
            for (int y = 0; y < binary.Rows && flipped < count; y++)
            {
                for (int x = 0; x < binary.Cols && flipped < count; x++)
                {
                    byte current = binary.Get<byte>(y, x);
                    binary.Set(y, x, (byte)(current == 0 ? 255 : 0));
                    flipped++;
                }
            }
        }

        private static LiveOverlaySession CreateSession(int pairCount, Func<DateTime> utcNow = null)
        {
            var records = new List<RegionPairRecord>();
            for (int i = 0; i < pairCount; i++)
            {
                records.Add(new RegionPairRecord
                {
                    Id = i + 1,
                    Capture = new OverlayRect(i * 100, 10, 80, 20),
                    Display = new OverlayRect(i * 100, 40, 80, 20)
                });
            }

            return new LiveOverlaySession(
                new MemoryOcrIntervalStore(),
                new MemoryRegionPairStore
                {
                    StoredPairs = records,
                    VoicePrimaryId = pairCount > 0 ? 1 : 0,
                    NextPairId = pairCount + 1
                },
                utcNow);
        }

        private sealed class MemoryOcrIntervalStore : IOcrIntervalStore
        {
            public int Read(int defaultValue)
            {
                return defaultValue;
            }

            public void Write(int milliseconds)
            {
            }
        }

        private sealed class MemoryRegionPairStore : IRegionPairStore
        {
            public LegacyRegionSlots Legacy = new LegacyRegionSlots();
            public List<RegionPairRecord> StoredPairs = new List<RegionPairRecord>();
            public int VoicePrimaryId;
            public int NextPairId;
            public OverlayRect DarkScreenDisplay = OverlayRect.Invalid;
            public OverlayRect DialogueOptionDisplay = OverlayRect.Invalid;
            public bool DarkScreenScan = true;
            public bool DialogueOptionScan;

            public IReadOnlyList<RegionPairRecord> ReadPairs()
            {
                return StoredPairs;
            }

            public LegacyRegionSlots ReadLegacy()
            {
                return Legacy;
            }

            public void WritePairs(IReadOnlyList<RegionPairRecord> pairs)
            {
                StoredPairs = new List<RegionPairRecord>(pairs);
            }

            public int ReadVoicePrimaryId()
            {
                return VoicePrimaryId;
            }

            public void WriteVoicePrimaryId(int id)
            {
                VoicePrimaryId = id;
            }

            public int ReadNextPairId()
            {
                return NextPairId;
            }

            public void WriteNextPairId(int id)
            {
                NextPairId = id;
            }

            public OverlayRect ReadDarkScreenDisplay()
            {
                return DarkScreenDisplay ?? OverlayRect.Invalid;
            }

            public void WriteDarkScreenDisplay(OverlayRect display)
            {
                DarkScreenDisplay = display ?? OverlayRect.Invalid;
            }

            public OverlayRect ReadDialogueOptionDisplay()
            {
                return DialogueOptionDisplay ?? OverlayRect.Invalid;
            }

            public void WriteDialogueOptionDisplay(OverlayRect display)
            {
                DialogueOptionDisplay = display ?? OverlayRect.Invalid;
            }

            public bool ReadDarkScreenScan()
            {
                return DarkScreenScan;
            }

            public void WriteDarkScreenScan(bool enabled)
            {
                DarkScreenScan = enabled;
            }

            public bool ReadDialogueOptionScan()
            {
                return DialogueOptionScan;
            }

            public void WriteDialogueOptionScan(bool enabled)
            {
                DialogueOptionScan = enabled;
            }

            public void SwitchGame(string gameName)
            {
            }
        }
    }
}
