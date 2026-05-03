using System;
using AsciiEngine;
using SolarSystemApp.Util;

namespace SolarSystemApp.World
{
    internal static class Starfield
    {
        public static void Draw(ConsoleRenderer r, int seed, double camWX, double camWY, double zoom, double density)
        {
            density = MathUtil.Clamp(density, 0.0, 1.0);

            DrawLayer(r, seed ^ 0xA13, camWX, camWY, zoom, density * 0.55, parallax: 0.25, z: 10.0);
            DrawLayer(r, seed ^ 0xB77, camWX, camWY, zoom, density * 0.85, parallax: 0.45, z: 9.5);
            DrawLayer(r, seed ^ 0xC91, camWX, camWY, zoom, density * 1.15, parallax: 0.70, z: 9.0);
        }

        private static void DrawLayer(ConsoleRenderer r, int seed, double camWX, double camWY, double zoom,
                                      double density, double parallax, double z)
        {
            int w = r.Width;
            int h = r.Height;

            double scale = 2.2 / Math.Max(0.15, zoom);

            double px = camWX * parallax;
            double py = camWY * parallax;

            for (int sy = 0; sy < h; sy++)
            {
                for (int sx = 0; sx < w; sx++)
                {
                    double fx = (sx - w * 0.5) * scale + px;
                    double fy = (sy - h * 0.5) * scale + py;

                    int ix = (int)Math.Floor(fx);
                    int iy = (int)Math.Floor(fy);

                    double roll = HashNoise.Hash01(seed, ix, iy);

                    if (roll > density) continue;

                    double kind = HashNoise.Hash01(seed ^ 12345, ix, iy);

                    char ch =
                        (kind < 0.10) ? '*' :
                        (kind < 0.35) ? '+' :
                        (kind < 0.70) ? '.' :
                                        '·';

                    AnsiColor fg =
                        (kind < 0.15) ? AnsiColor.BrightWhite :
                        (kind < 0.35) ? AnsiColor.BrightCyan :
                        (kind < 0.55) ? AnsiColor.BrightYellow :
                                        AnsiColor.White;

                    r.Set(sx, sy, ch, fg, AnsiColor.Black, z);
                }
            }
        }
    }
}
