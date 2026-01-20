using System;

namespace SolarSystemApp.Util
{
    internal static class MathUtil
    {
        public static double Clamp(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        public static int ClampInt(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        public static double Lerp(double a, double b, double t)
        {
            t = Clamp(t, 0.0, 1.0);
            return a + (b - a) * t;
        }

        public static int WrapIndex(int i, int n)
        {
            if (n <= 0) return 0;
            i %= n;
            if (i < 0) i += n;
            return i;
        }

        public static double Wrap(double v, double lo, double hi)
        {
            double span = hi - lo;
            if (span <= 0) return lo;

            while (v < lo) v += span;
            while (v > hi) v -= span;
            return v;
        }

        public static double Hash01(int x)
        {
            unchecked
            {
                uint n = (uint)x;
                n ^= n >> 16;
                n *= 0x7feb352d;
                n ^= n >> 15;
                n *= 0x846ca68b;
                n ^= n >> 16;
                return (n & 0xFFFFFF) / (double)0x1000000;
            }
        }

        public static char RampChar(string ramp, double brightness01)
        {
            if (string.IsNullOrEmpty(ramp)) return '#';
            if (brightness01 < 0) brightness01 = 0;
            if (brightness01 > 1) brightness01 = 1;

            int idx = (int)Math.Round(brightness01 * (ramp.Length - 1));
            if (idx < 0) idx = 0;
            if (idx >= ramp.Length) idx = ramp.Length - 1;
            return ramp[idx];
        }
    }
}
