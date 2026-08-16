using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GI_Subtitles.Common
{
    /// <summary>
    /// Normalizes upstream TextMap formats to the string dictionary consumed by the app.
    /// </summary>
    public static class TextMapNormalizer
    {
        /// <summary>
        /// Converts an array of { Id, Content } objects to a JSON object in place.
        /// Existing JSON objects are left unchanged for compatibility with legacy and custom sources.
        /// </summary>
        public static bool NormalizeIdContentArrayFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("A TextMap file path is required.", nameof(filePath));
            }

            string normalizedPath = filePath + ".normalized";
            if (File.Exists(normalizedPath))
            {
                File.Delete(normalizedPath);
            }

            try
            {
                using (var inputStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var streamReader = new StreamReader(inputStream, Encoding.UTF8, true, 8192))
                using (var jsonReader = new JsonTextReader(streamReader))
                {
                    if (!jsonReader.Read())
                    {
                        throw new InvalidDataException("The downloaded TextMap is empty.");
                    }

                    if (jsonReader.TokenType == JsonToken.StartObject)
                    {
                        return false;
                    }

                    if (jsonReader.TokenType != JsonToken.StartArray)
                    {
                        throw new InvalidDataException(
                            $"Unsupported TextMap root token: {jsonReader.TokenType}.");
                    }

                    using (var outputStream = new FileStream(normalizedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var streamWriter = new StreamWriter(outputStream, new UTF8Encoding(false), 8192))
                    using (var jsonWriter = new JsonTextWriter(streamWriter) { Formatting = Formatting.None })
                    {
                        var ids = new HashSet<string>(StringComparer.Ordinal);
                        jsonWriter.WriteStartObject();

                        while (jsonReader.Read())
                        {
                            if (jsonReader.TokenType == JsonToken.EndArray)
                            {
                                break;
                            }

                            if (jsonReader.TokenType == JsonToken.Comment)
                            {
                                continue;
                            }

                            if (jsonReader.TokenType != JsonToken.StartObject)
                            {
                                throw new InvalidDataException(
                                    $"Expected a TextMap record but found {jsonReader.TokenType}.");
                            }

                            JObject record = JObject.Load(jsonReader);
                            JToken idToken = record["Id"];
                            JToken contentToken = record["Content"];
                            if (idToken?.Type != JTokenType.String ||
                                string.IsNullOrEmpty(idToken.Value<string>()))
                            {
                                throw new InvalidDataException("A TextMap record has a missing or invalid Id.");
                            }
                            if (contentToken?.Type != JTokenType.String)
                            {
                                throw new InvalidDataException(
                                    $"TextMap record '{idToken.Value<string>()}' has missing or invalid Content.");
                            }

                            string id = idToken.Value<string>();
                            if (!ids.Add(id))
                            {
                                throw new InvalidDataException($"Duplicate TextMap Id: {id}");
                            }

                            jsonWriter.WritePropertyName(id);
                            jsonWriter.WriteValue(contentToken.Value<string>());
                        }

                        if (jsonReader.TokenType != JsonToken.EndArray)
                        {
                            throw new InvalidDataException("The TextMap array is incomplete.");
                        }

                        jsonWriter.WriteEndObject();
                    }
                }

                File.Delete(filePath);
                File.Move(normalizedPath, filePath);
                return true;
            }
            finally
            {
                if (File.Exists(normalizedPath))
                {
                    File.Delete(normalizedPath);
                }
            }
        }

        /// <summary>
        /// Merges a primary TextMap and ordered overlay TextMaps into the primary file.
        /// Later files replace earlier values with the same Id, matching the upstream
        /// Wuthering repository's patch semantics.
        /// </summary>
        public static void MergeIdContentArrayFiles(
            string primaryFilePath,
            IEnumerable<string> overlayFilePaths)
        {
            if (string.IsNullOrEmpty(primaryFilePath))
            {
                throw new ArgumentException("A primary TextMap file path is required.", nameof(primaryFilePath));
            }

            string[] overlays = (overlayFilePaths ?? Enumerable.Empty<string>()).ToArray();
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            ReadIntoDictionary(primaryFilePath, merged, overwriteExisting: false);
            foreach (string overlayPath in overlays)
            {
                ReadIntoDictionary(overlayPath, merged, overwriteExisting: true);
            }

            string mergedPath = primaryFilePath + ".normalized";
            if (File.Exists(mergedPath)) File.Delete(mergedPath);
            try
            {
                using (var outputStream = new FileStream(
                    mergedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var streamWriter = new StreamWriter(
                    outputStream, new UTF8Encoding(false), 8192))
                using (var jsonWriter = new JsonTextWriter(streamWriter) { Formatting = Formatting.None })
                {
                    jsonWriter.WriteStartObject();
                    foreach (KeyValuePair<string, string> item in merged)
                    {
                        jsonWriter.WritePropertyName(item.Key);
                        jsonWriter.WriteValue(item.Value);
                    }
                    jsonWriter.WriteEndObject();
                }

                File.Delete(primaryFilePath);
                File.Move(mergedPath, primaryFilePath);
            }
            finally
            {
                if (File.Exists(mergedPath)) File.Delete(mergedPath);
            }
        }

        private static void ReadIntoDictionary(
            string filePath,
            IDictionary<string, string> destination,
            bool overwriteExisting)
        {
            using (var inputStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var streamReader = new StreamReader(inputStream, Encoding.UTF8, true, 8192))
            using (var jsonReader = new JsonTextReader(streamReader))
            {
                if (!jsonReader.Read())
                {
                    throw new InvalidDataException($"The TextMap is empty: {filePath}");
                }

                if (jsonReader.TokenType == JsonToken.StartArray)
                {
                    while (jsonReader.Read() && jsonReader.TokenType != JsonToken.EndArray)
                    {
                        if (jsonReader.TokenType == JsonToken.Comment) continue;
                        if (jsonReader.TokenType != JsonToken.StartObject)
                        {
                            throw new InvalidDataException(
                                $"Expected a TextMap record but found {jsonReader.TokenType}.");
                        }

                        JObject record = JObject.Load(jsonReader);
                        AddRecord(
                            record["Id"]?.Value<string>(),
                            record["Content"],
                            destination,
                            overwriteExisting);
                    }

                    if (jsonReader.TokenType != JsonToken.EndArray)
                    {
                        throw new InvalidDataException("The TextMap array is incomplete.");
                    }
                    return;
                }

                if (jsonReader.TokenType == JsonToken.StartObject)
                {
                    while (jsonReader.Read() && jsonReader.TokenType != JsonToken.EndObject)
                    {
                        if (jsonReader.TokenType == JsonToken.Comment) continue;
                        if (jsonReader.TokenType != JsonToken.PropertyName)
                        {
                            throw new InvalidDataException(
                                $"Expected a TextMap property but found {jsonReader.TokenType}.");
                        }

                        string id = (string)jsonReader.Value;
                        if (!jsonReader.Read())
                        {
                            throw new InvalidDataException("The TextMap object is incomplete.");
                        }
                        AddRecord(id, JToken.Load(jsonReader), destination, overwriteExisting);
                    }

                    if (jsonReader.TokenType != JsonToken.EndObject)
                    {
                        throw new InvalidDataException("The TextMap object is incomplete.");
                    }
                    return;
                }

                throw new InvalidDataException($"Unsupported TextMap root token: {jsonReader.TokenType}.");
            }
        }

        private static void AddRecord(
            string id,
            JToken contentToken,
            IDictionary<string, string> destination,
            bool overwriteExisting)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException("A TextMap record has a missing or invalid Id.");
            }
            if (contentToken?.Type != JTokenType.String)
            {
                throw new InvalidDataException($"TextMap record '{id}' has missing or invalid Content.");
            }

            string content = contentToken.Value<string>();
            if (overwriteExisting)
            {
                destination[id] = content;
            }
            else if (destination.ContainsKey(id))
            {
                throw new InvalidDataException($"Duplicate TextMap Id: {id}");
            }
            else
            {
                destination.Add(id, content);
            }
        }
    }
}
