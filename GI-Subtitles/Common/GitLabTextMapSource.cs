using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace GI_Subtitles.Common
{
    public sealed class TextMapDownloadPlan
    {
        public IReadOnlyList<Uri> MainFiles { get; internal set; }
        public IReadOnlyList<Uri> MediumFiles { get; internal set; }
    }

    /// <summary>
    /// Discovers single-file and numbered TextMap resources from a GitLab directory listing.
    /// Language and part count are resolved from the listing instead of being encoded in code.
    /// </summary>
    public static class GitLabTextMapSource
    {
        private static readonly Regex LinkHeaderEntryRegex = new Regex(
            @"<(?<uri>[^>]+)>(?<parameters>[^<]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex LinkRelationRegex = new Regex(
            @"(?:^|;)\s*rel\s*=\s*(?:""(?<value>[^""]+)""|(?<value>[^;,\s]+))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private sealed class Candidate
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public int? PartIndex { get; set; }
        }

        public static async Task<TextMapDownloadPlan> DiscoverAsync(
            HttpClient client,
            Uri fileListUri,
            string fileUrlTemplate,
            string language,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (fileListUri == null) throw new ArgumentNullException(nameof(fileListUri));
            if (string.IsNullOrWhiteSpace(fileUrlTemplate))
            {
                throw new ArgumentException("A raw file URL template is required.", nameof(fileUrlTemplate));
            }
            if (string.IsNullOrWhiteSpace(language))
            {
                throw new ArgumentException("A language code is required.", nameof(language));
            }

            var entries = await ReadDirectoryEntriesAsync(client, fileListUri, cancellationToken)
                .ConfigureAwait(false);
            List<Candidate> main = new List<Candidate>();
            List<Candidate> medium = new List<Candidate>();

            foreach (JObject entry in entries)
            {
                if (!string.Equals(entry.Value<string>("type"), "blob", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = entry.Value<string>("name");
                string path = entry.Value<string>("path");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) ||
                    !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryParseCandidate(name, path, "TextMap_Medium" + language, out Candidate mediumCandidate))
                {
                    medium.Add(mediumCandidate);
                }
                else if (TryParseCandidate(name, path, "TextMap" + language, out Candidate mainCandidate))
                {
                    main.Add(mainCandidate);
                }
            }

            IReadOnlyList<Uri> mainFiles = BuildUris(
                SelectCompleteFileSet(main, "main TextMap", language), fileUrlTemplate);
            IReadOnlyList<Uri> mediumFiles = BuildUris(
                SelectCompleteFileSet(medium, "Medium TextMap", language), fileUrlTemplate);

            return new TextMapDownloadPlan
            {
                MainFiles = mainFiles,
                MediumFiles = mediumFiles
            };
        }

        private static async Task<List<JObject>> ReadDirectoryEntriesAsync(
            HttpClient client,
            Uri fileListUri,
            CancellationToken cancellationToken)
        {
            var result = new List<JObject>();
            int page = 1;
            Uri pageUri = AddPage(fileListUri, page);
            var requestedPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (pageUri != null)
            {
                if (!requestedPages.Add(pageUri.AbsoluteUri))
                {
                    throw new InvalidOperationException("GitLab returned a repeated repository page.");
                }

                using (var request = new HttpRequestMessage(HttpMethod.Get, pageUri))
                using (HttpResponseMessage response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    JArray entries = JArray.Parse(json);
                    result.AddRange(entries.OfType<JObject>());

                    Uri nextPageUri = GetNextPageUri(response, pageUri, fileListUri, page);
                    if (nextPageUri == null)
                    {
                        break;
                    }

                    pageUri = nextPageUri;
                    page = GetPageNumber(pageUri) ?? (page + 1);
                }
            }

            return result;
        }

        private static Uri GetNextPageUri(
            HttpResponseMessage response,
            Uri currentPageUri,
            Uri fileListUri,
            int currentPage)
        {
            if (response.Headers.TryGetValues("Link", out IEnumerable<string> linkHeaders))
            {
                foreach (string linkHeader in linkHeaders)
                {
                    foreach (Match link in LinkHeaderEntryRegex.Matches(linkHeader))
                    {
                        Match relation = LinkRelationRegex.Match(link.Groups["parameters"].Value);
                        if (!relation.Success || !relation.Groups["value"].Value
                            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                            .Any(value => string.Equals(value, "next", StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        string nextUrl = link.Groups["uri"].Value;
                        if (!Uri.TryCreate(currentPageUri, nextUrl, out Uri nextPageUri))
                        {
                            throw new InvalidOperationException("GitLab returned an invalid next-page link.");
                        }

                        return nextPageUri;
                    }
                }
            }

            if (!response.Headers.TryGetValues("X-Next-Page", out IEnumerable<string> nextPages))
            {
                return null;
            }

            string nextPage = nextPages.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(nextPage))
            {
                return null;
            }
            if (!int.TryParse(nextPage, NumberStyles.None, CultureInfo.InvariantCulture, out int nextPageNumber) ||
                nextPageNumber <= currentPage)
            {
                throw new InvalidOperationException("GitLab returned an invalid next-page value.");
            }

            return AddPage(fileListUri, nextPageNumber);
        }

        private static int? GetPageNumber(Uri uri)
        {
            string pageParameter = uri.Query
                .TrimStart('?')
                .Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(parameter => parameter.StartsWith("page=", StringComparison.OrdinalIgnoreCase));
            if (pageParameter == null)
            {
                return null;
            }

            string pageText = Uri.UnescapeDataString(pageParameter.Substring(pageParameter.IndexOf('=') + 1));
            return int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out int page)
                ? page
                : (int?)null;
        }

        private static Uri AddPage(Uri baseUri, int page)
        {
            var builder = new UriBuilder(baseUri);
            List<string> parameters = builder.Query
                .TrimStart('?')
                .Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(item => !item.StartsWith("page=", StringComparison.OrdinalIgnoreCase))
                .ToList();
            parameters.Add("page=" + page.ToString(CultureInfo.InvariantCulture));
            builder.Query = string.Join("&", parameters);
            return builder.Uri;
        }

        private static bool TryParseCandidate(
            string name,
            string path,
            string expectedStem,
            out Candidate candidate)
        {
            candidate = null;
            string stem = name.Substring(0, name.Length - ".json".Length);
            if (!stem.StartsWith(expectedStem, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string suffix = stem.Substring(expectedStem.Length);
            int? partIndex = null;
            if (suffix.Length > 0)
            {
                if (suffix[0] != '_' ||
                    !int.TryParse(suffix.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedIndex) ||
                    parsedIndex < 0)
                {
                    return false;
                }

                partIndex = parsedIndex;
            }

            candidate = new Candidate
            {
                Name = name,
                Path = path,
                PartIndex = partIndex
            };
            return true;
        }

        private static List<Candidate> SelectCompleteFileSet(
            List<Candidate> candidates,
            string resourceName,
            string language)
        {
            List<Candidate> parts = candidates
                .Where(candidate => candidate.PartIndex.HasValue)
                .OrderBy(candidate => candidate.PartIndex.Value)
                .ToList();
            if (parts.Count > 0)
            {
                for (int index = 0; index < parts.Count; index++)
                {
                    if (parts[index].PartIndex.Value != index)
                    {
                        throw new InvalidOperationException(
                            $"The {resourceName} parts for {language} are incomplete or duplicated.");
                    }
                }

                // Numbered parts take precedence if an old unsplit file remains in the directory.
                return parts;
            }

            List<Candidate> singleFiles = candidates
                .Where(candidate => !candidate.PartIndex.HasValue)
                .ToList();
            if (singleFiles.Count == 1)
            {
                return singleFiles;
            }
            if (singleFiles.Count > 1)
            {
                throw new InvalidOperationException(
                    $"The {resourceName} files for {language} are ambiguous.");
            }

            throw new InvalidOperationException(
                $"No {resourceName} files were found for {language}.");
        }

        private static IReadOnlyList<Uri> BuildUris(
            IEnumerable<Candidate> candidates,
            string fileUrlTemplate)
        {
            return candidates.Select(candidate =>
            {
                string escapedPath = string.Join("/", candidate.Path
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));
                string url = fileUrlTemplate.Replace("{Path}", escapedPath);
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                {
                    throw new InvalidOperationException(
                        $"The URL for TextMap file '{candidate.Name}' is invalid.");
                }
                return uri;
            }).ToList().AsReadOnly();
        }
    }
}
