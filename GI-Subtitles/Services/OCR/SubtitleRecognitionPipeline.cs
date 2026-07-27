using GI_Subtitles.Services.Translation;
using OpenCvSharp;
using PaddleOCRSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GI_Subtitles.Services.OCR
{
    internal enum DialogueUiPresence
    {
        Unknown,
        Present,
        Absent
    }

    internal sealed class DialogueUiStateMachine
    {
        private const int EnterHitCount = 2;
        private const int ExitMissCount = 4;
        private int _hits;
        private int _misses;

        public DialogueUiPresence State { get; private set; } = DialogueUiPresence.Unknown;

        public DialogueUiPresence Update(bool detected)
        {
            if (detected)
            {
                _hits++;
                _misses = 0;
                if (_hits >= EnterHitCount)
                {
                    State = DialogueUiPresence.Present;
                }
            }
            else
            {
                _hits = 0;
                _misses++;
                if (_misses >= ExitMissCount)
                {
                    State = DialogueUiPresence.Absent;
                }
            }

            return State;
        }
    }

    /// <summary>
    /// Cheap, language-independent soft signal for Genshin's top-left auto-dialogue icon.
    /// A synthetic edge template avoids binding recognition to the localized "Auto" text.
    /// </summary>
    internal static class GenshinDialogueUiDetector
    {
        public static bool TryDetect(Mat screenOrProbe, double scale, out double confidence)
        {
            confidence = 0;
            if (screenOrProbe == null || screenOrProbe.Empty())
            {
                return false;
            }

            int searchWidth = Math.Min(screenOrProbe.Width, Math.Max(100, (int)Math.Round(230 * scale)));
            int searchHeight = Math.Min(screenOrProbe.Height, Math.Max(55, (int)Math.Round(105 * scale)));
            if (searchWidth < 30 || searchHeight < 30)
            {
                return false;
            }

            using var gray = new Mat();
            if (screenOrProbe.Channels() == 1)
            {
                screenOrProbe.CopyTo(gray);
            }
            else
            {
                Cv2.CvtColor(
                    screenOrProbe,
                    gray,
                    screenOrProbe.Channels() == 4
                        ? ColorConversionCodes.BGRA2GRAY
                        : ColorConversionCodes.BGR2GRAY);
            }

            using var search = new Mat(gray, new Rect(0, 0, searchWidth, searchHeight));
            using var searchEdges = new Mat();
            Cv2.Canny(search, searchEdges, 60, 160);

            double playScore = MatchIcon(searchEdges, scale, false);
            double pauseScore = MatchIcon(searchEdges, scale, true);
            confidence = Math.Max(playScore, pauseScore);
            // This is deliberately a soft signal. False negatives are more harmful than
            // false positives because the subtitle contour gate still validates content.
            return confidence >= 0.19;
        }

        private static double MatchIcon(Mat searchEdges, double scale, bool pause)
        {
            int size = Math.Max(20, (int)Math.Round(42 * Math.Max(0.6, Math.Min(2.5, scale))));
            if (searchEdges.Width < size || searchEdges.Height < size)
            {
                return 0;
            }

            using var icon = new Mat(size, size, MatType.CV_8UC1, Scalar.All(0));
            int center = size / 2;
            int radius = Math.Max(7, (int)Math.Round(size * 0.38));
            int stroke = Math.Max(1, size / 16);
            Cv2.Circle(icon, new Point(center, center), radius, Scalar.All(255), stroke, LineTypes.AntiAlias);

            if (pause)
            {
                int barWidth = Math.Max(2, size / 10);
                int top = center - size / 6;
                int bottom = center + size / 6;
                Cv2.Rectangle(icon, new Rect(center - size / 8 - barWidth, top, barWidth, bottom - top), Scalar.All(255), -1);
                Cv2.Rectangle(icon, new Rect(center + size / 8, top, barWidth, bottom - top), Scalar.All(255), -1);
            }
            else
            {
                var triangle = new[]
                {
                    new Point(center - size / 10, center - size / 6),
                    new Point(center - size / 10, center + size / 6),
                    new Point(center + size / 6, center)
                };
                Cv2.FillConvexPoly(icon, triangle, Scalar.All(255), LineTypes.AntiAlias);
            }

            using var result = new Mat();
            Cv2.MatchTemplate(searchEdges, icon, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxValue);
            return maxValue;
        }
    }

    internal sealed class SubtitleVisualAnalysis : IDisposable
    {
        public SubtitleVisualAnalysis(Mat mask, double foregroundRatio, int contourCount)
        {
            Mask = mask;
            ForegroundRatio = foregroundRatio;
            ContourCount = contourCount;
        }

        public Mat Mask { get; }
        public double ForegroundRatio { get; }
        public int ContourCount { get; }
        public bool HasText => ContourCount >= 2 && ForegroundRatio >= 0.0008 && ForegroundRatio <= 0.35;
        public bool HasStrongText => ContourCount >= 4 && ForegroundRatio >= 0.0018 && ForegroundRatio <= 0.25;

        public void Dispose()
        {
            Mask?.Dispose();
        }
    }

    internal static class SubtitleVisualAnalyzer
    {
        public static SubtitleVisualAnalysis Analyze(Mat source)
        {
            if (source == null || source.Empty())
            {
                return new SubtitleVisualAnalysis(new Mat(), 0, 0);
            }

            using var bgr = EnsureBgr(source);
            using var hsv = new Mat();
            using var gray = new Mat();
            using var lowSaturationBright = new Mat();
            using var veryBright = new Mat();
            using var combined = new Mat();
            Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

            // Normal subtitles are near-white, while outlined/faded glyphs may be dimmer.
            Cv2.InRange(hsv, new Scalar(0, 0, 155), new Scalar(180, 105, 255), lowSaturationBright);
            Cv2.Threshold(gray, veryBright, 215, 255, ThresholdTypes.Binary);
            Cv2.BitwiseOr(lowSaturationBright, veryBright, combined);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
            Cv2.MorphologyEx(combined, combined, MorphTypes.Close, kernel);

            using var contourInput = combined.Clone();
            Cv2.FindContours(
                contourInput,
                out Point[][] contours,
                out _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            var filtered = new Mat(combined.Size(), MatType.CV_8UC1, Scalar.All(0));
            int kept = 0;
            int maxGlyphHeight = Math.Max(18, source.Height * 3 / 4);
            int maxGlyphWidth = Math.Max(30, source.Width / 5);
            for (int i = 0; i < contours.Length; i++)
            {
                Rect bounds = Cv2.BoundingRect(contours[i]);
                double area = Cv2.ContourArea(contours[i]);
                if (bounds.Height < 3 || bounds.Height > maxGlyphHeight ||
                    bounds.Width < 1 || bounds.Width > maxGlyphWidth ||
                    area < 2 || bounds.Width / (double)Math.Max(1, bounds.Height) > 8)
                {
                    continue;
                }

                Cv2.DrawContours(filtered, contours, i, Scalar.All(255), -1);
                kept++;
            }

            double ratio = filtered.Total() > 0
                ? Cv2.CountNonZero(filtered) / (double)filtered.Total()
                : 0;
            return new SubtitleVisualAnalysis(filtered, ratio, kept);
        }

        public static double CalculateChangeRatio(Mat currentMask, Mat previousMask)
        {
            if (currentMask == null || previousMask == null ||
                currentMask.Empty() || previousMask.Empty() ||
                currentMask.Size() != previousMask.Size())
            {
                return 1;
            }

            using var diff = new Mat();
            using var union = new Mat();
            Cv2.Absdiff(currentMask, previousMask, diff);
            Cv2.BitwiseOr(currentMask, previousMask, union);
            int changed = Cv2.CountNonZero(diff);
            int foreground = Cv2.CountNonZero(union);
            double denominator = Math.Max(foreground, currentMask.Total() * 0.003);
            return changed / denominator;
        }

        private static Mat EnsureBgr(Mat source)
        {
            var result = new Mat();
            if (source.Channels() == 4)
            {
                Cv2.CvtColor(source, result, ColorConversionCodes.BGRA2BGR);
            }
            else if (source.Channels() == 1)
            {
                Cv2.CvtColor(source, result, ColorConversionCodes.GRAY2BGR);
            }
            else
            {
                source.CopyTo(result);
            }

            return result;
        }
    }

    internal sealed class SubtitleFrameBatch : IDisposable
    {
        public SubtitleFrameBatch(long generation, List<Mat> frames)
        {
            Generation = generation;
            Frames = frames;
        }

        public long Generation { get; }
        public List<Mat> Frames { get; }

        public void Dispose()
        {
            foreach (Mat frame in Frames)
            {
                frame?.Dispose();
            }
            Frames.Clear();
        }
    }

    /// <summary>
    /// Tracks one visual subtitle generation at a time. Once a generation is accepted,
    /// it is locked until a materially different stable subtitle replaces it.
    /// </summary>
    internal sealed class SubtitleEpochTracker : IDisposable
    {
        private const double StableChangeThreshold = 0.04;
        private const double NewSubtitleChangeThreshold = 0.42;
        private const int RequiredStableFrames = 2;
        private const int ReviewFrameCount = 3;

        private Mat _previousMask;
        private Mat _lockedMask;
        private Mat _batchMask;
        private readonly List<Mat> _reviewFrames = new List<Mat>();
        private long _generation;
        private int _stableFrames;
        private int _transitionStableFrames;
        private int _missingFrames;
        private bool _batchIssued;
        private bool _locked;
        private bool _visualActive;

        public long CurrentGeneration => _generation;

        public SubtitleFrameBatch Process(
            Mat frame,
            SubtitleVisualAnalysis analysis,
            DialogueUiPresence dialogueUi)
        {
            bool usableText = analysis.HasText &&
                              (dialogueUi != DialogueUiPresence.Absent || analysis.HasStrongText);
            if (!usableText)
            {
                _missingFrames++;
                if (_missingFrames >= 3)
                {
                    ResetVisualState();
                }
                ReplaceMask(ref _previousMask, analysis.Mask);
                return null;
            }

            _missingFrames = 0;
            if (!_visualActive || _generation == 0 || _previousMask == null || _previousMask.Empty())
            {
                BeginGeneration();
                ReplaceMask(ref _previousMask, analysis.Mask);
                return null;
            }

            double previousChange = SubtitleVisualAnalyzer.CalculateChangeRatio(analysis.Mask, _previousMask);
            if (_locked)
            {
                double lockedChange = SubtitleVisualAnalyzer.CalculateChangeRatio(analysis.Mask, _lockedMask);
                if (lockedChange >= NewSubtitleChangeThreshold)
                {
                    _transitionStableFrames = previousChange <= StableChangeThreshold
                        ? _transitionStableFrames + 1
                        : 0;
                    if (_transitionStableFrames >= RequiredStableFrames)
                    {
                        BeginGeneration();
                    }
                }
                else
                {
                    _transitionStableFrames = 0;
                }

                ReplaceMask(ref _previousMask, analysis.Mask);
                return null;
            }

            if (_batchIssued)
            {
                if (previousChange >= NewSubtitleChangeThreshold)
                {
                    BeginGeneration();
                }
                ReplaceMask(ref _previousMask, analysis.Mask);
                return null;
            }

            if (previousChange <= StableChangeThreshold)
            {
                _stableFrames++;
            }
            else
            {
                _stableFrames = 0;
                ClearReviewFrames();
            }

            if (_stableFrames >= RequiredStableFrames)
            {
                _reviewFrames.Add(frame.Clone());
            }

            ReplaceMask(ref _previousMask, analysis.Mask);
            if (_reviewFrames.Count < ReviewFrameCount)
            {
                return null;
            }

            _batchIssued = true;
            ReplaceMask(ref _batchMask, analysis.Mask);
            var frames = new List<Mat>(_reviewFrames);
            _reviewFrames.Clear();
            return new SubtitleFrameBatch(_generation, frames);
        }

        public SubtitleFrameBatch CreateManualBatch(IEnumerable<Mat> frames)
        {
            BeginGeneration();
            _batchIssued = true;
            return new SubtitleFrameBatch(_generation, frames.Select(frame => frame.Clone()).ToList());
        }

        public bool IsCurrent(long generation)
        {
            return generation == _generation;
        }

        public bool Complete(long generation, bool accepted)
        {
            if (generation != _generation)
            {
                return false;
            }

            if (accepted)
            {
                _locked = true;
                ReplaceMask(ref _lockedMask, _batchMask ?? _previousMask);
            }
            else
            {
                _batchIssued = false;
                _stableFrames = 0;
            }

            return true;
        }

        private void BeginGeneration()
        {
            _generation++;
            _stableFrames = 0;
            _transitionStableFrames = 0;
            _batchIssued = false;
            _locked = false;
            _visualActive = true;
            _lockedMask?.Dispose();
            _lockedMask = null;
            _batchMask?.Dispose();
            _batchMask = null;
            ClearReviewFrames();
        }

        private void ResetVisualState()
        {
            if (_visualActive && _generation > 0)
            {
                _generation++;
            }
            _stableFrames = 0;
            _transitionStableFrames = 0;
            _batchIssued = false;
            _locked = false;
            _visualActive = false;
            _lockedMask?.Dispose();
            _lockedMask = null;
            _batchMask?.Dispose();
            _batchMask = null;
            ClearReviewFrames();
        }

        private static void ReplaceMask(ref Mat target, Mat source)
        {
            target?.Dispose();
            target = source == null || source.Empty() ? null : source.Clone();
        }

        private void ClearReviewFrames()
        {
            foreach (Mat frame in _reviewFrames)
            {
                frame?.Dispose();
            }
            _reviewFrames.Clear();
        }

        public void Dispose()
        {
            _previousMask?.Dispose();
            _lockedMask?.Dispose();
            _batchMask?.Dispose();
            ClearReviewFrames();
        }
    }

    internal sealed class SubtitleConsensusResult
    {
        public string Text { get; set; }
        public string MatchedKey { get; set; }
        public double Confidence { get; set; }
        public int AgreementCount { get; set; }
    }

    internal sealed class AdaptiveSubtitleOcrResult
    {
        public SubtitleConsensusResult Consensus { get; set; }
        public int OcrCallCount { get; set; }
    }

    internal static class AdaptiveSubtitleRecognizer
    {
        private const double DictionaryMatchConfidence = 0.72;
        private const double StandaloneConfidence = 0.90;

        public static AdaptiveSubtitleOcrResult Recognize(
            IReadOnlyList<Mat> stableFrames,
            Func<Mat, OCRResult> recognize,
            OptimizedMatcher matcher)
        {
            if (stableFrames == null || stableFrames.Count == 0)
            {
                return new AdaptiveSubtitleOcrResult
                {
                    Consensus = new SubtitleConsensusResult { Text = string.Empty }
                };
            }

            var results = new List<OCRResult>(Math.Min(3, stableFrames.Count));
            // Prefer the newest settled frame. Earlier frames are only fallbacks.
            for (int index = stableFrames.Count - 1;
                 index >= 0 && results.Count < 3;
                 index--)
            {
                OCRResult result = recognize(stableFrames[index]);
                results.Add(result);
                SubtitleConsensusResult consensus = SubtitleConsensusSelector.Select(results, matcher);

                if (results.Count == 1 && IsReliableSingleResult(consensus, matcher != null))
                {
                    return new AdaptiveSubtitleOcrResult
                    {
                        Consensus = consensus,
                        OcrCallCount = results.Count
                    };
                }

                // Two equivalent OCR/matcher outcomes are enough; the third frame
                // is reserved for an actual disagreement.
                if (results.Count >= 2 && consensus.AgreementCount >= 2)
                {
                    return new AdaptiveSubtitleOcrResult
                    {
                        Consensus = consensus,
                        OcrCallCount = results.Count
                    };
                }
            }

            return new AdaptiveSubtitleOcrResult
            {
                Consensus = SubtitleConsensusSelector.Select(results, matcher),
                OcrCallCount = results.Count
            };
        }

        private static bool IsReliableSingleResult(
            SubtitleConsensusResult result,
            bool matcherAvailable)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.Text))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(result.MatchedKey))
            {
                return result.Confidence >= DictionaryMatchConfidence;
            }

            return !matcherAvailable &&
                   result.Confidence >= StandaloneConfidence &&
                   result.Text.Length >= 4;
        }
    }

    internal static class SubtitleConsensusSelector
    {
        public static SubtitleConsensusResult Select(
            IReadOnlyList<OCRResult> results,
            OptimizedMatcher matcher)
        {
            var candidates = results
                .Where(result => result != null)
                .Select(result =>
                {
                    string text = GetPrimarySubtitleText(result);
                    string key = "";
                    matcher?.FindMatchWithHeaderSeparated(text, out key);
                    double confidence = result.TextBlocks != null && result.TextBlocks.Count > 0
                        ? result.TextBlocks.Average(block => block.Score)
                        : 0;
                    string identity = !string.IsNullOrEmpty(key) ? "key:" + key : "raw:" + Normalize(text);
                    return new { Text = text, Key = key, Confidence = confidence, Identity = identity };
                })
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Text))
                .ToList();

            if (candidates.Count == 0)
            {
                return new SubtitleConsensusResult { Text = string.Empty };
            }

            var winningGroup = candidates
                .GroupBy(candidate => candidate.Identity, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenByDescending(group => group.Count(candidate => !string.IsNullOrEmpty(candidate.Key)))
                .ThenByDescending(group => group.Max(candidate => candidate.Confidence))
                .First();
            var winner = winningGroup
                .OrderByDescending(candidate => !string.IsNullOrEmpty(candidate.Key))
                .ThenByDescending(candidate => candidate.Confidence)
                .ThenByDescending(candidate => Normalize(candidate.Text).Length)
                .First();

            return new SubtitleConsensusResult
            {
                Text = winner.Text,
                MatchedKey = winner.Key,
                Confidence = winner.Confidence,
                AgreementCount = winningGroup.Count()
            };
        }

        private static string Normalize(string value)
        {
            var builder = new StringBuilder(value?.Length ?? 0);
            foreach (char character in value ?? string.Empty)
            {
                if (!char.IsWhiteSpace(character) && !char.IsPunctuation(character))
                {
                    builder.Append(character);
                }
            }
            return builder.ToString();
        }

        private static string GetPrimarySubtitleText(OCRResult result)
        {
            var blocks = result.TextBlocks?
                .Where(block => block != null && !string.IsNullOrWhiteSpace(block.Text))
                .Select(block => new
                {
                    Text = block.Text.Trim(),
                    block.Score
                })
                .ToList();
            if (blocks == null || blocks.Count == 0)
            {
                return result.Text?.Trim() ?? string.Empty;
            }

            int longest = blocks.Max(block => Normalize(block.Text).Length);
            double bestConfidence = blocks.Max(block => block.Score);
            int minimumLength = Math.Max(2, (int)Math.Ceiling(longest * 0.35));
            var primaryBlocks = blocks
                .Where(block =>
                    Normalize(block.Text).Length >= minimumLength &&
                    block.Score >= Math.Max(0.40, bestConfidence - 0.30))
                .Select(block => block.Text)
                .Take(2)
                .ToList();

            return primaryBlocks.Count > 0
                ? string.Join("\n", primaryBlocks)
                : blocks.OrderByDescending(block => Normalize(block.Text).Length).First().Text;
        }
    }
}
