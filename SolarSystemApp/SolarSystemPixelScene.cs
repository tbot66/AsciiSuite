using System;
using System.Collections.Generic;
using AsciiEngine;
using SolarSystemApp.Rendering;
using SolarSystemApp.Util;
using SolarSystemApp.World;
using Color = global::AsciiEngine.Color;
using Colors = global::AsciiEngine.Colors;

namespace SolarSystemApp
{
    public sealed class SolarSystemPixelScene : IPixelApp
    {
        private const double SimStep = 1.0 / 60.0;
        private const double ZoomStep = 1.12;
        private const int StarCount = 220;
        private const double StarSpan = 120.0;
        private const int DebrisCount = 180;
        private const double DebrisSpan = 32.0;
        private const double DebrisMaxV = 0.22;
        private const double BlackHoleChanceFallback = 0.12;
        private const int PlanetTextureSize = 256;
        private const double SunCacheInterval = 0.18;

        private readonly Galaxy _galaxy = new Galaxy();
        private StarSystem? _sys;
        private int _systemIndex;
        private int _galaxySelectionIndex;

        private double _worldToScreen = 10.0;
        private double _orbitYScale = 0.55;
        private double _camWX;
        private double _camWY;
        private double _targetCamWX;
        private double _targetCamWY;
        private double _targetWorldToScreen = 10.0;
        private double _targetOrbitYScale = 0.55;
        private int _centerX;
        private int _centerY;

        private bool _smoothCam = true;
        private double _panResponsiveness = 14.0;
        private double _zoomResponsiveness = 18.0;

        private bool _follow;
        private double _followLerp = 0.18;

        private double _simTime;
        private double _timeScale = 1.0;
        private bool _paused;
        private double _simAccum;
        private bool _useKepler = true;

        private bool _showOrbits = true;
        private bool _showLabels = true;
        private bool _showStarfield = true;
        private bool _showDebris = true;
        private bool _showNebula = true;

        private bool _galaxyView;
        private double _galCamX;
        private double _galCamY;
        private double _galZoom = 1.6;

        private readonly List<SelectionItem> _selection = new List<SelectionItem>(64);
        private int _selIndex;

        private StarPt[] _starPts = Array.Empty<StarPt>();
        private int _starSeedBuilt = int.MinValue;

        private DebrisPt[] _debris = Array.Empty<DebrisPt>();
        private int _debrisSeedBuilt = int.MinValue;

        private int _forcedBlackHoleSystemIndex = -1;

        private readonly EventLog _events = new EventLog(10);
        private readonly PixelFont _font = new PixelFont();

        private readonly Dictionary<int, PlanetTextureCache> _planetTextureCache = new Dictionary<int, PlanetTextureCache>(64);
        private SunTextureCache? _sunCache;
        private PlanetDrawer.Occluder[] _occluders = Array.Empty<PlanetDrawer.Occluder>();
        private double[] _occluderDepths = Array.Empty<double>();

        private struct StarPt
        {
            public double WX;
            public double WY;
            public double Depth;
        }

        private sealed class PlanetTextureCache
        {
            public int Seed;
            public PlanetDrawer.PlanetTexture Texture;
            public Color[] BaseColors = Array.Empty<Color>();
            public Color[] EmissiveColors = Array.Empty<Color>();
            public float[] EmissiveStrength = Array.Empty<float>();
        }

        private sealed class SunTextureCache
        {
            public int SunRadius;
            public int CoronaRadius;
            public int Size;
            public Color[] Colors = Array.Empty<Color>();
            public byte[] Mask = Array.Empty<byte>();
            public double LastUpdateTime;
        }

        private readonly struct RingParams
        {
            public readonly double InnerMul;
            public readonly double OuterMul;
            public readonly double PlaneCos;
            public readonly double PlaneSin;
            public readonly double TiltSin;
            public readonly double TiltCos;
            public readonly double PatternCos;
            public readonly double PatternSin;
            public readonly double Ux;
            public readonly double Uy;
            public readonly double Uz;
            public readonly double Vx;
            public readonly double Vy;
            public readonly double Vz;
            public readonly double Nx;
            public readonly double Ny;
            public readonly double Nz;
            public readonly double EdgeWidthMul;

            public RingParams(
                double innerMul,
                double outerMul,
                double planeCos,
                double planeSin,
                double tiltSin,
                double tiltCos,
                double patternCos,
                double patternSin,
                double ux,
                double uy,
                double uz,
                double vx,
                double vy,
                double vz,
                double nx,
                double ny,
                double nz,
                double edgeWidthMul)
            {
                InnerMul = innerMul;
                OuterMul = outerMul;
                PlaneCos = planeCos;
                PlaneSin = planeSin;
                TiltSin = tiltSin;
                TiltCos = tiltCos;
                PatternCos = patternCos;
                PatternSin = patternSin;
                Ux = ux;
                Uy = uy;
                Uz = uz;
                Vx = vx;
                Vy = vy;
                Vz = vz;
                Nx = nx;
                Ny = ny;
                Nz = nz;
                EdgeWidthMul = edgeWidthMul;
            }

            public bool IsValid => OuterMul > InnerMul;
        }

        private struct DebrisPt
        {
            public double WX;
            public double WY;
            public double VX;
            public double VY;
            public double Depth;
        }

        private enum SelectionKind
        {
            Sun,
            Planet,
            Moon,
            Station,
            Ship,
            Asteroid,
            Nebula
        }

        private struct SelectionItem
        {
            public SelectionKind Kind;
            public int Index;
            public int SubIndex;
            public string Label;
            public double WX;
            public double WY;
            public double WZ;
        }

        public void Init(PixelEngineContext ctx)
        {
            _galaxy.Build(seed: 12345, count: 100);

            if (_galaxy.Systems.Count > 0)
            {
                double r01 = HashNoise.Hash01(12345 ^ 0xB00F, 901, 777);
                _forcedBlackHoleSystemIndex = MathUtil.ClampInt((int)Math.Floor(r01 * _galaxy.Systems.Count), 0, _galaxy.Systems.Count - 1);
            }

            SetActiveSystem(0, resetSimTime: true);
            FitSystemToView(ctx);
            SnapCamera(0.0, 0.0, _worldToScreen, _orbitYScale);
        }

        public void Update(PixelEngineContext ctx)
        {
            double dt = ctx.DeltaTime;

            HandleInput(ctx);

            if (!_galaxyView)
            {
                AdvanceSimulation(dt);
                UpdateCamera(dt);
            }
        }

        public void Draw(PixelEngineContext ctx)
        {
            PixelRenderer renderer = ctx.Renderer;
            renderer.Clear(Color.FromRgb(6, 8, 16));

            _centerX = ctx.Width / 2;
            _centerY = ctx.Height / 2;

            if (_galaxyView)
            {
                DrawGalaxyView(renderer, ctx);
                DrawGalaxyUi(renderer, ctx);
                return;
            }

            if (_sys == null)
            {
                DrawText(renderer, 4, 4, "NO SYSTEM", Colors.BrightWhite);
                return;
            }

            if (_showStarfield)
                DrawStarfield(renderer, ctx);

            if (_showNebula)
                DrawNebula(renderer);

            DrawAsteroids(renderer, ctx);

            if (_showDebris)
                DrawDebris(renderer, ctx);

            if (_showOrbits)
                DrawOrbits(renderer);

            if (IsBlackHoleSystem(_sys, _systemIndex))
                DrawBlackHole(renderer);
            else
                DrawSun(renderer);

            BuildOccluders();
            DrawPlanets(renderer, _occluders, _occluderDepths);
            DrawStations(renderer);
            DrawShips(renderer);
            DrawSelection(renderer);
            DrawUi(renderer, ctx);
        }

        private void HandleInput(PixelEngineContext ctx)
        {
            InputState input = ctx.Input;

            if (input.WasPressed(ConsoleKey.G))
            {
                _galaxyView = !_galaxyView;
                if (_galaxyView)
                    _galaxySelectionIndex = _systemIndex;
            }

            if (_galaxyView)
            {
                HandleGalaxyInput(input);
                return;
            }

            if (input.WasPressed(ConsoleKey.Spacebar))
                _paused = !_paused;

            if (input.WasPressed(ConsoleKey.O))
                _showOrbits = !_showOrbits;

            if (input.WasPressed(ConsoleKey.L))
                _showLabels = !_showLabels;

            if (input.WasPressed(ConsoleKey.H))
                _showStarfield = !_showStarfield;

            if (input.WasPressed(ConsoleKey.N))
                _showNebula = !_showNebula;

            if (input.WasPressed(ConsoleKey.B))
                _showDebris = !_showDebris;

            if (input.WasPressed(ConsoleKey.K))
                _useKepler = !_useKepler;

            if (input.WasPressed(ConsoleKey.F))
                _follow = !_follow;

            if (input.WasPressed(ConsoleKey.Z))
                CycleSelection(-1);

            if (input.WasPressed(ConsoleKey.X))
                CycleSelection(1);

            if (input.WasPressed(ConsoleKey.U) || input.WasPressed(ConsoleKey.OemPlus) || input.WasPressed(ConsoleKey.Add))
            {
                _targetWorldToScreen *= ZoomStep;
                _targetWorldToScreen = MathUtil.Clamp(_targetWorldToScreen, 2.0, 240.0);
            }

            if (input.WasPressed(ConsoleKey.J) || input.WasPressed(ConsoleKey.OemMinus) || input.WasPressed(ConsoleKey.Subtract))
            {
                _targetWorldToScreen /= ZoomStep;
                _targetWorldToScreen = MathUtil.Clamp(_targetWorldToScreen, 2.0, 240.0);
            }

            if (input.WasPressed(ConsoleKey.R))
                FitSystemToView(ctx);

            int dx;
            int dy;
            input.GetDirectional(out dx, out dy);
            if (dx != 0 || dy != 0)
            {
                double panPixels = 14.0;
                double panWorld = panPixels / Math.Max(1.0, _worldToScreen);
                _targetCamWX += dx * panWorld;
                _targetCamWY += dy * panWorld;
                _follow = false;
            }

            if (input.WasPressed(ConsoleKey.T))
                _timeScale = MathUtil.Clamp(_timeScale * 1.25, 0.25, 6.0);

            if (input.WasPressed(ConsoleKey.Y))
                _timeScale = MathUtil.Clamp(_timeScale / 1.25, 0.25, 6.0);
        }

