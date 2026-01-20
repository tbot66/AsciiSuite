using System.Text.RegularExpressions;
using AsciiEngine;
using Xunit;

namespace AsciiEngine.Tests
{
    public class FrameBuilderTests
    {
        [Fact]
        public void BuildFrameString_OutputsFixedGrid()
        {
            int width = 4;
            int height = 3;
            char[] chars = new char[width * height];
            Color[] fg = new Color[width * height];
            Color[] bg = new Color[width * height];

            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = 'A';
                fg[i] = Colors.DefaultFg;
                bg[i] = Colors.DefaultBg;
            }

            string frame = FrameBuilder.BuildFrameString(chars, fg, bg, width, height, unicodeOk: true);
            string stripped = StripAnsi(frame);

            string[] lines = stripped.Split('\n');
            Assert.Equal(height, lines.Length);
            foreach (string line in lines)
            {
                Assert.Equal(width, line.Length);
            }
        }

        [Theory]
        [InlineData('█', '#')]
        [InlineData('▓', '#')]
        [InlineData('▒', '*')]
        [InlineData('░', '.')]
        [InlineData('·', '.')]
        public void Sanitizer_MapsUnicodeToAscii(char input, char expected)
        {
            Assert.Equal(expected, AsciiSanitizer.SanitizeChar(input, unicodeOk: false));
        }

        [Fact]
        public void Sanitizer_NoOpWhenUnicodeOk()
        {
            Assert.Equal('█', AsciiSanitizer.SanitizeChar('█', unicodeOk: true));
        }

        [Theory]
        [InlineData(-10, -5, 1, 1)]
        [InlineData(0, 0, 1, 1)]
        [InlineData(80, 25, 80, 25)]
        public void ClampDimensions_NeverReturnsZeroOrNegative(int inputW, int inputH, int expectedW, int expectedH)
        {
            (int w, int h) = AsciiSizing.ClampDimensions(inputW, inputH);
            Assert.Equal(expectedW, w);
            Assert.Equal(expectedH, h);
        }

        private static string StripAnsi(string input)
        {
            return Regex.Replace(input, "\\x1b\\[[0-9;?]*[A-Za-z]", string.Empty);
        }
    }
}
