using System;
using System.Collections.Generic;

namespace AsciiEngine
{
    internal static class AsciiSanitizer
    {
        private static readonly Dictionary<char, char> GlyphMap = new()
        {
            ['█'] = '#',
            ['▓'] = '#',
            ['▒'] = '*',
            ['░'] = '.',
            ['·'] = '.',
            ['•'] = '*',
            ['◦'] = '.',
            ['■'] = '#',
            ['□'] = '#',
            ['▌'] = '#',
            ['▐'] = '#',
            ['▀'] = '#',
            ['▄'] = '#'
        };

        internal static char SanitizeChar(char c, bool unicodeOk)
        {
            if (unicodeOk) return c;
            return SanitizeChar(c);
        }

        internal static char SanitizeChar(char c)
        {
            if (c <= 127) return c;
            return GlyphMap.TryGetValue(c, out char mapped) ? mapped : '#';
        }
    }
}
