using System;

namespace AsciiEngine
{
    public enum ColorMode : byte
    {
        Ansi16 = 0,   // deprecated (old AnsiColor values)
        Ansi256 = 1,  // xterm 0..255
        Rgb24 = 2     // 0xRRGGBB
    }

    public readonly struct Color : IEquatable<Color>
    {
        public readonly ColorMode Mode;
        public readonly int Value;

        public Color(ColorMode mode, int value)
        {
            Mode = mode;
            Value = (mode == ColorMode.Rgb24) ? (value & 0x00FFFFFF) : value;
        }

        public bool IsRgb24 => Mode == ColorMode.Rgb24;

        // ----- RGB-first constructors -----
        public static Color FromRgb(byte r, byte g, byte b)
            => new Color(ColorMode.Rgb24, (r << 16) | (g << 8) | b);

        public static Color FromRgb(int rgb24)
            => new Color(ColorMode.Rgb24, rgb24 & 0x00FFFFFF);

        // ----- Legacy constructors (kept for compatibility) -----
        [Obsolete("Deprecated: prefer RGB colors via Color.FromRgb / Colors.*", false)]
        public static Color FromAnsi16(AnsiColor c) => new Color(ColorMode.Ansi16, (int)c);

        public static Color From256(byte idx) => new Color(ColorMode.Ansi256, idx);

        // Implicit conversion so old code keeps working (deprecated).
        [Obsolete("Deprecated: prefer RGB colors via Color.FromRgb / Colors.*", false)]
        public static implicit operator Color(AnsiColor c) => FromAnsi16(c);

        public bool Equals(Color other) => Mode == other.Mode && Value == other.Value;
        public override bool Equals(object obj) => obj is Color c && Equals(c);
        public override int GetHashCode() => ((int)Mode * 397) ^ Value;

        public static bool operator ==(Color a, Color b) => a.Mode == b.Mode && a.Value == b.Value;
        public static bool operator !=(Color a, Color b) => !(a == b);
    }
}
