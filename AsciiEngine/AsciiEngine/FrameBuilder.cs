using System.Text;

namespace AsciiEngine
{
    internal static class FrameBuilder
    {
        internal static string BuildFrameString(
            ReadOnlySpan<char> chars,
            ReadOnlySpan<Color> fg,
            ReadOnlySpan<Color> bg,
            int width,
            int height,
            bool unicodeOk)
        {
            (int clampedW, int clampedH) = AsciiSizing.ClampDimensions(width, height);

            int rough = clampedW * clampedH;
            StringBuilder sb = new StringBuilder(rough > 0 ? System.Math.Min(rough * 4, 2_000_000) : 256);

            sb.Append(Ansi.Home);

            Color curFg = default;
            Color curBg = default;
            bool hasFg = false;
            bool hasBg = false;

            // Commit-note: enforce full-frame redraw and sanitize glyphs when Unicode is unreliable.
            for (int y = 0; y < clampedH; y++)
            {
                int row = y * clampedW;
                if (y > 0)
                {
                    sb.Append('\n');
                }

                for (int x = 0; x < clampedW; x++)
                {
                    int idx = row + x;

                    Color cellFg = idx < fg.Length ? fg[idx] : Colors.DefaultFg;
                    Color cellBg = idx < bg.Length ? bg[idx] : Colors.DefaultBg;

                    if (!hasFg || cellFg != curFg)
                    {
                        Ansi.AppendFg(sb, cellFg);
                        curFg = cellFg;
                        hasFg = true;
                    }
                    if (!hasBg || cellBg != curBg)
                    {
                        Ansi.AppendBg(sb, cellBg);
                        curBg = cellBg;
                        hasBg = true;
                    }

                    char ch = idx < chars.Length ? chars[idx] : ' ';
                    ch = AsciiSanitizer.SanitizeChar(ch, unicodeOk);
                    sb.Append(ch);
                }
            }

            sb.Append(Ansi.Reset);
            return sb.ToString();
        }
    }
}
