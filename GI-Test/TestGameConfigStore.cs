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
    }
}
