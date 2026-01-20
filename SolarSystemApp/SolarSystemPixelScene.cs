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
        private bool _showDebugOverlay;

        private Planet? _selectedPlanet;
        private Station? _selectedStation;
        private Ship? _selectedShip;

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
            DrawSelection(renderer);

            if (_showDebugOverlay)
                DrawDebugOverlay(ctx);
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

            if (input.WasPressed(ConsoleKey.P))
                _showDebugOverlay = !_showDebugOverlay;

            int dx, dy;
            input.GetDirectional(out dx, out dy);
            if (dx != 0 || dy != 0)
            {
                double pan = 0.8;
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

            if (input.WasPressed(ConsoleKey.Escape))
                ClearSelection();

            if (input.MouseLeftPressed && _sys != null)
                SelectAt(input.MouseX, input.MouseY);

            if (input.WasPressed(ConsoleKey.T))
                _timeScale = MathUtil.Clamp(_timeScale * 1.25, 0.25, 6.0);

            if (input.WasPressed(ConsoleKey.Y))
                _timeScale = MathUtil.Clamp(_timeScale / 1.25, 0.25, 6.0);

            if (input.WasPressed(ConsoleKey.K))
                _useKepler = !_useKepler;
        }

        private void SelectAt(int mouseX, int mouseY)
        {
            if (_sys == null)
                return;

            _camera.PixelToWorld(mouseX, mouseY, out double wx, out double wy);

            double thresholdWorld = Math.Max(0.15, 6.0 / Math.Max(1.0, _camera.Zoom));

            Planet? nearestPlanet = null;
            double nearestPlanetDist = double.MaxValue;
            for (int i = 0; i < _sys.Planets.Count; i++)
            {
                Planet planet = _sys.Planets[i];
                double dx = planet.WX - wx;
                double dy = planet.WY - wy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double capture = planet.RadiusWorld + thresholdWorld;
                if (dist <= capture && dist < nearestPlanetDist)
                {
                    nearestPlanet = planet;
                    nearestPlanetDist = dist;
                }
            }

            if (nearestPlanet != null)
            {
                _selectedPlanet = nearestPlanet;
                _selectedStation = null;
                _selectedShip = null;
                return;
            }

            Station? nearestStation = null;
            double nearestStationDist = double.MaxValue;
            for (int i = 0; i < _sys.Stations.Count; i++)
            {
                Station station = _sys.Stations[i];
                double dx = station.WX - wx;
                double dy = station.WY - wy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= thresholdWorld && dist < nearestStationDist)
                {
                    nearestStation = station;
                    nearestStationDist = dist;
                }
            }

            if (nearestStation != null)
            {
                _selectedPlanet = null;
                _selectedStation = nearestStation;
                _selectedShip = null;
                return;
            }

            Ship? nearestShip = null;
            double nearestShipDist = double.MaxValue;
            for (int i = 0; i < _sys.Ships.Count; i++)
            {
                Ship ship = _sys.Ships[i];
                double dx = ship.WX - wx;
                double dy = ship.WY - wy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= thresholdWorld && dist < nearestShipDist)
                {
                    nearestShip = ship;
                    nearestShipDist = dist;
                }
            }

            if (nearestShip != null)
            {
                _selectedPlanet = null;
                _selectedStation = null;
                _selectedShip = nearestShip;
                return;
            }

            ClearSelection();
        }

        private void ClearSelection()
        {
            _selectedPlanet = null;
            _selectedStation = null;
            _selectedShip = null;
        }

        private void DrawSelection(PixelRenderer renderer)
        {
            Color highlight = Color.FromRgb(255, 210, 80);

            if (_selectedPlanet != null)
            {
                int cx = _camera.WorldToPixelX(_selectedPlanet.WX);
                int cy = _camera.WorldToPixelY(_selectedPlanet.WY);
                int radius = Math.Max(2, (int)Math.Round(_selectedPlanet.RadiusWorld * _camera.Zoom) + 2);
                renderer.DrawCircle(cx, cy, radius, highlight);
            }
            else if (_selectedStation != null)
            {
                int cx = _camera.WorldToPixelX(_selectedStation.WX);
                int cy = _camera.WorldToPixelY(_selectedStation.WY);
                renderer.DrawRect(cx - 3, cy - 3, 7, 7, highlight);
            }
            else if (_selectedShip != null)
            {
                int cx = _camera.WorldToPixelX(_selectedShip.WX);
                int cy = _camera.WorldToPixelY(_selectedShip.WY);
                renderer.DrawRect(cx - 3, cy - 3, 7, 7, highlight);
            }
        }

        private void DrawDebugOverlay(PixelEngineContext ctx)
        {
            PixelRenderer renderer = ctx.Renderer;
            int centerX = ctx.Width / 2;
            int centerY = ctx.Height / 2;

            Color crossColor = Color.FromRgb(200, 200, 200);
            renderer.DrawLine(centerX - 4, centerY, centerX + 4, centerY, crossColor);
            renderer.DrawLine(centerX, centerY - 4, centerX, centerY + 4, crossColor);

            int originX = _camera.WorldToPixelX(0.0);
            int originY = _camera.WorldToPixelY(0.0);
            Color axisColor = Color.FromRgb(120, 200, 255);
            renderer.DrawCircle(originX, originY, 3, axisColor);
            renderer.DrawLine(originX - 20, originY, originX + 20, originY, axisColor);
            renderer.DrawLine(originX, originY - 20, originX, originY + 20, axisColor);

            renderer.DrawRect(0, 0, renderer.Width, renderer.Height, Color.FromRgb(255, 255, 255));

            if (_sys != null)
            {
                double maxA = GetMaxA();
                int radius = Math.Max(1, (int)Math.Round(maxA * _camera.Zoom));
                renderer.DrawCircle(originX, originY, radius, Color.FromRgb(80, 160, 255));
            }
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

            double maxA = GetMaxA();

            double size = Math.Min(ctx.Width, ctx.Height);
            double zoom = Math.Max(2.0, size * 0.45 / maxA);
            _camera.Zoom = MathUtil.Clamp(zoom, 2.0, 200.0);
            _camera.TargetZoom = _camera.Zoom;
        }

        private double GetMaxA()
        {
            if (_sys == null)
                return 10.0;

            double maxA = 10.0;
            for (int i = 0; i < _sys.Planets.Count; i++)
                if (_sys.Planets[i].A > maxA) maxA = _sys.Planets[i].A;

            return maxA;
        }
    }
}
