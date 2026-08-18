using System;
using System.Collections.Generic;
using System.IO;
using GI_Subtitles.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace GI_Test
{
    [TestClass]
    public class TestTextMapNormalizer
    {
        [TestMethod]
        public void IdContentArray_IsConvertedToDictionary()
        {
            WithTempFile(
                "[{\"Id\":\"line_1\",\"Content\":\"第一句\"},{\"Id\":\"line_2\",\"Content\":\"\"}]",
                path =>
                {
                    Assert.IsTrue(TextMapNormalizer.NormalizeIdContentArrayFile(path));
                    var result = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                    Assert.AreEqual(2, result.Count);
                    Assert.AreEqual("第一句", result["line_1"]);
                    Assert.AreEqual(string.Empty, result["line_2"]);
                });
        }

        [TestMethod]
        public void Dictionary_IsLeftUnchanged()
        {
            const string json = "{\"line_1\":\"legacy\"}";
            WithTempFile(
                json,
                path =>
                {
                    Assert.IsFalse(TextMapNormalizer.NormalizeIdContentArrayFile(path));
                    Assert.AreEqual(json, File.ReadAllText(path));
                });
        }

        [TestMethod]
        public void DuplicateId_IsRejectedWithoutReplacingDownloadedFile()
        {
            const string json =
                "[{\"Id\":\"same\",\"Content\":\"one\"},{\"Id\":\"same\",\"Content\":\"two\"}]";
            WithTempFile(
                json,
                path =>
                {
                    Assert.ThrowsException<InvalidDataException>(
                        () => TextMapNormalizer.NormalizeIdContentArrayFile(path));
                    Assert.AreEqual(json, File.ReadAllText(path));
                    Assert.IsFalse(File.Exists(path + ".normalized"));
                });
        }

        [TestMethod]
        public void OrderedOverlays_ReplaceMaskedValuesAndIncludeNewIds()
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string primaryPath = Path.Combine(directory, "primary.json");
            string firstPath = Path.Combine(directory, "first.json");
            string laterPath = Path.Combine(directory, "later.json");
            try
            {
                File.WriteAllText(primaryPath,
                    "[{\"Id\":\"masked\",\"Content\":\"*****\"},{\"Id\":\"base\",\"Content\":\"base\"}]");
                File.WriteAllText(firstPath,
                    "[{\"Id\":\"masked\",\"Content\":\"restored\"},{\"Id\":\"new\",\"Content\":\"first\"}]");
                File.WriteAllText(laterPath,
                    "[{\"Id\":\"new\",\"Content\":\"latest\"}]");

                TextMapNormalizer.MergeIdContentArrayFiles(
                    primaryPath, new[] { firstPath, laterPath });

                var result = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                    File.ReadAllText(primaryPath));
                Assert.AreEqual(3, result.Count);
                Assert.AreEqual("restored", result["masked"]);
                Assert.AreEqual("base", result["base"]);
                Assert.AreEqual("latest", result["new"]);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void WithTempFile(string content, Action<string> assertion)
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "TextMap.json");
            try
            {
                File.WriteAllText(path, content);
                assertion(path);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