        private void HandleGalaxyInput(InputState input)
        {
            if (_galaxy.Systems.Count == 0)
                return;

            if (input.WasPressed(ConsoleKey.Z))
                _galaxySelectionIndex = MathUtil.WrapIndex(_galaxySelectionIndex - 1, _galaxy.Systems.Count);

            if (input.WasPressed(ConsoleKey.X))
                _galaxySelectionIndex = MathUtil.WrapIndex(_galaxySelectionIndex + 1, _galaxy.Systems.Count);

            int dx;
            int dy;
            input.GetDirectional(out dx, out dy);
            if (dx != 0 || dy != 0)
            {
                _galCamX += dx * 0.8 / Math.Max(0.1, _galZoom);
                _galCamY += dy * 0.8 / Math.Max(0.1, _galZoom);
            }

            if (input.WasPressed(ConsoleKey.U) || input.WasPressed(ConsoleKey.OemPlus) || input.WasPressed(ConsoleKey.Add))
                _galZoom = MathUtil.Clamp(_galZoom * 1.1, 0.3, 8.0);

            if (input.WasPressed(ConsoleKey.J) || input.WasPressed(ConsoleKey.OemMinus) || input.WasPressed(ConsoleKey.Subtract))
                _galZoom = MathUtil.Clamp(_galZoom / 1.1, 0.3, 8.0);

            if (input.WasPressed(ConsoleKey.Enter))
            {
                SetActiveSystem(_galaxySelectionIndex, resetSimTime: true);
                _galaxyView = false;
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
                    RefreshShipOrbitCenters(_sys);
                    StarSystemLogic.UpdateShips(_sys, SimStep);
                    UpdateDebris(SimStep);
                    _simAccum -= SimStep;
                }
            }
            else
            {
                StarSystemLogic.UpdateCelestials(_sys, _simTime, _useKepler);
            }
        }

        private void RefreshShipOrbitCenters(StarSystem sys)
        {
            for (int i = 0; i < sys.Ships.Count; i++)
            {
                Ship sh = sys.Ships[i];
                if (sh.Mode != ShipMode.Orbit)
                    continue;

                if (!TryGetOrbitTargetWorld(sys, sh.OrbitTargetKind, sh.OrbitTargetIndex, sh.OrbitTargetSubIndex, out double cx, out double cy))
                    continue;

                sh.TargetX = cx;
                sh.TargetY = cy;
            }
        }

        private static bool TryGetOrbitTargetWorld(StarSystem sys, OrbitTargetKind kind, int index, int subIndex, out double wx, out double wy)
        {
            wx = 0;
            wy = 0;

            switch (kind)
            {
                case OrbitTargetKind.Sun:
                    wx = 0;
                    wy = 0;
                    return true;
                case OrbitTargetKind.Planet:
                    if ((uint)index >= (uint)sys.Planets.Count) return false;
                    wx = sys.Planets[index].WX;
                    wy = sys.Planets[index].WY;
                    return true;
                case OrbitTargetKind.Station:
                    if ((uint)index >= (uint)sys.Stations.Count) return false;
                    wx = sys.Stations[index].WX;
                    wy = sys.Stations[index].WY;
                    return true;
                case OrbitTargetKind.Ship:
                    if ((uint)index >= (uint)sys.Ships.Count) return false;
                    wx = sys.Ships[index].WX;
                    wy = sys.Ships[index].WY;
                    return true;
                case OrbitTargetKind.Moon:
                    if ((uint)index >= (uint)sys.Planets.Count) return false;
                    Planet planet = sys.Planets[index];
                    if ((uint)subIndex >= (uint)planet.Moons.Count) return false;
                    wx = planet.Moons[subIndex].WX;
                    wy = planet.Moons[subIndex].WY;
                    return true;
                default:
                    return false;
            }
        }

        private void UpdateCamera(double dt)
        {
            _targetOrbitYScale = _orbitYScale;

            if (_follow)
            {
                SelectionItem? sel = GetSelection();
                if (sel.HasValue)
                {
                    _targetCamWX = MathUtil.Lerp(_targetCamWX, sel.Value.WX, _followLerp);
                    _targetCamWY = MathUtil.Lerp(_targetCamWY, sel.Value.WY, _followLerp);
                }
            }

            if (!_smoothCam)
            {
                _camWX = _targetCamWX;
                _camWY = _targetCamWY;
                _worldToScreen = _targetWorldToScreen;
                _orbitYScale = _targetOrbitYScale;
                return;
            }

            double aPan = 1.0 - Math.Exp(-_panResponsiveness * Math.Max(0.0, dt));
            double aZoom = 1.0 - Math.Exp(-_zoomResponsiveness * Math.Max(0.0, dt));

            _camWX = Lerp(_camWX, _targetCamWX, aPan);
            _camWY = Lerp(_camWY, _targetCamWY, aPan);
            _worldToScreen = Lerp(_worldToScreen, _targetWorldToScreen, aZoom);
            _orbitYScale = Lerp(_orbitYScale, _targetOrbitYScale, aZoom);
        }

        private void DrawStarfield(PixelRenderer renderer, PixelEngineContext ctx)
        {
            EnsureStarfield();

            for (int i = 0; i < _starPts.Length; i++)
            {
                StarPt sp = _starPts[i];
                double depth = sp.Depth;
                double par = 0.08 + 0.28 * depth;

                double relX = MathUtil.Wrap(sp.WX - _camWX * par, -StarSpan, StarSpan);
                double relY = MathUtil.Wrap(sp.WY - _camWY * par, -StarSpan, StarSpan);

                int sx = _centerX + (int)Math.Round(relX * _worldToScreen);
                int sy = _centerY + (int)Math.Round(relY * _worldToScreen * _orbitYScale);

                if ((uint)sx >= (uint)ctx.Width || (uint)sy >= (uint)ctx.Height)
                    continue;

                Color color = (depth > 0.85) ? Colors.BrightWhite : Colors.BrightBlack;
                renderer.SetPixel(sx, sy, color);
            }
        }

        private void DrawDebris(PixelRenderer renderer, PixelEngineContext ctx)
        {
            EnsureDebris();

            for (int i = 0; i < _debris.Length; i++)
            {
                DebrisPt d = _debris[i];
                double par = 0.10 + 0.25 * d.Depth;
                double wx = d.WX - _camWX * par;
                double wy = d.WY - _camWY * par;

                int sx = _centerX + (int)Math.Round(wx * _worldToScreen);
                int sy = _centerY + (int)Math.Round(wy * _worldToScreen * _orbitYScale);

                if ((uint)sx >= (uint)ctx.Width || (uint)sy >= (uint)ctx.Height)
                    continue;

                Color color = (d.Depth > 0.7) ? Colors.BrightBlack : Color.FromRgb(80, 84, 96);
                renderer.SetPixel(sx, sy, color);
            }
        }

        private void DrawNebula(PixelRenderer renderer)
        {
            if (_sys == null || _sys.Nebulae.Count == 0)
                return;

            for (int i = 0; i < _sys.Nebulae.Count; i++)
            {
                NebulaCloud cloud = _sys.Nebulae[i];
                int cx = WorldToScreenX(cloud.WX);
                int cy = WorldToScreenY(cloud.WY);
                int r = Math.Max(2, (int)Math.Round(cloud.RadiusWorld * _worldToScreen));
                int step = (r > 90) ? 3 : (r > 50 ? 2 : 1);
                int r2 = r * r;

                Color baseColor = ColorUtils.ToRgbColor((Color)cloud.Fg);
                double density = MathUtil.Clamp(cloud.Density01, 0.1, 1.0);

                for (int y = -r; y <= r; y += step)
                {
                    int yy = y * y;
                    for (int x = -r; x <= r; x += step)
                    {
                        int d2 = x * x + yy;
                        if (d2 > r2)
                            continue;

                        double dist = Math.Sqrt(d2) / Math.Max(1.0, r);
                        double falloff = 1.0 - dist;
                        double n = HashNoise.ValueNoise(cloud.NoiseSeed, (cx + x) * 0.05, (cy + y) * 0.05);
                        double alpha = density * falloff * (0.55 + 0.45 * n);
                        if (alpha < 0.08)
                            continue;

                        Color color = ColorUtils.Shade(baseColor, MathUtil.Clamp(alpha, 0.1, 1.0));
                        renderer.FillRect(cx + x, cy + y, step, step, color);
                    }
                }
            }
        }

        private void DrawOrbits(PixelRenderer renderer)
        {
            if (_sys == null)
                return;

            Color orbitColor = Color.FromRgb(72, 88, 120);

            for (int i = 0; i < _sys.Planets.Count; i++)
            {
                Planet planet = _sys.Planets[i];
                if (planet.A <= 0.0)
                    continue;

                int steps = Math.Max(120, (int)Math.Round(planet.A * 24));
                double prevX = 0.0;
                double prevY = 0.0;
                bool hasPrev = false;

                for (int s = 0; s <= steps; s++)
                {
                    double m = (s / (double)steps) * Math.PI * 2.0;
                    ComputeOrbitPoint(_sys.Seed, planet, m, out double wx, out double wy);

                    if (hasPrev)
                    {
                        renderer.DrawLine(WorldToScreenX(prevX), WorldToScreenY(prevY), WorldToScreenX(wx), WorldToScreenY(wy), orbitColor);
                    }

                    prevX = wx;
                    prevY = wy;
                    hasPrev = true;
                }

                for (int m = 0; m < planet.Moons.Count; m++)
                {
                    Moon moon = planet.Moons[m];
                    DrawMoonOrbit(renderer, planet, moon, orbitColor);
                }
            }
        }

