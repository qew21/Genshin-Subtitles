using System;
using System.IO;
using GI_Subtitles.Services.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestLocalVoiceFileResolver
    {
        private const string Md5 = "2f11ca2b7d9f3b4042077c804da21678";

        [TestMethod]
        public void ExistingMappedFile_IsResolvedRelativeToApplicationDataRoot()
        {
            WithTempRoot(root =>
            {
                string gameDirectory = Path.Combine(root, "Genshin");
                string audioDirectory = Path.Combine(gameDirectory, "7.0", "speaker");
                Directory.CreateDirectory(audioDirectory);
                string audioPath = Path.Combine(audioDirectory, Md5 + ".mp3");
                File.WriteAllBytes(audioPath, new byte[] { (byte)'I', (byte)'D', (byte)'3' });
                File.WriteAllText(
                    Path.Combine(gameDirectory, "md5_mapping.json"),
                    "{\"" + Md5 + "\":\"genshin/7.0/speaker/" + Md5 + ".mp3\"}");

                var resolver = new LocalVoiceFileResolver(root, "Genshin");

                Assert.IsTrue(resolver.TryResolve(Md5.ToUpperInvariant(), out string resolved));
                Assert.IsTrue(string.Equals(
                    audioPath, resolved, StringComparison.OrdinalIgnoreCase));
            });
        }

        [TestMethod]
        public void MissingMappedFile_FallsBack()
        {
            WithTempRoot(root =>
            {
                string gameDirectory = Path.Combine(root, "Genshin");
                Directory.CreateDirectory(gameDirectory);
                File.WriteAllText(
                    Path.Combine(gameDirectory, "md5_mapping.json"),
                    "{\"" + Md5 + "\":\"genshin/missing.mp3\"}");

                var resolver = new LocalVoiceFileResolver(root, "Genshin");

                Assert.IsFalse(resolver.TryResolve(Md5, out string resolved));
                Assert.IsNull(resolved);
            });
        }

        [TestMethod]
        public void MappingOutsideApplicationDataRoot_IsRejected()
        {
            WithTempRoot(root =>
            {
                string gameDirectory = Path.Combine(root, "Genshin");
                Directory.CreateDirectory(gameDirectory);
                File.WriteAllText(
                    Path.Combine(gameDirectory, "md5_mapping.json"),
                    "{\"" + Md5 + "\":\"../outside.mp3\"}");

                var resolver = new LocalVoiceFileResolver(root, "Genshin");

                Assert.IsFalse(resolver.TryResolve(Md5, out _));
            });
        }

        private static void WithTempRoot(Action<string> assertion)
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                assertion(root);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
