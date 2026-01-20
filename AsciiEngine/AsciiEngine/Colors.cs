namespace AsciiEngine
{
    /// <summary>
    /// RGB-first color constants for the engine.
    /// Prefer these over Ansi/AnsiColor going forward.
    /// </summary>
    public static class Colors
    {
        // Core
        public static readonly Color Black = Color.FromRgb(0, 0, 0);
        public static readonly Color White = Color.FromRgb(229, 229, 229);

        // ANSI-like set (matches the mapping used in ColorUtils)
        public static readonly Color Red = Color.FromRgb(205, 49, 49);
        public static readonly Color Green = Color.FromRgb(13, 188, 121);
        public static readonly Color Yellow = Color.FromRgb(229, 229, 16);
        public static readonly Color Blue = Color.FromRgb(36, 114, 200);
        public static readonly Color Magenta = Color.FromRgb(188, 63, 188);
        public static readonly Color Cyan = Color.FromRgb(17, 168, 205);

        // Bright variants
        public static readonly Color BrightBlack = Color.FromRgb(102, 102, 102);
        public static readonly Color BrightRed = Color.FromRgb(241, 76, 76);
        public static readonly Color BrightGreen = Color.FromRgb(35, 209, 139);
        public static readonly Color BrightYellow = Color.FromRgb(245, 245, 67);
        public static readonly Color BrightBlue = Color.FromRgb(59, 142, 234);
        public static readonly Color BrightMagenta = Color.FromRgb(214, 112, 214);
        public static readonly Color BrightCyan = Color.FromRgb(41, 184, 219);
        public static readonly Color BrightWhite = Color.FromRgb(255, 255, 255);

        // Grays
        public static readonly Color Gray = Color.FromRgb(128, 128, 128);
        public static readonly Color DarkGray = Color.FromRgb(64, 64, 64);
        public static readonly Color LightGray = Color.FromRgb(192, 192, 192);

        // Useful defaults
        public static readonly Color DefaultFg = BrightWhite;
        public static readonly Color DefaultBg = Black;
    }
}
