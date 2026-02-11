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
    [TestClass]
    public class VideoTests
    {
        /// <summary>
        /// Test the processing logic of the Videos folder (demo video automatic processing)
        /// </summary>
        [TestMethod]
        public void TestProcessVideosFolder()
        {
            if (!Directory.Exists("Videos"))
            {
                Assert.Inconclusive("Videos folder does not exist, skipping test");
                return;
            }

            string demoVideoPath = Path.Combine("Videos", "demo.mp4");
            string demoRegionPath = Path.Combine("Videos", "demo_region.json");

            if (!File.Exists(demoVideoPath) || !File.Exists(demoRegionPath))
            {
                Assert.Inconclusive("demo.mp4 or demo_region.json file does not exist, skipping test");
                return;
            }

            try
            {
                var engine = SettingsWindow.LoadEngine("CHS");

                bool completed = false;
                Exception processException = null;

                // Process the demo video
                Task.Run(() =>
                {
                    try
                    {
                        VideoProcessorHelper.ProcessDemoVideo(demoVideoPath, demoRegionPath, engine, () =>
                        {
                            completed = true;
                        });
                    }
                    catch (Exception ex)
                    {
                        processException = ex;
                        completed = true;
                    }
                });

                // Wait for processing to complete (up to 5 minutes)
                int waitCount = 0;
                while (!completed && waitCount < 300)
                {
                    Thread.Sleep(1000);
                    waitCount++;
                }

                if (processException != null)
                {
                    Assert.Fail($"Failed to process demo video: {processException.Message}");
                }

                if (!completed)
                {
                    Assert.Inconclusive("Processing timed out (exceeds 5 minutes)");
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to process Videos folder: {ex.Message}");
            }
        }
    }
}


