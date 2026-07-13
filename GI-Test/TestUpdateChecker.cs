using GI_Subtitles.Services.Update;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestUpdateChecker
    {
        private const string ValidManifest = @"{
          ""schemaVersion"": 1,
          ""version"": ""1.6.11"",
          ""publishedAt"": ""2026-07-13T12:00:00Z"",
          ""channel"": ""stable"",
          ""rollout"": 100,
          ""minimumVersion"": null,
          ""releaseNotes"": ""Fix update behavior"",
          ""assets"": {
            ""windows-x64-msi"": {
              ""url"": ""https://2langs.com/data/GI-Subtitles-1.6.11.msi"",
              ""sha256"": ""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"",
              ""size"": 83238912
            }
          }
        }";

        [TestMethod]
        public void NewManifestVersion_IsOfferedOnlyWhenNotIgnored()
        {
            var manifest = UpdateChecker.ParseManifest(ValidManifest);

            Assert.IsNotNull(manifest);
            Assert.IsTrue(UpdateChecker.ShouldOfferUpdate(manifest, "1.6.10.0", null, "installation-a"));
            Assert.IsFalse(UpdateChecker.ShouldOfferUpdate(manifest, "1.6.10.0", "1.6.11", "installation-a"));
            Assert.IsFalse(UpdateChecker.ShouldOfferUpdate(manifest, "1.6.11.0", null, "installation-a"));
        }

        [TestMethod]
        public void ZeroRollout_DoesNotOfferUpdate()
        {
            var manifest = UpdateChecker.ParseManifest(ValidManifest);
            manifest.Rollout = 0;

            Assert.IsFalse(UpdateChecker.ShouldOfferUpdate(manifest, "1.6.10.0", null, "installation-a"));
        }
    }
}
