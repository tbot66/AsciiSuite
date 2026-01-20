using System;
using AsciiEngine;
using SolarSystemApp.Rendering;
using SolarSystemApp.Util;
using SolarSystemApp.World;

namespace SolarSystemApp
{
    public sealed class SolarSystemPixelScene : IPixelApp
    {
        private const double SimStep = 1.0 / 60.0;

        private readonly Galaxy _galaxy = new Galaxy();
        private readonly PixelCamera2D _camera = new PixelCamera2D();
        private readonly PixelSystemRenderer _renderer;

        private StarSystem? _sys;
        private double _simTime;
        private double _simAccum;
        private double _timeScale = 1.0;
        private bool _paused;
        private bool _follow = true;
        private bool _showOrbits = true;
        private bool _useKepler = true;

        public SolarSystemPixelScene()
        {
            _renderer = new PixelSystemRenderer(_camera);
        }

        public void Init(PixelEngineContext ctx)
        {
            _galaxy.Build(seed: 12345, count: 100);
            _sys = _galaxy.Get(0);

            FitSystemToView(ctx);
            _camera.Snap(0.0, 0.0, _camera.Zoom);
        }

        public void Update(PixelEngineContext ctx)
        {
            double dt = ctx.DeltaTime;

            HandleInput(ctx);
            AdvanceSimulation(dt);

            if (_follow)
            {
                _camera.TargetX = 0.0;
                _camera.TargetY = 0.0;
            }

            _camera.SetViewport(ctx.Width, ctx.Height);
            _camera.Update(dt);
        }

        public void Draw(PixelEngineContext ctx)
        {
            PixelRenderer renderer = ctx.Renderer;
            renderer.Clear(Color.FromRgb(8, 8, 16));

            if (_sys == null)
                return;

            _renderer.DrawSystem(renderer, _sys, _showOrbits);
        }

        private void HandleInput(PixelEngineContext ctx)
        {
            InputState input = ctx.Input;

            if (input.WasPressed(ConsoleKey.Spacebar))
                _paused = !_paused;

            if (input.WasPressed(ConsoleKey.O))
                _showOrbits = !_showOrbits;

            if (input.WasPressed(ConsoleKey.F))
                _follow = !_follow;

            int dx, dy;
            input.GetDirectional(out dx, out dy);
            if (dx != 0 || dy != 0)
            {
                double pan = 12.0 / Math.Max(1.0, _camera.Zoom);
                _camera.NudgeTarget(dx * pan, dy * pan);
            }

            if (input.WasPressed(ConsoleKey.OemPlus) || input.WasPressed(ConsoleKey.Add))
                _camera.MultiplyTargetZoom(1.12, 2.0, 200.0);

            if (input.WasPressed(ConsoleKey.OemMinus) || input.WasPressed(ConsoleKey.Subtract))
                _camera.MultiplyTargetZoom(1.0 / 1.12, 2.0, 200.0);

            if (input.WasPressed(ConsoleKey.R))
            {
                FitSystemToView(ctx);
                _camera.Snap(0.0, 0.0, _camera.Zoom);
            }

            if (input.WasPressed(ConsoleKey.T))
                _timeScale = MathUtil.Clamp(_timeScale * 1.25, 0.25, 6.0);

            if (input.WasPressed(ConsoleKey.Y))
                _timeScale = MathUtil.Clamp(_timeScale / 1.25, 0.25, 6.0);

            if (input.WasPressed(ConsoleKey.K))
                _useKepler = !_useKepler;
        }

        private void AdvanceSimulation(double dt)
        {
            if (_sys == null)
                return;

            if (dt > 0.25) dt = 0.25;

            if (!_paused)
            {
                _simAccum += dt * _timeScale;
                if (_simAccum > 0.5) _simAccum = 0.5;

                while (_simAccum >= SimStep)
                {
                    _simTime += SimStep;
                    StarSystemLogic.UpdateCelestials(_sys, _simTime, _useKepler);
                    StarSystemLogic.UpdateShips(_sys, SimStep);
                    _simAccum -= SimStep;
                }
            }
            else
            {
                StarSystemLogic.UpdateCelestials(_sys, _simTime, _useKepler);
            }
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
