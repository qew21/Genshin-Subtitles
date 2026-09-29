using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Newtonsoft.Json;
using GI_Subtitles.Services.Translation;

namespace GI_Subtitles.Views.TextSearch
{
    public class Searcher
    {
        private readonly string _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GI-Subtitles"
        );

        private OptimizedMatcher _matcher;

        public Searcher(string game, string sourceLang, string targetLang)
        {
            var allResourcesExist = CheckResourcesExist(game, sourceLang, targetLang);
            if (!allResourcesExist) return;

            InitMatcher(game, sourceLang, targetLang);
        }

        private bool CheckResourcesExist(string game, string sourceLang, string targetLang)
        {
            var gameTextHashMapFolder = Path.Combine(_dataDir, game);
            var gameFolderExists = Directory.Exists(gameTextHashMapFolder);
            if (!gameFolderExists)
            {
                var prompt = string.Format(
                    (Application.Current.TryFindResource("GameFolderNotExitsPrompt") as string)!,
                    game,
                    targetLang,
                    sourceLang
                );
                MessageBox.Show(
                    prompt,
                    "",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            var sourceLangHashMapExists = File.Exists(
                Path.Combine(gameTextHashMapFolder, $"TextMap{sourceLang}.json"));
            if (!sourceLangHashMapExists)
            {
                var prompt = string.Format(
                    (Application.Current.TryFindResource("SourceLangTextMapNotExitsPrompt") as string)!,
                    sourceLang
                );
                MessageBox.Show(
                    prompt,
                    "",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            var targetLangHashMapExists = File.Exists(
                Path.Combine(gameTextHashMapFolder, $"TextMap{targetLang}.json"));
            if (!targetLangHashMapExists)
            {
                var prompt = string.Format(
                    (Application.Current.TryFindResource("TargetLangTextMapNotExitsPrompt") as string)!,
                    targetLang
                );
                MessageBox.Show(
                    prompt,
                    "",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private void InitMatcher(string game, string sourceLang, string targetLang)
        {
            var textMapsFolderPath = Path.Combine(_dataDir, game);
            var textMapperName =
                Path.Combine(textMapsFolderPath, $"TextMap{sourceLang}_TextMap{targetLang}.json");

            Dictionary<string, string> bilingualTextMap;
            using (StreamReader r = new StreamReader(textMapperName))
                bilingualTextMap = JsonConvert.DeserializeObject<Dictionary<string, string>>(r.ReadToEnd());

            _matcher = new OptimizedMatcher(bilingualTextMap, sourceLang);
        }

        public string Search(string text)
        {
            return _matcher.FindClosestMatch(text, out _);
        }
    }
}