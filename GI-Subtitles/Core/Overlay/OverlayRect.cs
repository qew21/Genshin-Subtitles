namespace GI_Subtitles.Core.Overlay
{
    public sealed class OverlayRect
    {
        public OverlayRect()
        {
        }

        public OverlayRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; set; }

        public int Y { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public bool IsValid
        {
            get { return Width > 0 && Height > 0; }
        }

        public static OverlayRect Invalid
        {
            get { return new OverlayRect(0, 0, 0, 0); }
        }

        public static bool TryParse(string csv, out OverlayRect rect)
        {
            rect = Invalid;
            if (string.IsNullOrWhiteSpace(csv))
            {
                return false;
            }

            string[] parts = csv.Split(',');
            if (parts.Length != 4)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out int x) ||
                !int.TryParse(parts[1], out int y) ||
                !int.TryParse(parts[2], out int width) ||
                !int.TryParse(parts[3], out int height))
            {
                return false;
            }

            rect = new OverlayRect(x, y, width, height);
            return rect.IsValid;
        }

        public OverlayRect Offset(int deltaX, int deltaY)
        {
            return new OverlayRect(X + deltaX, Y + deltaY, Width, Height);
        }

        public string ToCsv()
        {
            return X + "," + Y + "," + Width + "," + Height;
        }
    }
}
