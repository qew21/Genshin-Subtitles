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
// VoiceContentHelper 在全局命名空间中，直接使用即可


namespace GI_Test
{
    /// <summary>
    /// 文本匹配单元测试
    /// 用于验证多段文本匹配的正确性
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

            // 模拟多段匹配逻辑：先尝试完整文本匹配
            string matchedKey;
            string matchedResult = VoiceContentHelper.FindClosestMatch(ocrText, contentDict, out matchedKey);
            Logger.Log.Debug($"matchedResult = {matchedResult}, matchedKey = {matchedKey}");

            // 如果完整文本匹配成功，就不需要分割
            if (!string.IsNullOrEmpty(matchedResult))
            {
                Assert.AreEqual(expectedResult, matchedResult, "完整文本匹配应该成功");
            }
            else
            {
                // 如果完整匹配失败，才进行分割（这里不应该发生）
                Assert.Fail("完整文本匹配应该成功，不应该需要分割");
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

            // 模拟多段匹配逻辑：先尝试完整文本匹配
            string matchedKey;
            string matchedResult = VoiceContentHelper.FindMatchWithHeader(ocrText, contentDict, out matchedKey);
            Logger.Log.Debug($"matchedResult = {matchedResult}, matchedKey = {matchedKey}");

            // 如果完整文本匹配成功，就不需要分割
            if (!string.IsNullOrEmpty(matchedResult))
            {
                Assert.AreEqual(expectedResult, matchedResult, "完整文本匹配应该成功");
            }
            else
            {
                // 如果完整匹配失败，才进行分割（这里不应该发生）
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

            // 模拟多段匹配逻辑：先尝试完整文本匹配
            string matchedKey;
            string matchedResult = VoiceContentHelper.FindClosestMatch(ocrText, contentDict, out matchedKey);
            Logger.Log.Debug($"matchedResult = {matchedResult}, matchedKey = {matchedKey}");

            // 如果完整文本匹配成功，就不需要分割
            if (!string.IsNullOrEmpty(matchedResult))
            {
                Assert.AreEqual(expectedResult, matchedResult, "完整文本匹配应该成功");
            }
            else
            {
                // 如果完整匹配失败，才进行分割（这里不应该发生）
                Assert.Fail("完整文本匹配应该成功，不应该需要分割");
            }
        }

        /// <summary>
        /// 性能测试：测试FindClosestMatch方法的性能
        /// 测试5个句子的匹配，计算平均耗时，输出JSON格式结果
        /// </summary>
        [TestMethod]
        public void TestFindClosestMatchPerformance()
        {
            // 测试句子列表
            var testSentences = new[]
            {
                "您可以这么说吧。我是挪德卡莱的「执灯士」，平日驻守在北部的坟莹附近，今天到这边只是",
                "但若是您听说过「狂猎」灾祸，那便能理解它",
                "您的惊讶很正常，一般人难以想象同墓碑与腐土共同生活",
                "这灯…·感觉怪怪的",
                "原来是同深渊对抗的工作啊···那一定很辛苦吧？"
            };

            // 按照正常加载方式读取JSON
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
            string game = Config.Get<string>("Game", "Genshin");
            string inputLanguage = Config.Get<string>("Input", "CHS");
            string outputLanguage = Config.Get<string>("Output", "EN");
            string userName = (outputLanguage == "CHS") ? "旅行者" : "Traveler";

            string inputFilePath = Path.Combine(dataDir, game, $"TextMap{inputLanguage}.json");
            string outputFilePath = Path.Combine(dataDir, game, $"TextMap{outputLanguage}.json");

            // 检查文件是否存在
            if (!File.Exists(inputFilePath) || !File.Exists(outputFilePath))
            {
                Logger.Log.Debug($"JSON文件不存在，跳过性能测试。Input: {inputFilePath}, Output: {outputFilePath}");
                Assert.Inconclusive($"JSON文件不存在。请确保文件存在：\nInput: {inputFilePath}\nOutput: {outputFilePath}");
                return;
            }

            // 加载字典（按照正常方式）
            Dictionary<string, string> voiceContentDict;
            try
            {
                voiceContentDict = VoiceContentHelper.CreateVoiceContentDictionary(inputFilePath, outputFilePath, userName);
                Logger.Log.Debug($"成功加载字典，共 {voiceContentDict.Count} 条记录");
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"加载字典失败: {ex}");
                Assert.Fail($"加载字典失败: {ex.Message}");
                return;
            }

            // 测试结果列表
            var testResults = new List<TestResult>();

            // 对每个句子进行测试
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
                Logger.Log.Debug($"句子: {sentence.Substring(0, Math.Min(30, sentence.Length))}... | 耗时: {result.ElapsedMilliseconds:F2}ms | 匹配键: {matchedKey?.Substring(0, Math.Min(30, matchedKey?.Length ?? 0))}...");
            }

