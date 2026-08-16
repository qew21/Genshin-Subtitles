using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GI_Subtitles.Common;

namespace GI_Subtitles.Services.Translation
{
    public sealed class LoadedMatchData
    {
        public LoadedMatchData(
            Dictionary<string, string> content,
            OptimizedMatcher matcher,
            bool loadedFromMatcherCache)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            LoadedFromMatcherCache = loadedFromMatcherCache;
        }

        public Dictionary<string, string> Content { get; }
        public OptimizedMatcher Matcher { get; }
        public bool LoadedFromMatcherCache { get; }
    }

    /// <summary>
    /// Loads an immutable matcher state before it is published to the UI thread.
    /// </summary>
    public static class MatchDataLoader
    {
        public static LoadedMatchData Load(
            string inputFilePath,
            string outputFilePath,
            string contentJsonPath,
            string inputLanguage,
            string userName,
            bool renew)
        {
            var stopwatch = Stopwatch.StartNew();
            string matcherCachePath = OptimizedMatcherCache.GetCachePath(contentJsonPath);

            if (renew)
            {
                if (File.Exists(contentJsonPath)) File.Delete(contentJsonPath);
                if (File.Exists(matcherCachePath)) File.Delete(matcherCachePath);
            }

            string existingFingerprint = null;
            if (File.Exists(contentJsonPath))
            {
                existingFingerprint = OptimizedMatcherCache.CreateFingerprint(contentJsonPath, inputLanguage);
                if (OptimizedMatcherCache.TryLoad(
                    matcherCachePath,
                    existingFingerprint,
                    out OptimizedMatcher cachedMatcher))
                {
                    stopwatch.Stop();
                    Logger.Log.Info(
                        $"Loaded matcher cache with {cachedMatcher.ContentDictionary.Count} entries in {stopwatch.ElapsedMilliseconds} ms.");
                    return new LoadedMatchData(
                        cachedMatcher.ContentDictionary,
                        cachedMatcher,
                        true);
                }
            }

            Dictionary<string, string> content = VoiceContentHelper.CreateVoiceContentDictionary(
                inputFilePath,
                outputFilePath,
                userName);
            var matcher = new OptimizedMatcher(content, inputLanguage);
            string newFingerprint = existingFingerprint ??
                OptimizedMatcherCache.CreateFingerprint(contentJsonPath, inputLanguage);

            try
            {
                OptimizedMatcherCache.Save(matcherCachePath, newFingerprint, matcher);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Logger.Log.Error($"Failed to save matcher cache: {ex.Message}");
            }

            stopwatch.Stop();
            Logger.Log.Info(
                $"Built matcher with {content.Count} entries in {stopwatch.ElapsedMilliseconds} ms.");
            return new LoadedMatchData(content, matcher, false);
        }
    }
}