        private void DrawMoonOrbit(PixelRenderer renderer, Planet planet, Moon moon, Color orbitColor)
        {
            int steps = 60;
            double r = moon.LocalRadius;
            if (r <= 0.0)
                return;

            double prevX = 0.0;
            double prevY = 0.0;
            bool hasPrev = false;

            for (int s = 0; s <= steps; s++)
            {
                double a = (s / (double)steps) * Math.PI * 2.0;
                double wx = planet.WX + Math.Cos(a) * r;
                double wy = planet.WY + Math.Sin(a) * r;

                if (hasPrev)
                    renderer.DrawLine(WorldToScreenX(prevX), WorldToScreenY(prevY), WorldToScreenX(wx), WorldToScreenY(wy), orbitColor);

                prevX = wx;
                prevY = wy;
                hasPrev = true;
            }
        }

        private void DrawSun(PixelRenderer renderer)
        {
            if (_sys == null || !_sys.HasStar || _sys.SunRadiusWorld <= 0.0)
                return;

            int cx = WorldToScreenX(0.0);
            int cy = WorldToScreenY(0.0);
            int sunR = MathUtil.ClampInt((int)Math.Round(_sys.SunRadiusWorld * _worldToScreen), 2, 140);
            int coronaThickness = MathUtil.ClampInt((int)Math.Round(sunR * 0.28), 2, 18);
            int coronaR = MathUtil.ClampInt(sunR + coronaThickness, sunR + 2, 180);

            SunTextureCache cache = GetSunCache(sunR, coronaR, _simTime);

            int size = cache.Size;
            int radius = cache.CoronaRadius;
            int offset = radius;

            int x0 = cx - offset;
            int y0 = cy - offset;

            int idx = 0;
            for (int y = 0; y < size; y++)
            {
                int py = y0 + y;
                for (int x = 0; x < size; x++)
                {
                    if (cache.Mask[idx] != 0)
                    {
                        int px = x0 + x;
                        renderer.SetPixel(px, py, cache.Colors[idx]);
                    }
                    idx++;
                }
            }
        }

        private void DrawBlackHole(PixelRenderer renderer)
        {
            int cx = WorldToScreenX(0.0);
            int cy = WorldToScreenY(0.0);

            int basePx = MathUtil.ClampInt((int)Math.Round(1.2 * _worldToScreen), 7, 22);
            int shadowR = MathUtil.ClampInt((int)Math.Round(basePx * 1.10), 6, 26);
            int holeR = MathUtil.ClampInt((int)Math.Round(basePx * 0.85), 5, 22);
            int ringR = MathUtil.ClampInt((int)Math.Round(basePx * 1.25), 7, 30);
            int diskR0 = MathUtil.ClampInt((int)Math.Round(basePx * 1.60), 10, 40);
            int diskR1 = MathUtil.ClampInt((int)Math.Round(basePx * 2.35), 14, 60);

            renderer.FillCircle(cx, cy, shadowR, Colors.Black);
            renderer.FillCircle(cx, cy, holeR, Colors.Black);

            int ringSteps = Math.Max(120, ringR * 14);
            for (int i = 0; i < ringSteps; i++)
            {
                double a = (i / (double)ringSteps) * Math.PI * 2.0;
                double ex = Math.Cos(a) * ringR;
                double ey = Math.Sin(a) * ringR * 0.88;
                int px = cx + (int)Math.Round(ex);
                int py = cy + (int)Math.Round(ey);
                double beam = 0.5 + 0.5 * Math.Cos(a + _simTime * 0.2);
                beam = Math.Pow(beam, 2.0);
                Color ringColor = ColorUtils.Shade(Colors.BrightYellow, MathUtil.Clamp(beam, 0.2, 1.0));
                renderer.SetPixel(px, py, ringColor);
            }

            double tilt = 0.32;
            double diskFlat = 0.18;
            double rot = _simTime * 0.18;
            double ct = Math.Cos(tilt);
            double st = Math.Sin(tilt);
            int bands = 4;
            int steps = Math.Max(180, diskR1 * 16);

            for (int band = 0; band < bands; band++)
            {
                double t = (bands <= 1) ? 0.0 : band / (double)(bands - 1);
                double rr = MathUtil.Lerp(diskR0, diskR1, t);
                double radial = Math.Pow(1.0 - t, 0.85);

                for (int si = 0; si < steps; si++)
                {
                    double a = (si / (double)steps) * Math.PI * 2.0 - rot;
                    double x0 = Math.Cos(a) * rr;
                    double y0 = Math.Sin(a) * rr * diskFlat;
                    double xr = x0 * ct - y0 * st;
                    double yr = x0 * st + y0 * ct;

                    int px = cx + (int)Math.Round(xr);
                    int py = cy + (int)Math.Round(yr);

                    double beam = 0.5 + 0.5 * Math.Cos(a + rot);
                    beam = Math.Pow(beam, 2.2);
                    double b = 0.10 + 0.65 * radial;
                    b *= (0.40 + 0.85 * beam);
                    b = MathUtil.Clamp(b, 0.0, 1.0);

                    Color diskColor = ColorUtils.Shade(Colors.BrightWhite, b);
                    renderer.SetPixel(px, py, diskColor);
                }
            }
        }

        private void DrawPlanets(
            PixelRenderer renderer,
            ReadOnlySpan<PlanetDrawer.Occluder> occluders,
            ReadOnlySpan<double> occluderDepths)
        {
            if (_sys == null)
                return;

            for (int i = 0; i < _sys.Planets.Count; i++)
            {
                Planet planet = _sys.Planets[i];
                DrawPlanetBody(renderer, planet, occluders, occluderDepths);

                for (int m = 0; m < planet.Moons.Count; m++)
                {
                    Moon moon = planet.Moons[m];
                    DrawMoonBody(renderer, moon, occluders, occluderDepths);
                }
            }
        }

        private void DrawAsteroids(PixelRenderer renderer, PixelEngineContext ctx)
        {
            if (_sys == null || _sys.Asteroids.Count == 0)
                return;

            for (int i = 0; i < _sys.Asteroids.Count; i++)
            {
                Asteroid asteroid = _sys.Asteroids[i];
                int x = WorldToScreenX(asteroid.WX);
                int y = WorldToScreenY(asteroid.WY);
                if ((uint)x >= (uint)ctx.Width || (uint)y >= (uint)ctx.Height)
                    continue;

                int r = Math.Max(1, (int)Math.Round(asteroid.RadiusWorld * _worldToScreen));
                Color c = ColorUtils.ToRgbColor((Color)asteroid.Fg);
                if (r <= 1)
                    renderer.SetPixel(x, y, c);
                else
                    renderer.FillCircle(x, y, r, c);
            }
        }

        private void DrawStations(PixelRenderer renderer)
        {
            if (_sys == null)
                return;

            for (int i = 0; i < _sys.Stations.Count; i++)
            {
                Station station = _sys.Stations[i];
                int x = WorldToScreenX(station.WX);
                int y = WorldToScreenY(station.WY);
                renderer.FillRect(x - 2, y - 2, 4, 4, Color.FromRgb(200, 220, 255));
                renderer.DrawRect(x - 3, y - 3, 6, 6, Color.FromRgb(80, 120, 200));

                if (_showLabels)
                    DrawText(renderer, x + 4, y - 4, station.Name, Colors.BrightCyan);
            }
        }

        private void DrawShips(PixelRenderer renderer)
        {
            if (_sys == null)
                return;

            for (int i = 0; i < _sys.Ships.Count; i++)
            {
                Ship ship = _sys.Ships[i];
                DrawTrail(renderer, ship);

                int x = WorldToScreenX(ship.WX);
                int y = WorldToScreenY(ship.WY);
                Color shipColor = ColorUtils.ToRgbColor((Color)ship.Fg);
                DrawShipGlyph(renderer, x, y, ship.VX, ship.VY, shipColor);

                if (_showLabels)
                    DrawText(renderer, x + 4, y - 4, ship.Name, Colors.BrightYellow);
            }
        }

        private void DrawTrail(PixelRenderer renderer, Ship ship)
        {
            int count = 0;
            foreach (var _ in ship.Trail.Points)
                count++;
            if (count == 0)
                return;

            int idx = 0;
            Color baseColor = ColorUtils.ToRgbColor((Color)ship.Fg);

            foreach (var pt in ship.Trail.Points)
            {
                double t = (count <= 1) ? 1.0 : idx / (double)(count - 1);
                double fade = 0.2 + 0.6 * t;
                Color c = ColorUtils.Shade(baseColor, fade);

                int x = WorldToScreenX(pt.x);
                int y = WorldToScreenY(pt.y);
                renderer.SetPixel(x, y, c);
                idx++;
            }
        }

        private void DrawShipGlyph(PixelRenderer renderer, int x, int y, double vx, double vy, Color color)
        {
            if (Math.Abs(vx) > Math.Abs(vy))
            {
                int dir = vx >= 0 ? 1 : -1;
                renderer.SetPixel(x, y, color);
                renderer.SetPixel(x - dir, y - 1, color);
                renderer.SetPixel(x - dir, y + 1, color);
            }
            else
            {
                int dir = vy >= 0 ? 1 : -1;
                renderer.SetPixel(x, y, color);
                renderer.SetPixel(x - 1, y - dir, color);
                renderer.SetPixel(x + 1, y - dir, color);
            }
        }

        private void DrawSelection(PixelRenderer renderer)
        {
            SelectionItem? sel = GetSelection();
            if (!sel.HasValue)
                return;

            Color highlight = Color.FromRgb(255, 210, 80);
            SelectionItem item = sel.Value;

            int cx = WorldToScreenX(item.WX);
            int cy = WorldToScreenY(item.WY);
            int radius = 6;

            switch (item.Kind)
            {
                case SelectionKind.Sun:
                    radius = Math.Max(6, (int)Math.Round(_sys?.SunRadiusWorld * _worldToScreen ?? 6.0) + 4);
                    renderer.DrawCircle(cx, cy, radius, highlight);
                    break;
                case SelectionKind.Planet:
                    if (_sys != null && (uint)item.Index < (uint)_sys.Planets.Count)
                        radius = Math.Max(4, (int)Math.Round(_sys.Planets[item.Index].RadiusWorld * _worldToScreen) + 4);
                    else
                        radius = 6;
                    renderer.DrawCircle(cx, cy, radius, highlight);
                    break;
                case SelectionKind.Moon:
                    if (_sys != null && (uint)item.Index < (uint)_sys.Planets.Count)
                    {
                        Planet planet = _sys.Planets[item.Index];
                        if ((uint)item.SubIndex < (uint)planet.Moons.Count)
                            radius = Math.Max(3, (int)Math.Round(planet.Moons[item.SubIndex].RadiusWorld * _worldToScreen) + 3);
                        else
                            radius = 4;
                    }
                    else
                    {
                        radius = 4;
                    }
                    renderer.DrawCircle(cx, cy, radius, highlight);
                    break;
                case SelectionKind.Station:
                case SelectionKind.Ship:
                    renderer.DrawRect(cx - 4, cy - 4, 9, 9, highlight);
                    break;
                case SelectionKind.Asteroid:
                    renderer.DrawRect(cx - 3, cy - 3, 7, 7, highlight);
                    break;
                case SelectionKind.Nebula:
                    renderer.DrawCircle(cx, cy, 10, highlight);
                    break;
            }
        }

