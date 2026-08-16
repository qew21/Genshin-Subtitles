using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GI_Subtitles.Services.Translation
{
    /// <summary>
    /// Persists the complete matcher index so unchanged TextMaps do not need to be re-indexed.
    /// </summary>
    public static class OptimizedMatcherCache
    {
        private const string Magic = "GI-Subtitles.OptimizedMatcher";
        // Increment whenever normalization, entry ordering, hashing, or posting semantics change.
        private const int FormatVersion = 1;
        private const int MaxEntries = 5_000_000;
        private const int MaxNgrams = 10_000_000;
        private const int MaxPostingsPerNgram = 5_000_000;
        private const long MaxTotalPostings = 200_000_000;

        public static string GetCachePath(string contentJsonPath)
        {
            return contentJsonPath + ".matcher.bin";
        }

        public static string CreateFingerprint(string contentJsonPath, string inputLanguage)
        {
            using (var stream = new FileStream(contentJsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2 + 16);
                builder.Append(inputLanguage ?? string.Empty).Append(':');
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public static bool TryLoad(
            string cachePath,
            string expectedFingerprint,
            out OptimizedMatcher matcher)
        {
            matcher = null;
            if (!File.Exists(cachePath)) return false;

            try
            {
                using (var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, false))
                {
                    if (reader.ReadString() != Magic || reader.ReadInt32() != FormatVersion)
                    {
                        return false;
                    }
                    if (!string.Equals(reader.ReadString(), expectedFingerprint, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    bool isEnglish = reader.ReadBoolean();
                    int ngramSize = reader.ReadInt32();
                    if (ngramSize <= 0 || ngramSize > 16)
                    {
                        throw new InvalidDataException("Invalid matcher n-gram size.");
                    }

                    int entryCount = ReadCount(reader, MaxEntries, "entry");
                    var entries = new OptimizedMatcher.Entry[entryCount];
                    var content = new Dictionary<string, string>(entryCount, StringComparer.Ordinal);
                    for (int i = 0; i < entryCount; i++)
                    {
                        string normalizedKey = reader.ReadString();
                        string originalKey = reader.ReadString();
                        string value = reader.ReadString();
                        int length = reader.ReadInt32();
                        if (length != normalizedKey.Length || content.ContainsKey(originalKey))
                        {
                            throw new InvalidDataException("Invalid matcher entry.");
                        }

                        entries[i] = new OptimizedMatcher.Entry
                        {
                            NormalizedKey = normalizedKey,
                            OriginalKey = originalKey,
                            Value = value,
                            Length = length
                        };
                        content.Add(originalKey, value);
                    }

                    int ngramCount = ReadCount(reader, MaxNgrams, "n-gram");
                    var ngramIndex = new Dictionary<long, List<int>>(ngramCount);
                    long totalPostings = 0;
                    for (int i = 0; i < ngramCount; i++)
                    {
                        long hash = reader.ReadInt64();
                        int postingCount = ReadCount(reader, MaxPostingsPerNgram, "posting");
                        totalPostings += postingCount;
                        if (totalPostings > MaxTotalPostings)
                        {
                            throw new InvalidDataException("Matcher cache contains too many postings.");
                        }

                        var postings = new List<int>(postingCount);
                        for (int j = 0; j < postingCount; j++)
                        {
                            int entryIndex = reader.ReadInt32();
                            if (entryIndex < 0 || entryIndex >= entryCount)
                            {
                                throw new InvalidDataException("Matcher posting references an invalid entry.");
                            }
                            postings.Add(entryIndex);
                        }
                        ngramIndex.Add(hash, postings);
                    }

                    int shortKeyCount = ReadCount(reader, entryCount, "short key");
                    var shortKeys = new int[shortKeyCount];
                    for (int i = 0; i < shortKeyCount; i++)
                    {
                        shortKeys[i] = reader.ReadInt32();
                        if (shortKeys[i] < 0 || shortKeys[i] >= entryCount)
                        {
                            throw new InvalidDataException("Matcher cache contains an invalid short-key entry.");
                        }
                    }

                    if (stream.Position != stream.Length)
                    {
                        throw new InvalidDataException("Matcher cache contains trailing data.");
                    }

                    matcher = new OptimizedMatcher(
                        content,
                        entries,
                        ngramIndex,
                        shortKeys,
                        isEnglish,
                        ngramSize);
                    return true;
                }
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is InvalidDataException ||
                ex is ArgumentException)
            {
                matcher = null;
                return false;
            }
        }

        public static void Save(string cachePath, string fingerprint, OptimizedMatcher matcher)
        {
            string tempPath = cachePath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, false))
                {
                    writer.Write(Magic);
                    writer.Write(FormatVersion);
                    writer.Write(fingerprint);
                    writer.Write(matcher.isEng);
                    writer.Write(matcher.NgramSize);

                    OptimizedMatcher.Entry[] entries = matcher.Entries;
                    writer.Write(entries.Length);
                    foreach (OptimizedMatcher.Entry entry in entries)
                    {
                        writer.Write(entry.NormalizedKey);
                        writer.Write(entry.OriginalKey);
                        writer.Write(entry.Value);
                        writer.Write(entry.Length);
                    }

                    writer.Write(matcher.NgramIndex.Count);
                    foreach (KeyValuePair<long, List<int>> item in matcher.NgramIndex)
                    {
                        writer.Write(item.Key);
                        writer.Write(item.Value.Count);
                        foreach (int entryIndex in item.Value)
                        {
                            writer.Write(entryIndex);
                        }
                    }

                    writer.Write(matcher.ShortKeysIndices.Length);
                    foreach (int entryIndex in matcher.ShortKeysIndices)
                    {
                        writer.Write(entryIndex);
                    }
                }

                if (File.Exists(cachePath))
                {
                    File.Replace(tempPath, cachePath, null);
                }
                else
                {
                    File.Move(tempPath, cachePath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static int ReadCount(BinaryReader reader, int maximum, string label)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maximum)
            {
                throw new InvalidDataException($"Invalid matcher {label} count: {count}.");
            }
            return count;
        }
    }
}
