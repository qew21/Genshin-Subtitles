using System;
using System.Collections.Generic;
using GI_Subtitles;
using Microsoft.VisualStudio.TestTools.UnitTesting;
// VoiceContentHelper 在全局命名空间中，直接使用即可


namespace UnitTest
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
    }
}


