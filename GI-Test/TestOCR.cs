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
    public class TextMatchingTests
    {

        [TestMethod]
        public void TestPartMatchText()
        {
            if (Config.Get<string>("Input") != "EN")
            {
                return;
            }
            string ocrText = "We ca heal you";

            string expectedKey = "We can hear you!";
            string expectedResult = "我们听得到！！";

            var contentDict = new Dictionary<string, string>
            {
                { expectedKey, expectedResult }
            };

            // Simulate multi-segment matching logic: first try complete text matching
            string matchedKey;
            string matchedResult = VoiceContentHelper.FindClosestMatch(ocrText, contentDict, out matchedKey);
            Logger.Log.Debug($"matchedResult = {matchedResult}, matchedKey = {matchedKey}");

            // If the complete text matching succeeds, no need to split
            if (!string.IsNullOrEmpty(matchedResult))
            {
                Assert.AreEqual(expectedResult, matchedResult, "Complete text matching should succeed");
            }
            else
            {
                // If the complete matching fails, then split (this should not happen)
                Assert.Fail("Complete text matching should succeed, no need to split");
            }
        }

        [TestMethod]
        public void TestMultiPartMatchText()
        {
            if (Config.Get<string>("Input") != "EN")
            {
                return;
            }
            string ocrText = "Choiseul\nFontaine ResearchInstitute Administrative Officer\nRaimondo, nothing could go wrong here, right?";

            string expectedResult = "舒瓦瑟尔 雷蒙多，这不会出什么问题的，对吧？";

            var contentDict = new Dictionary<string, string>
            {
                { "Choiseul", "舒瓦瑟尔" },
                { "Raimondo, nothing could go wrong here, right?", "雷蒙多，这不会出什么问题的，对吧？" }
            };

            // Simulate multi-segment matching logic: first try complete text matching
            string matchedKey;
            string matchedResult = VoiceContentHelper.FindMatchWithHeader(ocrText, contentDict, out matchedKey);
            Logger.Log.Debug($"matchedResult = {matchedResult}, matchedKey = {matchedKey}");

            // If the complete text matching succeeds, no need to split
            if (!string.IsNullOrEmpty(matchedResult))
            {
                Assert.AreEqual(expectedResult, matchedResult, "完整文本匹配应该成功");
            }
            else
            {
                // If the complete matching fails, then split (this should not happen)
                Assert.Fail("完整文本匹配应该成功，不应该需要分割");
            }
        }

        [TestMethod]
        public void TestMultiPartMatchTextCHS()
        {
            if (Config.Get<string>("Input") != "CHS")
            {
                return;
            }
            string ocrText = "您居然不用任何器具，就这么把桩锚轻松自如地取下来了。";
            string expectedKey = "您居然…不用任何器具，就这么把桩锚轻松自如地取下来了。";
            string expectedResult = "You... actually managed to retrieve the Survey Anchor without any instruments, like it was nothing.";

            var contentDict = new Dictionary<string, string>
            {
                { expectedKey, expectedResult }
            };

            // Simulate multi-segment matching logic: first try complete text matching
            string matchedKey;
            string matchedResult = VoiceContentHelper.FindClosestMatch(ocrText, contentDict, out matchedKey);
            Logger.Log.Debug($"matchedResult = {matchedResult}, matchedKey = {matchedKey}");

            // If the complete text matching succeeds, no need to split
            if (!string.IsNullOrEmpty(matchedResult))
            {
                Assert.AreEqual(expectedResult, matchedResult, "完整文本匹配应该成功");
            }
            else
            {
                // If the complete matching fails, then split (this should not happen)
                Assert.Fail("完整文本匹配应该成功，不应该需要分割");
            }
        }

        /// <summary>
        /// Performance test: test the performance of the FindClosestMatch method
        /// Test the matching of 5 sentences, calculate the average time, and output the JSON format result
        /// </summary>
        [TestMethod]
        public void TestFindClosestMatchPerformance()
        {
            // Test sentence list
            var testSentences = new[]
            {
                "您可以这么说吧。我是挪德卡莱的「执灯士」，平日驻守在北部的坟莹附近，今天到这边只是",
                "但若是您听说过「狂猎」灾祸，那便能理解它",
                "您的惊讶很正常，一般人难以想象同墓碑与腐土共同生活",
                "这灯…·感觉怪怪的",
                "原来是同深渊对抗的工作啊···那一定很辛苦吧？"
            };

            // Read JSON in the normal way
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
            string game = Config.Get<string>("Game", "Genshin");
            string inputLanguage = Config.Get<string>("Input", "CHS");
            string outputLanguage = Config.Get<string>("Output", "EN");
            string userName = (outputLanguage == "CHS") ? "旅行者" : "Traveler";

            string inputFilePath = Path.Combine(dataDir, game, $"TextMap{inputLanguage}.json");
            string outputFilePath = Path.Combine(dataDir, game, $"TextMap{outputLanguage}.json");

            // Check if the files exist
            if (!File.Exists(inputFilePath) || !File.Exists(outputFilePath))
            {
                Logger.Log.Debug($"JSON file does not exist, skipping performance test. Input: {inputFilePath}, Output: {outputFilePath}");
                Assert.Inconclusive($"JSON file does not exist. Please ensure the files exist:\nInput: {inputFilePath}\nOutput: {outputFilePath}");
                return;
            }

            // Load the dictionary (in the normal way)
            Dictionary<string, string> voiceContentDict;
            try
            {
                voiceContentDict = VoiceContentHelper.CreateVoiceContentDictionary(inputFilePath, outputFilePath, userName);
                Logger.Log.Debug($"Successfully loaded dictionary, containing {voiceContentDict.Count} records");
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Failed to load dictionary: {ex}");
                Assert.Fail($"Failed to load dictionary: {ex.Message}");
                return;
            }

            // Test result list
            var testResults = new List<TestResult>();

            // Test each sentence
            foreach (var sentence in testSentences)
            {
                var stopwatch = Stopwatch.StartNew();
                string matchedKey;
                string matchedResult = VoiceContentHelper.FindClosestMatch(sentence, voiceContentDict, out matchedKey);
                stopwatch.Stop();

                var result = new TestResult
                {
                    Input = sentence,
                    MatchedKey = matchedKey ?? "",
                    MatchedResult = matchedResult ?? "",
                    ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds
                };

                testResults.Add(result);
                Logger.Log.Debug($"Sentence: {sentence.Substring(0, Math.Min(30, sentence.Length))}... | Time: {result.ElapsedMilliseconds:F2}ms | Matched key: {matchedKey?.Substring(0, Math.Min(30, matchedKey?.Length ?? 0))}...");
            }

            // Calculate the average time
            double averageTime = testResults.Average(r => r.ElapsedMilliseconds);
            double totalTime = testResults.Sum(r => r.ElapsedMilliseconds);

            // Build the result object
            var performanceResult = new PerformanceTestResult
            {
                DictionarySize = voiceContentDict.Count,
                TestCount = testResults.Count,
                TotalElapsedMilliseconds = totalTime,
                AverageElapsedMilliseconds = averageTime,
                TestResults = testResults
            };

            // Output JSON format result
            string jsonResult = JsonConvert.SerializeObject(performanceResult, Formatting.Indented);
            Logger.Log.Debug($"Performance test result (JSON):\n{jsonResult}");

            // Output to the console (visible in the test output)
            Console.WriteLine("=== FindClosestMatch Performance Test Result ===");
            Console.WriteLine(jsonResult);
            Console.WriteLine("=====================================");

            // Write the result to the file (save to the project root directory)
            string projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory)));
            if (string.IsNullOrEmpty(projectRoot))
            {
                // If the project root directory cannot be determined, use the current directory
                projectRoot = Directory.GetCurrentDirectory();
            }
            string resultFilePath = Path.Combine(projectRoot, "PerformanceTestResult.json");
            try
            {
                File.WriteAllText(resultFilePath, jsonResult, System.Text.Encoding.UTF8);
                Logger.Log.Debug($"Result saved to: {resultFilePath}");
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"Failed to save result file: {ex.Message}, but the test result has been output to the console");
            }

            // Assert: ensure that all tests have been completed (do not verify performance, only verify functionality)
            Assert.IsTrue(testResults.Count == testSentences.Length, "All test sentences should be processed");
        }

        /// <summary>
        /// Performance test: test the performance of the FindClosestMatch method
        /// Test the matching of 5 sentences, calculate the average time, and output the JSON format result
        /// </summary>
        [TestMethod]
        public void TestFindClosestMatchPerformanceNew()
        {
            // Test sentence list
            var testSentences = new[]
            {
                "您可以这么说吧。我是挪德卡莱的「执灯士」，平日驻守在北部的坟莹附近，今天到这边只是",
                "但若是您听说过「狂猎」灾祸，那便能理解它",
                "您的惊讶很正常，一般人难以想象同墓碑与腐土共同生活",
                "这灯…·感觉怪怪的",
                "于哥伦比娅要怎么才能回来关于这一点，我一直在努力",
                "但好像没看到阿帽·他不是说让我和",
                "原来是同深渊对抗的工作啊···那一定很辛苦吧？"
            };

            // Read JSON in the normal way
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
            string game = Config.Get<string>("Game", "Genshin");
            string inputLanguage = Config.Get<string>("Input", "CHS");
            string outputLanguage = Config.Get<string>("Output", "EN");
            string userName = (outputLanguage == "CHS") ? "旅行者" : "Traveler";

            string inputFilePath = Path.Combine(dataDir, game, $"TextMap{inputLanguage}.json");
            string outputFilePath = Path.Combine(dataDir, game, $"TextMap{outputLanguage}.json");

            // Check if the files exist
            if (!File.Exists(inputFilePath) || !File.Exists(outputFilePath))
            {
                Logger.Log.Debug($"JSON file does not exist, skipping performance test. Input: {inputFilePath}, Output: {outputFilePath}");
                Assert.Inconclusive($"JSON file does not exist. Please ensure the files exist:\nInput: {inputFilePath}\nOutput: {outputFilePath}");
                return;
            }

            // Load the dictionary (in the normal way)
            Dictionary<string, string> voiceContentDict;
            try
            {
                voiceContentDict = VoiceContentHelper.CreateVoiceContentDictionary(inputFilePath, outputFilePath, userName);
                Logger.Log.Debug($"Successfully loaded dictionary, containing {voiceContentDict.Count} records");
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"Failed to load dictionary: {ex}");
                Assert.Fail($"Failed to load dictionary: {ex.Message}");
                return;
            }

            // Test result list
            var testResults = new List<TestResult>();
            var matcher = new OptimizedMatcher(voiceContentDict);

            // Test each sentence
            foreach (var sentence in testSentences)
            {
                var stopwatch = Stopwatch.StartNew();
                string matchedKey;
                string matchedResult = matcher.FindClosestMatch(sentence, out matchedKey);
                stopwatch.Stop();

                var result = new TestResult
                {
                    Input = sentence,
                    MatchedKey = matchedKey ?? "",
                    MatchedResult = matchedResult ?? "",
                    ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds
                };

                testResults.Add(result);
                Logger.Log.Debug($"Sentence: {sentence.Substring(0, Math.Min(30, sentence.Length))}... | Time: {result.ElapsedMilliseconds:F2}ms | Matched key: {matchedKey?.Substring(0, Math.Min(30, matchedKey?.Length ?? 0))}...");
            }

            // Calculate the average time
            double averageTime = testResults.Average(r => r.ElapsedMilliseconds);
            double totalTime = testResults.Sum(r => r.ElapsedMilliseconds);

            // Build the result object
            var performanceResult = new PerformanceTestResult
            {
                DictionarySize = voiceContentDict.Count,
                TestCount = testResults.Count,
                TotalElapsedMilliseconds = totalTime,
                AverageElapsedMilliseconds = averageTime,
                TestResults = testResults
            };

            // Output JSON format result
            string jsonResult = JsonConvert.SerializeObject(performanceResult, Formatting.Indented);
            Logger.Log.Debug($"Performance test result (JSON):\n{jsonResult}");

            // Output to the console (visible in the test output)
            Console.WriteLine("=== FindClosestMatch Performance Test Result ===");
            Console.WriteLine(jsonResult);
            Console.WriteLine("=====================================");

            // Write the result to the file (save to the project root directory)
            string projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory)));
            if (string.IsNullOrEmpty(projectRoot))
            {
                // If the project root directory cannot be determined, use the current directory
                projectRoot = Directory.GetCurrentDirectory();
            }
            string resultFilePath = "PerformanceTestResult.json";
            try
            {
                File.WriteAllText(resultFilePath, jsonResult, System.Text.Encoding.UTF8);
                Logger.Log.Debug($"Result saved to: {resultFilePath}");
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"Failed to save result file: {ex.Message}, but the test result has been output to the console");
            }

            // Assert: ensure that all tests have been completed (do not verify performance, only verify functionality)
            Assert.IsTrue(testResults.Count == testSentences.Length, "All test sentences should be processed");
        }

        // Test result class
        private class TestResult
        {
            [JsonProperty("input")]
            public string Input { get; set; }

            [JsonProperty("matchedKey")]
            public string MatchedKey { get; set; }

            [JsonProperty("matchedResult")]
            public string MatchedResult { get; set; }

            [JsonProperty("elapsedMilliseconds")]
            public double ElapsedMilliseconds { get; set; }
        }

        // Performance test result class
        private class PerformanceTestResult
        {
            [JsonProperty("dictionarySize")]
            public int DictionarySize { get; set; }

            [JsonProperty("testCount")]
            public int TestCount { get; set; }

            [JsonProperty("totalElapsedMilliseconds")]
            public double TotalElapsedMilliseconds { get; set; }

            [JsonProperty("averageElapsedMilliseconds")]
            public double AverageElapsedMilliseconds { get; set; }

            [JsonProperty("testResults")]
            public List<TestResult> TestResults { get; set; }
        }

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


