using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GI_Subtitles.Views;
using GI_Subtitles.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using OpenCvSharp;
using PaddleOCRSharp;


namespace GI_Test
{
    /// <summary>
    /// Text matching unit tests
    /// Used to verify the correctness of multi-segment text matching
    /// </summary>
    [TestClass]
    public class OCRTests
    {

        /// <summary>
        /// Test the processing logic of the Images folder
        /// </summary>
        [TestMethod]
        public void TestProcessImagesFolder()
        {
            string appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            Directory.SetCurrentDirectory(appDir);
            if (!Directory.Exists("Images"))
            {
                Assert.Inconclusive("Images folder does not exist, skipping test");
                return;
            }

            try
            {
                var engine = SettingsWindow.LoadEngine("CHS");

                // Process the Images folder
                OCRSummary.ProcessFolder("Images", engine);

                // Verify that the result file exists
                Assert.IsTrue(File.Exists("result.json"), "Should generate result.json file");
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to process Images folder: {ex.Message}");
            }
        }

        [TestMethod]
        public void TestV6CpuAndOpenVinoProduceSameText()
        {
            string appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            AssertCpuAndOpenVinoProduceSameText(appDir, "V6");
        }

        [TestMethod]
        public void TestSettingsWindowLoadsV6ByDefault()
        {
            using (var engine = SettingsWindow.LoadEngine("CHS"))
            {
                Assert.AreEqual("V6", engine.ModelVersionName);
            }
        }

        [TestMethod]
        public void TestLegacyChineseModelsAreNotAvailableAtRuntime()
        {
            Assert.ThrowsException<NotSupportedException>(() =>
                SettingsWindow.LoadEngine(
                    "CHS",
                    "V4",
                    OCRExecutionProvider.Cpu));
        }

        [TestMethod]
        public void TestRuntimePackageContainsOnlyRequiredOcrModels()
        {
            string appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string inferenceDirectory = Path.Combine(appDir, "inference");
            string[] actualModels = Directory.GetFiles(
                    inferenceDirectory,
                    "*.onnx",
                    SearchOption.AllDirectories)
                .Select(path => path.Substring(inferenceDirectory.Length + 1)
                    .Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    "Det/V6/PP-OCRv6_tiny_det_infer/slim.onnx",
                    "Rec/V4/jp_PP-OCRv4_mobile_rec_infer/slim.onnx",
                    "Rec/V6/PP-OCRv6_tiny_rec_infer/slim.onnx"
                },
                actualModels);
        }

