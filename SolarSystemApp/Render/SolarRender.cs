using System;
using AsciiEngine;
using SolarSystemApp.Core;
using SolarSystemApp.World;
using SolarSystemApp.Util;

namespace SolarSystemApp.Render
{
    public static class SolarRender
    {
        public static int RenderRadiusFromZoom(int baseRadiusChars, double worldToScreen, double zoomRef)
        {
            double zoomFactor = worldToScreen / zoomRef;
            int r = (int)Math.Round(baseRadiusChars * zoomFactor);
            return MathUtil.ClampInt(r, 1, 80);
        }

        public static void DrawOrbits(ConsoleRenderer r, StarSystem sys, int sunX, int sunY, double worldToScreen, double orbitYScale)
        {
            foreach (var p in sys.Planets)
            {
                int steps = 360;
                for (int i = 0; i < steps; i += 2)
                {
                    double a = i * Math.PI / 180.0;
                    double wx = Math.Cos(a) * p.A;
                    double wy = Math.Sin(a) * p.A;

                    int x = sunX + (int)Math.Round(wx * worldToScreen);
                    int y = sunY + (int)Math.Round(wy * worldToScreen * orbitYScale);

                    r.Set(x, y, '.', AnsiColor.BrightBlack, AnsiColor.Black, z: ZLayers.Orbits);
                }
            }
        }

        public static void DrawSunScaled(ConsoleRenderer r, StarSystem sys, int sunX, int sunY,
            double worldToScreen, double sunRadiusWorld, double coronaRadiusWorld, string ramp)
        {
            ramp = string.IsNullOrEmpty(ramp) ? " .:-=+*#" : ramp;

            int sunR = MathUtil.ClampInt((int)Math.Round(sunRadiusWorld * worldToScreen), 2, 40);
            int coronaR = MathUtil.ClampInt((int)Math.Round(coronaRadiusWorld * worldToScreen), sunR + 2, 60);

            // corona ring
            for (int y = -coronaR; y <= coronaR; y++)
            {
                for (int x = -coronaR; x <= coronaR; x++)
                {
                    double d = Math.Sqrt(x * x + y * y);
                    if (d > coronaR) continue;

                    if (d >= coronaR - 1.0)
                    {
                        char ch = MathUtil.RampChar(ramp, 0.25);
                        r.Set(sunX + x, sunY + y, ch, sys.SunColor, AnsiColor.Black, z: ZLayers.SunCorona);
                    }
                }
            }

            // disc
            for (int y = -sunR; y <= sunR; y++)
            {
                for (int x = -sunR; x <= sunR; x++)
                {
                    double d2 = x * x + y * y;
                    if (d2 > sunR * sunR) continue;

                    double d = Math.Sqrt(d2);
                    double t = d / sunR;
                    double brightness = 1.0 - 0.6 * t;

                    char ch = MathUtil.RampChar(ramp, MathUtil.Clamp(brightness, 0.0, 1.0));
                    r.Set(sunX + x, sunY + y, ch, sys.SunColor, AnsiColor.Black, z: ZLayers.SunDisc);
                }
            }
        }

        public static void DrawTrails(ConsoleRenderer r, StarSystem sys,
            int sunX, int sunY, double worldToScreen, double orbitYScale)
        {
            // Planets
            for (int i = 0; i < sys.Planets.Count; i++)
            {
                var p = sys.Planets[i];

                p.Trail.ForEachNewest((pt, age, count) =>
                {
                    if ((age & 1) == 1) return;

                    double fade = 1.0 - (age / (double)Math.Max(1, count - 1));
                    if (fade < 0.12) return;

                    char ch = (fade > 0.70) ? '.' : (fade > 0.40) ? ',' : '`';

                    int x = sunX + (int)Math.Round(pt.X * worldToScreen);
                    int y = sunY + (int)Math.Round(pt.Y * worldToScreen * orbitYScale);

                    r.Set(x, y, ch, AnsiColor.BrightBlack, AnsiColor.Black, z: ZLayers.Trails);
                });

                // Moons
                for (int m = 0; m < p.Moons.Count; m++)
                {
                    var moon = p.Moons[m];

                    moon.Trail.ForEachNewest((pt, age, count) =>
                    {
                        if ((age & 1) == 1) return;

                        double fade = 1.0 - (age / (double)Math.Max(1, count - 1));
                        if (fade < 0.18) return;

                        char ch = (fade > 0.72) ? '.' : (fade > 0.44) ? ',' : '`';

                        int x = sunX + (int)Math.Round(pt.X * worldToScreen);
                        int y = sunY + (int)Math.Round(pt.Y * worldToScreen * orbitYScale);

                        r.Set(x, y, ch, AnsiColor.BrightBlack, AnsiColor.Black, z: ZLayers.Trails);
                    });
                }
            }
        }

        public static void DrawMoonOrbit(ConsoleRenderer r, int px, int py, double orbitWorld,
            double worldToScreen, double orbitYScale)
        {
            int steps = 180;
            for (int i = 0; i < steps; i += 6)
            {
                double a = i * Math.PI / 180.0;

                int x = px + (int)Math.Round(Math.Cos(a) * orbitWorld * worldToScreen * 0.35);
                int y = py + (int)Math.Round(Math.Sin(a) * orbitWorld * worldToScreen * orbitYScale * 0.35);

                r.Set(x, y, '.', AnsiColor.BrightBlack, AnsiColor.Black, z: ZLayers.Orbits);
            }
        }

        public static void DrawSimpleRings(ConsoleRenderer r, int cx, int cy, int planetR)
        {
            double inner = planetR * 1.25;
            double outer = planetR * 2.25;
            double tilt = 0.60;
            double ryMul = 1.0 - 0.55 * tilt;

            for (int deg = 0; deg < 360; deg += 2)
            {
                double a = deg * Math.PI / 180.0;
                bool thick = (deg % 10) < 5;
                double rad = thick ? outer : (inner + outer) * 0.55;

                double x = Math.Cos(a) * rad;
                double y = Math.Sin(a) * rad * ryMul;

                bool front = Math.Sin(a) > 0;
                double z = front ? ZLayers.RingsFront : ZLayers.RingsBack;

                r.Set(cx + (int)Math.Round(x), cy + (int)Math.Round(y),
                    thick ? '.' : ':', AnsiColor.BrightBlack, AnsiColor.Black, z);
            }
        }

        public static void DrawShips(ConsoleRenderer r, StarSystem sys, int sunX, int sunY, double worldToScreen, double orbitYScale)
        {
            for (int i = 0; i < sys.Ships.Count; i++)
            {
                var s = sys.Ships[i];

                int x = sunX + (int)Math.Round(s.WX * worldToScreen);
                int y = sunY + (int)Math.Round(s.WY * worldToScreen * orbitYScale);

                // ships should be in front of trails, behind labels/UI
                double z = ZLayers.Bodies - s.WZ * 6.0 - 5.0;

                r.Set(x, y, s.Glyph, s.Fg, AnsiColor.Black, z);
            }
        }
    }
}
