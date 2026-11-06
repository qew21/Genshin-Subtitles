using System.Collections.Generic;
using GI_Subtitles.Services.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestOptimizedMatcherMultilineFallback
    {
        [TestMethod]
        [DataRow("运输局列车员\n这事您就是找我，我也无能为力。呃，要不您找购入的商店退货试试看呗？\nTransport Bureau Conductor", "这事您就是找我，我也无能为力。呃，要不您找购入的商店退货试试看呗？", "Transport Bureau Conductor")]
        [DataRow("啊？你这是什么态度？\nC\nTransport Bureau Conductor", "啊？你这是什么态度？", "")]
        [DataRow("运输局列车员\n第一行，\n第二行。\nTransport Bureau Conductor", "第一行，第二行。", "Transport Bureau Conductor")]
        public void EnglishOverlayTail_DoesNotPreventExactDialogueMatch(string ocrText, string expectedKey, string expectedHeader)
        {
            var matcher = new OptimizedMatcher(new Dictionary<string, string>
            {
                { "运输局列车员", "Transport Bureau Conductor" },
                { expectedKey, "expected dialogue translation" }
            }, "CHS");

            var result = matcher.FindMatchWithHeaderSeparated(ocrText, out string key);

            Assert.AreEqual("expected dialogue translation", result.Content);
            Assert.AreEqual(expectedKey, key);
            Assert.AreEqual(expectedHeader, result.Header);
        }

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
