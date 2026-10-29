using System;
using System.IO;

namespace GI_Subtitles.Services.Translation
{
    /// <summary>
    /// Loads local text maps for an explicitly selected game and language pair.
    /// </summary>
    public sealed class TextSearchLookup
    {
        private readonly string _dataDirectory;

        public TextSearchLookup(string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                throw new ArgumentException("A data directory is required.", nameof(dataDirectory));
            }

            _dataDirectory = dataDirectory;
        }

        public BilingualTextSearchMatcher LoadMatcher(string game, string sourceLanguage, string targetLanguage)
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
            string userName = targetLanguage == "CHS" ? "旅行者" : "Traveler";

            OptimizedMatcher sourceMatcher = MatchDataLoader.Load(
                inputFilePath,
                outputFilePath,
                contentJsonPath,
                sourceLanguage,
                userName,
                renew: false).Matcher;
            return new BilingualTextSearchMatcher(sourceMatcher, targetLanguage);
        }
    }
}
