using System;
using OpenTK.Mathematics;
using SolarSystemApp.Util;

namespace SolarSystemApp.Rendering
{
    internal sealed class Camera2D
    {
        // zoom: chars per world unit
        public double WorldToScreen { get; set; } = 10.0;

        // orbit vertical squash
        public double OrbitYScale { get; set; } = 0.55;

        // camera world position (center)
        public double CamWX { get; set; } = 0.0;
        public double CamWY { get; set; } = 0.0;

        public int CenterX { get; set; }
        public int CenterY { get; set; }

        // -------------------------
        // NEW: smoothing targets
        // -------------------------
        public bool SmoothEnabled { get; set; } = true;

        // "where we want to be"
        public double TargetCamWX { get; set; } = 0.0;
        public double TargetCamWY { get; set; } = 0.0;
        public double TargetWorldToScreen { get; set; } = 10.0;
        public double TargetOrbitYScale { get; set; } = 0.55;

        // higher = snappier
        public double PanResponsiveness { get; set; } = 14.0;
        public double ZoomResponsiveness { get; set; } = 18.0;

        public Camera2D() { }

        public int WorldToScreenX(double wx)
            => CenterX + (int)Math.Round((wx - CamWX) * WorldToScreen);

        public int WorldToScreenY(double wy)
            => CenterY + (int)Math.Round((wy - CamWY) * WorldToScreen * OrbitYScale);

        public void ScreenToWorld(int sx, int sy, out double wx, out double wy)
        {
            wx = CamWX + (sx - CenterX) / Math.Max(0.000001, WorldToScreen);
            wy = CamWY + (sy - CenterY) / Math.Max(0.000001, (WorldToScreen * OrbitYScale));
        }

        public Matrix4 GetViewProjMatrix(int viewportWidth, int viewportHeight)
        {
            float w = Math.Max(1, viewportWidth);
            float h = Math.Max(1, viewportHeight);
            float sx = (float)(2.0 * WorldToScreen / w);
            float sy = (float)(2.0 * WorldToScreen * OrbitYScale / h);

            Matrix4 scale = Matrix4.CreateScale(sx, sy, 1f);
            Matrix4 translate = Matrix4.CreateTranslation((float)(-CamWX), (float)(-CamWY), 0f);
            return scale * translate;
        }

        // Existing behavior: immediate pan.
        public void PanChars(int dxChars, int dyChars)
        {
            CamWX += dxChars / Math.Max(0.000001, WorldToScreen);
            CamWY += dyChars / Math.Max(0.000001, (WorldToScreen * OrbitYScale));

            // keep targets in sync so enabling smoothing doesn't "snap"
            TargetCamWX = CamWX;
            TargetCamWY = CamWY;
        }

        public void ClampZoom(double lo, double hi)
        {
            WorldToScreen = MathUtil.Clamp(WorldToScreen, lo, hi);
            TargetWorldToScreen = MathUtil.Clamp(TargetWorldToScreen, lo, hi);
        }

        public void ClampOrbitScale(double lo, double hi)
        {
            OrbitYScale = MathUtil.Clamp(OrbitYScale, lo, hi);
            TargetOrbitYScale = MathUtil.Clamp(TargetOrbitYScale, lo, hi);
        }

        public void PanCharsSmooth(int dxChars, int dyChars)
        {
            TargetCamWX += dxChars / Math.Max(0.000001, WorldToScreen);
            TargetCamWY += dyChars / Math.Max(0.000001, (WorldToScreen * OrbitYScale));
        }

        public void ZoomAtScreenPoint(double newWorldToScreen, int anchorSX, int anchorSY)
        {
            newWorldToScreen = Math.Max(0.000001, newWorldToScreen);

            ScreenToWorld(anchorSX, anchorSY, out double wxBefore, out double wyBefore);

            WorldToScreen = newWorldToScreen;

            ScreenToWorld(anchorSX, anchorSY, out double wxAfter, out double wyAfter);

            CamWX += (wxBefore - wxAfter);
            CamWY += (wyBefore - wyAfter);

            TargetWorldToScreen = WorldToScreen;
            TargetCamWX = CamWX;
            TargetCamWY = CamWY;
        }

        public void ZoomAtScreenPointSmooth(double targetWorldToScreen, int anchorSX, int anchorSY)
        {
            targetWorldToScreen = Math.Max(0.000001, targetWorldToScreen);

            ScreenToWorld(anchorSX, anchorSY, out double wxBefore, out double wyBefore);

            TargetWorldToScreen = targetWorldToScreen;

            double old = WorldToScreen;
            WorldToScreen = TargetWorldToScreen;
            ScreenToWorld(anchorSX, anchorSY, out double wxAfter, out double wyAfter);
            WorldToScreen = old;

            TargetCamWX += (wxBefore - wxAfter);
            TargetCamWY += (wyBefore - wyAfter);
        }

        public void Update(double dt)
        {
            if (!SmoothEnabled)
            {
                TargetCamWX = CamWX;
                TargetCamWY = CamWY;
                TargetWorldToScreen = WorldToScreen;
                TargetOrbitYScale = OrbitYScale;
                return;
            }

            double aPan = 1.0 - Math.Exp(-PanResponsiveness * Math.Max(0.0, dt));
            double aZoom = 1.0 - Math.Exp(-ZoomResponsiveness * Math.Max(0.0, dt));

            CamWX = Lerp(CamWX, TargetCamWX, aPan);
            CamWY = Lerp(CamWY, TargetCamWY, aPan);

            WorldToScreen = Lerp(WorldToScreen, TargetWorldToScreen, aZoom);
            OrbitYScale = Lerp(OrbitYScale, TargetOrbitYScale, aZoom);
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