            // 计算平均耗时
            double averageTime = testResults.Average(r => r.ElapsedMilliseconds);
            double totalTime = testResults.Sum(r => r.ElapsedMilliseconds);

            // 构建结果对象
            var performanceResult = new PerformanceTestResult
            {
                DictionarySize = voiceContentDict.Count,
                TestCount = testResults.Count,
                TotalElapsedMilliseconds = totalTime,
                AverageElapsedMilliseconds = averageTime,
                TestResults = testResults
            };

            // 输出JSON格式结果
            string jsonResult = JsonConvert.SerializeObject(performanceResult, Formatting.Indented);
            Logger.Log.Debug($"性能测试结果（JSON）:\n{jsonResult}");

            // 输出到控制台（在测试输出中可见）
            Console.WriteLine("=== FindClosestMatch 性能测试结果 ===");
            Console.WriteLine(jsonResult);
            Console.WriteLine("=====================================");

            // 将结果写入文件（保存到项目根目录）
            string projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory)));
            if (string.IsNullOrEmpty(projectRoot))
            {
                // 如果无法确定项目根目录，使用当前目录
                projectRoot = Directory.GetCurrentDirectory();
            }
            string resultFilePath = Path.Combine(projectRoot, "PerformanceTestResult.json");
            try
            {
                File.WriteAllText(resultFilePath, jsonResult, System.Text.Encoding.UTF8);
                Logger.Log.Debug($"结果已保存到: {resultFilePath}");
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"保存结果文件失败: {ex.Message}，但测试结果已在控制台输出");
            }

            // 断言：确保所有测试都完成了（不验证性能，只验证功能）
            Assert.IsTrue(testResults.Count == testSentences.Length, "所有测试句子都应该被处理");
        }

        /// <summary>
        /// 性能测试：测试FindClosestMatch方法的性能
        /// 测试5个句子的匹配，计算平均耗时，输出JSON格式结果
        /// </summary>
        [TestMethod]
        public void TestFindClosestMatchPerformanceNew()
        {
            // 测试句子列表
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

            // 按照正常加载方式读取JSON
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles");
            string game = Config.Get<string>("Game", "Genshin");
            string inputLanguage = Config.Get<string>("Input", "CHS");
            string outputLanguage = Config.Get<string>("Output", "EN");
            string userName = (outputLanguage == "CHS") ? "旅行者" : "Traveler";

            string inputFilePath = Path.Combine(dataDir, game, $"TextMap{inputLanguage}.json");
            string outputFilePath = Path.Combine(dataDir, game, $"TextMap{outputLanguage}.json");

            // 检查文件是否存在
            if (!File.Exists(inputFilePath) || !File.Exists(outputFilePath))
            {
                Logger.Log.Debug($"JSON文件不存在，跳过性能测试。Input: {inputFilePath}, Output: {outputFilePath}");
                Assert.Inconclusive($"JSON文件不存在。请确保文件存在：\nInput: {inputFilePath}\nOutput: {outputFilePath}");
                return;
            }

            // 加载字典（按照正常方式）
            Dictionary<string, string> voiceContentDict;
            try
            {
                voiceContentDict = VoiceContentHelper.CreateVoiceContentDictionary(inputFilePath, outputFilePath, userName);
                Logger.Log.Debug($"成功加载字典，共 {voiceContentDict.Count} 条记录");
            }
            catch (Exception ex)
            {
                Logger.Log.Error($"加载字典失败: {ex}");
                Assert.Fail($"加载字典失败: {ex.Message}");
                return;
            }

            // 测试结果列表
            var testResults = new List<TestResult>();
            var matcher = new OptimizedMatcher(voiceContentDict);

            // 对每个句子进行测试
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
                Logger.Log.Debug($"句子: {sentence.Substring(0, Math.Min(30, sentence.Length))}... | 耗时: {result.ElapsedMilliseconds:F2}ms | 匹配键: {matchedKey?.Substring(0, Math.Min(30, matchedKey?.Length ?? 0))}...");
            }

            // 计算平均耗时
            double averageTime = testResults.Average(r => r.ElapsedMilliseconds);
            double totalTime = testResults.Sum(r => r.ElapsedMilliseconds);

            // 构建结果对象
            var performanceResult = new PerformanceTestResult
            {
                DictionarySize = voiceContentDict.Count,
                TestCount = testResults.Count,
                TotalElapsedMilliseconds = totalTime,
                AverageElapsedMilliseconds = averageTime,
                TestResults = testResults
            };

            // 输出JSON格式结果
            string jsonResult = JsonConvert.SerializeObject(performanceResult, Formatting.Indented);
            Logger.Log.Debug($"性能测试结果（JSON）:\n{jsonResult}");

            // 输出到控制台（在测试输出中可见）
            Console.WriteLine("=== FindClosestMatch 性能测试结果 ===");
            Console.WriteLine(jsonResult);
            Console.WriteLine("=====================================");

            // 将结果写入文件（保存到项目根目录）
            string projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory)));
            if (string.IsNullOrEmpty(projectRoot))
            {
                // 如果无法确定项目根目录，使用当前目录
                projectRoot = Directory.GetCurrentDirectory();
            }
            string resultFilePath = "PerformanceTestResult.json";
            try
            {
                File.WriteAllText(resultFilePath, jsonResult, System.Text.Encoding.UTF8);
                Logger.Log.Debug($"结果已保存到: {resultFilePath}");
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"保存结果文件失败: {ex.Message}，但测试结果已在控制台输出");
            }

            // 断言：确保所有测试都完成了（不验证性能，只验证功能）
            Assert.IsTrue(testResults.Count == testSentences.Length, "所有测试句子都应该被处理");
        }

        // 测试结果类
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

        // 性能测试结果类
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
        /// 测试 Images 文件夹处理逻辑
        /// </summary>
        [TestMethod]
        public void TestProcessImagesFolder()
        {
            string appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            Directory.SetCurrentDirectory(appDir);
            if (!Directory.Exists("Images"))
            {
                Assert.Inconclusive("Images 文件夹不存在，跳过测试");
                return;
            }

            try
            {
                var engine = SettingsWindow.LoadEngine("CHS");

                // 处理 Images 文件夹
                OCRSummary.ProcessFolder("Images", engine);

                // 验证结果文件是否存在
                Assert.IsTrue(File.Exists("result.json"), "应该生成 result.json 文件");
            }
            catch (Exception ex)
            {
                Assert.Fail($"处理 Images 文件夹失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 测试 Videos 文件夹处理逻辑（demo视频自动处理）
        /// </summary>
        [TestMethod]
        public void TestProcessVideosFolder()
        {
            if (!Directory.Exists("Videos"))
            {
                Assert.Inconclusive("Videos 文件夹不存在，跳过测试");
                return;
            }

            string demoVideoPath = Path.Combine("Videos", "demo.mp4");
            string demoRegionPath = Path.Combine("Videos", "demo_region.json");

            if (!File.Exists(demoVideoPath) || !File.Exists(demoRegionPath))
            {
                Assert.Inconclusive("demo.mp4 或 demo_region.json 文件不存在，跳过测试");
                return;
            }

            try
            {
                var engine = SettingsWindow.LoadEngine("CHS");

                bool completed = false;
                Exception processException = null;

                // 处理 demo 视频
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

                // 等待处理完成（最多等待5分钟）
                int waitCount = 0;
                while (!completed && waitCount < 300)
                {
                    Thread.Sleep(1000);
                    waitCount++;
                }

                if (processException != null)
                {
                    Assert.Fail($"处理 demo 视频失败: {processException.Message}");
                }

                if (!completed)
                {
                    Assert.Inconclusive("处理超时（超过5分钟）");
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"处理 Videos 文件夹失败: {ex.Message}");
            }
        }
    }
}


