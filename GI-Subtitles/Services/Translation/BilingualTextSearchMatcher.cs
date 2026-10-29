using System;
using System.Collections.Generic;
using System.IO;

namespace GI_Subtitles.Services.Translation
{
    /// <summary>
    /// Searches either side of the selected language pair and always returns
    /// source-language keys with target-language values.
    /// </summary>
    public sealed class BilingualTextSearchMatcher
    {
        private readonly OptimizedMatcher _sourceMatcher;
        private readonly OptimizedMatcher _targetMatcher;
        private readonly Dictionary<string, List<string>> _additionalSources =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        internal OptimizedMatcher TargetMatcher => _targetMatcher;
        internal bool LoadedTargetFromCache { get; }

        public BilingualTextSearchMatcher(OptimizedMatcher sourceMatcher, string targetLanguage)
            : this(sourceMatcher, targetLanguage, null)
        {
        }

        internal BilingualTextSearchMatcher(
            OptimizedMatcher sourceMatcher, string targetLanguage, OptimizedMatcher cachedTargetMatcher)
        {
            _sourceMatcher = sourceMatcher ?? throw new ArgumentNullException(nameof(sourceMatcher));
            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                throw new ArgumentException("A target language is required.", nameof(targetLanguage));
            }

            // Reuse the already loaded bilingual strings. Only repeated translations
            // need an extra source list; reversing a dictionary alone would lose them.
            var reversedContent = cachedTargetMatcher?.ContentDictionary
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in sourceMatcher.ContentDictionary)
            {
                // Empty translations remain available to forward search, but have no
                // text that can be indexed for reverse search.
                if (string.IsNullOrWhiteSpace(pair.Value)) continue;
                string searchableTarget = pair.Value.Replace("\\n", "\n");
                if (!reversedContent.TryGetValue(searchableTarget, out string firstSource))
                {
                    if (cachedTargetMatcher != null)
                    {
                        throw new InvalidDataException("The cached search index is missing a translation. Delete the search reverse matcher cache and retry.");
                    }
                    reversedContent.Add(searchableTarget, pair.Key);
                }
                else if (!string.Equals(firstSource, pair.Key, StringComparison.Ordinal))
                {
                    if (!_additionalSources.TryGetValue(searchableTarget, out List<string> sources))
                    {
                        sources = new List<string>();
                        _additionalSources.Add(searchableTarget, sources);
                    }
                    sources.Add(pair.Key);
                }
            }
            _targetMatcher = cachedTargetMatcher ?? new OptimizedMatcher(reversedContent, targetLanguage);
            LoadedTargetFromCache = cachedTargetMatcher != null;
        }

        public List<KeyValuePair<string, string>> FindDialogueMatches(
            string input, int maxResults, out bool hasMore)
        {
            hasMore = false;
            if (string.IsNullOrWhiteSpace(input) || maxResults <= 0)
            {
                return new List<KeyValuePair<string, string>>();
            }

            List<KeyValuePair<string, string>> sourceMatches = _sourceMatcher.FindDialogueMatches(
                input, maxResults, out bool moreSourceMatches);
            List<KeyValuePair<string, string>> targetMatches = _targetMatcher.FindDialogueMatches(
                input, maxResults, out bool moreTargetMatches);
            hasMore = moreSourceMatches || moreTargetMatches;

            var results = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<KeyValuePair<string, string>>();
            using (var source = sourceMatches.GetEnumerator())
            using (var target = ExpandTargetMatches(targetMatches).GetEnumerator())
            {
                // Interleave the ranked results so either language can contribute
                // when a short query has many matches. Apply the cap after deduplication.
                bool hasSource = source.MoveNext();
                bool hasTarget = target.MoveNext();
                while (hasSource || hasTarget)
                {
                    if (hasSource && seen.Add(source.Current)) results.Add(source.Current);
                    if (results.Count > maxResults) break;
                    if (hasTarget && seen.Add(target.Current)) results.Add(target.Current);
                    if (results.Count > maxResults) break;
                    hasSource = source.MoveNext();
                    hasTarget = target.MoveNext();
                }
            }
            if (results.Count > maxResults)
            {
                results.RemoveAt(maxResults);
                hasMore = true;
            }
            return results;
        }

        private IEnumerable<KeyValuePair<string, string>> ExpandTargetMatches(
            IEnumerable<KeyValuePair<string, string>> matches)
        {
            foreach (KeyValuePair<string, string> match in matches)
            {
                yield return new KeyValuePair<string, string>(
                    match.Value, _sourceMatcher.ContentDictionary[match.Value]);
                if (_additionalSources.TryGetValue(match.Key, out List<string> sources))
                {
                    foreach (string source in sources)
                    {
                        yield return new KeyValuePair<string, string>(
                            source, _sourceMatcher.ContentDictionary[source]);
                    }
                }
            }
        }
    }
}
