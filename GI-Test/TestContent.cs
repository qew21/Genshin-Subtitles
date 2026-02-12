using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
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
                "于哥伦比娅要怎么才能回来关于这一点，我一直在努力",
                "但好像没看到阿帽·他不是说让我和",
                "原来是同深渊对抗的工作啊···那一定很辛苦吧？"
            };

            // Read JSON in the normal way
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
            string game = "Genshin";
            string inputLanguage = "CHS";
            string outputLanguage = "EN";
            string userName = "旅行者";

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
            var matcher = new OptimizedMatcher(voiceContentDict, inputLanguage);

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


        [TestMethod]
        public void TestStarRailEnglish()
        {
            // Test sentence list
            var testSentences = new[]
            {
                "..Lok! The title of this game is Hanu's Adventure. According to the plot synopsis, you have been shrunken"
            };

            // Read JSON in the normal way
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
            string game = "StarRail";
            string inputLanguage = "EN";
            string outputLanguage = "CHS";
            string userName = "Traveler";

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
            var matcher = new OptimizedMatcher(voiceContentDict, inputLanguage);

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

            string resultFilePath = "PerformanceTestStarRail.json";
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

        [TestMethod]
        public void TestGenshinEnglish()
        {
            // Test sentence list
            var testSentences = new[]
            {
                "Lauma\r\nIn Nod-Krai, it's common knowledge that the\r\nCuratorium has a nose for profit and opportunity.\r\nthe same way aphids flit from flower to flower in a",
                "Ineffa\r\nCorrect. At some point, you may come across some\r\nstatues of their goddess — the Frostmoon Scions\r\ncarved these as well.",
                "？？？"
            };

            // Read JSON in the normal way
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
            string game = "Genshin";
            string inputLanguage = "EN";
            string outputLanguage = "CHS";
            string userName = "Traveler";

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
            var matcher = new OptimizedMatcher(voiceContentDict, inputLanguage);

            // Test each sentence
            foreach (var sentence in testSentences)
            {
                var stopwatch = Stopwatch.StartNew();
                string matchedKey;
                var matchResult = matcher.FindMatchWithHeaderSeparated(sentence, out matchedKey);
                stopwatch.Stop();

                var result = new TestResult
                {
                    Input = sentence,
                    MatchedKey = matchedKey ?? "",
                    MatchedResult = matchResult.Content ?? "",
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

            string resultFilePath = "PerformanceTestGenshin.json";
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
    }
}


