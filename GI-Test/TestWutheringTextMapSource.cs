using System;
using System.Linq;
using GI_Subtitles.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestWutheringTextMapSource
    {
        [TestMethod]
        public void DirectoryListing_DiscoversAndNaturallyOrdersAllParts()
        {
            var mainUri = new Uri(
                "https://raw.githubusercontent.com/Arikatsu/WutheringWaves_Data/HEAD/Textmaps/zh-Hans/multi_text/MultiText.json");
            const string json = @"[
                {""name"":""multi_text_10"",""type"":""dir""},
                {""name"":""skill"",""type"":""dir""},
                {""name"":""multi_text_2ndhalf"",""type"":""dir""},
                {""name"":""multi_text"",""type"":""dir""},
                {""name"":""multi_text_3"",""type"":""dir""},
                {""name"":""MultiText.json"",""type"":""file""},
                {""name"":""multi_text_1sthalf"",""type"":""dir""}
            ]";

            string[] urls = WutheringTextMapSource.ParsePartUris(mainUri, json)
                .Select(uri => uri.AbsoluteUri)
                .ToArray();

            CollectionAssert.AreEqual(new[]
            {
                mainUri.AbsoluteUri,
                mainUri.AbsoluteUri.Replace("/multi_text/", "/multi_text_1sthalf/"),
                mainUri.AbsoluteUri.Replace("/multi_text/", "/multi_text_2ndhalf/"),
                mainUri.AbsoluteUri.Replace("/multi_text/", "/multi_text_3/"),
                mainUri.AbsoluteUri.Replace("/multi_text/", "/multi_text_10/")
            }, urls);
        }

        [TestMethod]
        public void DefaultSource_BuildsGitHubContentsApiUri()
        {
            var mainUri = new Uri(
                "https://raw.githubusercontent.com/Arikatsu/WutheringWaves_Data/HEAD/Textmaps/zh-Hans/multi_text/MultiText.json");

            Assert.IsTrue(WutheringTextMapSource.TryCreateDirectoryApiUri(mainUri, out Uri apiUri));
            Assert.AreEqual(
                "https://api.github.com/repos/Arikatsu/WutheringWaves_Data/contents/Textmaps/zh-Hans",
                apiUri.AbsoluteUri);
        }
    }
}
