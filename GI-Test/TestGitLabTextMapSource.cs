using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GI_Subtitles.Common;
using GI_Subtitles.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestGitLabTextMapSource
    {
        private const string DirectoryListing = @"[
            {""name"":""TextMapRU_1.json"",""path"":""TextMap/TextMapRU_1.json"",""type"":""blob""},
            {""name"":""TextMap_MediumEN.json"",""path"":""TextMap/TextMap_MediumEN.json"",""type"":""blob""},
            {""name"":""TextMapCHS.json"",""path"":""TextMap/TextMapCHS.json"",""type"":""blob""},
            {""name"":""TextMap_MediumRU_0.json"",""path"":""TextMap/TextMap_MediumRU_0.json"",""type"":""blob""},
            {""name"":""TextMapEN.json"",""path"":""TextMap/TextMapEN.json"",""type"":""blob""},
            {""name"":""TextMapRU_0.json"",""path"":""TextMap/TextMapRU_0.json"",""type"":""blob""},
            {""name"":""TextMap_MediumCHS.json"",""path"":""TextMap/TextMap_MediumCHS.json"",""type"":""blob""},
            {""name"":""TextMap_MediumRU_1.json"",""path"":""TextMap/TextMap_MediumRU_1.json"",""type"":""blob""},
            {""name"":""TextMapRU.json"",""path"":""TextMap/TextMapRU.json"",""type"":""blob""},
            {""name"":""TextMapMainRU.json"",""path"":""TextMap/TextMapMainRU.json"",""type"":""blob""},
            {""name"":""TextMapEN.json"",""path"":""README/TextMapEN.json"",""type"":""tree""}
        ]";

        [TestMethod]
        public async Task DiscoversSingleFileLinksForEnglishAndSimplifiedChinese()
        {
            TextMapDownloadPlan english = await DiscoverAsync("EN");
            CollectionAssert.AreEqual(new[]
            {
                "https://gitlab.com/Dimbreath/animegamedata2/-/raw/main/TextMap/TextMapEN.json?inline=false"
            }, ToUrls(english.MainFiles));
            CollectionAssert.AreEqual(new[]
            {
                "https://gitlab.com/Dimbreath/animegamedata2/-/raw/main/TextMap/TextMap_MediumEN.json?inline=false"
            }, ToUrls(english.MediumFiles));

            TextMapDownloadPlan chinese = await DiscoverAsync("CHS");
            CollectionAssert.AreEqual(new[]
            {
                "https://gitlab.com/Dimbreath/animegamedata2/-/raw/main/TextMap/TextMapCHS.json?inline=false"
            }, ToUrls(chinese.MainFiles));
            CollectionAssert.AreEqual(new[]
            {
                "https://gitlab.com/Dimbreath/animegamedata2/-/raw/main/TextMap/TextMap_MediumCHS.json?inline=false"
            }, ToUrls(chinese.MediumFiles));
        }

        [TestMethod]
        public async Task DiscoversAndOrdersAllRussianMainAndMediumParts()
        {
            TextMapDownloadPlan russian = await DiscoverAsync("RU");

            CollectionAssert.AreEqual(new[]
            {
                "https://gitlab.com/Dimbreath/animegamedata2/-/raw/main/TextMap/TextMapRU_0.json?inline=false",
                "https://gitlab.com/Dimbreath/animegamedata2/-/raw/main/TextMap/TextMapRU_1.json?inline=false"
            }, ToUrls(russian.MainFiles));
            CollectionAssert.AreEqual(new[]
            {
                "https://gitlab.com/Dimbreath/animegamedata2/-/raw/main/TextMap/TextMap_MediumRU_0.json?inline=false",
                "https://gitlab.com/Dimbreath/animegamedata2/-/raw/main/TextMap/TextMap_MediumRU_1.json?inline=false"
            }, ToUrls(russian.MediumFiles));
        }

        private static async Task<TextMapDownloadPlan> DiscoverAsync(string language)
        {
            using (var client = new HttpClient(new DirectoryListingHandler(DirectoryListing)))
            {
                return await GitLabTextMapSource.DiscoverAsync(
                    client,
                    new Uri(GameConfigStore.GenshinTextMapFileListUrl),
                    GameConfigStore.GenshinTextMapFileUrlTemplate,
                    language);
            }
        }

        private static string[] ToUrls(System.Collections.Generic.IReadOnlyList<Uri> uris)
        {
            var urls = new string[uris.Count];
            for (int index = 0; index < uris.Count; index++)
            {
                urls[index] = uris[index].AbsoluteUri;
            }
            return urls;
        }

        private sealed class DirectoryListingHandler : HttpMessageHandler
        {
            private readonly string _json;

            public DirectoryListingHandler(string json)
            {
                _json = json;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_json, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
