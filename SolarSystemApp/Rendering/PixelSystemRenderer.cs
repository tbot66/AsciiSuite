using System;
using AsciiEngine;
using SolarSystemApp.Util;
using SolarSystemApp.World;

namespace SolarSystemApp.Rendering
{
    internal sealed class PixelSystemRenderer
    {
        private readonly PixelCamera2D _camera;

        public PixelSystemRenderer(PixelCamera2D camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        }

        public void DrawSystem(PixelRenderer renderer, StarSystem sys, bool showOrbits)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (sys == null) return;

            DrawSun(renderer, sys);

            if (showOrbits)
            {
                for (int i = 0; i < sys.Planets.Count; i++)
                    DrawOrbit(renderer, sys, sys.Planets[i]);
            }

            for (int i = 0; i < sys.Planets.Count; i++)
                DrawPlanet(renderer, sys.Planets[i]);

            for (int i = 0; i < sys.Stations.Count; i++)
                DrawStation(renderer, sys.Stations[i]);

            for (int i = 0; i < sys.Ships.Count; i++)
                DrawShip(renderer, sys.Ships[i]);
        }

        private void DrawSun(PixelRenderer renderer, StarSystem sys)
        {
            if (!sys.HasStar || sys.SunRadiusWorld <= 0.0)
                return;

            Color sunColor = ColorUtils.ToRgbColor((Color)sys.SunColor);
            int radius = WorldToPixels(sys.SunRadiusWorld);
            int corona = WorldToPixels(sys.CoronaRadiusWorld);

            int cx = _camera.WorldToPixelX(0.0);
            int cy = _camera.WorldToPixelY(0.0);

            if (corona > radius)
                renderer.DrawCircle(cx, cy, corona, ColorUtils.Shade(sunColor, 0.35));

            renderer.FillCircle(cx, cy, radius, sunColor);
        }

        private void DrawPlanet(PixelRenderer renderer, Planet planet)
        {
            int cx = _camera.WorldToPixelX(planet.WX);
            int cy = _camera.WorldToPixelY(planet.WY);
            int radius = WorldToPixels(planet.RadiusWorld);

            Color color = ColorUtils.ToRgbColor((Color)planet.Fg);
            renderer.FillCircle(cx, cy, radius, color);

            for (int i = 0; i < planet.Moons.Count; i++)
                DrawMoon(renderer, planet.Moons[i]);
        }

        private void DrawMoon(PixelRenderer renderer, Moon moon)
        {
            int cx = _camera.WorldToPixelX(moon.WX);
            int cy = _camera.WorldToPixelY(moon.WY);
            int radius = Math.Max(1, WorldToPixels(moon.RadiusWorld));

            Color color = ColorUtils.ToRgbColor((Color)moon.Fg);
            renderer.FillCircle(cx, cy, radius, color);
        }

        private void DrawStation(PixelRenderer renderer, Station station)
        {
            int cx = _camera.WorldToPixelX(station.WX);
            int cy = _camera.WorldToPixelY(station.WY);

            renderer.FillRect(cx - 1, cy - 1, 3, 3, Colors.BrightWhite);
            renderer.DrawRect(cx - 2, cy - 2, 5, 5, Color.FromRgb(80, 120, 200));
        }

        private void DrawShip(PixelRenderer renderer, Ship ship)
        {
            int cx = _camera.WorldToPixelX(ship.WX);
            int cy = _camera.WorldToPixelY(ship.WY);

            Color shipColor = ColorUtils.ToRgbColor((Color)ship.Fg);
            renderer.FillRect(cx - 1, cy - 1, 3, 3, shipColor);
        }

        private void DrawOrbit(PixelRenderer renderer, StarSystem sys, Planet planet)
        {
            if (planet.A <= 0.0)
                return;

            Color orbitColor = Color.FromRgb(80, 92, 120);

            const int steps = 120;
            double prevX = 0.0;
            double prevY = 0.0;
            bool hasPrev = false;

            for (int i = 0; i <= steps; i++)
            {
                double m = (i / (double)steps) * Math.PI * 2.0;
                ComputeOrbitPoint(sys.Seed, planet, m, out double wx, out double wy);

                if (hasPrev)
                {
                    int x0 = _camera.WorldToPixelX(prevX);
                    int y0 = _camera.WorldToPixelY(prevY);
                    int x1 = _camera.WorldToPixelX(wx);
                    int y1 = _camera.WorldToPixelY(wy);
                    renderer.DrawLine(x0, y0, x1, y1, orbitColor);
                }

                prevX = wx;
                prevY = wy;
                hasPrev = true;
            }
        }

        private static void ComputeOrbitPoint(int systemSeed, Planet planet, double meanAnomaly, out double wx, out double wy)
        {
            OrbitMath.Kepler2D(planet.A, MathUtil.Clamp(planet.E, 0.0, 0.95), 0.0, meanAnomaly, out double x, out double y);

            int pSeed = systemSeed ^ planet.Name.GetHashCode();
            double plane = (HashNoise.Hash01(pSeed, 101, 202) * 2.0 - 1.0) * 1.05;

            double c = Math.Cos(plane);
            double s = Math.Sin(plane);
            double rx = x * c - y * s;
            double ry = x * s + y * c;

            double inc = 0.55 + 0.45 * HashNoise.Hash01(pSeed, 303, 404);
            ry *= inc;

            double co = Math.Cos(planet.Omega);
            double so = Math.Sin(planet.Omega);
            wx = rx * co - ry * so;
            wy = rx * so + ry * co;
        }

        private int WorldToPixels(double worldRadius)
            => Math.Max(1, (int)Math.Round(worldRadius * _camera.Zoom));
    }
}
