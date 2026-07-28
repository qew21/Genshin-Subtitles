using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using PaddleOCRSharp;

namespace GI_Test
{
    [TestClass]
    [TestCategory("Benchmark")]
    [DoNotParallelize]
    public class OCRBenchmarkTests
    {
        private const int DefaultRounds = 10;

        [TestMethod]
        public void BenchmarkV4Cpu()
        {
            RunBenchmark("V4", OCRExecutionProvider.Cpu);
        }

        [TestMethod]
        public void BenchmarkV4OpenVino()
        {
            RunBenchmark("V4", OCRExecutionProvider.OpenVino);
        }

        [TestMethod]
        public void BenchmarkV5Cpu()
        {
            RunBenchmark("V5", OCRExecutionProvider.Cpu);
        }

        [TestMethod]
        public void BenchmarkV5OpenVino()
        {
            RunBenchmark("V5", OCRExecutionProvider.OpenVino);
        }

        [TestMethod]
        public void BenchmarkV6Cpu()
        {
            RunBenchmark("V6", OCRExecutionProvider.Cpu);
        }

        [TestMethod]
        public void BenchmarkV6OpenVino()
        {
            RunBenchmark("V6", OCRExecutionProvider.OpenVino);
        }

        private static void RunBenchmark(
            string modelVersion,
            OCRExecutionProvider requestedProvider)
        {
            string appDir = Path.GetDirectoryName(typeof(OCRBenchmarkTests).Assembly.Location);
            string imagesDirectory = Path.Combine(appDir, "Images");
            string[] imagePaths = Directory.GetFiles(imagesDirectory)
                .Where(path => !string.Equals(
                    Path.GetFileName(path),
                    "JP (1).jpg",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var groundTruth = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(appDir, "OCRGroundTruth.json"), Encoding.UTF8));
            int rounds = GetRounds();

            ForceGarbageCollection();
            var process = Process.GetCurrentProcess();
            RefreshProcess(process);
            long baselinePrivateBytes = process.PrivateMemorySize64;
            long baselineWorkingSetBytes = process.WorkingSet64;

            var initializationTimer = Stopwatch.StartNew();
            using (var engine = CreateEngine(appDir, modelVersion, requestedProvider))
            {
                initializationTimer.Stop();
                if (requestedProvider == OCRExecutionProvider.OpenVino &&
                    engine.ActiveExecutionProvider != OCRExecutionProvider.OpenVino)
                {
                    Assert.Inconclusive(
                        $"OpenVINO is unavailable; {modelVersion} fell back to ORT CPU.");
                }

                // Exercise the complete OCR pipeline once for both providers before sampling.
                engine.DetectText(imagePaths[0]);
                ForceGarbageCollection();
                RefreshProcess(process);
                long warmPrivateBytes = process.PrivateMemorySize64;
                long warmWorkingSetBytes = process.WorkingSet64;
                long peakPrivateBytes = warmPrivateBytes;
                long peakWorkingSetBytes = warmWorkingSetBytes;

                var durations = imagePaths.ToDictionary(
                    path => path,
                    path => new List<double>(),
                    StringComparer.OrdinalIgnoreCase);
                var recognizedText = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

                for (int round = 0; round < rounds; round++)
                {
                    foreach (string imagePath in imagePaths)
                    {
                        var timer = Stopwatch.StartNew();
                        var result = engine.DetectText(imagePath);
                        timer.Stop();
                        durations[imagePath].Add(timer.Elapsed.TotalMilliseconds);
                        if (round == 0)
                        {
                            recognizedText[Path.GetFileName(imagePath)] = result.Text ?? string.Empty;
                        }

                        RefreshProcess(process);
                        peakPrivateBytes = Math.Max(peakPrivateBytes, process.PrivateMemorySize64);
                        peakWorkingSetBytes = Math.Max(peakWorkingSetBytes, process.WorkingSet64);
                    }
                }

                var imageResults = imagePaths.Select(path =>
                {
                    string fileName = Path.GetFileName(path);
                    string expected = groundTruth[fileName];
                    string actual = recognizedText[fileName];
                    string normalizedExpected = NormalizeText(expected);
                    string normalizedActual = NormalizeText(actual);
                    int editDistance = LevenshteinDistance(normalizedExpected, normalizedActual);
                    return new OCRBenchmarkImageResult
                    {
                        FileName = fileName,
                        GroundTruth = expected,
                        RecognizedText = actual,
                        ExactMatch = normalizedExpected == normalizedActual,
                        EditDistance = editDistance,
                        GroundTruthCharacterCount = normalizedExpected.Length,
                        AverageDurationMs = Math.Round(durations[path].Average(), 3),
                        MinimumDurationMs = Math.Round(durations[path].Min(), 3),
                        MaximumDurationMs = Math.Round(durations[path].Max(), 3)
                    };
                }).ToList();

                int totalCharacters = imageResults.Sum(result => result.GroundTruthCharacterCount);
                int totalEditDistance = imageResults.Sum(result => result.EditDistance);
                var report = new OCRBenchmarkReport
                {
                    ModelVersion = modelVersion,
                    RequestedProvider = requestedProvider.ToString(),
                    ActiveProvider = engine.ExecutionProviderName,
                    Rounds = rounds,
                    ImageCount = imageResults.Count,
                    ExactMatchCount = imageResults.Count(result => result.ExactMatch),
                    CharacterErrorRatePercent = Math.Round(
                        totalEditDistance * 100.0 / totalCharacters,
                        3),
                    CharacterAccuracyPercent = Math.Round(
                        Math.Max(0, 1.0 - totalEditDistance / (double)totalCharacters) * 100.0,
                        3),
                    AverageDurationMs = Math.Round(
                        durations.Values.SelectMany(values => values).Average(),
                        3),
                    InitializationDurationMs = Math.Round(
                        initializationTimer.Elapsed.TotalMilliseconds,
                        3),
                    PrivateMemoryAfterWarmupMiB = ToMiB(warmPrivateBytes),
                    PrivateMemoryIncreaseMiB = ToMiB(warmPrivateBytes - baselinePrivateBytes),
                    PeakPrivateMemoryMiB = ToMiB(peakPrivateBytes),
                    PeakPrivateMemoryIncreaseMiB = ToMiB(peakPrivateBytes - baselinePrivateBytes),
                    WorkingSetAfterWarmupMiB = ToMiB(warmWorkingSetBytes),
                    WorkingSetIncreaseMiB = ToMiB(warmWorkingSetBytes - baselineWorkingSetBytes),
                    PeakWorkingSetMiB = ToMiB(peakWorkingSetBytes),
                    PeakWorkingSetIncreaseMiB = ToMiB(peakWorkingSetBytes - baselineWorkingSetBytes),
                    Images = imageResults
                };

                string providerName = requestedProvider == OCRExecutionProvider.OpenVino
                    ? "openvino"
                    : "cpu";
                string outputPath = Path.Combine(
                    appDir,
                    $"benchmark-{modelVersion.ToLowerInvariant()}-{providerName}.json");
                File.WriteAllText(
                    outputPath,
                    JsonConvert.SerializeObject(report, Formatting.Indented),
                    new UTF8Encoding(false));
                Console.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
            }
        }

