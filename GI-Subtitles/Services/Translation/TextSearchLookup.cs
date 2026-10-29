using System;
using System.IO;
using GI_Subtitles.Common;
using GI_Subtitles.Core.Cache;

namespace GI_Subtitles.Services.Translation
{
    /// <summary>
    /// Reuses local search indexes across windows, with a bounded process cache.
    /// </summary>
    public sealed class TextSearchLookup
    {
        private static readonly object CacheLock = new object();
        private static readonly object LoadLock = new object();
        private static readonly LRUCache<string, CachedMatcher> Matchers =
            new LRUCache<string, CachedMatcher>(2);
        private readonly string _dataDirectory;

        public TextSearchLookup(string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                throw new ArgumentException("A data directory is required.", nameof(dataDirectory));
            }

            _dataDirectory = Path.GetFullPath(dataDirectory);
        }

        public BilingualTextSearchMatcher LoadMatcher(string game, string sourceLanguage, string targetLanguage)
        {
            SearchFiles files = GetFiles(game, sourceLanguage, targetLanguage);
            lock (LoadLock)
            {
                lock (CacheLock)
                {
                    if (TryGetCachedMatcher(files, out BilingualTextSearchMatcher existing)) return existing;
                }

                string sourceStamp = GetSourceStamp(files);
                string sourceFingerprint = OptimizedMatcherCache.CreateFingerprint(files.Input, sourceLanguage)
                    + "|" + OptimizedMatcherCache.CreateFingerprint(files.Output, targetLanguage);
                OptimizedMatcher cachedTarget = null;
                if (File.Exists(files.Content))
                {
                    string fingerprint = GetSearchFingerprint(files, sourceFingerprint);
                    OptimizedMatcherCache.TryLoad(files.ReverseCache, fingerprint, out cachedTarget);
                }

                // The reverse-cache fingerprint also records both raw TextMaps. On
                // first use or after a pack update, rebuild the derived pair from the
                // current maps rather than trusting an unverified older pair JSON.
                bool renew = cachedTarget == null;
                if (renew)
                {
                    Logger.Log.Info($"Search cache missing, changed or invalid; rebuilding {files.Content} from current TextMaps.");
                }
                string userName = targetLanguage == "CHS" ? "旅行者" : "Traveler";
                OptimizedMatcher sourceMatcher = MatchDataLoader.Load(
                    files.Input, files.Output, files.Content, sourceLanguage, userName, renew).Matcher;
                var matcher = new BilingualTextSearchMatcher(sourceMatcher, targetLanguage, cachedTarget);
                EnsureSourcesUnchanged(files, sourceStamp);
                if (renew)
                {
                    try
                    {
                        OptimizedMatcherCache.Save(files.ReverseCache,
                            GetSearchFingerprint(files, sourceFingerprint), matcher.TargetMatcher);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        Logger.Log.Error($"Could not save search index '{files.ReverseCache}': {ex.Message}");
                    }
                }
                EnsureSourcesUnchanged(files, sourceStamp);
                lock (CacheLock)
                {
                    Matchers.AddOrUpdate(files.Content, new CachedMatcher
                    {
                        Stamp = GetStamp(files),
                        Matcher = matcher
                    });
                }
                Logger.Log.Info($"Loaded search pair {game} {sourceLanguage}->{targetLanguage}; reverseFromCache={matcher.LoadedTargetFromCache}.");
                return matcher;
            }
        }

        public bool TryGetCachedMatcher(
            string game, string sourceLanguage, string targetLanguage, out BilingualTextSearchMatcher matcher)
        {
            SearchFiles files = GetFiles(game, sourceLanguage, targetLanguage);
            lock (CacheLock)
            {
                return TryGetCachedMatcher(files, out matcher);
            }
        }

        private static bool TryGetCachedMatcher(SearchFiles files, out BilingualTextSearchMatcher matcher)
        {
            matcher = null;
            string stamp = GetStamp(files);
            if (!Matchers.TryGetValue(files.Content, out CachedMatcher cached)) return false;
            if (!string.Equals(cached.Stamp, stamp, StringComparison.Ordinal))
            {
                Matchers.Remove(files.Content);
                Logger.Log.Info($"TextMaps changed; invalidated search memory cache for {files.Content}.");
                return false;
            }
            matcher = cached.Matcher;
            return true;
        }

        private static string GetSearchFingerprint(SearchFiles files, string sourceFingerprint)
        {
            // Increment this marker when reverse-search normalization changes.
            return "bilingual-search-v1|" + sourceFingerprint + "|"
                + OptimizedMatcherCache.CreateFingerprint(files.Content, string.Empty);
        }

        private static void EnsureSourcesUnchanged(SearchFiles files, string expectedStamp)
        {
            if (!string.Equals(GetSourceStamp(files), expectedStamp, StringComparison.Ordinal))
            {
                throw new IOException("The selected TextMaps changed while loading. Wait for the language pack update to finish and search again.");
            }
        }

        private static string GetStamp(SearchFiles files)
        {
            return GetSourceStamp(files) + "|" + (File.Exists(files.Content)
                ? GetFileStamp(files.Content) : "missing");
        }

        private static string GetSourceStamp(SearchFiles files)
        {
            return GetFileStamp(files.Input) + "|" + GetFileStamp(files.Output);
        }

        private static string GetFileStamp(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("A selected language file is missing.", path);
            return info.Length + ":" + info.LastWriteTimeUtc.Ticks;
        }

        private SearchFiles GetFiles(string game, string sourceLanguage, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(game) ||
                string.IsNullOrWhiteSpace(sourceLanguage) ||
                string.IsNullOrWhiteSpace(targetLanguage))
            {
                throw new ArgumentException("Select a game and both languages before searching.");
            }

            string gameDirectory = Path.Combine(_dataDirectory, game);
            string inputFilePath = Path.Combine(gameDirectory, $"TextMap{sourceLanguage}.json");
            string outputFilePath = Path.Combine(gameDirectory, $"TextMap{targetLanguage}.json");
            if (!File.Exists(inputFilePath))
            {
                throw new FileNotFoundException("The selected source language text map is missing.", inputFilePath);
            }
            if (!File.Exists(outputFilePath))
            {
                throw new FileNotFoundException("The selected target language text map is missing.", outputFilePath);
            }

            string contentJsonPath = Path.Combine(
                gameDirectory,
                $"TextMap{sourceLanguage}_TextMap{targetLanguage}.json");
            return new SearchFiles
            {
                Input = inputFilePath,
                Output = outputFilePath,
                Content = contentJsonPath,
                ReverseCache = OptimizedMatcherCache.GetCachePath(contentJsonPath + ".search-reverse")
            };
        }

        private sealed class SearchFiles
        {
            public string Input;
            public string Output;
            public string Content;
            public string ReverseCache;
        }

        private sealed class CachedMatcher
        {
            public string Stamp;
            public BilingualTextSearchMatcher Matcher;
        }
    }
}
