using System;
using System.Text;
using AsciiEngine; // for AnsiColor

namespace SolarSystemApp.Interiors
{
    internal sealed class InteriorMap
    {
        public readonly int W;
        public readonly int H;

        private readonly char[] _tiles; // row-major

        // Canonical tiles used by prefab stamping:
        public const char VOID = '#';   // outside ship (blocked)
        public const char WALL = 'X';   // ship wall (blocked)
        public const char FLOOR = '.';  // walkable floor
        public const char DOOR = 'D';   // walkable door
        public const char WINDOW = 'W'; // window wall (blocked)

        public InteriorMap(int w, int h, char fill = VOID)
        {
            W = Math.Max(4, w);
            H = Math.Max(4, h);
            _tiles = new char[W * H];
            for (int i = 0; i < _tiles.Length; i++) _tiles[i] = fill;
        }

        public bool InBounds(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H;

        public char Get(int x, int y)
        {
            if (!InBounds(x, y)) return VOID;
            return _tiles[y * W + x];
        }

        public void Set(int x, int y, char ch)
        {
            if (!InBounds(x, y)) return;
            _tiles[y * W + x] = ch;
        }

        // Only true walkable tiles:
        // - floor '.' and door 'D' and any props you place later
        // Blocked: VOID '#', WALL 'X', WINDOW 'W'
        public bool IsWalkable(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            char t = Get(x, y);
            return t != VOID && t != WALL && t != WINDOW;
        }

        // Optional debug helper if you want to print in a stable way (not required by your engine).
        public string ToAscii(int playerX, int playerY)
        {
            var sb = new StringBuilder((W + 1) * H);
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    bool isPlayer = (x == playerX && y == playerY);

                    GetStyledTile(x, y, isPlayer, out char g, out _, out _);
                    sb.Append(g);
                }
                if (y < H - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        // Minimal styling: walls are █, floors are space, void is space (outside ship), doors are ▣, windows are ░.
        // Keeps signature compatible with your existing renderer.
        public void GetStyledTile(int x, int y, bool isPlayer, out char glyph, out AnsiColor fg, out AnsiColor bg)
        {
            bg = AnsiColor.Black;

            if (!InBounds(x, y))
            {
                glyph = ' ';
                fg = AnsiColor.Black;
                return;
            }

            if (isPlayer)
            {
                glyph = '@';
                fg = AnsiColor.BrightYellow;
                return;
            }

            char t = Get(x, y);

            switch (t)
            {
                case VOID:
                    glyph = ' '; // outside ship: empty
                    fg = AnsiColor.Black;
                    break;

                case FLOOR:
                    glyph = ' '; // interior space: empty
                    fg = AnsiColor.White;
                    break;

                case WALL:
                    glyph = '█';
                    fg = AnsiColor.BrightBlack;
                    break;

                case WINDOW:
                    glyph = '░';
                    fg = AnsiColor.BrightBlue;
                    break;

                case DOOR:
                    glyph = '▣';
                    fg = AnsiColor.BrightCyan;
                    break;

                default:
                    // Any future props you stamp: show as-is
                    glyph = t;
                    fg = AnsiColor.BrightWhite;
                    break;
            }
        }
    }
}