        private void DrawUi(PixelRenderer renderer, PixelEngineContext ctx)
        {
            if (_sys == null)
                return;

            string header = $"{_sys.Name} [{_sys.Descriptor}]  t={_simTime:0.0}  x{_timeScale:0.0}  zoom={_worldToScreen:0.0}";
            if (_paused)
                header += "  PAUSED";

            DrawText(renderer, 4, 4, header, Colors.BrightWhite);

            SelectionItem? sel = GetSelection();
            if (sel.HasValue)
            {
                SelectionItem item = sel.Value;
                string info = $"Selected: {item.Label} ({item.Kind}) @ {item.WX:0.0},{item.WY:0.0}";
                DrawText(renderer, 4, 14, info, Colors.BrightYellow);
            }

            int lineHeight = _font.Height + 2;
            int line = ctx.Height - lineHeight - 4;
            foreach (string msg in _events.GetNewestFirst(6))
            {
                DrawText(renderer, 4, line, msg, Colors.BrightBlack);
                line -= lineHeight;
                if (line < 28)
                    break;
            }

            string controls = "WASD/Arrows Pan  U/J Zoom  Z/X Select  F Follow  G Galaxy  O Orbits  L Labels";
            DrawText(renderer, 4, ctx.Height - 12, controls, Colors.BrightCyan);
        }

        private void DrawGalaxyView(PixelRenderer renderer, PixelEngineContext ctx)
        {
            int w = ctx.Width;
            int h = ctx.Height;
            int cx = w / 2;
            int cy = h / 2;

            Color linkColor = Color.FromRgb(40, 60, 90);
            for (int i = 0; i < _galaxy.Links.Count; i++)
            {
                Galaxy.Link link = _galaxy.Links[i];
                StarSystem a = _galaxy.Systems[link.A];
                StarSystem b = _galaxy.Systems[link.B];

                int ax = cx + (int)Math.Round((a.GalaxyX - _galCamX) * 18.0 * _galZoom);
                int ay = cy + (int)Math.Round((a.GalaxyY - _galCamY) * 18.0 * _galZoom);
                int bx = cx + (int)Math.Round((b.GalaxyX - _galCamX) * 18.0 * _galZoom);
                int by = cy + (int)Math.Round((b.GalaxyY - _galCamY) * 18.0 * _galZoom);

                renderer.DrawLine(ax, ay, bx, by, linkColor);
            }

            for (int i = 0; i < _galaxy.Systems.Count; i++)
            {
                StarSystem sys = _galaxy.Systems[i];
                int sx = cx + (int)Math.Round((sys.GalaxyX - _galCamX) * 18.0 * _galZoom);
                int sy = cy + (int)Math.Round((sys.GalaxyY - _galCamY) * 18.0 * _galZoom);

                Color c = (i == _galaxySelectionIndex) ? Colors.BrightYellow : Colors.BrightWhite;
                if (i == _systemIndex)
                    c = Colors.BrightCyan;

                renderer.FillRect(sx - 1, sy - 1, 3, 3, c);
            }
        }

        private void DrawGalaxyUi(PixelRenderer renderer, PixelEngineContext ctx)
        {
            DrawText(renderer, 4, 4, "GALAXY VIEW", Colors.BrightWhite);
            DrawText(renderer, 4, 14, "Z/X Select  Enter Jump  G Back", Colors.BrightCyan);

            if (_galaxy.Systems.Count > 0)
            {
                StarSystem sys = _galaxy.Systems[_galaxySelectionIndex];
                DrawText(renderer, 4, 24, $"{sys.Name} [{sys.Descriptor}]", Colors.BrightYellow);
            }
        }

        private void DrawPlanetBody(
            PixelRenderer renderer,
            Planet planet,
            ReadOnlySpan<PlanetDrawer.Occluder> occluders,
            ReadOnlySpan<double> occluderDepths)
        {
            int seed = _sys == null ? 0 : _sys.Seed ^ (planet.Name?.GetHashCode() ?? 0);
            double spinTurns = PlanetDrawer.SpinTurns(_simTime, planet.SpinSpeed);
            DrawTexturedBody(renderer,
                planet.WX, planet.WY, planet.RadiusWorld,
                planet.Name,
                planet.HasRings,
                planet.Texture,
                seed,
                spinTurns,
                planet.AxisTilt,
                planet.WZ,
                occluders,
                occluderDepths);
        }

        private void DrawMoonBody(
            PixelRenderer renderer,
            Moon moon,
            ReadOnlySpan<PlanetDrawer.Occluder> occluders,
            ReadOnlySpan<double> occluderDepths)
        {
            int seed = _sys == null ? 0 : _sys.Seed ^ (moon.Name?.GetHashCode() ?? 0);
            double spinTurns = ComputeMoonSpinTurns(moon, seed);
            DrawTexturedBody(renderer,
                moon.WX, moon.WY, moon.RadiusWorld,
                moon.Name,
                false,
                moon.Texture,
                seed,
                spinTurns,
                axisTilt: 0.0,
                moon.WZ,
                occluders,
                occluderDepths);
        }

        private void DrawTexturedBody(
            PixelRenderer renderer,
            double wx,
            double wy,
            double radiusWorld,
            string label,
            bool rings,
            PlanetDrawer.PlanetTexture texture,
            int seed,
            double spinTurns,
            double axisTilt,
            double bodyDepth,
            ReadOnlySpan<PlanetDrawer.Occluder> occluders,
            ReadOnlySpan<double> occluderDepths)
        {
            int cx = WorldToScreenX(wx);
            int cy = WorldToScreenY(wy);
            int r = Math.Max(1, (int)Math.Round(radiusWorld * _worldToScreen));
            if (r <= 0)
                return;

            PlanetTextureCache cache = GetPlanetTextureCache(seed, texture);
            ComputeLightDir(wx, wy, out double lx, out double ly, out double lz);

            RingParams ringParams = default;
            bool hasRings = rings;
            if (rings)
                ringParams = BuildRingParams(seed, axisTilt, _simTime);

            DrawTexturedSphere(renderer, cx, cy, r, spinTurns, axisTilt, cache, lx, ly, lz,
                hasRings, ringParams, bodyDepth, occluders, occluderDepths);

            if (rings)
            {
                DrawRings(renderer, cx, cy, r, texture, seed, ringParams, lx, ly, lz,
                    bodyDepth, occluders, occluderDepths);
            }

            if (_showLabels)
                DrawText(renderer, cx + r + 2, cy - r - 2, label, Colors.BrightWhite);
        }

        private void DrawTexturedSphere(
            PixelRenderer renderer,
            int cx,
            int cy,
            int r,
            double spinTurns,
            double axisTilt,
            PlanetTextureCache cache,
            double lx,
            double ly,
            double lz,
            bool hasRings,
            in RingParams ringParams,
            double bodyDepth,
            ReadOnlySpan<PlanetDrawer.Occluder> occluders,
            ReadOnlySpan<double> occluderDepths)
        {
            if (r <= 0)
                return;

            bool useRingShadow = hasRings && ringParams.IsValid;

            double ct = Math.Cos(axisTilt);
            double st = Math.Sin(axisTilt);
            bool hasTilt = Math.Abs(axisTilt) > 1e-6;

            int rr = r * r;
            double invR = 1.0 / r;

            for (int y = -r; y <= r; y++)
            {
                int yy = y * y;
                for (int x = -r; x <= r; x++)
                {
                    int d2 = x * x + yy;
                    if (d2 > rr)
                        continue;

                    int px = cx + x;
                    int py = cy + y;

                    if (IsOccludedPixel(px, py, bodyDepth, cx, cy, r, occluders, occluderDepths, skipSelf: true))
                        continue;

                    double nx = x * invR;
                    double ny = y * invR;
                    double nz = Math.Sqrt(Math.Max(0.0, 1.0 - nx * nx - ny * ny));

                    double tx = nx;
                    double ty = ny;
                    double tz = nz;
                    if (hasTilt)
                    {
                        double x2 = tx * ct - ty * st;
                        double y2 = tx * st + ty * ct;
                        tx = x2;
                        ty = y2;
                    }

                    double lon = Math.Atan2(tx, tz);
                    double u = lon / (Math.PI * 2.0) + 0.5 + spinTurns;
                    double v = Math.Asin(MathUtil.Clamp(ty, -1.0, 1.0)) / Math.PI + 0.5;

                    SamplePlanetTexture(cache, u, v, out Color baseColor, out float emissive, out Color emissiveColor);

                    double ndotlRaw = nx * lx + ny * ly + nz * lz;
                    double ndotl = ndotlRaw;
                    if (ndotl < 0.0) ndotl = 0.0;
                    double limb = 0.78 + 0.22 * nz;
                    double light = MathUtil.Clamp(ndotl * limb, 0.0, 1.0);

                    if (useRingShadow)
                    {
                        double ringShadow = RingShadowFactor(nx, ny, nz, lx, ly, lz, ringParams);
                        light *= ringShadow;
                    }

                    Color shaded = ColorUtils.Shade(baseColor, light);

                    if (emissive > 0.0001f)
                    {
                        double night = MathUtil.Clamp((0.22 - ndotlRaw) / 0.70, 0.0, 1.0);
                        double e = emissive * night;
                        if (e > 0.0001)
                            shaded = BlendRgb(shaded, emissiveColor, MathUtil.Clamp(e, 0.0, 0.85));
                    }

                    renderer.SetPixel(px, py, shaded);
                }
            }
        }

