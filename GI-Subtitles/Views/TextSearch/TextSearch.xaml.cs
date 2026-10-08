using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static GI_Subtitles.Views.TextSearch.Util;

namespace GI_Subtitles.Views.TextSearch
{
    // class methods
    public partial class TextSearch
    {
        private string _targetLang = "";
        private string _sourceLang = "";
        private string _game = "";

        private Searcher _searcher;

        private readonly Line _line = new Line();
    }

    // event handlers
    public partial class TextSearch
    {
        public TextSearch()
        {
            SizeChanged += TextSearch_OnSizeChanged;
            InitializeComponent();

            DataContext = this;

            SourceLangSelector.ItemsSource = SourceLangs.Keys.ToList();
            TargetLangSelector.ItemsSource = TargetLangs.Keys.ToList();

            LoadSupportedGames(GameSelector);
            GameSelector.SelectionChanged += GameSelector_OnSelectionChanged;

            TextListView.ItemsSource = _line.Rows;
        }

        private void TargetLangSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is ComboBox targetLangSelector))
                return;

            var selectedItem = targetLangSelector.SelectedItem;
            if (selectedItem == null)
                _targetLang = null;
            else
            {
                var selectedLang = selectedItem.ToString();
                _targetLang = selectedLang;
            }
        }

        private void SourceLangSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var sourceLangSelector = sender as ComboBox;
            _sourceLang = sourceLangSelector!.SelectedItem as string;

            if ((string)TargetLangSelector.SelectedItem == _sourceLang)
            {
                // Remove the language with the same name in TargetLangSelector
                TargetLangSelector.SelectedItem = null;
                // Clear previous _targetLang, if any.
                _targetLang = "";
            }

            // Filter out selected source language from target languages.
            var targetLangExcludedSelectedOne = TargetLangs.Keys.Where(t => t != _sourceLang).ToList();
            TargetLangSelector.ItemsSource = targetLangExcludedSelectedOne;
            TargetLangSelector.IsEnabled = true;
        }

        private void GameSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is ComboBox comboBox))
                return;

            if (comboBox.SelectedValue is string newValue)
                _game = newValue;
        }

        private void SearchBtn_OnClick(object sender, RoutedEventArgs e)
        {
            UpdateLayout();

            var gameName = GetSearchSettings("GAME");
            var sourceLang = GetSearchSettings("SOURCE_LANG");
            var targetLang = GetSearchSettings("TARGET_LANG");

            if (
                String.IsNullOrWhiteSpace(gameName) ||
                String.IsNullOrWhiteSpace(sourceLang) ||
                String.IsNullOrWhiteSpace(targetLang)
            ) return;

            _searcher ??= new Searcher(gameName, sourceLang, targetLang);

            foreach (var row in _line.Rows)
            {
                var sourceText = row.SourceText.Trim();
                if (string.IsNullOrWhiteSpace(sourceText))
                    continue;
                row.TargetText = _searcher.Search(sourceText);
            }
        }

        private void SourceTextBox_OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is TextBox sourceTextBox))
                return;
            var availWidth = _line.CalcTextBoxWidth();
            sourceTextBox.Width = availWidth;
        }

        private void SourceTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is TextBox textBox)) return;

            var textUid = int.Parse(textBox.Uid);

            if (e.Key == Key.Enter)
            {
                TextListView.ItemsSource =
                    _line.LineOperation("ADD", textUid);

                UpdateLayout();

                // Focus next line
                var textBoxes = GetTextBoxes(TextListView);
                var sourceTextBoxes = textBoxes.Where(tb => tb.Name == "SourceTextBox").ToList();
                TextListView.ScrollIntoView(sourceTextBoxes[textUid + 1]);
                sourceTextBoxes[textUid + 1].Focus();

                return;
            }

            if (e.Key == Key.Back)
            {
                if (textBox.Uid == "0") return; // Do not remove the first line
                if (textBox.CaretIndex != 0) return; // When caret is at start of the <TextBox>, try to remove the line.

                var isToDelete = false;
                if (String.IsNullOrWhiteSpace(textBox.Text)) isToDelete = true;
                else
                {
                    var result = MessageBox.Show(
                        GetLocalizedString("RemovingNonemptyTextBoxPrompt"),
                        "",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                        isToDelete = true;
                }

                if (!isToDelete) return;

                TextListView.ItemsSource =
                    _line.LineOperation("DEL", textUid);

                var textBoxes = GetTextBoxes(TextListView);
                var sourceTextBoxes = textBoxes.Where(tb => tb.Name == "SourceTextBox").ToList();
                // When there is only one line, namely the first line, do not remove it.
                if (sourceTextBoxes.Count == 1)
                {
                    sourceTextBoxes.First().Focus();
                    return;
                }

                // When there are lines more than one, remove current focused line.
                // Focus previous line
                sourceTextBoxes[textUid - 1].Focus();
            }
        }

        private void TextSearch_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!(sender is Window)) return;

            TextListView.Width = TextGrid.ActualWidth;
        }

        private void TextListView_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!(sender is ListView)) return;

            _line.UpdateTextBoxActualWidth(TextListView.ActualWidth);
        }
    }
    
    // methods used by event handlers
    public partial class TextSearch
    {
        private string GetSearchSettings(string settingName)
        {
            string settingInfo;
            string notSetPromptKey;

            switch (settingName)
            {
                case "GAME":
                    settingInfo = _game;
                    notSetPromptKey = "GameNotSetPrompt";
                    break;
                case "SOURCE_LANG":
                    settingInfo = _sourceLang;
                    notSetPromptKey = "SourceLangNotSetPrompt";
                    break;
                case "TARGET_LANG":
                    settingInfo = _targetLang;
                    notSetPromptKey = "TargetLangNotSetPrompt";
                    break;
                default:
                    return "";
            }

            if (String.IsNullOrWhiteSpace(settingInfo))
            {
                MessageBox.Show(
                    GetLocalizedString(notSetPromptKey),
                    "",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return "";
            }

            switch (settingName)
            {
                case "GAME":
                    return _game;
                case "SOURCE_LANG":
                    return SourceLangs[_sourceLang];
                case "TARGET_LANG":
                    return TargetLangs[_targetLang];
                default:
                    return "";
            }
        }

        private static string GetLocalizedString(string key)
        {
            return Application.Current.TryFindResource(key) as string ?? key;
        }
    }
}