        private static PaddleOCREngine CreateEngine(
            string appDir,
            string modelVersion,
            OCRExecutionProvider provider)
        {
            string modelRoot = Path.Combine(appDir, "inference");
            string detectorPath;
            string recognizerPath;
            string keysPath = null;
            switch (modelVersion)
            {
                case "V6":
                    detectorPath = @"Det\V6\PP-OCRv6_small_det_infer\slim.onnx";
                    recognizerPath = @"Rec\V6\PP-OCRv6_small_rec_infer\slim.onnx";
                    break;
                case "V5":
                    detectorPath = @"Det\V5\PP-OCRv5_mobile_det_infer\slim.onnx";
                    recognizerPath = @"Rec\V5\PP-OCRv5_mobile_rec_infer\slim.onnx";
                    break;
                default:
                    detectorPath = @"Det\V4\PP-OCRv4_mobile_det_infer\slim.onnx";
                    recognizerPath = @"Rec\V4\PP-OCRv4_mobile_rec_infer\slim.onnx";
                    keysPath = Path.Combine(
                        modelRoot,
                        @"Rec\V4\PP-OCRv4_mobile_rec_infer\dict.txt");
                    break;
            }

            var config = new OCRModelConfig
            {
                det_infer = Path.Combine(modelRoot, detectorPath),
                rec_infer = Path.Combine(modelRoot, recognizerPath),
                keys = keysPath,
                model_version = modelVersion
            };
            return new PaddleOCREngine(
                config,
                new OCRParameter
                {
                    execution_provider = provider,
                    warm_up_openvino = true
                });
        }