        private static void ComputeLightDir(double wx, double wy, out double lx, out double ly, out double lz)
        {
            lx = -wx;
            ly = -wy;
            lz = 0.6;

            double len = Math.Sqrt(lx * lx + ly * ly + lz * lz);
            if (len > 0.0001)
            {
                lx /= len;
                ly /= len;
                lz /= len;
            }
        }

        private static RingParams BuildRingParams(int seed, double axisTilt, double simTime)
        {
            const double InnerMin = 1.15;
            const double InnerMax = 1.35;
            const double OuterMin = 1.35;
            const double OuterMax = 1.75;

            double rIn = HashNoise.Hash01(seed, 10, 20);
            double rOut = HashNoise.Hash01(seed, 30, 40);

            double innerMul = InnerMin + (InnerMax - InnerMin) * rIn;
            double outerMul = OuterMin + (OuterMax - OuterMin) * rOut;

            if (outerMul < innerMul + 0.20) outerMul = innerMul + 0.20;

            double planeAngle = HashNoise.Hash01(seed, 777, 999) * Math.PI * 2.0;
            double ringSpinRate = 0.02 + 0.03 * HashNoise.Hash01(seed, 222, 333);
            double patternAngle = simTime * ringSpinRate + HashNoise.Hash01(seed, 888, 111) * Math.PI * 2.0;

            double planeCos = Math.Cos(planeAngle);
            double planeSin = Math.Sin(planeAngle);
            double patternCos = Math.Cos(patternAngle);
            double patternSin = Math.Sin(patternAngle);

            double tiltSin = Math.Sin(axisTilt);
            double tiltCos = Math.Cos(axisTilt);

            double ux = planeCos;
            double uy = planeSin;
            double uz = 0.0;

            double vx = -planeSin * tiltCos;
            double vy = planeCos * tiltCos;
            double vz = -tiltSin;

            double nx = -planeSin * tiltSin;
            double ny = planeCos * tiltSin;
            double nz = tiltCos;

            double edgeWidthMul = MathUtil.Clamp((outerMul - innerMul) * 0.10, 0.04, 0.12);

            return new RingParams(
                innerMul,
                outerMul,
                planeCos,
                planeSin,
                tiltSin,
                tiltCos,
                patternCos,
                patternSin,
                ux,
                uy,
                uz,
                vx,
                vy,
                vz,
                nx,
                ny,
                nz,
                edgeWidthMul);
        }

        private void DrawRings(
            PixelRenderer renderer,
            int cx,
            int cy,
            int planetR,
            PlanetDrawer.PlanetTexture texture,
            int seed,
            RingParams ring,
            double lx,
            double ly,
            double lz,
            double planetDepth,
            ReadOnlySpan<PlanetDrawer.Occluder> occluders,
            ReadOnlySpan<double> occluderDepths)
        {
            if (!ring.IsValid)
                return;

            PlanetTextures.PaletteId pal = default;
            bool useVariantPalette = (texture != PlanetDrawer.PlanetTexture.EarthLike && texture != PlanetDrawer.PlanetTexture.Continents);
            if (useVariantPalette)
                pal = PlanetTextures.PickPaletteVariant(seed, texture);

            PlanetTextures.GetPalette(texture, pal, out var dark, out var mid, out var light);

            Color c2 = DustifyColor(Color.FromRgb(dark.r, dark.g, dark.b), amount: 0.55);
            Color c1 = DustifyColor(Color.FromRgb(mid.r, mid.g, mid.b), amount: 0.45);
            Color c0 = DustifyColor(Color.FromRgb(light.r, light.g, light.b), amount: 0.35);

            DrawRingLayer(renderer, cx, cy, planetR, ring, c2, c1, c0, lx, ly, lz,
                planetDepth + 0.02, front: false, occluders, occluderDepths, seed);

            DrawRingLayer(renderer, cx, cy, planetR, ring, c2, c1, c0, lx, ly, lz,
                planetDepth - 0.02, front: true, occluders, occluderDepths, seed);
        }

        private void DrawRingLayer(
            PixelRenderer renderer,
            int cx,
            int cy,
            int planetR,
            RingParams ring,
            Color dark,
            Color mid,
            Color light,
            double lx,
            double ly,
            double lz,
            double ringDepth,
            bool front,
            ReadOnlySpan<PlanetDrawer.Occluder> occluders,
            ReadOnlySpan<double> occluderDepths,
            int seed)
        {
            double inner = planetR * ring.InnerMul;
            double outer = planetR * ring.OuterMul;
            if (outer <= inner + 0.01)
                return;

            double span = outer - inner;
            double invSpan = 1.0 / span;
            double tiltScale = Math.Max(0.15, Math.Abs(ring.TiltCos));
            double edgeWidth = Math.Max(1.0, span * ring.EdgeWidthMul);

            int box = (int)Math.Ceiling(outer) + 2;
            double invOuter = 1.0 / Math.Max(1.0, outer);

            double gapMajor = 0.55 + 0.05 * (HashNoise.Hash01(seed, 901, 902) - 0.5);
            double gapMajorW = 0.030 + 0.010 * HashNoise.Hash01(seed, 903, 904);

            double gap2 = 0.18 + 0.04 * (HashNoise.Hash01(seed, 905, 906) - 0.5);
            double gap2W = 0.018 + 0.008 * HashNoise.Hash01(seed, 907, 908);

            double gap3 = 0.82 + 0.04 * (HashNoise.Hash01(seed, 909, 910) - 0.5);
            double gap3W = 0.020 + 0.010 * HashNoise.Hash01(seed, 911, 912);

            bool faceOn = Math.Abs(ring.TiltSin) < 0.08;

            for (int y = -box; y <= box; y++)
            {
                int py = cy + y;
                for (int x = -box; x <= box; x++)
                {
                    int px = cx + x;

                    double rx = x * ring.PlaneCos + y * ring.PlaneSin;
                    double ry = -x * ring.PlaneSin + y * ring.PlaneCos;
                    double ryPlane = ry / tiltScale;

                    if (!faceOn)
                    {
                        bool isFront = (ryPlane * ring.TiltSin) < 0.0;
                        if (front != isFront)
                            continue;
                    }
                    else if (!front)
                    {
                        continue;
                    }

                    double d = Math.Sqrt(rx * rx + ryPlane * ryPlane);
                    if (d < inner || d > outer)
                        continue;

                    if (IsOccludedPixel(px, py, ringDepth, cx, cy, planetR, occluders, occluderDepths, skipSelf: front))
                        continue;

                    double band = (d - inner) * invSpan;
                    band = MathUtil.Clamp(band, 0.0, 1.0);

                    double dens = 1.0 - 0.55 * band;
                    dens = MathUtil.Clamp(dens, 0.0, 1.0);

                    double prx = rx * ring.PatternCos - ryPlane * ring.PatternSin;
                    double pry = rx * ring.PatternSin + ryPlane * ring.PatternCos;

                    double f1 = Math.Abs(Math.Sin((band * 18.0 + 0.7 * prx * 0.10) * Math.PI));
                    double f2 = Math.Abs(Math.Sin((band * 41.0 + 0.9 * pry * 0.08) * Math.PI));
                    double n1 = HashNoise.Hash01(seed, (int)Math.Round(prx * 0.4), (int)Math.Round(pry * 0.4));

                    dens *= (0.65 + 0.25 * f1 + 0.18 * f2);
                    dens *= (0.82 + 0.20 * (n1 - 0.5));

                    double gMajor = Math.Abs(band - gapMajor);
                    if (gMajor < gapMajorW) dens *= 0.12 + 0.35 * (gMajor / gapMajorW);

                    double g2 = Math.Abs(band - gap2);
                    if (g2 < gap2W) dens *= 0.25 + 0.55 * (g2 / gap2W);

                    double g3 = Math.Abs(band - gap3);
                    if (g3 < gap3W) dens *= 0.25 + 0.55 * (g3 / gap3W);

                    double dot = (x * lx + y * ly) * invOuter;
                    double ringLight = MathUtil.Clamp(0.55 + 0.40 * dot, 0.15, 1.0);

                    double edgeIn = (d - inner) / edgeWidth;
                    double edgeOut = (outer - d) / edgeWidth;
                    double edge = Math.Min(edgeIn, edgeOut);
                    edge = SmoothStep01(MathUtil.Clamp(edge, 0.0, 1.0));

                    double ru = rx / planetR;
                    double rv = ryPlane / planetR;
                    double shadow = RingPointShadowFactor(ru, rv, ring, lx, ly, lz);

                    double brightness = edge * (0.20 + 0.80 * dens) * (0.35 + 0.65 * ringLight) * shadow;
                    if (brightness <= 0.02)
                        continue;

                    Color fg = (dens < 0.33) ? dark : (dens < 0.66 ? mid : light);
                    fg = ColorUtils.Shade(fg, MathUtil.Clamp(brightness, 0.0, 1.0));

                    renderer.SetPixel(px, py, fg);
                }
            }
        }

        private static double RingShadowFactor(
            double nx,
            double ny,
            double nz,
            double lx,
            double ly,
            double lz,
            in RingParams ring)
        {
            double denom = ring.Nx * lx + ring.Ny * ly + ring.Nz * lz;
            if (Math.Abs(denom) < 1e-6)
                return 1.0;

            double t = -(ring.Nx * nx + ring.Ny * ny + ring.Nz * nz) / denom;
            if (t <= 0.0)
                return 1.0;

            double qx = nx + lx * t;
            double qy = ny + ly * t;
            double qz = nz + lz * t;

            double ru = qx * ring.Ux + qy * ring.Uy + qz * ring.Uz;
            double rv = qx * ring.Vx + qy * ring.Vy + qz * ring.Vz;
            double r = Math.Sqrt(ru * ru + rv * rv);

            if (r < ring.InnerMul || r > ring.OuterMul)
                return 1.0;

            double edgeIn = (r - ring.InnerMul) / ring.EdgeWidthMul;
            double edgeOut = (ring.OuterMul - r) / ring.EdgeWidthMul;
            double edge = Math.Min(edgeIn, edgeOut);
            edge = SmoothStep01(MathUtil.Clamp(edge, 0.0, 1.0));

            const double shadowMin = 0.65;
            return 1.0 - (1.0 - shadowMin) * edge;
        }

