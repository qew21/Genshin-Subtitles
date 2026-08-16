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
