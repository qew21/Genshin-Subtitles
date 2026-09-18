using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace GI_Subtitles.Views
{
    /// <summary>
    /// Click-through top-center hint adapter for the live overlay session.
    /// </summary>
    internal sealed class OverlayHintChrome
    {
        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExLayered = 0x00080000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private readonly Window _window;
        private readonly TextBlock _text;
        private bool _shown;

        public OverlayHintChrome()
        {
            _text = new TextBlock
            {
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 18,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };

            var hint = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 10, 12, 18)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(28, 10, 28, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 18, 0, 0),
                Child = _text,
                IsHitTestVisible = false
            };

            _window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                ResizeMode = ResizeMode.NoResize,
                Width = SystemParameters.PrimaryScreenWidth,
                Height = SystemParameters.PrimaryScreenHeight,
                Left = 0,
                Top = 0,
                Content = hint,
                Visibility = Visibility.Hidden
            };
            _window.SourceInitialized += (sender, args) => ApplyClickThrough();
        }

        public void Show(string text, Rect screen)
        {
            _text.Text = text ?? string.Empty;
            // Recomputed per show so the hint follows the applied game's layout
            // and self-heals after resolution or screen-layout changes.
            if (screen.IsEmpty || screen.Width <= 0 || screen.Height <= 0)
            {
                // The hint never straddles monitors, so even a degenerate rect
                // falls back to the primary screen, not the combined desktop.
                screen = new Rect(
                    0,
                    0,
                    SystemParameters.PrimaryScreenWidth,
                    SystemParameters.PrimaryScreenHeight);
            }

            _window.Left = screen.Left;
            _window.Top = screen.Top;
            _window.Width = screen.Width;
            _window.Height = screen.Height;
            if (!_shown)
            {
                _window.Show();
                _shown = true;
            }

            _window.Visibility = Visibility.Visible;
        }

        public void Hide()
        {
            _window.Visibility = Visibility.Hidden;
        }

        public void Close()
        {
            _window.Close();
        }

        private void ApplyClickThrough()
        {
            IntPtr hwnd = new WindowInteropHelper(_window).Handle;
            int exStyle = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(
                hwnd,
                GwlExStyle,
                exStyle | WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate);
        }
    }
}
