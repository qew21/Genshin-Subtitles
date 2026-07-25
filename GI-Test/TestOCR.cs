using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GI_Subtitles.Views;
using GI_Subtitles.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
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
        public void TestCpuAndOpenVinoProduceSameText()
        {
            string appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string[] imagePaths = Directory.GetFiles(Path.Combine(appDir, "Images"))
                .OrderBy(path => path)
                .ToArray();

            using (var cpuEngine = CreateEngine(appDir, OCRExecutionProvider.Cpu))
            using (var openVinoEngine = CreateEngine(appDir, OCRExecutionProvider.OpenVino))
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
                        $"CPU and OpenVINO should recognize identical text for {Path.GetFileName(imagePath)}.");
                }
            }
        }

        private static PaddleOCREngine CreateEngine(
            string appDir,
            OCRExecutionProvider executionProvider)
        {
            string modelRoot = Path.Combine(appDir, "inference");
            var config = new OCRModelConfig
            {
                det_infer = Path.Combine(
                    modelRoot,
                    @"Det\V4\PP-OCRv4_mobile_det_infer\slim.onnx"),
                rec_infer = Path.Combine(
                    modelRoot,
                    @"Rec\V4\PP-OCRv4_mobile_rec_infer\slim.onnx"),
                keys = Path.Combine(
                    modelRoot,
                    @"Rec\V4\PP-OCRv4_mobile_rec_infer\dict.txt")
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


