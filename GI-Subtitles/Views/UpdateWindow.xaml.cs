using GI_Subtitles.Models;
using System;
using System.Windows;

namespace GI_Subtitles.Views
{
    public partial class UpdateWindow : Window
    {
        public bool InstallRequested { get; private set; }
        public bool IgnoreRequested { get; private set; }

        public UpdateWindow(ReleaseManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            InitializeComponent();
            VersionValue.Text = manifest.Version ?? string.Empty;
            PublishedValue.Text = manifest.PublishedAt ?? string.Empty;
            ReleaseNotesTextBox.Text = NormalizeLineEndings(manifest.ReleaseNotes);
        }

        private static string NormalizeLineEndings(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", Environment.NewLine);
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            InstallRequested = true;
            Close();
        }

        private void IgnoreButton_Click(object sender, RoutedEventArgs e)
        {
            IgnoreRequested = true;
            Close();
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
