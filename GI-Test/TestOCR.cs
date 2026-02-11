using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GI_Subtitles;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;


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

    }
}


