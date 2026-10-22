using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GI_Subtitles.Core.Config;
using GI_Subtitles.Models;
using GI_Subtitles.Services.Translation;

namespace GI_Subtitles.Views
{
    public partial class TextSearchWindow : Window
    {
        private static readonly LanguageOption[] SourceLanguages =
        {
            new LanguageOption("CHS", "简体中文"),
            new LanguageOption("EN", "English"),
            new LanguageOption("JP", "日本語")
        };

        private static readonly LanguageOption[] TargetLanguages =
        {
            new LanguageOption("CHS", "简体中文"),
            new LanguageOption("EN", "English"),
            new LanguageOption("JP", "日本語"),
            new LanguageOption("CHT", "繁體中文"),
            new LanguageOption("DE", "Deutsch"),
            new LanguageOption("ES", "Español"),
            new LanguageOption("FR", "Français"),
            new LanguageOption("ID", "Bahasa Indonesia"),
            new LanguageOption("KR", "한국어"),
            new LanguageOption("PT", "Português"),
            new LanguageOption("RU", "Русский"),
            new LanguageOption("TH", "ไทย"),
            new LanguageOption("VI", "Tiếng Việt")
        };

        private readonly string _dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GI-Subtitles");
        private readonly TextSearchLookup _lookup;
        private readonly string _noMatchText;
        private string _matcherKey;
        private OptimizedMatcher _matcher;
        private bool _updatingLanguageSelectors;

        public ObservableCollection<TextSearchResult> Results { get; } =
            new ObservableCollection<TextSearchResult>();

        public TextSearchWindow(IEnumerable<GameMetadata> supportedGames)
        {
            if (supportedGames == null)
            {
                throw new ArgumentNullException(nameof(supportedGames));
            }

            var games = supportedGames
                .Where(game => game != null && !string.IsNullOrWhiteSpace(game.Name))
                .ToList();
            if (games.Count == 0)
            {
                throw new ArgumentException("At least one supported game is required.", nameof(supportedGames));
            }

            InitializeComponent();
            DataContext = this;
            _lookup = new TextSearchLookup(_dataDirectory);
            _noMatchText = FindResource("TextSearch_NoMatch") as string ?? "No match";

            string uiLanguage = Config.Get("UILang", "zh-CN");
            GameSelector.ItemsSource = games.Select(game => new GameOption
            {
                Name = game.Name,
                Display = game.DisplayNames != null && game.DisplayNames.TryGetValue(uiLanguage, out string displayName)
                    ? displayName
                    : game.Name
            }).ToList();

            SourceLanguageSelector.ItemsSource = SourceLanguages;
            TargetLanguageSelector.ItemsSource = TargetLanguages;

            GameSelector.SelectedValue = Config.Get("Game", string.Empty);
            SourceLanguageSelector.SelectedValue = Config.Get("Input", string.Empty);
            UpdateTargetLanguages(Config.Get("Output", string.Empty));
            UpdateEmptyState();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string game = GameSelector.SelectedValue as string;
            string sourceLanguage = SourceLanguageSelector.SelectedValue as string;
            string targetLanguage = TargetLanguageSelector.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(game) ||
                string.IsNullOrWhiteSpace(sourceLanguage) ||
                string.IsNullOrWhiteSpace(targetLanguage))
            {
                SetStatus(FindResource("TextSearch_SelectSettings") as string, isError: true);
                return;
            }

            var lines = InputTextBox.Text
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select((text, index) => new { Text = text.Trim(), LineNumber = index + 1 })
                .Where(line => line.Text.Length > 0)
                .ToList();
            if (lines.Count == 0)
            {
                SetStatus(FindResource("TextSearch_EnterText") as string, isError: true);
                InputTextBox.Focus();
                return;
            }

            string matcherKey = string.Join("|", game, sourceLanguage, targetLanguage);
            SearchButton.IsEnabled = false;
            GameSelector.IsEnabled = false;
            SourceLanguageSelector.IsEnabled = false;
            TargetLanguageSelector.IsEnabled = false;
            SetStatus(FindResource("TextSearch_Loading") as string, isError: false);

