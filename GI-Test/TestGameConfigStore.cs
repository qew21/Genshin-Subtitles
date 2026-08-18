using System;
using System.IO;
using GI_Subtitles.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace GI_Test
{
    [TestClass]
    public class TestGameConfigStore
    {
        [TestMethod]
        public void MissingTargetConfig_CreatesFreshConfigInsteadOfReusingPreviousGame()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            try
            {
                string genshinPath = Path.Combine(tempDirectory, "Genshin.json");
                string starRailPath = Path.Combine(tempDirectory, "StarRail.json");
                var genshinConfig = new GameConfig { RepoUrl = "genshin-repository" };
                File.WriteAllText(genshinPath, JsonConvert.SerializeObject(genshinConfig));

                GameConfig first = GameConfigStore.LoadOrCreate(
                    genshinPath,
                    () => new GameConfig { RepoUrl = "unexpected-default" });
                GameConfig second = GameConfigStore.LoadOrCreate(
                    starRailPath,
                    () => new GameConfig { RepoUrl = "starrail-default" });

                Assert.AreEqual("genshin-repository", first.RepoUrl);
                Assert.AreEqual("starrail-default", second.RepoUrl);
                Assert.AreNotSame(first, second);
                Assert.IsTrue(File.Exists(starRailPath));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [TestMethod]
        public void LegacyWutheringRepository_IsMigratedToArikatsuHead()
        {
            var config = new GameConfig
            {
                RepoUrl = "https://github.com/Dimbreath/WutheringData/commits/master.atom",
                RepoType = "GitHubAtom",
                InputUrlTemplate = "https://raw.githubusercontent.com/Dimbreath/WutheringData/refs/heads/master/TextMap/{Language}/MultiText.json",
                OutputUrlTemplate = "https://raw.githubusercontent.com/Dimbreath/WutheringData/refs/heads/master/TextMap/{Language}/MultiText.json"
            };

            Assert.IsTrue(GameConfigStore.MigrateWutheringRepository(config));
            Assert.AreEqual(GameConfigStore.WutheringRepoUrl, config.RepoUrl);
            Assert.AreEqual(GameConfigStore.WutheringTextMapUrlTemplate, config.InputUrlTemplate);
            Assert.AreEqual(GameConfigStore.WutheringTextMapUrlTemplate, config.OutputUrlTemplate);
            Assert.IsFalse(GameConfigStore.MigrateWutheringRepository(config));
        }

        [TestMethod]
        public void CustomWutheringRepository_IsPreserved()
        {
            var config = new GameConfig
            {
                RepoUrl = "https://example.com/custom.atom",
                InputUrlTemplate = "https://example.com/{Language}.json",
                OutputUrlTemplate = "https://example.com/{Language}.json"
            };

            Assert.IsFalse(GameConfigStore.MigrateWutheringRepository(config));
            Assert.AreEqual("https://example.com/custom.atom", config.RepoUrl);
            Assert.AreEqual("https://example.com/{Language}.json", config.InputUrlTemplate);
        }

        [TestMethod]
        public void LegacyEndfieldRepository_IsMigratedToCepChunks()
        {
            var config = new GameConfig
            {
                RepoUrl = "https://github.com/XiaBei-cy/EndfieldData/commits/master.atom",
                RepoType = "GitHubAtom",
                InputUrlTemplate = "https://raw.githubusercontent.com/XiaBei-cy/EndfieldData/refs/heads/master/i18n/I18nTextTable_{Language}.json",
                OutputUrlTemplate = "https://raw.githubusercontent.com/XiaBei-cy/EndfieldData/refs/heads/master/i18n/I18nTextTable_{Language}.json",
                LanguageMapping = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["CHS"] = "CN",
                    ["PT"] = "PT"
                }
            };

            Assert.IsTrue(GameConfigStore.MigrateEndfieldRepository(config));
            Assert.AreEqual(GameConfigStore.EndfieldRepoUrl, config.RepoUrl);
            Assert.AreEqual(GameConfigStore.EndfieldTextMapUrlTemplate, config.InputUrlTemplate);
            Assert.AreEqual("zh-CN", config.LanguageMapping["CHS"]);
            Assert.AreEqual("zh-TW", config.LanguageMapping["CHT"]);
            Assert.AreEqual("pt-BR", config.LanguageMapping["PT"]);
            Assert.AreEqual("es-MX", config.LanguageMapping["ES"]);
            Assert.IsFalse(GameConfigStore.MigrateEndfieldRepository(config));
        }

        [TestMethod]
        public void CustomEndfieldRepository_IsPreserved()
        {
            var config = new GameConfig
            {
                RepoUrl = "https://example.com/custom.atom",
                InputUrlTemplate = "https://example.com/{Language}.json",
                OutputUrlTemplate = "https://example.com/{Language}.json"
            };

            Assert.IsFalse(GameConfigStore.MigrateEndfieldRepository(config));
            Assert.AreEqual("https://example.com/custom.atom", config.RepoUrl);
            Assert.AreEqual("https://example.com/{Language}.json", config.InputUrlTemplate);
        }

        [TestMethod]
        public void LegacyWutheringCache_IsMigratedAndPersisted()
        {
            AssertLegacyCacheIsMigratedAndPersisted(
                "Wuthering",
                new GameConfig
                {
                    RepoUrl = "https://github.com/Dimbreath/WutheringData/commits/master.atom",
                    InputUrlTemplate = "https://raw.githubusercontent.com/Dimbreath/WutheringData/master/TextMap/{Language}/MultiText.json",
                    OutputUrlTemplate = "https://raw.githubusercontent.com/Dimbreath/WutheringData/master/TextMap/{Language}/MultiText.json"
                },
                persisted =>
                {
                    Assert.AreEqual(GameConfigStore.WutheringRepoUrl, persisted.RepoUrl);
                    Assert.AreEqual(GameConfigStore.WutheringTextMapUrlTemplate, persisted.InputUrlTemplate);
                    Assert.AreEqual(GameConfigStore.WutheringTextMapUrlTemplate, persisted.OutputUrlTemplate);
                });
        }

        [TestMethod]
        public void LegacyEndfieldCache_IsMigratedAndPersisted()
        {
            AssertLegacyCacheIsMigratedAndPersisted(
                "Endfield",
                new GameConfig
                {
                    RepoUrl = "https://github.com/XiaBei-cy/EndfieldData/commits/master.atom",
                    InputUrlTemplate = "https://raw.githubusercontent.com/XiaBei-cy/EndfieldData/master/i18n/I18nTextTable_{Language}.json",
                    OutputUrlTemplate = "https://raw.githubusercontent.com/XiaBei-cy/EndfieldData/master/i18n/I18nTextTable_{Language}.json"
                },
                persisted =>
                {
                    Assert.AreEqual(GameConfigStore.EndfieldRepoUrl, persisted.RepoUrl);
                    Assert.AreEqual(GameConfigStore.EndfieldTextMapUrlTemplate, persisted.InputUrlTemplate);
                    Assert.AreEqual(GameConfigStore.EndfieldTextMapUrlTemplate, persisted.OutputUrlTemplate);
                    Assert.AreEqual("zh-CN", persisted.LanguageMapping["CHS"]);
                    Assert.AreEqual("pt-BR", persisted.LanguageMapping["PT"]);
                });
        }

        private static void AssertLegacyCacheIsMigratedAndPersisted(
            string gameName,
            GameConfig legacyConfig,
            Action<GameConfig> assertPersisted)
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            try
            {
                string configPath = Path.Combine(tempDirectory, gameName + ".json");
                File.WriteAllText(configPath, JsonConvert.SerializeObject(legacyConfig));
                GameConfig loaded = GameConfigStore.LoadOrCreate(configPath, () => new GameConfig());

                Assert.IsTrue(GameConfigStore.MigrateCachedRepository(configPath, gameName, loaded));

                GameConfig persisted = JsonConvert.DeserializeObject<GameConfig>(File.ReadAllText(configPath));
                assertPersisted(persisted);
                Assert.IsFalse(GameConfigStore.MigrateCachedRepository(configPath, gameName, persisted));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }
}
