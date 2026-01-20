using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using StbTrueTypeSharp;

namespace AsciiEngine
{
    public sealed class GlyphAtlas : IDisposable
    {
        public const int FirstChar = 32;
        public const int LastChar = 126;

        public readonly struct GlyphInfo
        {
            public readonly float U0;
            public readonly float V0;
            public readonly float U1;
            public readonly float V1;

            public GlyphInfo(float u0, float v0, float u1, float v1)
            {
                U0 = u0;
                V0 = v0;
                U1 = u1;
                V1 = v1;
            }
        }

        public int TextureId { get; private set; }
        public int TextureWidth { get; }
        public int TextureHeight { get; }
        public int CellWidth { get; }
        public int CellHeight { get; }

        private readonly GlyphInfo[] _glyphs;
        private bool _disposed;

        public GlyphAtlas(string fontPath, int pixelHeight, int padding = 1, int columns = 16)
        {
            if (string.IsNullOrWhiteSpace(fontPath))
                throw new ArgumentException("Font path must be provided.", nameof(fontPath));
            if (!File.Exists(fontPath))
                throw new FileNotFoundException("Font file not found.", fontPath);
            if (pixelHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(pixelHeight));
            if (columns <= 0)
                throw new ArgumentOutOfRangeException(nameof(columns));

            byte[] fontData = File.ReadAllBytes(fontPath);
            _glyphs = new GlyphInfo[LastChar - FirstChar + 1];

            int atlasWidth;
            int atlasHeight;
            byte[] atlasPixels;

            unsafe
            {
                stbtt_fontinfo font;
                fixed (byte* fontPtr = fontData)
                {
                    if (StbTrueType.stbtt_InitFont(&font, fontPtr, 0) == 0)
                        throw new InvalidOperationException("Failed to initialize font data.");

                    float scale = StbTrueType.stbtt_ScaleForPixelHeight(&font, pixelHeight);

                    int ascent;
                    int descent;
                    int lineGap;
                    StbTrueType.stbtt_GetFontVMetrics(&font, &ascent, &descent, &lineGap);

                    int baseline = (int)MathF.Ceiling(ascent * scale);
                    int lineHeight = (int)MathF.Ceiling((ascent - descent + lineGap) * scale);

                    int advance;
                    int lsb;
                    StbTrueType.stbtt_GetCodepointHMetrics(&font, (int)'M', &advance, &lsb);

                    int cellWidth = (int)MathF.Ceiling(advance * scale) + padding * 2;
                    int cellHeight = lineHeight + padding * 2;

                    CellWidth = Math.Max(1, cellWidth);
                    CellHeight = Math.Max(1, cellHeight);

                    int glyphCount = _glyphs.Length;
                    int rows = (glyphCount + columns - 1) / columns;

                    atlasWidth = columns * CellWidth;
                    atlasHeight = rows * CellHeight;
                    atlasPixels = new byte[atlasWidth * atlasHeight];

                    for (int c = FirstChar; c <= LastChar; c++)
                    {
                        int index = c - FirstChar;
                        int col = index % columns;
                        int row = index / columns;

                        int cellX = col * CellWidth;
                        int cellY = row * CellHeight;

                        int w;
                        int h;
                        int xoff;
                        int yoff;
                        byte* bitmap = StbTrueType.stbtt_GetCodepointBitmap(&font, 0, scale, c, &w, &h, &xoff, &yoff);

                        int xStart = cellX + padding + xoff;
                        int yStart = cellY + padding + baseline + yoff;

                        for (int py = 0; py < h; py++)
                        {
                            for (int px = 0; px < w; px++)
                            {
                                int destX = xStart + px;
                                int destY = yStart + py;
                                if ((uint)destX >= (uint)atlasWidth || (uint)destY >= (uint)atlasHeight)
                                    continue;

                                atlasPixels[destY * atlasWidth + destX] = bitmap[py * w + px];
                            }
                        }

                        StbTrueType.stbtt_FreeBitmap(bitmap, null);

                        float u0 = (float)cellX / atlasWidth;
                        float v0 = (float)cellY / atlasHeight;
                        float u1 = (float)(cellX + CellWidth) / atlasWidth;
                        float v1 = (float)(cellY + CellHeight) / atlasHeight;
                        _glyphs[index] = new GlyphInfo(u0, v0, u1, v1);
                    }
                }
            }

            TextureWidth = atlasWidth;
            TextureHeight = atlasHeight;

            TextureId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, TextureId);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, atlasWidth, atlasHeight, 0, PixelFormat.Red, PixelType.UnsignedByte, atlasPixels);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        public GlyphInfo GetGlyph(char c)
        {
            int index = c - FirstChar;
            if ((uint)index < (uint)_glyphs.Length)
                return _glyphs[index];

            int fallback = '?' - FirstChar;
            return _glyphs[(uint)fallback < (uint)_glyphs.Length ? fallback : 0];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (TextureId != 0)
            {
                GL.DeleteTexture(TextureId);
                TextureId = 0;
            }
        }
    }
}
