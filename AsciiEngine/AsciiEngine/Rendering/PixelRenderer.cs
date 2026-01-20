using System;

namespace AsciiEngine
{
    public sealed class PixelRenderer
    {
        public int Width { get; private set; }
        public int Height { get; private set; }

        private byte[] _buffer = Array.Empty<byte>();

        public int BufferLength => _buffer.Length;

        internal byte[] Buffer => _buffer;
        public ReadOnlySpan<byte> Pixels => _buffer;

        public PixelRenderer(int width, int height)
        {
            Resize(width, height);
        }

        public void Resize(int width, int height)
        {
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);

            Width = w;
            Height = h;

            _buffer = new byte[w * h * 4];

            Diagnostics.Log($"[AsciiEngine] PixelRenderer resize: size={Width}x{Height}, bufferLen={_buffer.Length}.");
        }

        public void Clear(Color bg)
        {
            (byte r, byte g, byte b) = ColorUtils.ToRgbBytes(bg);
            for (int i = 0; i < _buffer.Length; i += 4)
            {
                _buffer[i] = r;
                _buffer[i + 1] = g;
                _buffer[i + 2] = b;
                _buffer[i + 3] = 255;
            }
        }

        public void SetPixel(int x, int y, Color color)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;

            int idx = (y * Width + x) * 4;
            (byte r, byte g, byte b) = ColorUtils.ToRgbBytes(color);
            _buffer[idx] = r;
            _buffer[idx + 1] = g;
            _buffer[idx + 2] = b;
            _buffer[idx + 3] = 255;
        }

        public void FillRect(int x, int y, int w, int h, Color color)
        {
            if (w <= 0 || h <= 0) return;

            int x0 = Math.Max(0, x);
            int y0 = Math.Max(0, y);
            int x1 = Math.Min(Width, x + w);
            int y1 = Math.Min(Height, y + h);

            if (x0 >= x1 || y0 >= y1) return;

            (byte r, byte g, byte b) = ColorUtils.ToRgbBytes(color);

            for (int yy = y0; yy < y1; yy++)
            {
                int row = (yy * Width + x0) * 4;
                for (int xx = x0; xx < x1; xx++)
                {
                    _buffer[row] = r;
                    _buffer[row + 1] = g;
                    _buffer[row + 2] = b;
                    _buffer[row + 3] = 255;
                    row += 4;
                }
            }
        }

        public void DrawRect(int x, int y, int w, int h, Color color)
        {
            if (w <= 0 || h <= 0) return;

            int x1 = x + w - 1;
            int y1 = y + h - 1;

            DrawLine(x, y, x1, y, color);
            DrawLine(x, y, x, y1, color);
            DrawLine(x1, y, x1, y1, color);
            DrawLine(x, y1, x1, y1, color);
        }

        public void DrawLine(int x0, int y0, int x1, int y1, Color color)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                SetPixel(x0, y0, color);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public void DrawCircle(int cx, int cy, int r, Color color)
        {
            if (r <= 0) return;

            int x = r;
            int y = 0;
            int err = 0;

            while (x >= y)
            {
                SetPixel(cx + x, cy + y, color);
                SetPixel(cx + y, cy + x, color);
                SetPixel(cx - y, cy + x, color);
                SetPixel(cx - x, cy + y, color);
                SetPixel(cx - x, cy - y, color);
                SetPixel(cx - y, cy - x, color);
                SetPixel(cx + y, cy - x, color);
                SetPixel(cx + x, cy - y, color);

                y++;
                if (err <= 0)
                {
                    err += 2 * y + 1;
                }
                else
                {
                    x--;
                    err += 2 * (y - x) + 1;
                }
            }
        }

        public void FillCircle(int cx, int cy, int r, Color color)
        {
            if (r <= 0) return;

            int rr = r * r;
            for (int y = -r; y <= r; y++)
            {
                int yy = y * y;
                int span = (int)Math.Sqrt(Math.Max(0, rr - yy));
                for (int x = -span; x <= span; x++)
                {
                    SetPixel(cx + x, cy + y, color);
                }
            }
        }
    }
}
