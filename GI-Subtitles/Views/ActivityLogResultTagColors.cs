using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using GI_Subtitles.Core.Overlay;

namespace GI_Subtitles.Views
{
    /// <summary>
    /// Category colors for the result column (ADR 0016): Paul Tol high-contrast
    /// triad for in-cell stripe scan (OCR cool, 原文 warm, 译文 rose). Stripe
    /// hexes are independent of Video.xaml preview badge Foregrounds.
    /// <see cref="BrushFor"/> returns null for
    /// <see cref="ActivityLogResultTag.None"/> (no stripe). The per-line stripe
    /// binds these brushes via <c>x:Static</c>; tag text stays default
    /// foreground.
    /// </summary>
    public static class ActivityLogResultTagColors
    {
        private static readonly Dictionary<ActivityLogResultTag, SolidColorBrush> Brushes =
            new Dictionary<ActivityLogResultTag, SolidColorBrush>
            {
                { ActivityLogResultTag.Ocr, Frozen(ParseHex("#004488")) },
                { ActivityLogResultTag.Original, Frozen(ParseHex("#DDAA33")) },
                { ActivityLogResultTag.Translation, Frozen(ParseHex("#BB5566")) }
            };

        /// <summary>Frozen OCR stripe brush for XAML <c>x:Static</c>.</summary>
        public static Brush Ocr
        {
            get { return BrushFor(ActivityLogResultTag.Ocr); }
        }

        /// <summary>Frozen source stripe brush for XAML <c>x:Static</c>.</summary>
        public static Brush Original
        {
            get { return BrushFor(ActivityLogResultTag.Original); }
        }

        /// <summary>Frozen translation stripe brush for XAML <c>x:Static</c>.</summary>
        public static Brush Translation
        {
            get { return BrushFor(ActivityLogResultTag.Translation); }
        }

        /// <summary>A frozen, shareable brush of the category color, or null
        /// when the line has no stripe (untagged). Frozen brushes are safe to
        /// hand to any element on any thread.</summary>
        public static Brush BrushFor(ActivityLogResultTag tag)
        {
            SolidColorBrush brush;
            return Brushes.TryGetValue(tag, out brush) ? brush : null;
        }

        private static SolidColorBrush ParseHex(string hex)
        {
            byte r = byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber);
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private static SolidColorBrush Frozen(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