        private static double RingPointShadowFactor(
            double ru,
            double rv,
            in RingParams ring,
            double lx,
            double ly,
            double lz)
        {
            double px = ring.Ux * ru + ring.Vx * rv;
            double py = ring.Uy * ru + ring.Vy * rv;
            double pz = ring.Uz * ru + ring.Vz * rv;

            double b = px * lx + py * ly + pz * lz;
            double c = px * px + py * py + pz * pz - 1.0;
            double disc = b * b - c;

            if (disc <= 0.0)
                return 1.0;

            double t = -b - Math.Sqrt(disc);
            if (t <= 0.0)
                return 1.0;

            double closest2 = c - b * b;
            double dist = Math.Sqrt(Math.Max(0.0, closest2));
            double penumbra = SmoothStep01(MathUtil.Clamp((1.0 - dist) / 0.20, 0.0, 1.0));

            return 1.0 - 0.45 * penumbra;
        }

        private void BuildOccluders()
        {
            if (_sys == null)
            {
                _occluders = Array.Empty<PlanetDrawer.Occluder>();
                _occluderDepths = Array.Empty<double>();
                return;
            }

            var occluderList = OccluderBuilder.BuildForSystem(
                _sys,
                worldToScreenX: wx => WorldToScreenX(wx),
                worldToScreenY: wy => WorldToScreenY(wy),
                worldRadiusToChars: rw => MathUtil.ClampInt((int)Math.Round(rw * _worldToScreen), 1, 200));

            int occCount = (occluderList == null) ? 0 : occluderList.Count;
            if (occCount == 0)
            {
                _occluders = Array.Empty<PlanetDrawer.Occluder>();
                _occluderDepths = Array.Empty<double>();
                return;
            }

            if (_occluders.Length != occCount)
                _occluders = new PlanetDrawer.Occluder[occCount];
            if (_occluderDepths.Length != occCount)
                _occluderDepths = new double[occCount];

            for (int i = 0; i < occCount; i++)
                _occluders[i] = occluderList[i];

            int idx = 0;
            for (int i = 0; i < _sys.Planets.Count && idx < occCount; i++)
            {
                Planet p = _sys.Planets[i];
                _occluderDepths[idx++] = p.WZ;

                for (int m = 0; m < p.Moons.Count && idx < occCount; m++)
                {
                    Moon moon = p.Moons[m];
                    _occluderDepths[idx++] = moon.WZ;
                }
            }

            for (; idx < occCount; idx++)
                _occluderDepths[idx] = 0.0;
        }

        private static bool IsOccludedPixel(
            int px,
            int py,
            double depth,
            int selfX,
            int selfY,
            int selfR,
            ReadOnlySpan<PlanetDrawer.Occluder> occluders,
            ReadOnlySpan<double> occluderDepths,
            bool skipSelf)
        {
            if (occluders.IsEmpty || occluderDepths.IsEmpty)
                return false;

            int count = Math.Min(occluders.Length, occluderDepths.Length);
            for (int i = 0; i < count; i++)
            {
                var occ = occluders[i];
                if (skipSelf && occ.X == selfX && occ.Y == selfY && occ.R == selfR)
                    continue;

                if (occluderDepths[i] >= depth - 1e-6)
                    continue;

                int dx = px - occ.X;
                int dy = py - occ.Y;
                if (dx * dx + dy * dy <= occ.R * occ.R)
                    return true;
            }

            return false;
        }

        private double ComputeMoonSpinTurns(Moon moon, int seed)
        {
            if (Math.Abs(moon.SpinSpeed) > 1e-6)
                return PlanetDrawer.SpinTurns(_simTime, moon.SpinSpeed);

            double period = Math.Max(0.001, moon.LocalPeriod);
            if (period <= 0.0001)
            {
                double fallback = 0.6 + 1.4 * HashNoise.Hash01(seed, 73, 91);
                return PlanetDrawer.SpinTurns(_simTime, fallback);
            }

            double ang = moon.LocalPhase + (_simTime * (Math.PI * 2.0) / period);
            return ang / (Math.PI * 2.0);
        }

        private static double SmoothStep01(double x)
        {
            x = MathUtil.Clamp(x, 0.0, 1.0);
            return x * x * (3.0 - 2.0 * x);
        }

        private static Color DustifyColor(Color baseColor, double amount)
        {
            return BlendRgb(baseColor, Color.FromRgb(220, 225, 230), amount);
        }

        private PlanetTextureCache GetPlanetTextureCache(int seed, PlanetDrawer.PlanetTexture texture)
        {
            int key = unchecked(seed * 397) ^ ((int)texture << 16) ^ PlanetTextureSize;
            if (_planetTextureCache.TryGetValue(key, out PlanetTextureCache cache))
                return cache;

            cache = BuildPlanetTextureCache(seed, texture);
            _planetTextureCache[key] = cache;
            return cache;
        }

        private PlanetTextureCache BuildPlanetTextureCache(int seed, PlanetDrawer.PlanetTexture texture)
        {
            int size = PlanetTextureSize;
            int total = size * size;

            PlanetTextureCache cache = new PlanetTextureCache
            {
                Seed = seed,
                Texture = texture,
                BaseColors = new Color[total],
                EmissiveColors = new Color[total],
                EmissiveStrength = new float[total]
            };

            double invSize = 1.0 / size;

            int idx = 0;
            for (int y = 0; y < size; y++)
            {
                double v = (y + 0.5) * invSize;
                double lat = (v - 0.5) * Math.PI;
                double sinLat = Math.Sin(lat);
                double cosLat = Math.Cos(lat);

                for (int x = 0; x < size; x++)
                {
                    double u = (x + 0.5) * invSize;
                    double lon = (u - 0.5) * Math.PI * 2.0;
                    double sinLon = Math.Sin(lon);
                    double cosLon = Math.Cos(lon);

                    double nx = sinLon * cosLat;
                    double ny = sinLat;
                    double nz = cosLon * cosLat;

                    PlanetDrawer.SamplePlanetSurface(seed, texture, nx, ny, nz, 0.0,
                        out Color fg, out double emissive01, out Color emissiveColor);

                    cache.BaseColors[idx] = ColorUtils.ToRgbColor(fg);
                    cache.EmissiveColors[idx] = ColorUtils.ToRgbColor(emissiveColor);
                    cache.EmissiveStrength[idx] = (float)emissive01;
                    idx++;
                }
            }

            return cache;
        }

        private static void SamplePlanetTexture(PlanetTextureCache cache, double u, double v, out Color baseColor, out float emissive, out Color emissiveColor)
        {
            u -= Math.Floor(u);
            if (u < 0.0) u += 1.0;

            if (v < 0.0) v = 0.0;
            if (v > 1.0) v = 1.0;

            int x = (int)(u * (PlanetTextureSize - 1));
            int y = (int)(v * (PlanetTextureSize - 1));
            int idx = y * PlanetTextureSize + x;

            baseColor = cache.BaseColors[idx];
            emissive = cache.EmissiveStrength[idx];
            emissiveColor = cache.EmissiveColors[idx];
        }

        private SunTextureCache GetSunCache(int sunR, int coronaR, double time)
        {
            if (_sunCache == null || _sunCache.SunRadius != sunR || _sunCache.CoronaRadius != coronaR)
            {
                _sunCache = BuildSunCache(sunR, coronaR, time);
                return _sunCache;
            }

            if (time - _sunCache.LastUpdateTime >= SunCacheInterval)
                _sunCache = BuildSunCache(sunR, coronaR, time);

            return _sunCache;
        }

