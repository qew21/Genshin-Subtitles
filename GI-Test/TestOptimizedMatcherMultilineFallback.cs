using System.Collections.Generic;
using GI_Subtitles.Services.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestOptimizedMatcherMultilineFallback
    {
        [TestMethod]
        public void FindMatchWithHeaderSeparated_FallsBackToWholeOcrBlock()
        {
            const string ocrText = "称呼文字\n正文甲乙丙X";
            const string expectedKey = "称呼文字正文甲乙丙丁";
            const string expectedContent = "whole subtitle translation";
            var matcher = new OptimizedMatcher(
                new Dictionary<string, string> { { expectedKey, expectedContent } },
                "CHS");

            var result = matcher.FindMatchWithHeaderSeparated(ocrText, out string matchedKey);

            Assert.AreEqual(expectedContent, result.Content);
            Assert.AreEqual(expectedKey, matchedKey);
            Assert.AreEqual("", result.Header);
        }

        [TestMethod]
        public void ReportedOcrText_MatchesWholeSentenceCandidate()
        {
            const string ocrText = "您同样是\n位出色的冒险家，我所走过的旅程\n定可以在您这里得到回响:";
            const string expectedKey = "您同样是一位出色的冒险家，我所走过的旅程，一定可以在您这里得到回响。";
            const string expectedContent = "expected translation";
            var matcher = new OptimizedMatcher(
                new Dictionary<string, string> { { expectedKey, expectedContent } },
                "CHS");

            var result = matcher.FindMatchWithHeaderSeparated(ocrText, out string matchedKey);

            Assert.AreEqual(expectedContent, result.Content);
            Assert.AreEqual(expectedKey, matchedKey);
        }
    }
}
