using System;
using System.Text;

namespace AsciiEngine
{
    public sealed class ConsoleRenderer
    {
        public int Width { get; private set; }
        public int Height { get; private set; }

        private char[] _chars;
        private Color[] _fg;
        private Color[] _bg;
        private double[] _z;

        // Dirty span per row (min/max X)
        private int[] _dirtyMinX;
        private int[] _dirtyMaxX;

        private readonly StringBuilder _sb = new StringBuilder(16384);

        public ConsoleRenderer(int w, int h)
        {
            Resize(w, h);
        }

        public void Resize(int w, int h)
        {
            Width = w;
            Height = h;

            int n = w * h;
            _chars = new char[n];
            _fg = new Color[n];
            _bg = new Color[n];
            _z = new double[n];

            _dirtyMinX = new int[h];
            _dirtyMaxX = new int[h];
            ResetDirtySpans(fullDirty: true);

            // RGB-first default clear
            Clear(' ', Colors.DefaultFg, Colors.DefaultBg);
        }

        private void ResetDirtySpans(bool fullDirty)
        {
            for (int y = 0; y < Height; y++)
            {
                if (fullDirty)
                {
                    _dirtyMinX[y] = 0;
                    _dirtyMaxX[y] = Width - 1;
                }
                else
                {
                    _dirtyMinX[y] = int.MaxValue;
                    _dirtyMaxX[y] = int.MinValue;
                }
            }
        }

        // ---------- RGB-first API ----------
        public void Clear(char c, Color fg, Color bg)
        {
            Array.Fill(_chars, c);

            // Normalize to RGB24 so everything downstream is truecolor.
            Color fgc = NormalizeColor(fg);
            Color bgc = NormalizeColor(bg);

            Array.Fill(_fg, fgc);
            Array.Fill(_bg, bgc);
            Array.Fill(_z, double.PositiveInfinity);

            ResetDirtySpans(fullDirty: true);
        }

        // Smaller z = closer (wins)
        public void Set(int x, int y, char c, Color fg, Color bg, double z)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;

            int idx = y * Width + x;
            if (z > _z[idx]) return;

            // Normalize to RGB24 (RGB-first policy)
            fg = NormalizeColor(fg);
            bg = NormalizeColor(bg);

            if (_chars[idx] == c && _fg[idx] == fg && _bg[idx] == bg && _z[idx] == z)
                return;

            _chars[idx] = c;
            _fg[idx] = fg;
            _bg[idx] = bg;
            _z[idx] = z;

            if (x < _dirtyMinX[y]) _dirtyMinX[y] = x;
            if (x > _dirtyMaxX[y]) _dirtyMaxX[y] = x;
        }

        public void DrawString(int x, int y, string s, Color fg, Color bg, double z)
        {
            if (string.IsNullOrEmpty(s)) return;
            if ((uint)y >= (uint)Height) return;

            for (int i = 0; i < s.Length; i++)
            {
                int xx = x + i;
                if ((uint)xx >= (uint)Width) continue;
                Set(xx, y, s[i], fg, bg, z);
            }
        }

        public void DrawRect(int x, int y, int w, int h, char c, Color fg, Color bg, double z)
        {
            if (w <= 0 || h <= 0) return;
            for (int i = 0; i < w; i++)
            {
                Set(x + i, y, c, fg, bg, z);
                Set(x + i, y + h - 1, c, fg, bg, z);
            }
            for (int j = 0; j < h; j++)
            {
                Set(x, y + j, c, fg, bg, z);
                Set(x + w - 1, y + j, c, fg, bg, z);
            }
        }

        public void FillRect(int x, int y, int w, int h, char c, Color fg, Color bg, double z)
        {
            if (w <= 0 || h <= 0) return;
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    Set(xx, yy, c, fg, bg, z);
        }

        public void DrawSprite(int x, int y, AsciiSprite spr, Color fg, Color bg, double z)
        {
            for (int sy = 0; sy < spr.H; sy++)
            {
                int yy = y + sy;
                if ((uint)yy >= (uint)Height) continue;

                for (int sx = 0; sx < spr.W; sx++)
                {
                    int xx = x + sx;
                    if ((uint)xx >= (uint)Width) continue;

                    char ch = spr.Get(sx, sy);

                    if (spr.TransparentChar != '\0' && ch == spr.TransparentChar)
                        continue;

                    Set(xx, yy, ch, fg, bg, z);
                }
            }
        }

        public void DrawSpriteCentered(int cx, int cy, AsciiSprite spr, Color fg, Color bg, double z)
        {
            int x = cx - spr.W / 2;
            int y = cy - spr.H / 2;
            DrawSprite(x, y, spr, fg, bg, z);
        }

