using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GI_Subtitles.Common
{
    public static class WutheringTextMapSource
    {
        private static readonly Regex PartDirectoryPattern =
            new Regex(@"^multi_text(?:_.+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex SequencePattern =
            new Regex(@"\d+", RegexOptions.CultureInvariant);

        public static bool TryCreateDirectoryApiUri(Uri mainTextMapUri, out Uri apiUri)
        {
            apiUri = null;
            if (mainTextMapUri == null ||
                !string.Equals(mainTextMapUri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] segments = mainTextMapUri.AbsolutePath
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 7 ||
                !string.Equals(segments[0], "Arikatsu", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(segments[1], "WutheringWaves_Data", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(segments[3], "Textmaps", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(segments[5], "multi_text", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(segments[6], "MultiText.json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string contentsPath = string.Join("/", segments.Skip(3).Take(2).Select(Uri.EscapeDataString));
            string apiUrl =
                $"https://api.github.com/repos/Arikatsu/WutheringWaves_Data/contents/{contentsPath}";
            string sourceRef = Uri.UnescapeDataString(segments[2]);
            if (!string.Equals(sourceRef, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                apiUrl += "?ref=" + Uri.EscapeDataString(sourceRef);
            }

            apiUri = new Uri(apiUrl);
            return true;
        }

        public static IReadOnlyList<Uri> ParsePartUris(Uri mainTextMapUri, string directoryJson)
        {
            if (mainTextMapUri == null) throw new ArgumentNullException(nameof(mainTextMapUri));
            if (string.IsNullOrWhiteSpace(directoryJson))
            {
                throw new ArgumentException("The Wuthering TextMap directory response is empty.", nameof(directoryJson));
            }

            JArray entries = JArray.Parse(directoryJson);
            List<string> directoryNames = entries
                .OfType<JObject>()
                .Where(entry => string.Equals(
                    entry.Value<string>("type"), "dir", StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Value<string>("name"))
                .Where(name => !string.IsNullOrEmpty(name) && PartDirectoryPattern.IsMatch(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetSequence)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!directoryNames.Any(name =>
                string.Equals(name, "multi_text", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("The Wuthering TextMap directory does not contain multi_text.");
            }

            string mainUrl = mainTextMapUri.AbsoluteUri;
            const string mainDirectorySegment = "/multi_text/";
            int segmentIndex = mainUrl.LastIndexOf(mainDirectorySegment, StringComparison.OrdinalIgnoreCase);
            if (segmentIndex < 0)
            {
                throw new InvalidOperationException("The Wuthering TextMap URL has an unexpected format.");
            }

            return directoryNames
                .Select(name => new Uri(
                    mainUrl.Substring(0, segmentIndex) + "/" + name + "/" +
                    mainUrl.Substring(segmentIndex + mainDirectorySegment.Length)))
                .ToList();
        }

        private static int GetSequence(string directoryName)
        {
            if (string.Equals(directoryName, "multi_text", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            Match match = SequencePattern.Match(directoryName ?? string.Empty);
            return match.Success && int.TryParse(match.Value, out int sequence)
                ? sequence
                : int.MaxValue;
        }
    }
}
