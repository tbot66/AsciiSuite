using System;
using SolarSystemApp.Util;

namespace SolarSystemApp.Rendering
{
    internal sealed class PixelCamera2D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Zoom { get; set; } = 10.0; // pixels per world unit
        public double OrbitYScale { get; set; } = 0.55;

        public double TargetX { get; set; }
        public double TargetY { get; set; }
        public double TargetZoom { get; set; } = 10.0;
        public double TargetOrbitYScale { get; set; } = 0.55;

        public int CenterX { get; private set; }
        public int CenterY { get; private set; }

        public bool SmoothEnabled { get; set; } = true;
        public double PanResponsiveness { get; set; } = 14.0;
        public double ZoomResponsiveness { get; set; } = 18.0;

        public void SetViewport(int width, int height)
        {
            CenterX = width / 2;
            CenterY = height / 2;
        }

        public void Snap(double x, double y, double zoom)
        {
            X = TargetX = x;
            Y = TargetY = y;
            Zoom = TargetZoom = zoom;
        }

        public void NudgeTarget(double dxWorld, double dyWorld)
        {
            TargetX += dxWorld;
            TargetY += dyWorld;
        }

        public void MultiplyTargetZoom(double factor, double minZoom, double maxZoom)
        {
            TargetZoom *= factor;
            TargetZoom = MathUtil.Clamp(TargetZoom, minZoom, maxZoom);
        }

        public void Update(double dt)
        {
            if (!SmoothEnabled)
            {
                TargetX = X;
                TargetY = Y;
                TargetZoom = Zoom;
                TargetOrbitYScale = OrbitYScale;
                return;
            }

            double aPan = 1.0 - Math.Exp(-PanResponsiveness * Math.Max(0.0, dt));
            double aZoom = 1.0 - Math.Exp(-ZoomResponsiveness * Math.Max(0.0, dt));

            X = Lerp(X, TargetX, aPan);
            Y = Lerp(Y, TargetY, aPan);
            Zoom = Lerp(Zoom, TargetZoom, aZoom);
            OrbitYScale = Lerp(OrbitYScale, TargetOrbitYScale, aZoom);
        }

        public int WorldToPixelX(double wx)
            => CenterX + (int)Math.Round((wx - X) * Zoom);

        public int WorldToPixelY(double wy)
            => CenterY + (int)Math.Round((wy - Y) * Zoom * OrbitYScale);

        public void PixelToWorld(int sx, int sy, out double wx, out double wy)
        {
            double zoom = Math.Max(0.000001, Zoom);
            double zoomY = Math.Max(0.000001, Zoom * OrbitYScale);
            wx = X + (sx - CenterX) / zoom;
            wy = Y + (sy - CenterY) / zoomY;
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
