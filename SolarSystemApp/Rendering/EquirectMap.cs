// Rendering/EquirectMap.cs
using System;
using System.IO;
using AsciiEngine;
using SolarSystemApp.Util;

namespace SolarSystemApp.Rendering
{
    internal sealed class EquirectMap
    {
        public readonly int Width;
        public readonly int Height;
        private readonly byte[] _rgb;     // flat RGB array, row-major, 3 bytes per texel
        private readonly char[] _glyphs;  // ASCII glyph per texel
        private readonly double[] _emissive; // emissive intensity per texel (0..1)
        private readonly byte[] _emissiveRgb; // emissive color per texel, 3 bytes

        public EquirectMap(int width, int height)
        {
            Width = width;
            Height = height;
            _rgb = new byte[width * height * 3];
            _glyphs = new char[width * height];
            _emissive = new double[width * height];
            _emissiveRgb = new byte[width * height * 3];
        }

        public void SetTexel(int x, int y, byte r, byte g, byte b, char glyph,
            double emissive01, byte emR, byte emG, byte emB)
        {
            int i = (y * Width + x);
            int i3 = i * 3;
            _rgb[i3] = r;
            _rgb[i3 + 1] = g;
            _rgb[i3 + 2] = b;
            _glyphs[i] = glyph;
            _emissive[i] = emissive01;
            _emissiveRgb[i3] = emR;
            _emissiveRgb[i3 + 1] = emG;
            _emissiveRgb[i3 + 2] = emB;
        }

        public void Sample(double u, double v,
            out byte r, out byte g, out byte b,
            out char glyph,
            out double emissive01,
            out byte emR, out byte emG, out byte emB)
        {
            // Wrap u to [0,1), clamp v to [0,1]
            u = u - Math.Floor(u);
            v = MathUtil.Clamp(v, 0.0, 1.0 - 1e-9);

            double fx = u * Width;
            double fy = v * Height;

            int x0 = (int)fx;
            int y0 = (int)fy;
            int x1 = (x0 + 1) % Width; // wrap horizontally
            int y1 = Math.Min(y0 + 1, Height - 1);

            double tx = fx - x0;
            double ty = fy - y0;

            // Bilinear interpolation for RGB
            int i00 = (y0 * Width + x0) * 3;
            int i10 = (y0 * Width + x1) * 3;
            int i01 = (y1 * Width + x0) * 3;
            int i11 = (y1 * Width + x1) * 3;

            double invTx = 1.0 - tx;
            double invTy = 1.0 - ty;
            double w00 = invTx * invTy;
            double w10 = tx * invTy;
            double w01 = invTx * ty;
            double w11 = tx * ty;

            r = (byte)MathUtil.Clamp(
                _rgb[i00] * w00 + _rgb[i10] * w10 + _rgb[i01] * w01 + _rgb[i11] * w11, 0, 255);
            g = (byte)MathUtil.Clamp(
                _rgb[i00 + 1] * w00 + _rgb[i10 + 1] * w10 + _rgb[i01 + 1] * w01 + _rgb[i11 + 1] * w11, 0, 255);
            b = (byte)MathUtil.Clamp(
                _rgb[i00 + 2] * w00 + _rgb[i10 + 2] * w10 + _rgb[i01 + 2] * w01 + _rgb[i11 + 2] * w11, 0, 255);

            // Nearest-neighbor for glyph (can't interpolate characters)
            int nearest = (ty < 0.5) ? (y0 * Width + ((tx < 0.5) ? x0 : x1))
                                     : (y1 * Width + ((tx < 0.5) ? x0 : x1));
            glyph = _glyphs[nearest];

            // Bilinear for emissive intensity
            int e00 = y0 * Width + x0;
            int e10 = y0 * Width + x1;
            int e01 = y1 * Width + x0;
            int e11 = y1 * Width + x1;
            emissive01 = _emissive[e00] * w00 + _emissive[e10] * w10
                       + _emissive[e01] * w01 + _emissive[e11] * w11;

            // Bilinear for emissive color
            emR = (byte)MathUtil.Clamp(
                _emissiveRgb[i00] * w00 + _emissiveRgb[i10] * w10 + _emissiveRgb[i01] * w01 + _emissiveRgb[i11] * w11, 0, 255);
            emG = (byte)MathUtil.Clamp(
                _emissiveRgb[i00 + 1] * w00 + _emissiveRgb[i10 + 1] * w10 + _emissiveRgb[i01 + 1] * w01 + _emissiveRgb[i11 + 1] * w11, 0, 255);
            emB = (byte)MathUtil.Clamp(
                _emissiveRgb[i00 + 2] * w00 + _emissiveRgb[i10 + 2] * w10 + _emissiveRgb[i01 + 2] * w01 + _emissiveRgb[i11 + 2] * w11, 0, 255);
        }

        private const byte FormatVersion = 2;

        public void WriteToDisk(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536);
            // Header: version (1) + width (2) + height (2) = 5 bytes
            fs.WriteByte(FormatVersion);
            fs.WriteByte((byte)(Width & 0xFF));
            fs.WriteByte((byte)((Width >> 8) & 0xFF));
            fs.WriteByte((byte)(Height & 0xFF));
            fs.WriteByte((byte)((Height >> 8) & 0xFF));
            // Pixel data
            fs.Write(_rgb, 0, _rgb.Length);
            // Glyph data (as UTF-16 LE, 2 bytes per char)
            for (int i = 0; i < _glyphs.Length; i++)
            {
                fs.WriteByte((byte)(_glyphs[i] & 0xFF));
                fs.WriteByte((byte)((_glyphs[i] >> 8) & 0xFF));
            }
            // Emissive intensity (as 1 byte each, mapped 0..1 → 0..255)
            for (int i = 0; i < _emissive.Length; i++)
                fs.WriteByte((byte)MathUtil.Clamp(_emissive[i] * 255.0, 0, 255));
            // Emissive RGB
            fs.Write(_emissiveRgb, 0, _emissiveRgb.Length);
        }

        public static EquirectMap? ReadFromDisk(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
                int ver = fs.ReadByte();
                if (ver != FormatVersion) return null;

                int wLo = fs.ReadByte(), wHi = fs.ReadByte();
                int hLo = fs.ReadByte(), hHi = fs.ReadByte();
                int w = wLo | (wHi << 8);
                int h = hLo | (hHi << 8);

                var map = new EquirectMap(w, h);

                if (fs.Read(map._rgb, 0, map._rgb.Length) != map._rgb.Length) return null;

                for (int i = 0; i < map._glyphs.Length; i++)
                {
                    int lo = fs.ReadByte(), hi = fs.ReadByte();
                    if (lo < 0 || hi < 0) return null;
                    map._glyphs[i] = (char)(lo | (hi << 8));
                }

                for (int i = 0; i < map._emissive.Length; i++)
                {
                    int b = fs.ReadByte();
                    if (b < 0) return null;
                    map._emissive[i] = b / 255.0;
                }

                if (fs.Read(map._emissiveRgb, 0, map._emissiveRgb.Length) != map._emissiveRgb.Length) return null;

                return map;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
