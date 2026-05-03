using AsciiEngine;
using SolarSystemApp.Core;
using SolarSystemApp.World;
using SolarSystemApp.Util;

namespace SolarSystemApp.Render
{
    public static class Starfield
    {
        public static void Draw(ConsoleRenderer r, EngineContext ctx, StarSystem sys,
            double camWX, double camWY, double worldToScreen, double orbitYScale)
        {
            int count = 220;
            int cx = ctx.Width / 2;
            int cy = ctx.Height / 2;

            double span = 120.0;
            int seed = sys.Seed;

            for (int i = 0; i < count; i++)
            {
                double rx = MathUtil.Hash01(seed + i * 17 + 1);
                double ry = MathUtil.Hash01(seed + i * 17 + 2);
                double rd = MathUtil.Hash01(seed + i * 17 + 3);

                double depth = rd;
                double par = 0.08 + 0.28 * depth;

                double wx = (rx * 2.0 - 1.0) * span;
                double wy = (ry * 2.0 - 1.0) * span;

                double relX = MathUtil.Wrap(wx - camWX * par, -span, span);
                double relY = MathUtil.Wrap(wy - camWY * par, -span, span);

                int sx = cx + (int)System.Math.Round(relX * worldToScreen);
                int sy = cy + (int)System.Math.Round(relY * worldToScreen * orbitYScale);

                if (sx < 0 || sx >= ctx.Width || sy < 0 || sy >= ctx.Height) continue;

                char ch = (depth > 0.85) ? '*' : '.';
                var fg = (depth > 0.85) ? AsciiEngine.AnsiColor.BrightWhite : AsciiEngine.AnsiColor.BrightBlack;

                r.Set(sx, sy, ch, fg, AsciiEngine.AnsiColor.Black, z: 300);
            }
        }
    }
}
