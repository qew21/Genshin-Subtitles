using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GI_Subtitles.Services.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace GI_Test
{
    [TestClass]
    public class TestTextSearchLookupCache
    {
        [TestMethod]
        public void RepeatedSearchAndNewWindow_ReuseTheSameMatcher()
        {
            WithMaps((directory, lookup) =>
            {
                var first = lookup.LoadMatcher("GameA", "CHS", "EN");
                Assert.IsFalse(first.LoadedTargetFromCache);
                Assert.IsTrue(lookup.TryGetCachedMatcher("GameA", "CHS", "EN", out var cached));
                Assert.AreSame(first, cached);
                Assert.AreSame(first, lookup.LoadMatcher("GameA", "CHS", "EN"));
                Assert.AreSame(first, new TextSearchLookup(directory).LoadMatcher("GameA", "CHS", "EN"));
            });
        }

        [TestMethod]
        public void SwitchingPairs_ReusesRecentEntriesAndEvictsTheLeastRecentlyUsed()
        {
            WithMaps((directory, lookup) =>
            {
                var first = lookup.LoadMatcher("GameA", "CHS", "EN");
                var second = lookup.LoadMatcher("GameB", "CHS", "EN");
                Assert.AreSame(first, lookup.LoadMatcher("GameA", "CHS", "EN"));
                lookup.LoadMatcher("GameC", "CHS", "EN");
                Assert.IsTrue(lookup.TryGetCachedMatcher("GameA", "CHS", "EN", out var stillRecent));
                Assert.AreSame(first, stillRecent);
                Assert.IsFalse(lookup.TryGetCachedMatcher("GameB", "CHS", "EN", out _));

                // An evicted pair represents the same cold-memory path as a restart.
                var restored = lookup.LoadMatcher("GameB", "CHS", "EN");
                Assert.AreNotSame(second, restored);
                Assert.IsTrue(restored.LoadedTargetFromCache);
                var results = restored.FindDialogueMatches("WELCOME TO", 50, out bool hasMore);
                Assert.HasCount(2, results, "A cached reverse index must retain shared translations.");
                Assert.IsFalse(hasMore);
            });
        }

        [TestMethod]
        public void ConcurrentLoads_OnlyPublishOneMatcherForThePair()
        {
            WithMaps((directory, lookup) =>
            {
                Task<BilingualTextSearchMatcher> first = Task.Run(() => lookup.LoadMatcher("GameA", "CHS", "EN"));
                Task<BilingualTextSearchMatcher> second = Task.Run(() => lookup.LoadMatcher("GameA", "CHS", "EN"));
                Task.WaitAll(first, second);
                Assert.AreSame(first.Result, second.Result);
            });
        }

        [TestMethod]
        public void UpdatedLanguagePack_InvalidatesMemoryAndDiskAndRebuildsTheDerivedPair()
        {
            WithMaps((directory, lookup) =>
            {
                var original = lookup.LoadMatcher("GameA", "CHS", "EN");
                string gameDirectory = Path.Combine(directory, "GameA");
                WriteMap(gameDirectory, "EN", new Dictionary<string, string>
                {
                    { "1", "The updated dialogue is now available." },
                    { "2", "Another newly updated dialogue." }
                });
                Assert.IsFalse(lookup.TryGetCachedMatcher("GameA", "CHS", "EN", out _));

                var updated = lookup.LoadMatcher("GameA", "CHS", "EN");
                Assert.AreNotSame(original, updated);
                Assert.IsFalse(updated.LoadedTargetFromCache);
                var matches = updated.FindDialogueMatches("updated dialogue is", 50, out _);
                Assert.HasCount(1, matches);
                Assert.AreEqual("欢迎来到冒险家协会。", matches[0].Key);
                Assert.AreEqual("The updated dialogue is now available.", matches[0].Value);
                Assert.IsEmpty(updated.FindDialogueMatches("Welcome to", 50, out _));
            });
        }

        [TestMethod]
        public void UpdatedLanguagePack_AfterEvictionDoesNotUseAnOldDiskIndex()
        {
            WithMaps((directory, lookup) =>
            {
                lookup.LoadMatcher("GameA", "CHS", "EN");
                lookup.LoadMatcher("GameB", "CHS", "EN");
                lookup.LoadMatcher("GameC", "CHS", "EN");
                WriteMap(Path.Combine(directory, "GameA"), "CHS", new Dictionary<string, string>
                {
                    { "1", "这是更新后的第一句完整对话。" },
                    { "2", "这是更新后的第二句完整对话。" }
                });

                var restored = new TextSearchLookup(directory).LoadMatcher("GameA", "CHS", "EN");
                Assert.IsFalse(restored.LoadedTargetFromCache);
                var matches = restored.FindDialogueMatches("Welcome to", 50, out _);
                Assert.HasCount(2, matches);
                foreach (var match in matches)
                {
                    StringAssert.StartsWith(match.Key, "这是更新后的");
                }
            });
        }

        [TestMethod]
        public void MissingOrInvalidUpdatedMap_FailsInsteadOfUsingCachedData()
        {
            WithMaps((directory, lookup) =>
            {
                lookup.LoadMatcher("GameA", "CHS", "EN");
                string outputPath = Path.Combine(directory, "GameA", "TextMapEN.json");
                File.Delete(outputPath);
                var missing = Assert.ThrowsExactly<FileNotFoundException>(() =>
                    lookup.LoadMatcher("GameA", "CHS", "EN"));
                Assert.AreEqual(outputPath, missing.FileName);

                File.WriteAllText(outputPath, "invalid updated language map");
                Assert.ThrowsExactly<JsonReaderException>(() => lookup.LoadMatcher("GameA", "CHS", "EN"));
            });
        }

        private static void WithMaps(Action<string, TextSearchLookup> assertion)
        {
            string directory = Path.Combine(Path.GetTempPath(), "GI-TextSearch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                foreach (string game in new[] { "GameA", "GameB", "GameC" })
                {
                    string gameDirectory = Path.Combine(directory, game);
                    Directory.CreateDirectory(gameDirectory);
                    WriteMap(gameDirectory, "CHS", new Dictionary<string, string>
                    {
                        { "1", "欢迎来到冒险家协会。" },
                        { "2", "欢迎光临冒险家协会。" }
                    });
                    WriteMap(gameDirectory, "EN", new Dictionary<string, string>
                    {
                        { "1", "Welcome to the Adventurers' Guild." },
                        { "2", "Welcome to the Adventurers' Guild." }
                    });
                }
                assertion(directory, new TextSearchLookup(directory));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void WriteMap(string gameDirectory, string language, Dictionary<string, string> content)
        {
            File.WriteAllText(Path.Combine(gameDirectory, "TextMap" + language + ".json"),
                JsonConvert.SerializeObject(content));
        }
    }
}
