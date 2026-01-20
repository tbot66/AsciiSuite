using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace AsciiEngine
{
    public static class Ansi
    {
        public const string Esc = "\x1b[";
        public const string Home = Esc + "H";
        public const string ClearScreen = Esc + "2J";
        public const string HideCursor = Esc + "?25l";
        public const string ShowCursor = Esc + "?25h";
        public const string EnterAlternateBuffer = Esc + "?1049h";
        public const string ExitAlternateBuffer = Esc + "?1049l";
        public const string Reset = Esc + "0m";

        // Cached common sequences (no per-frame allocations).
        // Index 0..15 maps to ANSI16: 30..37, 90..97
        private static readonly string[] _fg16Cache = new string[16];
        private static readonly string[] _bg16Cache = new string[16];

        // Index 0..255 maps to xterm 256-color
        private static readonly string[] _fg256Cache = new string[256];
        private static readonly string[] _bg256Cache = new string[256];

        static Ansi()
        {
            // ANSI16 cache
            for (int i = 0; i < 8; i++)
            {
                _fg16Cache[i] = Esc + (30 + i).ToString() + "m";
                _bg16Cache[i] = Esc + (40 + i).ToString() + "m";
            }
            for (int i = 0; i < 8; i++)
            {
                _fg16Cache[8 + i] = Esc + (90 + i).ToString() + "m";
                _bg16Cache[8 + i] = Esc + (100 + i).ToString() + "m";
            }

            // ANSI256 cache
            for (int i = 0; i < 256; i++)
            {
                _fg256Cache[i] = Esc + "38;5;" + i.ToString() + "m";
                _bg256Cache[i] = Esc + "48;5;" + i.ToString() + "m";
            }
        }

        public static void Write(string s) => Console.Write(s);

        // Kept for compatibility; now returns cached strings for ANSI16
        public static string Fg(AnsiColor c)
        {
            int idx = MapAnsi16SgrToIndex((int)c);
            return (idx >= 0) ? _fg16Cache[idx] : (Esc + ((int)c).ToString() + "m");
        }

        public static string Bg(AnsiColor c)
        {
            int idx = MapAnsi16SgrToIndex((int)c);
            // Bg uses +10; but our cache already stores correct background SGRs 40..47/100..107
            return (idx >= 0) ? _bg16Cache[idx] : (Esc + (((int)c) + 10).ToString() + "m");
        }

        // Kept for compatibility; now returns cached strings for ANSI256
        public static string Fg256(byte idx) => _fg256Cache[idx];
        public static string Bg256(byte idx) => _bg256Cache[idx];

        public static string CursorPos(int row, int col) => Esc + row + ";" + col + "H";

        public static void EnableVirtualTerminalIfPossible()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            try
            {
                IntPtr hOut = GetStdHandle(STD_OUTPUT_HANDLE);
                uint mode;
                if (!GetConsoleMode(hOut, out mode)) return;
                mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                SetConsoleMode(hOut, mode);
            }
            catch { }
        }

        // Existing API kept. Still returns strings; optimized ConsoleRenderer bypasses this.
        public static string Fg(Color c)
        {
            return c.Mode switch
            {
                ColorMode.Ansi16 => FgFromAnsi16Sgr(c.Value),
                ColorMode.Ansi256 => _fg256Cache[c.Value & 255],
                ColorMode.Rgb24 => FgRgbString(c.Value),
                _ => Esc + "37m"
            };
        }

        public static string Bg(Color c)
        {
            return c.Mode switch
            {
                ColorMode.Ansi16 => BgFromAnsi16Sgr(c.Value),
                ColorMode.Ansi256 => _bg256Cache[c.Value & 255],
                ColorMode.Rgb24 => BgRgbString(c.Value),
                _ => Esc + "40m"
            };
        }

        // ---------- NEW: allocation-free append helpers (hot path) ----------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void AppendCursorPos(StringBuilder sb, int row, int col)
        {
            sb.Append(Esc);
            sb.Append(row);
            sb.Append(';');
            sb.Append(col);
            sb.Append('H');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void AppendFg(StringBuilder sb, Color c)
        {
            switch (c.Mode)
            {
                case ColorMode.Ansi16:
                    AppendFgAnsi16Sgr(sb, c.Value);
                    return;

                case ColorMode.Ansi256:
                    sb.Append(_fg256Cache[c.Value & 255]);
                    return;

                case ColorMode.Rgb24:
                    AppendRgb(sb, isFg: true, c.Value);
                    return;

                default:
                    sb.Append(Esc).Append("37m");
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void AppendBg(StringBuilder sb, Color c)
        {
            switch (c.Mode)
            {
                case ColorMode.Ansi16:
                    AppendBgAnsi16Sgr(sb, c.Value);
                    return;

                case ColorMode.Ansi256:
                    sb.Append(_bg256Cache[c.Value & 255]);
                    return;

                case ColorMode.Rgb24:
                    AppendRgb(sb, isFg: false, c.Value);
                    return;

                default:
                    sb.Append(Esc).Append("40m");
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AppendFgAnsi16Sgr(StringBuilder sb, int sgr)
        {
            int idx = MapAnsi16SgrToIndex(sgr);
            if (idx >= 0)
            {
                sb.Append(_fg16Cache[idx]);
                return;
            }

            // Fallback (should be rare)
            sb.Append(Esc);
            sb.Append(sgr);
            sb.Append('m');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AppendBgAnsi16Sgr(StringBuilder sb, int sgr)
        {
            int idx = MapAnsi16SgrToIndex(sgr);
            if (idx >= 0)
            {
                sb.Append(_bg16Cache[idx]);
                return;
            }

            // Fallback (should be rare)
            sb.Append(Esc);
            sb.Append(sgr + 10);
            sb.Append('m');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AppendRgb(StringBuilder sb, bool isFg, int rgb)
        {
            byte r = (byte)((rgb >> 16) & 0xFF);
            byte g = (byte)((rgb >> 8) & 0xFF);
            byte b = (byte)(rgb & 0xFF);

            sb.Append(Esc);
            sb.Append(isFg ? "38;2;" : "48;2;");
            sb.Append(r);
            sb.Append(';');
            sb.Append(g);
            sb.Append(';');
            sb.Append(b);
            sb.Append('m');
        }

        // ---------- string-return helpers (kept for compatibility) ----------
        private static string FgFromAnsi16Sgr(int sgr)
        {
            int idx = MapAnsi16SgrToIndex(sgr);
            return (idx >= 0) ? _fg16Cache[idx] : (Esc + sgr.ToString() + "m");
        }

        private static string BgFromAnsi16Sgr(int sgr)
        {
            int idx = MapAnsi16SgrToIndex(sgr);
            return (idx >= 0) ? _bg16Cache[idx] : (Esc + (sgr + 10).ToString() + "m");
        }

        private static string FgRgbString(int rgb)
        {
            byte r = (byte)((rgb >> 16) & 0xFF);
            byte g = (byte)((rgb >> 8) & 0xFF);
            byte b = (byte)(rgb & 0xFF);
            return Esc + "38;2;" + r + ";" + g + ";" + b + "m";
        }

        private static string BgRgbString(int rgb)
        {
            byte r = (byte)((rgb >> 16) & 0xFF);
            byte g = (byte)((rgb >> 8) & 0xFF);
            byte b = (byte)(rgb & 0xFF);
            return Esc + "48;2;" + r + ";" + g + ";" + b + "m";
        }

        // Existing private overloads kept (but now cache-backed)
        private static string Fg256(int idx)
        {
            if (idx < 0) idx = 0;
            if (idx > 255) idx = 255;
            return _fg256Cache[idx];
        }

        private static string Bg256(int idx)
        {
            if (idx < 0) idx = 0;
            if (idx > 255) idx = 255;
            return _bg256Cache[idx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int MapAnsi16SgrToIndex(int sgr)
        {
            // 30..37 -> 0..7, 90..97 -> 8..15
            if ((uint)(sgr - 30) <= 7u) return sgr - 30;
            if ((uint)(sgr - 90) <= 7u) return 8 + (sgr - 90);
            return -1;
        }

        private const int STD_OUTPUT_HANDLE = -11;
        private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    }
}