        private static int GetRounds()
        {
            return int.TryParse(
                       Environment.GetEnvironmentVariable("OCR_BENCHMARK_ROUNDS"),
                       out int rounds) &&
                   rounds > 0
                ? rounds
                : DefaultRounds;
        }

        private static string NormalizeText(string value)
        {
            return new string((value ?? string.Empty)
                .Where(character => !char.IsWhiteSpace(character))
                .ToArray());
        }

        private static int LevenshteinDistance(string expected, string actual)
        {
            var previous = Enumerable.Range(0, actual.Length + 1).ToArray();
            var current = new int[actual.Length + 1];
            for (int expectedIndex = 1; expectedIndex <= expected.Length; expectedIndex++)
            {
                current[0] = expectedIndex;
                for (int actualIndex = 1; actualIndex <= actual.Length; actualIndex++)
                {
                    int substitutionCost =
                        expected[expectedIndex - 1] == actual[actualIndex - 1] ? 0 : 1;
                    current[actualIndex] = Math.Min(
                        Math.Min(
                            current[actualIndex - 1] + 1,
                            previous[actualIndex] + 1),
                        previous[actualIndex - 1] + substitutionCost);
                }

                var swap = previous;
                previous = current;
                current = swap;
            }

            return previous[actual.Length];
        }

        private static void ForceGarbageCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static void RefreshProcess(Process process)
        {
            process.Refresh();
        }

        private static double ToMiB(long bytes)
        {
            return Math.Round(bytes / 1024.0 / 1024.0, 3);
        }

        private sealed class OCRBenchmarkReport
        {
            public string ModelVersion { get; set; }
            public string RequestedProvider { get; set; }
            public string ActiveProvider { get; set; }
            public int Rounds { get; set; }
            public int ImageCount { get; set; }
            public int ExactMatchCount { get; set; }
            public double CharacterErrorRatePercent { get; set; }
            public double CharacterAccuracyPercent { get; set; }
            public double AverageDurationMs { get; set; }
            public double InitializationDurationMs { get; set; }
            public double PrivateMemoryAfterWarmupMiB { get; set; }
            public double PrivateMemoryIncreaseMiB { get; set; }
            public double PeakPrivateMemoryMiB { get; set; }
            public double PeakPrivateMemoryIncreaseMiB { get; set; }
            public double WorkingSetAfterWarmupMiB { get; set; }
            public double WorkingSetIncreaseMiB { get; set; }
            public double PeakWorkingSetMiB { get; set; }
            public double PeakWorkingSetIncreaseMiB { get; set; }
            public List<OCRBenchmarkImageResult> Images { get; set; }
        }

        private sealed class OCRBenchmarkImageResult
        {
            public string FileName { get; set; }
            public string GroundTruth { get; set; }
            public string RecognizedText { get; set; }
            public bool ExactMatch { get; set; }
            public int EditDistance { get; set; }
            public int GroundTruthCharacterCount { get; set; }
            public double AverageDurationMs { get; set; }
            public double MinimumDurationMs { get; set; }
            public double MaximumDurationMs { get; set; }
        }
    }
}
