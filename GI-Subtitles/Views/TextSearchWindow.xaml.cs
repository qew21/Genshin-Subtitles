using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using GI_Subtitles.Core.Config;
using GI_Subtitles.Models;
using GI_Subtitles.Services.Translation;

namespace GI_Subtitles.Views
{
    public partial class TextSearchWindow : Window
    {
        private const int MaxDialogueMatchesPerLine = 50;
        private const int MaxSearchHistoryEntries = 50;
        private const string SearchHistoryFileName = "TextSearchHistory.json";

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
        private readonly string _searchHistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GI-Subtitles",
            SearchHistoryFileName);
        private bool _updatingLanguageSelectors;
        private bool _historyPersistenceAvailable = true;
        private bool _isClosed;

        public ObservableCollection<TextSearchResult> Results { get; } =
            new ObservableCollection<TextSearchResult>();
        public ObservableCollection<SearchHistoryEntry> SearchHistory { get; } =
            new ObservableCollection<SearchHistoryEntry>();

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
            FitToWorkArea();
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
            LoadSearchHistory();
        }

        private void FitToWorkArea()
        {
            Rect workArea = SystemParameters.WorkArea;
            MaxWidth = workArea.Width * 0.94;
            MaxHeight = workArea.Height * 0.90;
            Width = Math.Min(1100, MaxWidth);
            Height = Math.Min(700, MaxHeight);
            MinWidth = Math.Min(800, Width);
            MinHeight = Math.Min(560, Height);
        }

        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;
            base.OnClosed(e);
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

            string inputText = InputTextBox.Text;
            var lines = inputText
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

            SearchButton.IsEnabled = false;
            GameSelector.IsEnabled = false;
            SourceLanguageSelector.IsEnabled = false;
            TargetLanguageSelector.IsEnabled = false;
            InputTextBox.IsEnabled = false;
            HistorySelector.IsEnabled = false;
            try
            {
                bool cached = _lookup.TryGetCachedMatcher(
                    game, sourceLanguage, targetLanguage, out BilingualTextSearchMatcher matcher);
                SetStatus(FindResource(cached ? "TextSearch_Searching" : "TextSearch_Loading") as string,
                    isError: false);
                if (!cached)
                {
                    matcher = await Task.Run(() => _lookup.LoadMatcher(game, sourceLanguage, targetLanguage));
                }
                if (_isClosed) return;
                SetStatus(FindResource("TextSearch_Searching") as string, isError: false);

                var results = await Task.Run(() =>
                {
                    bool hasMoreMatches = false;
                    var foundResults = new List<TextSearchResult>();
                    foreach (var line in lines)
                    {
                        List<KeyValuePair<string, string>> matches = matcher.FindDialogueMatches(
                            line.Text,
                            MaxDialogueMatchesPerLine,
                            out bool lineHasMoreMatches);
                        hasMoreMatches |= lineHasMoreMatches;

                        if (matches.Count == 0)
                        {
                            foundResults.Add(new TextSearchResult
                            {
                                LineNumber = line.LineNumber,
                                Input = line.Text,
                                MatchedText = _noMatchText,
                                Translation = _noMatchText,
                                IsMatch = false
                            });
                            continue;
                        }

                        foreach (KeyValuePair<string, string> match in matches)
                        {
                            foundResults.Add(new TextSearchResult
                            {
                                LineNumber = line.LineNumber,
                                Input = line.Text,
                                MatchedText = match.Key,
                                Translation = match.Value,
                                IsMatch = true
                            });
                        }
                    }

                    return new SearchResults(foundResults, hasMoreMatches);
                });

                if (_isClosed) return;
                Results.Clear();
                foreach (TextSearchResult result in results.Items)
                {
                    Results.Add(result);
                }
                ResultsGrid.SelectedIndex = -1;

                UpdateEmptyState();
                int matchedCount = results.Items.Count(result => result.IsMatch);
                int matchedLineCount = results.Items
                    .Where(result => result.IsMatch)
                    .Select(result => result.LineNumber)
                    .Distinct()
                    .Count();
                string summaryKey = results.HasMoreMatches
                    ? "TextSearch_ResultSummaryLimited"
                    : "TextSearch_ResultSummary";
                string statusTemplate = FindResource(summaryKey) as string
                    ?? "Found {0} dialogue matches from {1} of {2} input lines.";
                if (results.HasMoreMatches)
                {
                    SetStatus(string.Format(
                        statusTemplate,
                        matchedCount,
                        matchedLineCount,
                        lines.Count,
                        MaxDialogueMatchesPerLine),
                        isError: false);
                }
                else
                {
                    SetStatus(string.Format(statusTemplate, matchedCount, matchedLineCount, lines.Count),
                        isError: matchedCount == 0);
                }

                try
                {
                    RecordSearchHistory(game, sourceLanguage, targetLanguage, inputText);
                }
                catch (Exception ex)
                {
                    GI_Subtitles.Common.Logger.Log.Error($"Failed to save text search history: {ex}");
                    string historyError = FindResource("TextSearch_HistorySaveFailed") as string
                        ?? "Search completed, but history could not be saved: {0}";
                    SetStatus(string.Format(historyError, ex.Message), isError: true);
                }
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
                InputTextBox.IsEnabled = true;
                HistorySelector.IsEnabled = _historyPersistenceAvailable && SearchHistory.Count > 0;
            }
        }

        private void LoadSearchHistory()
        {
            if (!File.Exists(_searchHistoryPath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(_searchHistoryPath);
                List<SearchHistoryEntry> entries = JsonConvert.DeserializeObject<List<SearchHistoryEntry>>(json);
                if (entries == null || entries.Any(entry => entry == null ||
                    string.IsNullOrWhiteSpace(entry.Game) ||
                    string.IsNullOrWhiteSpace(entry.SourceLanguage) ||
                    string.IsNullOrWhiteSpace(entry.TargetLanguage) ||
                    string.IsNullOrWhiteSpace(entry.Query) ||
                    entry.SearchedAtUtc == DateTime.MinValue))
                {
                    throw new InvalidDataException("The search history file contains invalid or incomplete entries.");
                }

                foreach (SearchHistoryEntry entry in entries
                    .OrderByDescending(item => item.SearchedAtUtc)
                    .Take(MaxSearchHistoryEntries))
                {
                    SearchHistory.Add(entry);
                }
                HistorySelector.IsEnabled = SearchHistory.Count > 0;
            }
            catch (Exception ex)
            {
                _historyPersistenceAvailable = false;
                GI_Subtitles.Common.Logger.Log.Error($"Failed to load text search history from '{_searchHistoryPath}': {ex}");
                string historyError = FindResource("TextSearch_HistoryLoadFailed") as string
                    ?? "Search history could not be loaded from {0}: {1}";
                SetStatus(string.Format(historyError, _searchHistoryPath, ex.Message), isError: true);
            }
        }

        private void RecordSearchHistory(string game, string sourceLanguage, string targetLanguage, string query)
        {
            if (!_historyPersistenceAvailable)
            {
                throw new InvalidOperationException("Search history is unavailable because its existing file could not be read.");
            }

            var entry = new SearchHistoryEntry
            {
                Game = game,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
                Query = query,
                SearchedAtUtc = DateTime.UtcNow
            };

            List<SearchHistoryEntry> updated = SearchHistory
                .Where(item => !IsSameSearch(item, entry))
                .ToList();
            updated.Insert(0, entry);
            updated = updated.Take(MaxSearchHistoryEntries).ToList();

            string directory = Path.GetDirectoryName(_searchHistoryPath);
            Directory.CreateDirectory(directory);
            string tempPath = _searchHistoryPath + ".tmp";
            try
            {
                File.WriteAllText(tempPath, JsonConvert.SerializeObject(updated, Formatting.Indented));
                if (File.Exists(_searchHistoryPath))
                {
                    File.Replace(tempPath, _searchHistoryPath, null);
                }
                else
                {
                    File.Move(tempPath, _searchHistoryPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            SearchHistory.Clear();
            foreach (SearchHistoryEntry item in updated)
            {
                SearchHistory.Add(item);
            }
            HistorySelector.IsEnabled = SearchHistory.Count > 0;
        }

        private static bool IsSameSearch(SearchHistoryEntry left, SearchHistoryEntry right)
        {
            return string.Equals(left.Game, right.Game, StringComparison.Ordinal) &&
                string.Equals(left.SourceLanguage, right.SourceLanguage, StringComparison.Ordinal) &&
                string.Equals(left.TargetLanguage, right.TargetLanguage, StringComparison.Ordinal) &&
                string.Equals(left.Query, right.Query, StringComparison.Ordinal);
        }

        private void HistorySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(HistorySelector.SelectedItem is SearchHistoryEntry entry))
            {
                return;
            }

            GameSelector.SelectedValue = entry.Game;
            SourceLanguageSelector.SelectedValue = entry.SourceLanguage;
            UpdateTargetLanguages(entry.TargetLanguage);
            TargetLanguageSelector.SelectedValue = entry.TargetLanguage;
            InputTextBox.Text = entry.Query;
            HistorySelector.SelectedIndex = -1;

            SearchButton_Click(this, new RoutedEventArgs());
        }

        private void SourceLanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingLanguageSelectors)
            {
                return;
            }

            string targetLanguage = TargetLanguageSelector.SelectedValue as string;
            UpdateTargetLanguages(targetLanguage);
            ResetSearchResults();
        }

        private void GameSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ResetSearchResults();
        }

        private void TargetLanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_updatingLanguageSelectors)
            {
                ResetSearchResults();
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

        private void ResetSearchResults()
        {
            SourceTextColumn.Header = (SourceLanguageSelector.SelectedItem as LanguageOption)?.Display
                ?? FindResource("TextSearch_ColumnMatch");
            TargetTextColumn.Header = (TargetLanguageSelector.SelectedItem as LanguageOption)?.Display
                ?? FindResource("TextSearch_ColumnTranslation");
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

        public sealed class SearchHistoryEntry
        {
            public string Game { get; set; }
            public string SourceLanguage { get; set; }
            public string TargetLanguage { get; set; }
            public string Query { get; set; }
            public DateTime SearchedAtUtc { get; set; }

            [JsonIgnore]
            public string DisplayLabel
            {
                get
                {
                    string preview = Query.Replace("\r", " ").Replace("\n", " ").Trim();
                    if (preview.Length > 42)
                    {
                        preview = preview.Substring(0, 39) + "…";
                    }
                    return SearchedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
                        + "  ·  " + preview;
                }
            }
        }

        private sealed class SearchResults
        {
            public SearchResults(List<TextSearchResult> items, bool hasMoreMatches)
            {
                Items = items;
                HasMoreMatches = hasMoreMatches;
            }

            public List<TextSearchResult> Items { get; }
            public bool HasMoreMatches { get; }
        }
    }
}