        [TestMethod]
        public void TestV6UsesDedicatedV4RecognizerForJapanese()
        {
            using (var engine = SettingsWindow.LoadEngine(
                       "JP",
                       "V6",
                       OCRExecutionProvider.Cpu))
            {
                Assert.AreEqual(
                    "V6-Tiny-Det+V4-JP-Rec",
                    engine.ModelVersionName);

                string imagePath = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "Images",
                    "JP (1).jpg");
                if (File.Exists(imagePath))
                {
                    OCRResult result = engine.DetectText(imagePath);
                    StringAssert.Matches(
                        result.Text,
                        new Regex("[ぁ-んァ-ヶ]"),
                        "Japanese recognition should preserve kana characters.");
                }
            }
        }

        [TestMethod]
        public void TestDetectionSettingsAreLoadedFromEachModelYaml()
        {
            string appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            using (var v6 = CreateEngine(appDir, "V6", OCRExecutionProvider.Cpu))
            {
                AssertDetectionSettings(v6, 0.2f, 0.4f, 1.4f, false);
            }
        }

        [TestMethod]
        public void TestDetectionReadingOrderClustersNearlyAlignedBoxes()
        {
            MethodInfo sortMethod = typeof(PaddleOCREngine).GetMethod(
                "SortTextRegions",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(sortMethod);
            var rightHalf = new RotatedRect(
                new Point2f(1040f, 218.4f),
                new Size2f(500f, 40f),
                0);
            var leftHalf = new RotatedRect(
                new Point2f(520f, 219.2f),
                new Size2f(500f, 40f),
                0);
            var nextLine = new RotatedRect(
                new Point2f(510f, 264f),
                new Size2f(800f, 40f),
                0);

            var sorted = (RotatedRect[])sortMethod.Invoke(
                null,
                new object[] { new[] { rightHalf, nextLine, leftHalf } });

            CollectionAssert.AreEqual(
                new[] { 520f, 1040f, 510f },
                sorted.Select(rect => rect.Center.X).ToArray());
        }

        [TestMethod]
        public void TestPerspectiveCropUsesRotatedTextBox()
        {
            MethodInfo cropMethod = typeof(PaddleOCREngine).GetMethod(
                "GetPerspectiveCrop",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(cropMethod);
            using (var source = new Mat(200, 200, MatType.CV_8UC3, Scalar.Black))
            {
                var rect = new RotatedRect(
                    new Point2f(100f, 100f),
                    new Size2f(120f, 40f),
                    25f);
                Point[] polygon = rect.Points()
                    .Select(point => new Point(
                        (int)Math.Round(point.X),
                        (int)Math.Round(point.Y)))
                    .ToArray();
                Cv2.FillConvexPoly(source, polygon, Scalar.White);

                using (var crop = (Mat)cropMethod.Invoke(
                           null,
                           new object[] { source, rect }))
                {
                    Assert.IsTrue(crop.Width >= 115 && crop.Width <= 125);
                    Assert.IsTrue(crop.Height >= 35 && crop.Height <= 45);
                    Assert.IsTrue(crop.Mean().Val0 > 200);
                }
            }
        }

        private static void AssertDetectionSettings(
            PaddleOCREngine engine,
            float expectedPixelThreshold,
            float expectedBoxThreshold,
            float expectedUnclipRatio,
            bool expectedDilation)
        {
            FieldInfo settingsField = typeof(PaddleOCREngine).GetField(
                "_detectionSettings",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(settingsField);
            object settings = settingsField.GetValue(engine);
            Type settingsType = settings.GetType();
            Assert.AreEqual(
                expectedPixelThreshold,
                (float)settingsType.GetProperty("PixelThreshold").GetValue(settings),
                0.0001f);
            Assert.AreEqual(
                expectedBoxThreshold,
                (float)settingsType.GetProperty("BoxThreshold").GetValue(settings),
                0.0001f);
            Assert.AreEqual(
                expectedUnclipRatio,
                (float)settingsType.GetProperty("UnclipRatio").GetValue(settings),
                0.0001f);
            Assert.AreEqual(
                expectedDilation,
                (bool)settingsType.GetProperty("UseDilation").GetValue(settings));
        }

        private static void AssertCpuAndOpenVinoProduceSameText(
            string appDir,
            string modelVersion)
        {
            string[] imagePaths = Directory.GetFiles(Path.Combine(appDir, "Images"))
                .OrderBy(path => path)
                .ToArray();

            using (var cpuEngine = CreateEngine(
                       appDir,
                       modelVersion,
                       OCRExecutionProvider.Cpu))
            using (var openVinoEngine = CreateEngine(
                       appDir,
                       modelVersion,
                       OCRExecutionProvider.OpenVino))
            {
                Assert.AreEqual(
                    OCRExecutionProvider.Cpu,
                    cpuEngine.ActiveExecutionProvider,
                    "Forced CPU mode should use ORT CPU.");

                if (openVinoEngine.ActiveExecutionProvider != OCRExecutionProvider.OpenVino)
                {
                    Assert.Inconclusive(
                        "OpenVINO is unavailable on this machine; CPU fallback was verified instead.");
                }

                foreach (string imagePath in imagePaths)
                {
                    var cpuResult = cpuEngine.DetectText(imagePath);
                    var openVinoResult = openVinoEngine.DetectText(imagePath);
                    Assert.AreEqual(
                        cpuResult.Text,
                        openVinoResult.Text,
                        $"CPU and OpenVINO should recognize identical {modelVersion} text for {Path.GetFileName(imagePath)}.");
                }
            }
        }

        private static PaddleOCREngine CreateEngine(
            string appDir,
            string modelVersion,
            OCRExecutionProvider executionProvider)
        {
            string modelRoot = Path.Combine(appDir, "inference");
            Assert.AreEqual("V6", modelVersion);
            var config = new OCRModelConfig
            {
                det_infer = Path.Combine(
                    modelRoot,
                    @"Det\V6\PP-OCRv6_tiny_det_infer\slim.onnx"),
                rec_infer = Path.Combine(
                    modelRoot,
                    @"Rec\V6\PP-OCRv6_tiny_rec_infer\slim.onnx"),
                keys = null,
                model_version = "V6"
            };
            var parameter = new OCRParameter
            {
                execution_provider = executionProvider,
                warm_up_openvino = true
            };
            return new PaddleOCREngine(config, parameter);
        }

    }
}


