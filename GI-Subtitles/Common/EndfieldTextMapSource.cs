using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GI_Subtitles.Common
{
    public static class EndfieldTextMapSource
    {
        public static bool TryCreateManifestUri(Uri firstChunkUri, out Uri manifestUri)
        {
            manifestUri = null;
            if (!TryParseSourceUri(firstChunkUri, out string[] segments, out _))
            {
                return false;
            }

            string prefix = string.Join("/", segments.Take(5));
            manifestUri = new Uri(
                $"https://raw.githubusercontent.com/{prefix}/manifest.json");
            return true;
        }

        public static IReadOnlyList<Uri> ParseChunkUris(Uri firstChunkUri, string manifestJson)
        {
            if (!TryParseSourceUri(firstChunkUri, out string[] segments, out string locale))
            {
                throw new InvalidOperationException("The Endfield TextMap URL has an unexpected format.");
            }
            if (string.IsNullOrWhiteSpace(manifestJson))
            {
                throw new ArgumentException("The Endfield manifest is empty.", nameof(manifestJson));
            }

            JObject manifest = JObject.Parse(manifestJson);
            JObject localeManifest = manifest["locales"]?[locale] as JObject;
            JArray chunks = localeManifest?["chunks"] as JArray;
            if (chunks == null || chunks.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The Endfield manifest does not contain chunks for locale '{locale}'.");
            }

            int expectedEntries = localeManifest.Value<int?>("entryCount") ?? -1;
            int listedEntries = 0;
            var chunkUris = new List<Uri>();
            string rawRoot = "https://raw.githubusercontent.com/" +
                string.Join("/", segments.Take(5)) + "/";
            foreach (JObject chunk in chunks.OfType<JObject>())
            {
                string relativePath = chunk.Value<string>("file");
                int? count = chunk.Value<int?>("count");
                if (string.IsNullOrWhiteSpace(relativePath) || !count.HasValue || count.Value < 0)
                {
                    throw new InvalidOperationException("The Endfield manifest contains an invalid chunk.");
                }

                string[] relativeSegments = relativePath
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (relativeSegments.Length != 2 ||
                    !string.Equals(relativeSegments[0], locale, StringComparison.Ordinal) ||
                    relativeSegments.Any(segment => segment == "." || segment == "..") ||
                    !relativeSegments[1].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The Endfield manifest contains an unsafe chunk path: {relativePath}");
                }

                listedEntries += count.Value;
                chunkUris.Add(new Uri(rawRoot + string.Join(
                    "/", relativeSegments.Select(Uri.EscapeDataString))));
            }

            if (expectedEntries >= 0 && listedEntries != expectedEntries)
            {
                throw new InvalidOperationException(
                    $"Endfield manifest entry count mismatch for '{locale}': " +
                    $"expected {expectedEntries}, listed {listedEntries}.");
            }
            if (!chunkUris.Any(uri => string.Equals(
                uri.AbsoluteUri, firstChunkUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "The Endfield manifest does not contain the configured first chunk.");
            }

            return chunkUris;
        }

        private static bool TryParseSourceUri(
            Uri sourceUri,
            out string[] segments,
            out string locale)
        {
            segments = Array.Empty<string>();
            locale = null;
            if (sourceUri == null ||
                !string.Equals(sourceUri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            segments = sourceUri.AbsolutePath
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.UnescapeDataString)
                .ToArray();
            if (segments.Length != 7 ||
                !string.Equals(segments[0], "cmyyx", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(segments[1], "cep", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(segments[3], "public", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(segments[4], "game-i18n", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(segments[6], "000.json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            locale = segments[5];
            return true;
        }
    }
}
