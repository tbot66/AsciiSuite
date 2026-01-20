using System;

namespace AsciiEngine
{
    internal static class AsciiSizing
    {
        internal const int MinWidth = 1;
        internal const int MinHeight = 1;

        internal static (int width, int height) ClampDimensions(int width, int height)
        {
            int w = Math.Max(MinWidth, width);
            int h = Math.Max(MinHeight, height);
            return (w, h);
        }
    }
}