        public void DrawLine(int x0, int y0, int x1, int y1, char c, Color fg, Color bg, double z)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                Set(x0, y0, c, fg, bg, z);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public bool TryGet(int x, int y, out char c, out Color fg, out Color bg, out double z)
        {
            c = default;
            fg = default;
            bg = default;
            z = default;

            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return false;

            int idx = y * Width + x;
            c = _chars[idx];
            fg = _fg[idx];
            bg = _bg[idx];
            z = _z[idx];
            return true;
        }

        // Convenience: fastest when you only need FG/BG
        public bool TryGetColors(int x, int y, out Color fg, out Color bg)
        {
            fg = default;
            bg = default;

            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return false;

            int idx = y * Width + x;
            fg = _fg[idx];
            bg = _bg[idx];
            return true;
        }


        // ---------- Legacy overloads (kept to avoid breaking older code) ----------
        [Obsolete("Deprecated: use Color/Colors (RGB) overloads.", false)]
        public void Clear(char c, AnsiColor fg, AnsiColor bg)
            => Clear(c, (Color)fg, (Color)bg);

        [Obsolete("Deprecated: use Color/Colors (RGB) overloads.", false)]
        public void Set(int x, int y, char c, AnsiColor fg, AnsiColor bg, double z)
            => Set(x, y, c, (Color)fg, (Color)bg, z);

        [Obsolete("Deprecated: use Color/Colors (RGB) overloads.", false)]
        public void DrawString(int x, int y, string s, AnsiColor fg, AnsiColor bg, double z)
            => DrawString(x, y, s, (Color)fg, (Color)bg, z);

        [Obsolete("Deprecated: use Color/Colors (RGB) overloads.", false)]
        public void DrawRect(int x, int y, int w, int h, char c, AnsiColor fg, AnsiColor bg, double z)
            => DrawRect(x, y, w, h, c, (Color)fg, (Color)bg, z);

        [Obsolete("Deprecated: use Color/Colors (RGB) overloads.", false)]
        public void FillRect(int x, int y, int w, int h, char c, AnsiColor fg, AnsiColor bg, double z)
            => FillRect(x, y, w, h, c, (Color)fg, (Color)bg, z);

        [Obsolete("Deprecated: use Color/Colors (RGB) overloads.", false)]
        public void DrawSprite(int x, int y, AsciiSprite spr, AnsiColor fg, AnsiColor bg, double z)
            => DrawSprite(x, y, spr, (Color)fg, (Color)bg, z);

        [Obsolete("Deprecated: use Color/Colors (RGB) overloads.", false)]
        public void DrawSpriteCentered(int cx, int cy, AsciiSprite spr, AnsiColor fg, AnsiColor bg, double z)
            => DrawSpriteCentered(cx, cy, spr, (Color)fg, (Color)bg, z);

        [Obsolete("Deprecated: use Color/Colors (RGB) overloads.", false)]
        public void DrawLine(int x0, int y0, int x1, int y1, char c, AnsiColor fg, AnsiColor bg, double z)
            => DrawLine(x0, y0, x1, y1, c, (Color)fg, (Color)bg, z);

        public void Present()
        {
            _sb.Clear();

            int rough = Width * Height;
            if (rough > 0) _sb.EnsureCapacity(Math.Min(rough * 4, 2_000_000));

            Color curFg = default;
            Color curBg = default;
            bool hasFg = false, hasBg = false;

            for (int y = 0; y < Height; y++)
            {
                int minX = _dirtyMinX[y];
                int maxX = _dirtyMaxX[y];

                if (maxX < minX) continue;

                Ansi.AppendCursorPos(_sb, y + 1, minX + 1);

                int row = y * Width;
                for (int x = minX; x <= maxX; x++)
                {
                    int idx = row + x;

                    Color fg = _fg[idx];
                    Color bg = _bg[idx];

                    if (!hasFg || fg != curFg)
                    {
                        Ansi.AppendFg(_sb, fg);
                        curFg = fg;
                        hasFg = true;
                    }
                    if (!hasBg || bg != curBg)
                    {
                        Ansi.AppendBg(_sb, bg);
                        curBg = bg;
                        hasBg = true;
                    }

                    _sb.Append(_chars[idx]);
                }

                _dirtyMinX[y] = int.MaxValue;
                _dirtyMaxX[y] = int.MinValue;
            }

            _sb.Append(Ansi.Reset);
            Console.Write(_sb.ToString());
        }

        private static Color NormalizeColor(Color color)
            => color.IsRgb24 ? color : ColorUtils.ToRgbColor(color);
    }
}
