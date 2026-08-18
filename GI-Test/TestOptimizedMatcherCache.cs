using System;
using System.Collections.Generic;
using System.IO;
using GI_Subtitles.Services.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace GI_Test
{
    [TestClass]
    public class TestOptimizedMatcherCache
    {
        [TestMethod]
        public void SavedMatcher_PreservesMatchResultsAndPostingOrder()
        {
            WithTempDirectory(directory =>
            {
                string contentPath = Path.Combine(directory, "content.json");
                File.WriteAllText(contentPath, "{\"你好世界\":\"hello\",\"你好旅行者\":\"hello traveler\",\"世界任务\":\"world quest\"}");
                var content = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(contentPath));
                var original = new OptimizedMatcher(content, "CHS");
                string fingerprint = OptimizedMatcherCache.CreateFingerprint(contentPath, "CHS");
                string cachePath = OptimizedMatcherCache.GetCachePath(contentPath);

                OptimizedMatcherCache.Save(cachePath, fingerprint, original);

                Assert.IsTrue(OptimizedMatcherCache.TryLoad(cachePath, fingerprint, out OptimizedMatcher restored));
                AssertMatchEqual(original, restored, "你好世界");
                AssertMatchEqual(original, restored, "你好旅行");
                AssertMatchEqual(original, restored, "世界任");
                Assert.IsFalse(OptimizedMatcherCache.TryLoad(cachePath, "wrong-fingerprint", out _));
            });
        }

        [TestMethod]
        public void MatchDataLoader_UsesCacheOnSecondLoad()
        {
            WithTempDirectory(directory =>
            {
                string inputPath = Path.Combine(directory, "TextMapCHS.json");
                string outputPath = Path.Combine(directory, "TextMapEN.json");
                string contentPath = Path.Combine(directory, "TextMapCHS_TextMapEN.json");
                File.WriteAllText(inputPath, "{\"1\":\"你好世界\",\"2\":\"世界任务\"}");
                File.WriteAllText(outputPath, "{\"1\":\"hello\",\"2\":\"world quest\"}");

                LoadedMatchData first = MatchDataLoader.Load(
                    inputPath, outputPath, contentPath, "CHS", "Traveler", false);
                LoadedMatchData second = MatchDataLoader.Load(
                    inputPath, outputPath, contentPath, "CHS", "Traveler", false);

                Assert.IsFalse(first.LoadedFromMatcherCache);
                Assert.IsTrue(second.LoadedFromMatcherCache);
                Assert.AreEqual(first.Content.Count, second.Content.Count);
                AssertMatchEqual(first.Matcher, second.Matcher, "你好世界");
            });
        }

        [TestMethod]
        public void CorruptCache_FallsBackToColdBuild()
        {
            WithTempDirectory(directory =>
            {
                string inputPath = Path.Combine(directory, "TextMapCHS.json");
                string outputPath = Path.Combine(directory, "TextMapEN.json");
                string contentPath = Path.Combine(directory, "TextMapCHS_TextMapEN.json");
                File.WriteAllText(inputPath, "{\"1\":\"测试文本\"}");
                File.WriteAllText(outputPath, "{\"1\":\"test text\"}");
                File.WriteAllText(contentPath, "{\"测试文本\":\"test text\"}");
                File.WriteAllBytes(OptimizedMatcherCache.GetCachePath(contentPath), new byte[] { 1, 2, 3 });

                LoadedMatchData result = MatchDataLoader.Load(
                    inputPath, outputPath, contentPath, "CHS", "Traveler", false);

                Assert.IsFalse(result.LoadedFromMatcherCache);
                AssertMatch(result.Matcher, "测试文本", "测试文本", "test text");
            });
        }

        [TestMethod]
        public void ChangedContentJson_InvalidatesMatcherCache()
        {
            WithTempDirectory(directory =>
            {
                string inputPath = Path.Combine(directory, "TextMapCHS.json");
                string outputPath = Path.Combine(directory, "TextMapEN.json");
                string contentPath = Path.Combine(directory, "TextMapCHS_TextMapEN.json");
                File.WriteAllText(inputPath, "{\"1\":\"第一版文本\"}");
                File.WriteAllText(outputPath, "{\"1\":\"version one\"}");

                LoadedMatchData first = MatchDataLoader.Load(
                    inputPath, outputPath, contentPath, "CHS", "Traveler", false);
                File.WriteAllText(contentPath, "{\"第二版文本\":\"version two\"}");
                LoadedMatchData changed = MatchDataLoader.Load(
                    inputPath, outputPath, contentPath, "CHS", "Traveler", false);

                Assert.IsFalse(first.LoadedFromMatcherCache);
                Assert.IsFalse(changed.LoadedFromMatcherCache);
                AssertMatch(changed.Matcher, "第二版文本", "第二版文本", "version two");
            });
        }

        private static void AssertMatchEqual(
            OptimizedMatcher expectedMatcher,
            OptimizedMatcher actualMatcher,
            string input)
        {
            string expected = expectedMatcher.FindClosestMatch(input, out string expectedKey);
            string actual = actualMatcher.FindClosestMatch(input, out string actualKey);
            Assert.AreEqual(expectedKey, actualKey);
            Assert.AreEqual(expected, actual);
        }

        private static void AssertMatch(
            OptimizedMatcher matcher,
            string input,
            string expectedKey,
            string expectedValue)
        {
            string actual = matcher.FindClosestMatch(input, out string actualKey);
            Assert.AreEqual(expectedKey, actualKey);
            Assert.AreEqual(expectedValue, actual);
        }

        private static void WithTempDirectory(Action<string> assertion)
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                assertion(directory);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
