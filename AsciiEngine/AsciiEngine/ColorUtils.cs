using System;

namespace AsciiEngine
{
    public static class ColorUtils
    {
        // ===== Legacy mapping (still useful during migration) =====
        [Obsolete("Deprecated: use RGB-first colors; this exists only during migration.", false)]
        public static byte To256(AnsiColor c)
        {
            return c switch
            {
                AnsiColor.Black => 0,
                AnsiColor.Red => 1,
                AnsiColor.Green => 2,
                AnsiColor.Yellow => 3,
                AnsiColor.Blue => 4,
                AnsiColor.Magenta => 5,
                AnsiColor.Cyan => 6,
                AnsiColor.White => 7,

                AnsiColor.BrightBlack => 8,
                AnsiColor.BrightRed => 9,
                AnsiColor.BrightGreen => 10,
                AnsiColor.BrightYellow => 11,
                AnsiColor.BrightBlue => 12,
                AnsiColor.BrightMagenta => 13,
                AnsiColor.BrightCyan => 14,
                AnsiColor.BrightWhite => 15,
                _ => 7
            };
        }

        public static Color Shade256(byte baseIdx, int delta)
        {
            int v = baseIdx + delta;
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return Color.From256((byte)v);
        }

        [Obsolete("Deprecated: use RGB-first shading; this exists only during migration.", false)]
        public static Color Shade(AnsiColor baseColor, int delta256)
            => Shade256(To256(baseColor), delta256);

        // ===== RGB-first shading (stable) =====
        // light01 in [0..1] (Lambert). Preserves hue; no palette jumps.
        public static Color Shade(Color baseColor, double light01)
        {
            light01 = Clamp01(light01);

            const double ambient = 0.18;
            double k = ambient + (1.0 - ambient) * light01;

            (byte r, byte g, byte b) = ToRgbBytes(baseColor);

            byte rr = Mul(r, k);
            byte gg = Mul(g, k);
            byte bb = Mul(b, k);

            return Color.FromRgb(rr, gg, bb);
        }

        // ===== NEW: public conversion helpers =====
        public static (byte r, byte g, byte b) ToRgbBytes(Color c)
        {
            return c.Mode switch
            {
                ColorMode.Rgb24 => Rgb24ToTuple(c.Value),
                ColorMode.Ansi16 => Ansi16ToRgb((AnsiColor)c.Value),
                ColorMode.Ansi256 => Ansi256ToRgb((byte)(c.Value & 0xFF)),
                _ => (255, 255, 255)
            };
        }

        public static Color ToRgbColor(Color c)
        {
            (byte r, byte g, byte b) = ToRgbBytes(c);
            return Color.FromRgb(r, g, b);
        }

        // ---------- helpers ----------
        private static double Clamp01(double x) => (x < 0) ? 0 : (x > 1 ? 1 : x);

        private static byte Mul(byte c, double k)
        {
            int v = (int)Math.Round(c * k);
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return (byte)v;
        }

        private static (byte r, byte g, byte b) Rgb24ToTuple(int rgb)
        {
            byte r = (byte)((rgb >> 16) & 0xFF);
            byte g = (byte)((rgb >> 8) & 0xFF);
            byte b = (byte)(rgb & 0xFF);
            return (r, g, b);
        }

        private static (byte r, byte g, byte b) Ansi16ToRgb(AnsiColor c)
        {
            // Reasonable defaults.
            return c switch
            {
                AnsiColor.Black => (0, 0, 0),
                AnsiColor.Red => (205, 49, 49),
                AnsiColor.Green => (13, 188, 121),
                AnsiColor.Yellow => (229, 229, 16),
                AnsiColor.Blue => (36, 114, 200),
                AnsiColor.Magenta => (188, 63, 188),
                AnsiColor.Cyan => (17, 168, 205),
                AnsiColor.White => (229, 229, 229),

                AnsiColor.BrightBlack => (102, 102, 102),
                AnsiColor.BrightRed => (241, 76, 76),
                AnsiColor.BrightGreen => (35, 209, 139),
                AnsiColor.BrightYellow => (245, 245, 67),
                AnsiColor.BrightBlue => (59, 142, 234),
                AnsiColor.BrightMagenta => (214, 112, 214),
                AnsiColor.BrightCyan => (41, 184, 219),
                AnsiColor.BrightWhite => (255, 255, 255),
                _ => (229, 229, 229)
            };
        }

        private static (byte r, byte g, byte b) Ansi256ToRgb(byte idx)
        {
            if (idx < 16)
                return Ansi16ToRgb((AnsiColor)(idx < 8 ? 30 + idx : 90 + (idx - 8)));

            if (idx >= 232)
            {
                byte gray = (byte)(8 + (idx - 232) * 10);
                return (gray, gray, gray);
            }

            int i = idx - 16;
            int br = i / 36;
            int bg = (i / 6) % 6;
            int bb = i % 6;

            byte r = Cube(br);
            byte g = Cube(bg);
            byte b = Cube(bb);
            return (r, g, b);
        }

        private static byte Cube(int c)
        {
            return c switch
            {
                0 => 0,
                1 => 95,
                2 => 135,
                3 => 175,
                4 => 215,
                _ => 255
            };
        }
    }
}
