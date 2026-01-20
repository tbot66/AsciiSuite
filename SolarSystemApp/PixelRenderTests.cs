using System;
using AsciiEngine;
using SolarSystemApp.Rendering;
using SolarSystemApp.Util;
using SolarSystemApp.World;

namespace SolarSystemApp
{
    public sealed class PixelRenderTests : IPixelApp
    {
        private readonly PixelCamera2D _camera = new PixelCamera2D();
        private readonly PixelSystemRenderer _renderer;
        private readonly Galaxy _galaxy = new Galaxy();
        private StarSystem? _sys;
        private float _angle;

        public PixelRenderTests()
        {
            _renderer = new PixelSystemRenderer(_camera);
        }

        public void Init(PixelEngineContext ctx)
        {
            _galaxy.Build(seed: 12345, count: 1);
            _sys = _galaxy.Get(0);
            FitSystemToView(ctx);
            _camera.Snap(0.0, 0.0, _camera.Zoom);
        }

        public void Update(PixelEngineContext ctx)
        {
            _angle += (float)ctx.DeltaTime;
            _camera.SetViewport(ctx.Width, ctx.Height);
            _camera.Update(ctx.DeltaTime);

            if (_sys != null)
                StarSystemLogic.UpdateCelestials(_sys, ctx.Time, useKepler: true);
        }

        public void Draw(PixelEngineContext ctx)
        {
            PixelRenderer renderer = ctx.Renderer;
            renderer.Clear(Color.FromRgb(10, 10, 20));

            int rectW = Math.Max(8, renderer.Width / 6);
            int rectH = Math.Max(8, renderer.Height / 6);

            int x = (int)((Math.Sin(_angle) * 0.5f + 0.5f) * (renderer.Width - rectW));
            int y = (int)((Math.Cos(_angle * 0.9f) * 0.5f + 0.5f) * (renderer.Height - rectH));

            renderer.FillRect(x, y, rectW, rectH, Color.FromRgb(92, 180, 255));
            renderer.DrawLine(0, 0, renderer.Width - 1, renderer.Height - 1, Color.FromRgb(255, 180, 80));

            if (_sys != null)
                _renderer.DrawSystem(renderer, _sys, showOrbits: true);
        }

        private void FitSystemToView(PixelEngineContext ctx)
        {
            if (_sys == null)
                return;

            double maxA = 10.0;
            for (int i = 0; i < _sys.Planets.Count; i++)
                if (_sys.Planets[i].A > maxA) maxA = _sys.Planets[i].A;

            double size = Math.Min(ctx.Width, ctx.Height);
            double zoom = Math.Max(2.0, size * 0.45 / maxA);
            _camera.Zoom = MathUtil.Clamp(zoom, 2.0, 200.0);
            _camera.TargetZoom = _camera.Zoom;
        }
    }
}
