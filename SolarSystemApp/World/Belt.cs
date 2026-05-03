using System;
using System.Collections.Generic;
using AsciiEngine;

namespace SolarSystemApp.World
{
    public sealed class Belt
    {
        private readonly Random _rng;
        private readonly List<Asteroid> _a = new List<Asteroid>();

        private readonly double _radius;
        private readonly double _thickness;
        private readonly double _baseSpeed;

        public Belt(int seed, int count, double radius, double thickness, double baseSpeed)
        {
            _rng = new Random(seed);
            _radius = radius;
            _thickness = thickness;
            _baseSpeed = baseSpeed;

            Build(count);
        }

        private void Build(int count)
        {
            _a.Clear();
            for (int i = 0; i < count; i++)
            {
                double a0 = _rng.NextDouble() * Math.PI * 2.0;
                double dr = (_rng.NextDouble() * 2.0 - 1.0) * _thickness;
                double off = (_rng.NextDouble() * 2.0 - 1.0) * _thickness;

                double speedMul = 0.85 + 0.30 * _rng.NextDouble();
                int tier = (_rng.NextDouble() < 0.05) ? 1 : 0;

                _a.Add(new Asteroid { Angle0 = a0, Radius = _radius + dr, Offset = off, SpeedMul = speedMul, Tier = tier });
            }
        }

        public void Draw(ConsoleRenderer r, int sunX, int sunY, double time, double worldToScreen, double orbitYScale)
        {
            for (int i = 0; i < _a.Count; i++)
            {
                var a = _a[i];

                double ang = a.Angle0 + time * _baseSpeed * a.SpeedMul;

                double wx = Math.Cos(ang) * a.Radius;
                double wy = Math.Sin(ang) * a.Radius;

                double px = -Math.Sin(ang) * a.Offset;
                double py = Math.Cos(ang) * a.Offset;

                int x = sunX + (int)Math.Round((wx + px) * worldToScreen);
                int y = sunY + (int)Math.Round((wy + py) * worldToScreen * orbitYScale);

                char ch = (a.Tier == 1) ? '*' : '.';
                r.Set(x, y, ch, AnsiColor.BrightBlack, AnsiColor.Black, z: 180);
            }
        }

        private struct Asteroid
        {
            public double Angle0;
            public double Radius;
            public double Offset;
            public double SpeedMul;
            public int Tier;
        }
    }
}