        private SunTextureCache BuildSunCache(int sunR, int coronaR, double time)
        {
            SunTextureCache cache = new SunTextureCache
            {
                SunRadius = sunR,
                CoronaRadius = coronaR,
                Size = coronaR * 2 + 1,
                LastUpdateTime = time
            };

            int size = cache.Size;
            int total = size * size;
            cache.Colors = new Color[total];
            cache.Mask = new byte[total];

            int seed = _sys == null ? 0 : _sys.Seed ^ 0x51A7BEEF;
            Color sunBase = _sys == null ? Colors.BrightYellow : ColorUtils.ToRgbColor((Color)_sys.SunColor);

            int sunR2 = sunR * sunR;
            int coronaR2 = coronaR * coronaR;
            double invBandDen = 1.0 / Math.Max(1.0, (coronaR - sunR));

            int idx = 0;
            for (int y = -coronaR; y <= coronaR; y++)
            {
                for (int x = -coronaR; x <= coronaR; x++)
                {
                    int d2i = x * x + y * y;
                    if (d2i > coronaR2)
                    {
                        idx++;
                        continue;
                    }

                    if (d2i >= sunR2)
                    {
                        double d = Math.Sqrt(d2i);
                        double band = (d - sunR) * invBandDen;
                        if (band < 0.0 || band > 1.0)
                        {
                            idx++;
                            continue;
                        }

                        double edge = 1.0 - band;
                        edge = MathUtil.Clamp(edge, 0.0, 1.0);

                        double ang = Math.Atan2(y, x);
                        double n1 = HashNoise.FBm(seed + 11,
                            x * 0.08 + Math.Cos(ang) * 0.35,
                            y * 0.08 + Math.Sin(ang) * 0.35 + time * 0.35,
                            octaves: 3);

                        double n2 = HashNoise.ValueNoise(seed + 33,
                            x * 0.22 + time * 0.18,
                            y * 0.22 - time * 0.14);

                        double b = 0.10 + 0.55 * edge;
                        b += 0.20 * (n1 - 0.5);
                        b += 0.10 * (n2 - 0.5);
                        b *= MathUtil.Clamp(edge * 1.15, 0.0, 1.0);

                        if (b < 0.10)
                        {
                            idx++;
                            continue;
                        }

                        double bb = MathUtil.Clamp(b, 0.0, 1.0);
                        cache.Colors[idx] = ColorUtils.Shade(sunBase, bb);
                        cache.Mask[idx] = 1;
                        idx++;
                        continue;
                    }

                    double dCore = Math.Sqrt(d2i);
                    double u = x / (double)sunR;
                    double v = y / (double)sunR;

                    double tEdge = dCore / sunR;
                    double limb = 1.0 - 0.65 * Math.Pow(tEdge, 1.25);

                    double gran = HashNoise.FBm(seed + 101,
                        u * 9.5 + time * 0.10,
                        v * 9.5 - time * 0.08,
                        octaves: 4);

                    double angCore = Math.Atan2(v, u);
                    double swirl = HashNoise.ValueNoise(seed + 202,
                        Math.Cos(angCore) * 2.5 + u * 1.2 + time * 0.06,
                        Math.Sin(angCore) * 2.5 + v * 1.2 - time * 0.05);

                    double spot = 0.0;
                    for (int s = 0; s < 3; s++)
                    {
                        double sx = (HashNoise.Hash01(seed + 900 + s * 31, 12, 34) * 1.2 - 0.6);
                        double sy = (HashNoise.Hash01(seed + 900 + s * 31, 56, 78) * 1.2 - 0.6);
                        double sr = 0.10 + 0.10 * HashNoise.Hash01(seed + 900 + s * 31, 90, 12);

                        double dx = u - sx;
                        double dy = v - sy;
                        double dd = Math.Sqrt(dx * dx + dy * dy);

                        double w = MathUtil.Clamp(1.0 - (dd / Math.Max(0.0001, sr)), 0.0, 1.0);
                        spot = Math.Max(spot, w);
                    }

                    double bCore = 0.82 * limb;
                    bCore += 0.22 * (gran - 0.5);
                    bCore += 0.10 * (swirl - 0.5);
                    bCore -= 0.55 * spot;

                    double bbCore = MathUtil.Clamp(bCore, 0.0, 1.0);
                    cache.Colors[idx] = ColorUtils.Shade(sunBase, bbCore);
                    cache.Mask[idx] = 1;
                    idx++;
                }
            }

            return cache;
        }

        private static Color BlendRgb(Color a, Color b, double t)
        {
            t = MathUtil.Clamp(t, 0.0, 1.0);
            if (t <= 0.0) return a;
            if (t >= 1.0) return b;

            (byte ar, byte ag, byte ab) = ColorUtils.ToRgbBytes(a);
            (byte br, byte bg, byte bb) = ColorUtils.ToRgbBytes(b);

            int rr = (int)Math.Round(ar + (br - ar) * t);
            int gg = (int)Math.Round(ag + (bg - ag) * t);
            int bb2 = (int)Math.Round(ab + (bb - ab) * t);

            return Color.FromRgb((byte)ClampToByte(rr), (byte)ClampToByte(gg), (byte)ClampToByte(bb2));
        }

        private static int ClampToByte(int v) => (v < 0) ? 0 : (v > 255 ? 255 : v);

        private void EnsureStarfield()
        {
            if (_sys == null)
                return;

            int seed = _sys.Seed;
            if (_starSeedBuilt == seed && _starPts.Length == StarCount)
                return;

            _starSeedBuilt = seed;
            _starPts = new StarPt[StarCount];

            for (int i = 0; i < StarCount; i++)
            {
                double rx = MathUtil.Hash01(seed + i * 17 + 1);
                double ry = MathUtil.Hash01(seed + i * 17 + 2);
                double rd = MathUtil.Hash01(seed + i * 17 + 3);

                _starPts[i] = new StarPt
                {
                    WX = (rx * 2.0 - 1.0) * StarSpan,
                    WY = (ry * 2.0 - 1.0) * StarSpan,
                    Depth = rd
                };
            }
        }

        private void EnsureDebris()
        {
            if (_sys == null)
                return;

            int seed = _sys.Seed ^ 0x1F3D5B79;
            if (_debrisSeedBuilt == seed && _debris.Length == DebrisCount)
                return;

            _debrisSeedBuilt = seed;
            _debris = new DebrisPt[DebrisCount];

            for (int i = 0; i < DebrisCount; i++)
            {
                double rx = HashNoise.Hash01(seed + i * 31, 1, 2);
                double ry = HashNoise.Hash01(seed + i * 31, 3, 4);
                double rvx = HashNoise.Hash01(seed + i * 31, 5, 6);
                double rvy = HashNoise.Hash01(seed + i * 31, 7, 8);
                double rd = HashNoise.Hash01(seed + i * 31, 9, 10);

                double wx = (rx * 2.0 - 1.0) * DebrisSpan;
                double wy = (ry * 2.0 - 1.0) * DebrisSpan;
                double vx = (rvx * 2.0 - 1.0) * DebrisMaxV;
                double vy = (rvy * 2.0 - 1.0) * DebrisMaxV;

                _debris[i] = new DebrisPt
                {
                    WX = wx,
                    WY = wy,
                    VX = vx,
                    VY = vy,
                    Depth = rd
                };
            }
        }

        private void UpdateDebris(double dt)
        {
            if (!_showDebris)
                return;

            EnsureDebris();

            for (int i = 0; i < _debris.Length; i++)
            {
                DebrisPt d = _debris[i];
                d.WX += d.VX * dt;
                d.WY += d.VY * dt;
                d.WX = MathUtil.Wrap(d.WX, -DebrisSpan, DebrisSpan);
                d.WY = MathUtil.Wrap(d.WY, -DebrisSpan, DebrisSpan);
                _debris[i] = d;
            }
        }

