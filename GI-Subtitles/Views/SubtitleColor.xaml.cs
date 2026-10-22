using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GI_Subtitles.Core.Config;

namespace GI_Subtitles.Views
{
    public partial class SubtitleColor : Window
    {
        private static readonly string[] PresetColors =
        {
            "#FFFFFF",
            "#FFE66D",
            "#66E4FF",
            "#7CF1B1",
            "#FF9FCA",
            "#FFC078",
            "#A6C8FF",
            "#D6B4FF"
        };

        private readonly List<Button> _swatchButtons = new List<Button>();
        private Color _draftColor = Colors.White;
        private bool _isUpdatingHexInput;

        public SubtitleColor()
        {
            InitializeComponent();

            int previewFontSize = Math.Max(16, Math.Min(24, Config.Get<int>("Size", 20)));
            DarkScenePreview.FontSize = previewFontSize;
            LightScenePreview.FontSize = previewFontSize;
            DarkScenePreview.SetResourceReference(TextBlock.FontFamilyProperty, "SubtitleFontFamily");
            LightScenePreview.SetResourceReference(TextBlock.FontFamilyProperty, "SubtitleFontFamily");

            foreach (string hex in PresetColors)
            {
                AddPresetSwatch(hex);
            }

            Color color;
            if (!Config.TryGetString("SubtitleColor", out string configuredColor) ||
                !App.TryParseSubtitleColor(configuredColor, out color))
            {
                color = Colors.White;
                ValidationText.Text = GetLocalizedString("SubtitleColor_InvalidHex");
                ValidationText.Visibility = Visibility.Visible;
                ApplyButton.IsEnabled = false;
            }
            SetDraftColor(color, true);
        }

        private void AddPresetSwatch(string hex)
        {
            App.TryParseSubtitleColor(hex, out Color color);
            var swatch = new Button
            {
                Width = 48,
                Height = 36,
                Margin = new Thickness(0, 0, 8, 6),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromRgb(216, 224, 234)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = hex,
                Tag = hex,
                Content = new TextBlock
                {
                    Text = "✓",
                    FontFamily = new FontFamily("Segoe UI Symbol"),
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = GetReadableCheckColor(color),
                    Visibility = Visibility.Collapsed,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            AutomationProperties.SetName(swatch, hex);
            swatch.Click += PresetSwatch_Click;
            PresetColorPanel.Children.Add(swatch);
            _swatchButtons.Add(swatch);
        }

        private void PresetSwatch_Click(object sender, RoutedEventArgs e)
        {
            var swatch = sender as Button;
            var hex = swatch?.Tag as string;
            if (App.TryParseSubtitleColor(hex, out Color color))
            {
                ValidationText.Visibility = Visibility.Collapsed;
                ApplyButton.IsEnabled = true;
                SetDraftColor(color, true);
            }
        }

        private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingHexInput || HexInput == null || ValidationText == null || ApplyButton == null)
            {
                return;
            }

            if (App.TryParseSubtitleColor(HexInput.Text, out Color color))
            {
                ValidationText.Visibility = Visibility.Collapsed;
                ApplyButton.IsEnabled = true;
                SetDraftColor(color, false);
            }
            else
            {
                ValidationText.Text = GetLocalizedString("SubtitleColor_InvalidHex");
                ValidationText.Visibility = Visibility.Visible;
                ApplyButton.IsEnabled = false;
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ValidationText.Visibility = Visibility.Collapsed;
            ApplyButton.IsEnabled = true;
            SetDraftColor(Colors.White, true);
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!App.TryParseSubtitleColor(HexInput.Text, out Color color))
            {
                ValidationText.Text = GetLocalizedString("SubtitleColor_InvalidHex");
                ValidationText.Visibility = Visibility.Visible;
                ApplyButton.IsEnabled = false;
                return;
            }

            try
            {
                string hex = ToHex(color);
                Config.Set("SubtitleColor", hex);
                App.ApplySubtitleColor(color);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                string messageTemplate = GetLocalizedString("SubtitleColor_SaveFailed");
                ValidationText.Text = string.Format(CultureInfo.CurrentCulture, messageTemplate, ex.Message);
                ValidationText.Visibility = Visibility.Visible;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SetDraftColor(Color color, bool updateHexInput)
        {
            _draftColor = color;
            var brush = new SolidColorBrush(color);
            DarkScenePreview.Foreground = brush;
            LightScenePreview.Foreground = brush;
            CustomColorPreview.Background = brush;

            if (updateHexInput)
            {
                _isUpdatingHexInput = true;
                HexInput.Text = ToHex(color);
                _isUpdatingHexInput = false;
            }

            UpdateSwatchSelection();
        }

        private void UpdateSwatchSelection()
        {
            foreach (Button swatch in _swatchButtons)
            {
                bool isSelected = string.Equals(
                    swatch.Tag as string,
                    ToHex(_draftColor),
                    StringComparison.OrdinalIgnoreCase);
                swatch.BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromRgb(22, 119, 255))
                    : new SolidColorBrush(Color.FromRgb(216, 224, 234));
                swatch.BorderThickness = new Thickness(isSelected ? 3 : 1);
                var checkmark = swatch.Content as TextBlock;
                if (checkmark != null)
                {
                    checkmark.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private static Brush GetReadableCheckColor(Color color)
        {
            double luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
            return luminance > 150 ? Brushes.Black : Brushes.White;
        }

        private static string ToHex(Color color)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private static string GetLocalizedString(string key)
        {
            return Application.Current.TryFindResource(key) as string ?? key;
        }
    }
}
