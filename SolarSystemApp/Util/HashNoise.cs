using System;

namespace SolarSystemApp.Util
{
    internal static class HashNoise
    {
        public static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }

        public static uint Hash2(int seed, int x, int y)
        {
            unchecked
            {
                uint h = (uint)seed;
                h ^= (uint)x * 0x9e3779b1u;
                h = Hash(h);
                h ^= (uint)y * 0x85ebca6bu;
                h = Hash(h);
                return h;
            }
        }

        public static double Hash01(int seed, int x, int y)
        {
            uint h = Hash2(seed, x, y);
            return (h & 0x00FFFFFFu) / 16777216.0;
        }

        public static double ValueNoise(int seed, double x, double y)
        {
            int xi = (int)Math.Floor(x);
            int yi = (int)Math.Floor(y);

            double tx = x - xi;
            double ty = y - yi;

            double a = Hash01(seed, xi, yi);
            double b = Hash01(seed, xi + 1, yi);
            double c = Hash01(seed, xi, yi + 1);
            double d = Hash01(seed, xi + 1, yi + 1);

            double u = Smooth(tx);
            double v = Smooth(ty);

            double ab = Lerp(a, b, u);
            double cd = Lerp(c, d, u);
            return Lerp(ab, cd, v);
        }

        public static double FBm(int seed, double x, double y, int octaves, double lacunarity = 2.0, double gain = 0.5)
        {
            double amp = 1.0;
            double freq = 1.0;
            double sum = 0.0;
            double norm = 0.0;

            for (int i = 0; i < octaves; i++)
            {
                sum += amp * ValueNoise(seed + i * 1013, x * freq, y * freq);
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }

            return (norm > 0) ? (sum / norm) : 0.0;
        }

        public static int StableHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;

            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 16777619u;
                }
                return (int)h;
            }
        }

        private static double Smooth(double t) => t * t * (3.0 - 2.0 * t);
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
