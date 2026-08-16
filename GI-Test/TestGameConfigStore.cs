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
    }
}
