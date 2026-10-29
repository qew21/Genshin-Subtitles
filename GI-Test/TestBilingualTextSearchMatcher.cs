using System.Collections.Generic;
using GI_Subtitles.Services.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestBilingualTextSearchMatcher
    {
        [TestMethod]
        [DataRow("c", "The cat is sleeping peacefully.")]
        [DataRow("ca", "The cat is sleeping peacefully.")]
        [DataRow("cat", "The cat is sleeping peacefully.")]
        [DataRow("CAT", "The cat is sleeping peacefully.")]
        [DataRow("Hi!", "Hi!")]
        [DataRow("猫", "The cat is sleeping peacefully.")]
        [DataRow("你好", "Hi!")]
        public void ShortKeywords_SearchCompleteDialogueInEitherLanguage(string query, string expectedSource)
        {
            var content = new Dictionary<string, string>
            {
                { "The cat is sleeping peacefully.", "猫正在安静地睡觉。" },
                { "Hi!", "你好！" }
            };
            var matcher = new BilingualTextSearchMatcher(new OptimizedMatcher(content, "EN"), "CHS");
            var results = matcher.FindDialogueMatches(query, 50, out bool hasMore);

            Assert.HasCount(1, results);
            Assert.AreEqual(expectedSource, results[0].Key);
            Assert.AreEqual(content[expectedSource], results[0].Value);
            Assert.IsFalse(hasMore);
        }

        [TestMethod]
        public void ShortKeyword_KeepsExactDialogueFirstAndReportsTruncatedResults()
        {
            var matcher = new OptimizedMatcher(new Dictionary<string, string>
            {
                { "The cat is sleeping peacefully.", "长句译文" },
                { "A cat is here.", "短句译文" },
                { "Cat!", "精确匹配译文" },
                { "cat", "单独的词条" }
            }, "EN");
            var limited = matcher.FindDialogueMatches("cat", 2, out bool hasMore);
            Assert.HasCount(2, limited);
            Assert.AreEqual("Cat!", limited[0].Key);
            Assert.AreEqual("A cat is here.", limited[1].Key);
            Assert.IsTrue(hasMore);

            var all = matcher.FindDialogueMatches("cat", 3, out bool moreAll);
            Assert.HasCount(3, all);
            Assert.IsFalse(moreAll);
        }

        [TestMethod]
        public void NormalizedEmptyQuery_DoesNotMatchEveryEnglishDialogue()
        {
            var matcher = new OptimizedMatcher(new Dictionary<string, string>
            {
                { "The cat is sleeping peacefully.", "猫正在安静地睡觉。" }
            }, "EN");
            Assert.IsEmpty(matcher.FindDialogueMatches("---", 50, out bool hasMore));
            Assert.IsFalse(hasMore);
        }

        [TestMethod]
        public void EitherLanguage_ReturnsBothTextsInSelectedOrder()
        {
            const string source = "冒险家协会全年无休。";
            const string target = "The Adventurers' Guild is open all year round.";
            var matcher = CreateMatcher(new Dictionary<string, string> { { source, target } });

            // Consecutive lines in a pasted paragraph can use different languages.
            foreach (string line in new[] { "冒险家协会", "GUILD IS OPEN" })
            {
                var results = matcher.FindDialogueMatches(line, 50, out bool hasMore);
                Assert.HasCount(1, results);
                Assert.AreEqual(source, results[0].Key);
                Assert.AreEqual(target, results[0].Value);
                Assert.IsFalse(hasMore);
            }
        }

        [TestMethod]
        public void OutputLanguageQuery_MatchesAcrossTextMapLineBreakMarkers()
        {
            const string source = "冒险家协会全年无休。";
            const string target = "The Adventurers'\\nGuild is open all year round.";
            var matcher = CreateMatcher(new Dictionary<string, string> { { source, target } });

            var results = matcher.FindDialogueMatches("Adventurers' Guild is open", 50, out bool hasMore);
            Assert.HasCount(1, results);
            Assert.AreEqual(source, results[0].Key);
            Assert.AreEqual(target, results[0].Value);
            Assert.IsFalse(hasMore);
        }

        [TestMethod]
        public void OutputLanguageQuery_PreservesEnglishToChineseColumnOrder()
        {
            const string source = "The Adventurers' Guild is open all year round.";
            const string target = "冒险家协会全年无休。";
            var matcher = new BilingualTextSearchMatcher(
                new OptimizedMatcher(new Dictionary<string, string> { { source, target } }, "EN"), "CHS");

            var results = matcher.FindDialogueMatches("冒险家协会", 50, out bool hasMore);
            Assert.HasCount(1, results);
            Assert.AreEqual(source, results[0].Key);
            Assert.AreEqual(target, results[0].Value);
            Assert.IsFalse(hasMore);
        }

        [TestMethod]
        public void SharedTranslation_PreservesAllSourceDialoguesAndReportsTruncation()
        {
            const string target = "Welcome to the Adventurers' Guild.";
            var content = new Dictionary<string, string>
            {
                { "欢迎来到冒险家协会。", target },
                { "欢迎光临冒险家协会。", target },
                { "冒险家协会欢迎你。", target }
            };
            var matcher = CreateMatcher(content);

            var all = matcher.FindDialogueMatches("Welcome to", 50, out bool moreAll);
            Assert.HasCount(3, all);
            Assert.IsFalse(moreAll);
            CollectionAssert.AreEquivalent(new List<KeyValuePair<string, string>>(content), all);

            var limited = matcher.FindDialogueMatches("Welcome to", 2, out bool moreLimited);
            Assert.HasCount(2, limited);
            Assert.IsTrue(moreLimited);
        }

        [TestMethod]
        public void BothSidesMatch_DeduplicatesTheSameBilingualPair()
        {
            const string text = "This dialogue is shared by both languages.";
            var matcher = CreateMatcher(new Dictionary<string, string> { { text, text } });

            var results = matcher.FindDialogueMatches("dialogue", 1, out bool hasMore);
            Assert.HasCount(1, results);
            Assert.AreEqual(text, results[0].Key);
            Assert.AreEqual(text, results[0].Value);
            Assert.IsFalse(hasMore);
        }

        [TestMethod]
        public void ResultCap_AppliesAcrossBothDirectionsAfterDeduplication()
        {
            var matcher = CreateMatcher(new Dictionary<string, string>
            {
                { "common source dialogue.", "第一句中文译文。" },
                { "第二句中文原文。", "common target dialogue." }
            });

            var all = matcher.FindDialogueMatches("common", 2, out bool moreAll);
            Assert.HasCount(2, all);
            Assert.IsFalse(moreAll);
            Assert.AreEqual("common source dialogue.", all[0].Key);
            Assert.AreEqual("第二句中文原文。", all[1].Key);

            var limited = matcher.FindDialogueMatches("common", 1, out bool moreLimited);
            Assert.HasCount(1, limited);
            Assert.IsTrue(moreLimited);
        }

        [TestMethod]
        public void UnmatchedAndEmptyQueries_ReturnNoPairs()
        {
            var matcher = CreateMatcher(new Dictionary<string, string>
            {
                { "冒险家协会全年无休。", "The Adventurers' Guild is open all year round." }
            });
            foreach (string query in new[] { "不存在的对话", "no such dialogue", "", " " })
            {
                var results = matcher.FindDialogueMatches(query, 50, out bool hasMore);
                Assert.IsEmpty(results);
                Assert.IsFalse(hasMore);
            }
        }

        private static BilingualTextSearchMatcher CreateMatcher(Dictionary<string, string> content)
        {
            return new BilingualTextSearchMatcher(new OptimizedMatcher(content, "CHS"), "EN");
        }
    }
}
