using System;
using System.Linq;
using GI_Subtitles.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestEndfieldTextMapSource
    {
        [TestMethod]
        public void Manifest_DiscoversEveryLocaleChunkInDeclaredOrder()
        {
            var firstChunk = new Uri(
                "https://raw.githubusercontent.com/cmyyx/cep/main/public/game-i18n/th/000.json");
            const string manifest = @"{
                ""locales"": {
                    ""th"": {
                        ""entryCount"": 10,
                        ""chunks"": [
                            {""file"":""th/000.json"",""count"":3},
                            {""file"":""th/001.json"",""count"":3},
                            {""file"":""th/002.json"",""count"":2},
                            {""file"":""th/003.json"",""count"":2}
                        ]
                    }
                }
            }";

            string[] urls = EndfieldTextMapSource.ParseChunkUris(firstChunk, manifest)
                .Select(uri => uri.AbsoluteUri)
                .ToArray();

            CollectionAssert.AreEqual(new[]
            {
                "https://raw.githubusercontent.com/cmyyx/cep/main/public/game-i18n/th/000.json",
                "https://raw.githubusercontent.com/cmyyx/cep/main/public/game-i18n/th/001.json",
                "https://raw.githubusercontent.com/cmyyx/cep/main/public/game-i18n/th/002.json",
                "https://raw.githubusercontent.com/cmyyx/cep/main/public/game-i18n/th/003.json"
            }, urls);
        }

        [TestMethod]
        public void FirstChunk_BuildsSiblingManifestUri()
        {
            var firstChunk = new Uri(
                "https://raw.githubusercontent.com/cmyyx/cep/main/public/game-i18n/zh-CN/000.json");

            Assert.IsTrue(EndfieldTextMapSource.TryCreateManifestUri(
                firstChunk, out Uri manifestUri));
            Assert.AreEqual(
                "https://raw.githubusercontent.com/cmyyx/cep/main/public/game-i18n/manifest.json",
                manifestUri.AbsoluteUri);
        }

        [TestMethod]
        public void ManifestEntryCountMismatch_IsRejected()
        {
            var firstChunk = new Uri(
                "https://raw.githubusercontent.com/cmyyx/cep/main/public/game-i18n/en/000.json");
            const string manifest = @"{
                ""locales"": {
                    ""en"": {
                        ""entryCount"": 2,
                        ""chunks"": [{""file"":""en/000.json"",""count"":1}]
                    }
                }
            }";

            Assert.ThrowsException<InvalidOperationException>(() =>
                EndfieldTextMapSource.ParseChunkUris(firstChunk, manifest));
        }
    }
}
