using AsciiEngine;
using Xunit;

namespace AsciiEngine.Tests
{
    public class PixelRendererTests
    {
        [Fact]
        public void Clear_FillsBuffer()
        {
            PixelRenderer renderer = new PixelRenderer(4, 3);
            Color color = Color.FromRgb(10, 20, 30);

            renderer.Clear(color);

            ReadOnlySpan<byte> pixels = renderer.Pixels;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                Assert.Equal(10, pixels[i]);
                Assert.Equal(20, pixels[i + 1]);
                Assert.Equal(30, pixels[i + 2]);
                Assert.Equal(255, pixels[i + 3]);
            }
        }

        [Fact]
        public void SetPixel_WritesExpectedIndex()
        {
            PixelRenderer renderer = new PixelRenderer(3, 2);
            renderer.Clear(Colors.Black);

            renderer.SetPixel(1, 1, Color.FromRgb(100, 110, 120));

            int idx = (1 * renderer.Width + 1) * 4;
            ReadOnlySpan<byte> pixels = renderer.Pixels;
            Assert.Equal(100, pixels[idx]);
            Assert.Equal(110, pixels[idx + 1]);
            Assert.Equal(120, pixels[idx + 2]);
            Assert.Equal(255, pixels[idx + 3]);
        }

        [Fact]
        public void SetPixel_OutOfBounds_DoesNotThrowAndDoesNotWrite()
        {
            PixelRenderer renderer = new PixelRenderer(2, 2);
            renderer.Clear(Color.FromRgb(1, 2, 3));

            byte[] before = renderer.Pixels.ToArray();
            renderer.SetPixel(-1, -1, Color.FromRgb(200, 200, 200));
            renderer.SetPixel(5, 5, Color.FromRgb(200, 200, 200));

            Assert.Equal(before, renderer.Pixels.ToArray());
        }

        [Fact]
        public void Resize_UpdatesDimensionsAndBufferLength()
        {
            PixelRenderer renderer = new PixelRenderer(2, 2);
            renderer.Resize(5, 4);

            Assert.Equal(5, renderer.Width);
            Assert.Equal(4, renderer.Height);
            Assert.Equal(5 * 4 * 4, renderer.BufferLength);
        }

        [Fact]
        public void DrawLine_BresenhamWritesAtLeastEndpoints()
        {
            PixelRenderer renderer = new PixelRenderer(6, 6);
            renderer.Clear(Colors.Black);

            renderer.DrawLine(1, 1, 4, 4, Color.FromRgb(250, 0, 0));

            int startIdx = (1 * renderer.Width + 1) * 4;
            int endIdx = (4 * renderer.Width + 4) * 4;

            ReadOnlySpan<byte> pixels = renderer.Pixels;
            Assert.Equal(250, pixels[startIdx]);
            Assert.Equal(250, pixels[endIdx]);
        }
    }
}
