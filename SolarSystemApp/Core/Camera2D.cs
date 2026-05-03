using SolarSystemApp.Util;

namespace SolarSystemApp.Core
{
    public sealed class Camera2D
    {
        public double X { get; private set; }
        public double Y { get; private set; }
        public double Zoom { get; private set; } // chars per world unit

        public double TargetX { get; private set; }
        public double TargetY { get; private set; }
        public double TargetZoom { get; private set; }

        private double _vx, _vy;

        // Tunables
        public double ZoomLerp = 0.18;     // smoothing toward target zoom
        public double PosLerp = 0.18;      // smoothing toward target position
        public double Damping = 0.85;      // velocity damping (per second-ish feel)

        public Camera2D(double x = 0, double y = 0, double zoom = 10)
        {
            X = TargetX = x;
            Y = TargetY = y;
            Zoom = TargetZoom = zoom;
        }

        public void Snap(double x, double y, double zoom)
        {
            X = TargetX = x;
            Y = TargetY = y;
            Zoom = TargetZoom = zoom;
            _vx = _vy = 0;
        }

        public void SetTarget(double x, double y) { TargetX = x; TargetY = y; }
        public void NudgeTarget(double dx, double dy) { TargetX += dx; TargetY += dy; }

        // Pan input in WORLD units/sec (we’ll compute speed outside based on zoom)
        public void AddVelocity(double vx, double vy)
        {
            _vx += vx;
            _vy += vy;
        }

        public void MultiplyTargetZoom(double factor, double minZoom, double maxZoom)
        {
            TargetZoom *= factor;
            TargetZoom = MathUtil.Clamp(TargetZoom, minZoom, maxZoom);
        }

        public void Update(double dt)
        {
            if (dt <= 0) dt = 0.016;

            // Integrate velocity -> target
            TargetX += _vx * dt;
            TargetY += _vy * dt;

            // Dampen velocity
            double damp = System.Math.Pow(Damping, dt * 60.0);
            _vx *= damp;
            _vy *= damp;

            // Smooth toward target
            double zt = 1.0 - System.Math.Pow(1.0 - ZoomLerp, dt * 60.0);
            double pt = 1.0 - System.Math.Pow(1.0 - PosLerp, dt * 60.0);

            Zoom = MathUtil.Lerp(Zoom, TargetZoom, zt);
            X = MathUtil.Lerp(X, TargetX, pt);
            Y = MathUtil.Lerp(Y, TargetY, pt);
        }
    }
}
