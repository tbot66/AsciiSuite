using System;

namespace AsciiEngine
{
    /// <summary>
    /// Simple immutable-ish ASCII sprite used by ConsoleRenderer.DrawSprite().
    /// Stores W*H characters in row-major order.
    /// </summary>
    public sealed class AsciiSprite
    {
        public int W { get; }
        public int H { get; }
        public char TransparentChar { get; }

        private readonly char[] _data;

        /// <summary>
        /// Create a blank sprite filled with fillChar.
        /// </summary>
        public AsciiSprite(int w, int h, char fillChar = ' ', char transparentChar = '\0')
        {
            if (w <= 0) throw new ArgumentOutOfRangeException(nameof(w));
            if (h <= 0) throw new ArgumentOutOfRangeException(nameof(h));

            W = w;
            H = h;
            TransparentChar = transparentChar;

            _data = new char[w * h];
            Array.Fill(_data, fillChar);
        }

        /// <summary>
        /// Create a sprite from explicit data. Data length must be w*h.
        /// </summary>
        public AsciiSprite(int w, int h, char[] data, char transparentChar = '\0')
        {
            if (w <= 0) throw new ArgumentOutOfRangeException(nameof(w));
            if (h <= 0) throw new ArgumentOutOfRangeException(nameof(h));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length != w * h) throw new ArgumentException("data.Length must equal w*h", nameof(data));

            W = w;
            H = h;
            TransparentChar = transparentChar;

            _data = new char[data.Length];
            Array.Copy(data, _data, data.Length);
        }

        /// <summary>
        /// Create a sprite from lines. Width is max line length; missing cells are fillChar.
        /// </summary>
        public AsciiSprite(string[] lines, char fillChar = ' ', char transparentChar = '\0')
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            if (lines.Length == 0) throw new ArgumentException("lines must not be empty", nameof(lines));

            int h = lines.Length;
            int w = 0;
            for (int i = 0; i < lines.Length; i++)
                if (lines[i] != null && lines[i].Length > w) w = lines[i].Length;

            if (w <= 0) w = 1;

            W = w;
            H = h;
            TransparentChar = transparentChar;

            _data = new char[w * h];
            Array.Fill(_data, fillChar);

            for (int y = 0; y < h; y++)
            {
                string line = lines[y] ?? string.Empty;
                int len = Math.Min(line.Length, w);
                for (int x = 0; x < len; x++)
                    _data[y * w + x] = line[x];
            }
        }

        public char Get(int x, int y)
        {
            if ((uint)x >= (uint)W || (uint)y >= (uint)H) return ' ';
            return _data[y * W + x];
        }

        public void Set(int x, int y, char c)
        {
            if ((uint)x >= (uint)W || (uint)y >= (uint)H) return;
            _data[y * W + x] = c;
        }

        public char[] CloneData()
        {
            var copy = new char[_data.Length];
            Array.Copy(_data, copy, _data.Length);
            return copy;
        }
    }
}