        private void SetActiveSystem(int index, bool resetSimTime)
        {
            if (_galaxy.Systems.Count == 0)
                return;

            _systemIndex = MathUtil.ClampInt(index, 0, _galaxy.Systems.Count - 1);
            _sys = _galaxy.Get(_systemIndex);
            if (resetSimTime)
                _simTime = 0.0;

            _camWX = 0.0;
            _camWY = 0.0;
            _targetCamWX = 0.0;
            _targetCamWY = 0.0;

            _starSeedBuilt = int.MinValue;
            _debrisSeedBuilt = int.MinValue;
            _planetTextureCache.Clear();
            _sunCache = null;

            if (_sys != null)
            {
                StarSystemLogic.UpdateCelestials(_sys, _simTime, _useKepler);
                RebuildSelection();
                _events.Add(_simTime, $"Entered system: {_sys.Name}");
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
            _worldToScreen = MathUtil.Clamp(zoom, 2.0, 200.0);
            _targetWorldToScreen = _worldToScreen;
        }

        private void SnapCamera(double wx, double wy, double zoom, double orbitScale)
        {
            _camWX = _targetCamWX = wx;
            _camWY = _targetCamWY = wy;
            _worldToScreen = _targetWorldToScreen = zoom;
            _orbitYScale = _targetOrbitYScale = orbitScale;
        }

        private void RebuildSelection()
        {
            _selection.Clear();

            _selection.Add(new SelectionItem
            {
                Kind = SelectionKind.Sun,
                Index = -1,
                SubIndex = -1,
                Label = "Sun",
                WX = 0.0,
                WY = 0.0,
                WZ = 0.0
            });

            if (_sys == null)
                return;

            for (int i = 0; i < _sys.Planets.Count; i++)
            {
                Planet planet = _sys.Planets[i];
                _selection.Add(new SelectionItem
                {
                    Kind = SelectionKind.Planet,
                    Index = i,
                    SubIndex = -1,
                    Label = planet.Name,
                    WX = planet.WX,
                    WY = planet.WY,
                    WZ = planet.WZ
                });

                for (int m = 0; m < planet.Moons.Count; m++)
                {
                    Moon moon = planet.Moons[m];
                    _selection.Add(new SelectionItem
                    {
                        Kind = SelectionKind.Moon,
                        Index = i,
                        SubIndex = m,
                        Label = moon.Name,
                        WX = moon.WX,
                        WY = moon.WY,
                        WZ = moon.WZ
                    });
                }
            }

            for (int i = 0; i < _sys.Stations.Count; i++)
            {
                Station station = _sys.Stations[i];
                _selection.Add(new SelectionItem
                {
                    Kind = SelectionKind.Station,
                    Index = i,
                    SubIndex = -1,
                    Label = station.Name,
                    WX = station.WX,
                    WY = station.WY,
                    WZ = station.WZ
                });
            }

            for (int i = 0; i < _sys.Ships.Count; i++)
            {
                Ship ship = _sys.Ships[i];
                _selection.Add(new SelectionItem
                {
                    Kind = SelectionKind.Ship,
                    Index = i,
                    SubIndex = -1,
                    Label = ship.Name,
                    WX = ship.WX,
                    WY = ship.WY,
                    WZ = ship.WZ
                });
            }

            for (int i = 0; i < _sys.Asteroids.Count; i++)
            {
                Asteroid asteroid = _sys.Asteroids[i];
                _selection.Add(new SelectionItem
                {
                    Kind = SelectionKind.Asteroid,
                    Index = i,
                    SubIndex = -1,
                    Label = $"Asteroid-{i + 1}",
                    WX = asteroid.WX,
                    WY = asteroid.WY,
                    WZ = asteroid.WZ
                });
            }

            for (int i = 0; i < _sys.Nebulae.Count; i++)
            {
                NebulaCloud cloud = _sys.Nebulae[i];
                _selection.Add(new SelectionItem
                {
                    Kind = SelectionKind.Nebula,
                    Index = i,
                    SubIndex = -1,
                    Label = $"Nebula-{i + 1}",
                    WX = cloud.WX,
                    WY = cloud.WY,
                    WZ = cloud.WZ
                });
            }

            _selIndex = MathUtil.ClampInt(_selIndex, 0, Math.Max(0, _selection.Count - 1));
        }

        private SelectionItem? GetSelection()
        {
            if (_selection.Count == 0)
                RebuildSelection();
            if (_selection.Count == 0)
                return null;

            _selIndex = MathUtil.ClampInt(_selIndex, 0, _selection.Count - 1);
            SelectionItem item = _selection[_selIndex];

            if (_sys == null)
                return item;

            switch (item.Kind)
            {
                case SelectionKind.Sun:
                    item.WX = 0;
                    item.WY = 0;
                    item.WZ = 0;
                    break;
                case SelectionKind.Planet:
                    if ((uint)item.Index < (uint)_sys.Planets.Count)
                    {
                        Planet p = _sys.Planets[item.Index];
                        item.WX = p.WX;
                        item.WY = p.WY;
                        item.WZ = p.WZ;
                    }
                    break;
                case SelectionKind.Moon:
                    if ((uint)item.Index < (uint)_sys.Planets.Count)
                    {
                        Planet planet = _sys.Planets[item.Index];
                        if ((uint)item.SubIndex < (uint)planet.Moons.Count)
                        {
                            Moon moon = planet.Moons[item.SubIndex];
                            item.WX = moon.WX;
                            item.WY = moon.WY;
                            item.WZ = moon.WZ;
                        }
                    }
                    break;
                case SelectionKind.Station:
                    if ((uint)item.Index < (uint)_sys.Stations.Count)
                    {
                        Station s = _sys.Stations[item.Index];
                        item.WX = s.WX;
                        item.WY = s.WY;
                        item.WZ = s.WZ;
                    }
                    break;
                case SelectionKind.Ship:
                    if ((uint)item.Index < (uint)_sys.Ships.Count)
                    {
                        Ship sh = _sys.Ships[item.Index];
                        item.WX = sh.WX;
                        item.WY = sh.WY;
                        item.WZ = sh.WZ;
                    }
                    break;
                case SelectionKind.Asteroid:
                    if ((uint)item.Index < (uint)_sys.Asteroids.Count)
                    {
                        Asteroid a = _sys.Asteroids[item.Index];
                        item.WX = a.WX;
                        item.WY = a.WY;
                        item.WZ = a.WZ;
                    }
                    break;
                case SelectionKind.Nebula:
                    if ((uint)item.Index < (uint)_sys.Nebulae.Count)
                    {
                        NebulaCloud n = _sys.Nebulae[item.Index];
                        item.WX = n.WX;
                        item.WY = n.WY;
                        item.WZ = n.WZ;
                    }
                    break;
            }

            _selection[_selIndex] = item;
            return item;
        }

        private void CycleSelection(int dir)
        {
            if (_selection.Count == 0)
                RebuildSelection();

            if (_selection.Count == 0)
                return;

            _selIndex = MathUtil.WrapIndex(_selIndex + dir, _selection.Count);
        }

        private bool IsBlackHoleSystem(StarSystem sys, int systemIndex)
        {
            if (systemIndex == _forcedBlackHoleSystemIndex)
                return true;

            if (sys.Kind == SystemKind.BlackHole)
                return true;

            double r01 = HashNoise.Hash01(sys.Seed ^ 0x6A09E667, 123, 456);
            return r01 < BlackHoleChanceFallback;
        }

        private int WorldToScreenX(double wx)
            => _centerX + (int)Math.Round((wx - _camWX) * _worldToScreen);

        private int WorldToScreenY(double wy)
            => _centerY + (int)Math.Round((wy - _camWY) * _worldToScreen * _orbitYScale);

        private static void ComputeOrbitPoint(int systemSeed, Planet planet, double meanAnomaly, out double wx, out double wy)
        {
            OrbitMath.Kepler2D(planet.A, MathUtil.Clamp(planet.E, 0.0, 0.95), 0.0, meanAnomaly, out double x, out double y);

            int pSeed = systemSeed ^ planet.Name.GetHashCode();
            double plane = (HashNoise.Hash01(pSeed, 101, 202) * 2.0 - 1.0) * 1.05;

            double c = Math.Cos(plane);
            double s = Math.Sin(plane);
            double rx = x * c - y * s;
            double ry = x * s + y * c;

            double inc = 0.55 + 0.45 * HashNoise.Hash01(pSeed, 303, 404);
            ry *= inc;

            double co = Math.Cos(planet.Omega);
            double so = Math.Sin(planet.Omega);
            wx = rx * co - ry * so;
            wy = rx * so + ry * co;
        }

        private static double Lerp(double a, double b, double t)
            => a + (b - a) * t;

        private void DrawText(PixelRenderer renderer, int x, int y, string text, Color color)
        {
            _font.DrawText(renderer, x, y, text, color);
        }

        private sealed class PixelFont
        {
            private readonly Dictionary<char, byte[]> _glyphs = new Dictionary<char, byte[]>();
            public int Width { get; } = 5;
            public int Height { get; } = 7;

            public PixelFont()
            {
                Add('A', "01110", "10001", "10001", "11111", "10001", "10001", "10001");
                Add('B', "11110", "10001", "10001", "11110", "10001", "10001", "11110");
                Add('C', "01110", "10001", "10000", "10000", "10000", "10001", "01110");
                Add('D', "11110", "10001", "10001", "10001", "10001", "10001", "11110");
                Add('E', "11111", "10000", "10000", "11110", "10000", "10000", "11111");
                Add('F', "11111", "10000", "10000", "11110", "10000", "10000", "10000");
                Add('G', "01110", "10001", "10000", "10111", "10001", "10001", "01110");
                Add('H', "10001", "10001", "10001", "11111", "10001", "10001", "10001");
                Add('I', "11111", "00100", "00100", "00100", "00100", "00100", "11111");
                Add('J', "11111", "00010", "00010", "00010", "00010", "10010", "01100");
                Add('K', "10001", "10010", "10100", "11000", "10100", "10010", "10001");
                Add('L', "10000", "10000", "10000", "10000", "10000", "10000", "11111");
                Add('M', "10001", "11011", "10101", "10101", "10001", "10001", "10001");
                Add('N', "10001", "11001", "10101", "10011", "10001", "10001", "10001");
                Add('O', "01110", "10001", "10001", "10001", "10001", "10001", "01110");
                Add('P', "11110", "10001", "10001", "11110", "10000", "10000", "10000");
                Add('Q', "01110", "10001", "10001", "10001", "10101", "10010", "01101");
                Add('R', "11110", "10001", "10001", "11110", "10100", "10010", "10001");
                Add('S', "01111", "10000", "10000", "01110", "00001", "00001", "11110");
                Add('T', "11111", "00100", "00100", "00100", "00100", "00100", "00100");
                Add('U', "10001", "10001", "10001", "10001", "10001", "10001", "01110");
                Add('V', "10001", "10001", "10001", "10001", "10001", "01010", "00100");
                Add('W', "10001", "10001", "10001", "10101", "10101", "10101", "01010");
                Add('X', "10001", "10001", "01010", "00100", "01010", "10001", "10001");
                Add('Y', "10001", "10001", "01010", "00100", "00100", "00100", "00100");
                Add('Z', "11111", "00001", "00010", "00100", "01000", "10000", "11111");

                Add('0', "01110", "10001", "10011", "10101", "11001", "10001", "01110");
                Add('1', "00100", "01100", "00100", "00100", "00100", "00100", "01110");
                Add('2', "01110", "10001", "00001", "00010", "00100", "01000", "11111");
                Add('3', "11110", "00001", "00001", "01110", "00001", "00001", "11110");
                Add('4', "00010", "00110", "01010", "10010", "11111", "00010", "00010");
                Add('5', "11111", "10000", "10000", "11110", "00001", "00001", "11110");
                Add('6', "01110", "10000", "10000", "11110", "10001", "10001", "01110");
                Add('7', "11111", "00001", "00010", "00100", "01000", "01000", "01000");
                Add('8', "01110", "10001", "10001", "01110", "10001", "10001", "01110");
                Add('9', "01110", "10001", "10001", "01111", "00001", "00001", "01110");

                Add('-', "00000", "00000", "00000", "11111", "00000", "00000", "00000");
                Add('.', "00000", "00000", "00000", "00000", "00000", "00110", "00110");
                Add(':', "00000", "00110", "00110", "00000", "00110", "00110", "00000");
                Add('/', "00001", "00010", "00100", "01000", "10000", "00000", "00000");
                Add(',', "00000", "00000", "00000", "00000", "00000", "00110", "00100");
                Add('[', "01110", "01000", "01000", "01000", "01000", "01000", "01110");
                Add(']', "01110", "00010", "00010", "00010", "00010", "00010", "01110");
                Add('(', "00010", "00100", "01000", "01000", "01000", "00100", "00010");
                Add(')', "01000", "00100", "00010", "00010", "00010", "00100", "01000");
                Add('+', "00000", "00100", "00100", "11111", "00100", "00100", "00000");
                Add('=', "00000", "11111", "00000", "11111", "00000", "00000", "00000");
                Add(' ', "00000", "00000", "00000", "00000", "00000", "00000", "00000");
            }

            public void DrawText(PixelRenderer renderer, int x, int y, string text, Color color)
            {
                if (renderer == null || string.IsNullOrEmpty(text))
                    return;

                int cursorX = x;
                int cursorY = y;

                for (int i = 0; i < text.Length; i++)
                {
                    char c = char.ToUpperInvariant(text[i]);
                    if (c == '\n')
                    {
                        cursorX = x;
                        cursorY += Height + 2;
                        continue;
                    }

                    if (!_glyphs.TryGetValue(c, out byte[]? rows))
                        rows = _glyphs[' '];

                    for (int row = 0; row < Height; row++)
                    {
                        byte bits = rows[row];
                        for (int col = 0; col < Width; col++)
                        {
                            if ((bits & (1 << (Width - 1 - col))) != 0)
                                renderer.SetPixel(cursorX + col, cursorY + row, color);
                        }
                    }

                    cursorX += Width + 1;
                }
            }

            private void Add(char c, params string[] rows)
            {
                byte[] data = new byte[Height];
                for (int i = 0; i < Height; i++)
                {
                    string row = rows[i];
                    byte mask = 0;
                    for (int j = 0; j < Width; j++)
                    {
                        if (row[j] == '1')
                            mask |= (byte)(1 << (Width - 1 - j));
                    }
                    data[i] = mask;
                }
                _glyphs[c] = data;
            }
        }
    }
}
