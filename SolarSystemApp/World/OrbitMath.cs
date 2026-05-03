using System;

namespace SolarSystemApp.World
{
    internal static class OrbitMath
    {
        public static double SolveEccentricAnomaly(double M, double e)
        {
            M = NormalizeAngle(M);

            double E = (e < 0.8) ? M : Math.PI;

            for (int i = 0; i < 8; i++)
            {
                double f = E - e * Math.Sin(E) - M;
                double fp = 1.0 - e * Math.Cos(E);

                if (Math.Abs(fp) < 1e-9) break;

                double d = f / fp;
                E -= d;

                if (Math.Abs(d) < 1e-10) break;
            }

            return E;
        }

        public static void Kepler2D(double a, double e, double omega, double M, out double x, out double y)
        {
            double E = SolveEccentricAnomaly(M, e);

            double cosE = Math.Cos(E);
            double sinE = Math.Sin(E);

            double r = a * (1.0 - e * cosE);

            double cosV = (cosE - e) / (1.0 - e * cosE);
            double sinV = (Math.Sqrt(1.0 - e * e) * sinE) / (1.0 - e * cosE);

            double v = Math.Atan2(sinV, cosV);

            double px = r * Math.Cos(v);
            double py = r * Math.Sin(v);

            double co = Math.Cos(omega);
            double so = Math.Sin(omega);

            x = px * co - py * so;
            y = px * so + py * co;
        }

        public static double NormalizeAngle(double a)
        {
            while (a > Math.PI) a -= 2.0 * Math.PI;
            while (a < -Math.PI) a += 2.0 * Math.PI;
            return a;
        }
    }
}