            try
            {
                OptimizedMatcher matcher = _matcherKey == matcherKey && _matcher != null
                    ? _matcher
                    : await Task.Run(() => _lookup.LoadMatcher(game, sourceLanguage, targetLanguage));

                var results = await Task.Run(() => lines.Select(line =>
                {
                    string translation = matcher.FindClosestMatch(line.Text, out string matchedText);
                    bool found = !string.IsNullOrEmpty(translation);
                    return new TextSearchResult
                    {
                        LineNumber = line.LineNumber,
                        Input = line.Text,
                        MatchedText = found ? matchedText : _noMatchText,
                        Translation = found ? translation : _noMatchText,
                        IsMatch = found
                    };
                }).ToList());

                _matcher = matcher;
                _matcherKey = matcherKey;
                Results.Clear();
                foreach (TextSearchResult result in results)
                {
                    Results.Add(result);
                }

                UpdateEmptyState();
                int matchedCount = results.Count(result => result.IsMatch);
                string statusTemplate = FindResource("TextSearch_ResultSummary") as string ?? "Matched {0} of {1} lines.";
                SetStatus(string.Format(statusTemplate, matchedCount, results.Count), isError: matchedCount == 0);
            }
            catch (FileNotFoundException ex)
            {
                string missingTemplate = FindResource("TextSearch_MissingMap") as string
                    ?? "A selected language file is missing: {0}. Download it from the Language Pack Manager.";
                SetStatus(string.Format(missingTemplate, ex.FileName), isError: true);
                Results.Clear();
                UpdateEmptyState();
            }
            catch (Exception ex)
            {
                string errorTemplate = FindResource("TextSearch_Failed") as string
                    ?? "Search failed: {0}";
                SetStatus(string.Format(errorTemplate, ex.Message), isError: true);
                Results.Clear();
                UpdateEmptyState();
            }
            finally
            {
                SearchButton.IsEnabled = true;
                GameSelector.IsEnabled = true;
                SourceLanguageSelector.IsEnabled = true;
                TargetLanguageSelector.IsEnabled = true;
            }
        }

        private void SourceLanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingLanguageSelectors)
            {
                return;
            }

            string targetLanguage = TargetLanguageSelector.SelectedValue as string;
            UpdateTargetLanguages(targetLanguage);
            InvalidateMatcher();
        }

        private void GameSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            InvalidateMatcher();
        }

        private void TargetLanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_updatingLanguageSelectors)
            {
                InvalidateMatcher();
            }
        }

        private void UpdateTargetLanguages(string preferredTargetLanguage)
        {
            _updatingLanguageSelectors = true;
            try
            {
                string sourceLanguage = SourceLanguageSelector.SelectedValue as string;
                var options = TargetLanguages
                    .Where(option => option.Code != sourceLanguage)
                    .ToArray();
                TargetLanguageSelector.ItemsSource = options;
                TargetLanguageSelector.SelectedValue = options.Any(option => option.Code == preferredTargetLanguage)
                    ? preferredTargetLanguage
                    : null;
            }
            finally
            {
                _updatingLanguageSelectors = false;
            }
        }

        private void InvalidateMatcher()
        {
            _matcher = null;
            _matcherKey = null;
            Results.Clear();
            UpdateEmptyState();
            StatusText.Text = string.Empty;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            InputTextBox.Clear();
            Results.Clear();
            UpdateEmptyState();
            StatusText.Text = string.Empty;
            InputTextBox.Focus();
        }

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (InputPlaceholder != null)
            {
                InputPlaceholder.Visibility = string.IsNullOrEmpty(InputTextBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void UpdateEmptyState()
        {
            bool hasResults = Results.Count > 0;
            EmptyResultsPanel.Visibility = hasResults ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SetStatus(string message, bool isError)
        {
            StatusText.Text = message ?? string.Empty;
            StatusText.Foreground = isError
                ? (Brush)FindResource("WarningBrush")
                : (Brush)FindResource("TextSecondaryBrush");
        }

        private sealed class GameOption
        {
            public string Name { get; set; }
            public string Display { get; set; }
        }

        private sealed class LanguageOption
        {
            public LanguageOption(string code, string display)
            {
                Code = code;
                Display = display;
            }

            public string Code { get; }
            public string Display { get; }
        }

        public sealed class TextSearchResult
        {
            public int LineNumber { get; set; }
            public string Input { get; set; }
            public string MatchedText { get; set; }
            public string Translation { get; set; }
            public bool IsMatch { get; set; }
        }
    }
}
