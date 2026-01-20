using AsciiEngine;
using SolarSystemApp.Gameplay;
using SolarSystemApp.Persistence;
using SolarSystemApp.Rendering;
using SolarSystemApp.Util;
using SolarSystemApp.World;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SolarSystemApp.Interiors;
using Color = global::AsciiEngine.Color;
using Colors = global::AsciiEngine.Colors;


namespace SolarSystemApp
{
    public sealed class SolarSystemScene : IAsciiApp
    {
        // =========================
        // View / Camera (keep old fields to avoid breaking SaveManager usage)
        // =========================
        private double _worldToScreen = 10.0; // zoom (chars per world unit)
        private double _orbitYScale = 0.55;
        private const string RampShort = " .:-=+*#";
        private const string RampBlocks = " ░▒▓█";
        private const string RampLongRaw = "$@B%8&WM#*oahkbdpqwmZO0QLCJUYXzcvunxrjft/|()1{}[]?-_+~<>i!lI;:,^";
        private static readonly string RampLong = new string(RampLongRaw.ToCharArray().Reverse().ToArray());

        private string _ramp = RampLong;

        private double _camWX = 0.0;
        private double _camWY = 0.0;

        // Camera smoothing (system view)
        private bool _smoothCam = true;              // toggle if you want later
        private double _zoomRef = 10.0;              // used for RenderRadiusFromZoom if you want consistency
        private const double ZoomStep = 1.10;        // U/J zoom multiplier

        private readonly Camera2D _cam = new Camera2D();

        // Follow selection
        private bool _follow = false;
        private double _followLerp = 0.18;

        // Fast pan toggle
        private bool _fastPan = false;

        // =========================
        // Simulation
        // =========================
        private double _simTime = 0.0;
        private double _timeScale = 1.0;
        private bool _paused = false;

        // PERF/STABILITY: fixed-step simulation accumulator (prevents render slowdown from slowing sim)
        private double _simAccum = 0.0;
        private const double SimStep = 1.0 / 60.0; // 60 Hz simulation

        private bool _useKepler = true;

        // toggles
        private bool _showOrbits = true;
        private bool _showBelts = true;
        private bool _showRings = true;
        private bool _showLabels = true;
        private bool _showStarfield = true;

        // =========================
        // Post-FX buffers (real bloom needs readback)
        // =========================
        private FxPost _fx = new FxPost();
        private bool _fxEnabled = true;
        private bool _fxLensFlare = true;
        private double _fxFlareStrength = 1.00;     // 0..1



        // =========================
        // Ambient motion / "dead system" animation
        // =========================
        private bool _showDebris = true;

        // Debris (asteroids) drifting around system space (deterministic per system)
        private struct DebrisPt { public double wx, wy, vx, vy, depth; }
        private DebrisPt[] _debris = Array.Empty<DebrisPt>();
        private int _debrisSeedBuilt = int.MinValue;
        private const int DebrisCount = 180;
        private const double DebrisSpan = 32.0;     // world-space half-extent of the debris field
        private const double DebrisMaxV = 0.22;     // max speed in world units/sec

        // "Black hole system" detection / guarantee
        private const double BlackHoleChanceFallback = 0.12; // only used if world model doesn't expose BH
        private int _forcedBlackHoleSystemIndex = -1;

        // Reflection cache: optional system properties if you have them in your world model
        private static readonly Dictionary<Type, PropertyInfo?> _isBlackHolePropCache = new Dictionary<Type, PropertyInfo?>(16);
        private static readonly Dictionary<Type, PropertyInfo?> _sunKindPropCache = new Dictionary<Type, PropertyInfo?>(16);


        // Galaxy view
        private bool _galaxyView = false;

        // Interior view (Layout 1: full-screen interior)
        private bool _interiorView = false;
        private readonly Dictionary<string, InteriorSession> _interiorByShip = new Dictionary<string, InteriorSession>();
        private InteriorSession _activeInterior = null;

        // Spectre caches for interior
        private string _spectreInteriorHeaderCache = "";

        private double _galCamX = 0.0;
        private double _galCamY = 0.0;
        private double _galZoom = 1.6;

        // Textures
        private int _texMode = 1;

        // =========================
        // World
        // =========================
        private readonly Galaxy _galaxy = new Galaxy();
        private int _systemIndex = 0;
        private StarSystem _sys;
        private int _starfieldFrame = 0;

        private Belt _belt;

        // Selection
        private readonly List<SelectionItem> _selection = new List<SelectionItem>();
        private int _selIndex = 0;

        // “Armed ship” for order issuing
        private int _armedShipIndex = -1;

        // UI flash
        private double _msgTimer = 0.0;
        private string _msg = "";

        // =========================
        // Spectre UI (rendered into strings, then blitted into ConsoleRenderer)
        // =========================
        private string _spectreHeaderCache = "";
        private string _spectreInfoCache = "";
        private double _spectreUiTimer = 0.0;      // throttle UI rebuild if you want
        private const double SpectreUiEvery = 0.10; // rebuild 10 times/sec

        // NEW: event log + job scaffolding
        private readonly EventLog _events = new EventLog(12);
        private readonly ShipJobs _jobs = new ShipJobs();
        private long _credits = 0; // lightweight progression (doesn't touch save)

        private enum GlyphMode
        {
            RampLong,
            RampBlocks,
            RampShort,
            SolidColorOnly
        }

        private GlyphMode _glyphMode = GlyphMode.RampLong;

        // =========================
        // PERF: shared constants / caches
        // =========================
        private const double Deg2Rad = Math.PI / 180.0;

        // Orbit LUT (keeps visuals identical; used mainly for circular model)
        private const int OrbitSteps = 360;
        private static readonly (double ca, double sa)[] _unitCircle = BuildUnitCircle(OrbitSteps);
        private static (double ca, double sa)[] BuildUnitCircle(int steps)
        {
            var arr = new (double ca, double sa)[steps];
            for (int i = 0; i < steps; i++)
            {
                double a = i * Deg2Rad;
                arr[i] = (Math.Cos(a), Math.Sin(a));
            }
            return arr;
        }

        // Starfield cache (precomputed star positions per system seed)
        private struct StarPt { public double wx, wy, depth; }
        private StarPt[] _starPts = Array.Empty<StarPt>();
        private int _starSeedBuilt = int.MinValue;
        private const int StarCount = 220;
        private const double StarSpan = 120.0;

        // Occluder array cache (avoid ToArray per frame; keep exact length to preserve behavior)
        private PlanetDrawer.Occluder[] _occluders = Array.Empty<PlanetDrawer.Occluder>();
        private int _occluderSeed = int.MinValue; // optional: can help if you want to rebuild only when needed later

        // Reflection caches (avoid repeated GetProperty costs)
        private static readonly Dictionary<Type, PropertyInfo?> _moonsPropCache = new Dictionary<Type, PropertyInfo?>(16);
        private static readonly Dictionary<Type, PropertyInfo?> _wxPropCache = new Dictionary<Type, PropertyInfo?>(16);
        private static readonly Dictionary<Type, PropertyInfo?> _wyPropCache = new Dictionary<Type, PropertyInfo?>(16);
        private static readonly Dictionary<Type, PropertyInfo?> _wzPropCache = new Dictionary<Type, PropertyInfo?>(16);
        private static readonly Dictionary<Type, PropertyInfo?> _namePropCache = new Dictionary<Type, PropertyInfo?>(16);

        // =========================
        // RGB helpers (bridge while world types still use AnsiColor)
        // =========================
        private static Color ToRgb(AnsiColor c) => ColorUtils.ToRgbColor((Color)c);

        private static Color Brighten(Color c)
        {
            // Simple brighten: blend toward white to mimic "Bright*" ANSI.
            return BlendRgb(c, Colors.BrightWhite, 0.35);
        }

        private static Color ShadeFromBrightness(Color baseColor, double b01)
        {
            // Keep your old "4-ish levels" look (but in RGB).
            b01 = MathUtil.Clamp(b01, 0.0, 1.0);
            if (b01 < 0.22) return Colors.BrightBlack;
            if (b01 < 0.52) return baseColor;
            if (b01 < 0.82) return Brighten(baseColor);
            return Colors.BrightWhite;
        }

        private static Color BlendRgb(Color a, Color b, double t)
        {
            t = MathUtil.Clamp(t, 0.0, 1.0);
            a = ColorUtils.ToRgbColor(a);
            b = ColorUtils.ToRgbColor(b);

            int av = a.Value, bv = b.Value;
            int ar = (av >> 16) & 255, ag = (av >> 8) & 255, ab = av & 255;
            int br = (bv >> 16) & 255, bg = (bv >> 8) & 255, bb = bv & 255;

            int rr = ClampToByte((int)Math.Round(ar + (br - ar) * t));
            int gg = ClampToByte((int)Math.Round(ag + (bg - ag) * t));
            int bb2 = ClampToByte((int)Math.Round(ab + (bb - ab) * t));

            return Color.FromRgb((byte)rr, (byte)gg, (byte)bb2);
        }

        private static int ClampToByte(int v) => (v < 0) ? 0 : (v > 255 ? 255 : v);

        private static PropertyInfo? GetCachedProp(Dictionary<Type, PropertyInfo?> cache, Type t, string name)
        {
            if (cache.TryGetValue(t, out var p)) return p;
            p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            cache[t] = p;
            return p;
        }

        private static double ReadDoubleCached(object obj, Dictionary<Type, PropertyInfo?> cache, string propName)
        {
            if (obj == null) return 0;
            var t = obj.GetType();
            var p = GetCachedProp(cache, t, propName);
            if (p == null) return 0;
            object v = p.GetValue(obj);
            return (v == null) ? 0 : Convert.ToDouble(v);
        }

        private static string ReadStringCached(object obj, Dictionary<Type, PropertyInfo?> cache, string propName)
        {
            if (obj == null) return "";
            var t = obj.GetType();
            var p = GetCachedProp(cache, t, propName);
            if (p == null) return "";
            object v = p.GetValue(obj);
            return v?.ToString() ?? "";
        }

        // =========================
        // Init
        // =========================
        public void Init(EngineContext ctx)
        {
            if (string.IsNullOrEmpty(_ramp)) _ramp = RampLong;
            PlanetDrawer.Ramp = _ramp;

            _galaxy.Build(seed: 12345, count: 100);
            // Guarantee at least ONE black-hole-looking system (so you actually see it during testing)
            if (_galaxy.Systems.Count > 0)
            {
                // Deterministic pick based on galaxy seed
                double r01 = HashNoise.Hash01(12345 ^ 0xB00F, 901, 777);
                _forcedBlackHoleSystemIndex = MathUtil.ClampInt((int)Math.Floor(r01 * _galaxy.Systems.Count), 0, _galaxy.Systems.Count - 1);
            }

            SetActiveSystem(0, resetSimTime: true);
            _sys = _galaxy.Get(_systemIndex);

            FitSystemToScreen(ctx);
            // Initialize camera smoothing targets so it doesn't "snap" on first update
            _cam.SmoothEnabled = _smoothCam;
            _cam.PanResponsiveness = 14.0;
            _cam.ZoomResponsiveness = 18.0;

            _cam.TargetCamWX = _camWX;
            _cam.TargetCamWY = _camWY;
            _cam.TargetWorldToScreen = _worldToScreen;
            _cam.TargetOrbitYScale = _orbitYScale;
            RebuildSelection();

            // Keep existing sync behavior (sets _cam from fields) — but now it would overwrite!
            // So we do NOT call SyncCameraToFields() here anymore.

            _events.Add(_simTime, $"Entered system: {_sys.Name}");
        }

        // =========================
        // Update
        // =========================
        public void Update(EngineContext ctx)
        {
            double dt = ctx.DeltaTime;

            UpdateMessageTimer(dt);
            HandleSaveLoad(ctx);
            HandleFxToggles(ctx);
            HandleInteriorToggle(ctx);
            HandleGalaxyViewToggle(ctx);
            HandleFastPanToggle(ctx);
            HandleGlyphToggle(ctx);
            HandleTextureMode(ctx);
            HandleDisplayToggles(ctx);
            HandleOrbitModelToggle(ctx);
            HandlePauseToggle(ctx);
            HandleTimeScale(ctx);
            HandleZoom(ctx);
            HandleOrbitSquash(ctx);

            bool prev = ctx.Input.WasPressed(ConsoleKey.Z);
            bool next = ctx.Input.WasPressed(ConsoleKey.X);
            if (HandleSelectionCycle(ctx, prev, next))
            {
                return;
            }

            HandleFollowToggle(ctx);
            HandleCameraCenter(ctx);
            HandleSpawnShip(ctx);
            HandleSpawnStation(ctx);
            HandleArmShip(ctx);
            HandleTravelOrder(ctx);
            HandleOrbitOrder(ctx);
            HandleJobAssignment(ctx);

            if (!_interiorView)
                HandleSystemPanning(ctx);

            AdvanceSimulation(dt);
            UpdateCameraFollow(ctx);
        }

        private void UpdateMessageTimer(double dt)
        {
            if (_msgTimer <= 0) return;
            _msgTimer -= dt;
            if (_msgTimer <= 0) _msg = "";
        }

        private void HandleSaveLoad(EngineContext ctx)
        {
            if (ctx.Input.WasPressed(ConsoleKey.F5))
            {
                bool ok = SaveManager.Save(_galaxy, _systemIndex, _simTime, _timeScale, _paused,
                                           _galaxyView, _camWX, _camWY, _worldToScreen, _orbitYScale,
                                           _galCamX, _galCamY, _galZoom, _useKepler);
                Flash(ok ? "Saved to savegame.json" : "Save FAILED");
                _events.Add(_simTime, ok ? "Saved game" : "Save FAILED");
            }

            if (ctx.Input.WasPressed(ConsoleKey.F9))
            {
                if (SaveManager.Load(out var sg))
                {
                    SaveManager.ApplyToGalaxy(sg, _galaxy);

                    _systemIndex = MathUtil.ClampInt(sg.CurrentSystemIndex, 0, _galaxy.Systems.Count - 1);
                    _sys = _galaxy.Get(_systemIndex);

                    _simTime = sg.SimTime;
                    _timeScale = sg.TimeScale;
                    _paused = sg.Paused;

                    _galaxyView = sg.GalaxyView;
                    _camWX = sg.CamWX; _camWY = sg.CamWY;
                    _worldToScreen = sg.Zoom;
                    _orbitYScale = sg.OrbitYScale;

                    // after setting _camWX/_camWY/_worldToScreen/_orbitYScale from save:
                    _cam.CamWX = _camWX;
                    _cam.CamWY = _camWY;
                    _cam.WorldToScreen = _worldToScreen;
                    _cam.OrbitYScale = _orbitYScale;

                    _cam.TargetCamWX = _camWX;
                    _cam.TargetCamWY = _camWY;
                    _cam.TargetWorldToScreen = _worldToScreen;
                    _cam.TargetOrbitYScale = _orbitYScale;

                    _galCamX = sg.GalCamX; _galCamY = sg.GalCamY;
                    _galZoom = sg.GalZoom;

                    _useKepler = sg.UseKepler;

                    BuildBeltForSystem();
                    RebuildSelection();

                    Flash("Loaded savegame.json");
                    _events.Add(_simTime, "Loaded game");
                }
                else
                {
                    Flash("Load FAILED (no savegame.json?)");
                    _events.Add(_simTime, "Load FAILED");
                }
            }
        }

        private void HandleFxToggles(EngineContext ctx)
        {
            if (ctx.Input.WasPressed(ConsoleKey.F2)) _fxEnabled = !_fxEnabled;
            if (ctx.Input.WasPressed(ConsoleKey.F3)) _fxLensFlare = !_fxLensFlare;
        }

        private void HandleInteriorToggle(EngineContext ctx)
        {
            // Toggle Interior View (F6) - later we’ll gate this behind “docked”
            if (!_galaxyView && ctx.Input.WasPressed(ConsoleKey.F6))
            {
                if (_interiorView) ExitInterior();
                else EnterInterior();
            }

            // If we’re in interior view, we consume movement keys here and skip world controls.
            // We still keep simulation + camera smoothing running via the existing code below.
            if (_interiorView)
                UpdateInteriorControls(ctx);
        }

        private void HandleGalaxyViewToggle(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.G)) return;

            _galaxyView = !_galaxyView;
            if (_galaxyView)
            {
                _galCamX = _sys.GalaxyX;
                _galCamY = _sys.GalaxyY;
            }
        }

        private void HandleFastPanToggle(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.P)) return;

            _fastPan = !_fastPan;
            Flash(_fastPan ? "Fast pan ON" : "Fast pan OFF");
        }

        private void HandleGlyphToggle(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.R)) return;

            _glyphMode = _glyphMode switch
            {
                GlyphMode.RampLong => GlyphMode.RampBlocks,
                GlyphMode.RampBlocks => GlyphMode.RampShort,
                GlyphMode.RampShort => GlyphMode.SolidColorOnly,
                _ => GlyphMode.RampLong,
            };

            ApplyGlyphMode();
        }

        private void ApplyGlyphMode()
        {
            switch (_glyphMode)
            {
                case GlyphMode.RampLong:
                    PlanetDrawer.Ramp = RampLong;
                    PlanetDrawer.PlanetColorOnlyShading = false;
                    PlanetDrawer.ForceRampShading = false;
                    break;

                case GlyphMode.RampBlocks:
                    PlanetDrawer.Ramp = RampBlocks;
                    PlanetDrawer.PlanetColorOnlyShading = false;
                    PlanetDrawer.ForceRampShading = true;   // important for block ramp look
                    break;

                case GlyphMode.RampShort:
                    PlanetDrawer.Ramp = RampShort;
                    PlanetDrawer.PlanetColorOnlyShading = false;
                    PlanetDrawer.ForceRampShading = false;
                    break;

                case GlyphMode.SolidColorOnly:
                    // Ramp can stay whatever; it won't be used while PlanetColorOnlyShading == true
                    PlanetDrawer.PlanetColorOnlyShading = true;   // the "forced solid" line will trigger
                    PlanetDrawer.ForceRampShading = false;
                    break;
            }
        }

        private void HandleTextureMode(EngineContext ctx)
        {
            if (ctx.Input.WasPressed(ConsoleKey.D1)) _texMode = 1;
            if (ctx.Input.WasPressed(ConsoleKey.D2)) _texMode = 2;
            if (ctx.Input.WasPressed(ConsoleKey.D3)) _texMode = 3;
        }

        private void HandleDisplayToggles(EngineContext ctx)
        {
            if (ctx.Input.WasPressed(ConsoleKey.O)) _showOrbits = !_showOrbits;
            if (ctx.Input.WasPressed(ConsoleKey.B)) _showBelts = !_showBelts;
            if (ctx.Input.WasPressed(ConsoleKey.H)) _showRings = !_showRings;
            if (ctx.Input.WasPressed(ConsoleKey.L)) _showLabels = !_showLabels;
            if (ctx.Input.WasPressed(ConsoleKey.F1)) _showStarfield = !_showStarfield;
        }

        private void HandleOrbitModelToggle(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.T)) return;

            _useKepler = !_useKepler;
            Flash(_useKepler ? "Orbit model: KEPLER" : "Orbit model: CIRCULAR");
            _events.Add(_simTime, _useKepler ? "Orbit model: KEPLER" : "Orbit model: CIRCULAR");
        }

        private void HandlePauseToggle(EngineContext ctx)
        {
            if (ctx.Input.WasPressed(ConsoleKey.Spacebar) && !_galaxyView && !_interiorView)
                _paused = !_paused;
        }

        private void HandleTimeScale(EngineContext ctx)
        {
            if (ctx.Input.WasPressed(ConsoleKey.OemPlus) || ctx.Input.WasPressed(ConsoleKey.Add))
                _timeScale *= 1.25;
            if (ctx.Input.WasPressed(ConsoleKey.OemMinus) || ctx.Input.WasPressed(ConsoleKey.Subtract))
                _timeScale /= 1.25;
            _timeScale = MathUtil.Clamp(_timeScale, 0.05, 10.0);
        }

        private void HandleZoom(EngineContext ctx)
        {
            if (ctx.Input.WasPressed(ConsoleKey.U))
            {
                if (_galaxyView)
                {
                    _galZoom *= ZoomStep;
                }
                else
                {
                    int anchorSX = ctx.Width / 2;
                    int anchorSY = ctx.Height / 2;

                    double newZoom = _cam.TargetWorldToScreen * ZoomStep;
                    newZoom = MathUtil.Clamp(newZoom, 1.0, 80.0);

                    _cam.ZoomAtScreenPointSmooth(newZoom, anchorSX, anchorSY);
                }
            }

            if (ctx.Input.WasPressed(ConsoleKey.J))
            {
                if (_galaxyView)
                {
                    _galZoom /= ZoomStep;
                }
                else
                {
                    int anchorSX = ctx.Width / 2;
                    int anchorSY = ctx.Height / 2;

                    double newZoom = _cam.TargetWorldToScreen / ZoomStep;
                    newZoom = MathUtil.Clamp(newZoom, 1.0, 80.0);

                    _cam.ZoomAtScreenPointSmooth(newZoom, anchorSX, anchorSY);
                }
            }

            // Galaxy zoom clamp stays immediate
            _galZoom = MathUtil.Clamp(_galZoom, 0.5, 20.0);

            // System zoom is now driven by _cam targets (smooth)
            _cam.ClampZoom(1.0, 80.0);
        }

        private void HandleOrbitSquash(EngineContext ctx)
        {
            if (_galaxyView) return;

            if (ctx.Input.WasPressed(ConsoleKey.I)) _orbitYScale += 0.03;
            if (ctx.Input.WasPressed(ConsoleKey.K)) _orbitYScale -= 0.03;
            _orbitYScale = MathUtil.Clamp(_orbitYScale, 0.20, 1.20);
            _cam.TargetOrbitYScale = _orbitYScale;
        }

        private bool HandleSelectionCycle(EngineContext ctx, bool prev, bool next)
        {
            if (_galaxyView)
            {
                if (prev) _systemIndex = MathUtil.WrapIndex(_systemIndex - 1, _galaxy.Systems.Count);
                if (next) _systemIndex = MathUtil.WrapIndex(_systemIndex + 1, _galaxy.Systems.Count);

                // Jump
                if (ctx.Input.WasPressed(ConsoleKey.Enter))
                {
                    SetActiveSystem(_systemIndex, resetSimTime: false);
                    _galaxyView = false;
                    FitSystemToScreen(ctx);
                    RebuildSelection();
                    Flash($"Jumped to {_sys.Name}");
                    _events.Add(_simTime, $"Jumped to {_sys.Name}");
                }

                HandleGalaxyPanning(ctx);
                return true;
            }

            if (prev || next)
            {
                if (_selection.Count == 0) RebuildSelection();
                if (_selection.Count > 0)
                {
                    _selIndex = MathUtil.WrapIndex(_selIndex + (next ? 1 : -1), _selection.Count);
                    _follow = false;
                }
            }

            return false;
        }

        private void HandleFollowToggle(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.F)) return;

            _follow = !_follow;
            Flash(_follow ? "Follow ON" : "Follow OFF");
        }

        private void HandleCameraCenter(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.C)) return;

            _follow = false;
            _cam.TargetCamWX = 0.0;
            _cam.TargetCamWY = 0.0;
            _cam.CamWX = 0.0;
            _cam.CamWY = 0.0; // optional: instant snap instead of easing
        }

        private void HandleSpawnShip(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.Y)) return;

            SpawnShipNearSun();
            RebuildSelection();
            Flash("Spawned ship");
            _events.Add(_simTime, "Spawned ship near sun");
        }

        private void HandleSpawnStation(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.Q)) return;

            var sel = GetSelection();
            if (sel != null && sel.Kind == EntityKind.Planet)
            {
                SpawnStationAroundPlanet(sel.Index);
                RebuildSelection();
                Flash("Spawned station");
                _events.Add(_simTime, $"Spawned station near {sel.Label}");
            }
            else
            {
                Flash("Select a PLANET to spawn station (Q)");
            }
        }

        private void HandleArmShip(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.E)) return;

            var sel = GetSelection();
            if (sel != null && sel.Kind == EntityKind.Ship)
            {
                _armedShipIndex = sel.Index;
                Flash($"Armed ship: {_sys.Ships[_armedShipIndex].Name}");
                _events.Add(_simTime, $"Armed ship: {_sys.Ships[_armedShipIndex].Name}");
            }
            else
            {
                _armedShipIndex = -1;
                Flash("No ship selected (E)");
            }
        }

        private void HandleTravelOrder(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.V)) return;

            if (_armedShipIndex < 0 || _armedShipIndex >= _sys.Ships.Count)
            {
                Flash("No armed ship (press E on a ship)");
            }
            else
            {
                var target = GetSelection();
                if (target == null)
                {
                    Flash("No target selected");
                }
                else
                {
                    IssueTravelOrder(_sys.Ships[_armedShipIndex], target);
                    Flash($"Order: {_sys.Ships[_armedShipIndex].Name} -> {target.Label}");
                    _events.Add(_simTime, $"Order travel: {_sys.Ships[_armedShipIndex].Name} -> {target.Label}");
                }
            }
        }

        private void HandleOrbitOrder(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.N)) return;

            if (!TryGetArmedShip(out var ship))
            {
                Flash("No armed ship (press E on a ship)");
            }
            else
            {
                var target = GetSelection();
                if (target == null) { Flash("No target selected"); }
                else
                {
                    IssueOrbitOrder(ship, target);
                    Flash($"Orbit: {ship.Name} around {target.Label}");
                    _events.Add(_simTime, $"Order orbit: {ship.Name} around {target.Label}");
                }
            }
        }

        private void HandleJobAssignment(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.M)) return;

            if (!TryGetArmedShip(out var ship))
            {
                Flash("No armed ship (press E on a ship)");
            }
            else
            {
                var st = _jobs.GetOrCreate(ship.Name);
                st.Job = ShipJobType.Mine;
                st.Accumulator = 0;
                st.TargetLabel = "Local space";
                Flash($"Job: {ship.Name} -> MINE");
                _events.Add(_simTime, $"Job set: {ship.Name} -> MINE");
            }
        }

        private void AdvanceSimulation(double dt)
        {
            // --- PERF/STABILITY: clamp dt to avoid "spiral of death" on hitches ---
            if (dt > 0.25) dt = 0.25;

            // Advance sim (fixed step so render slowdown doesn't slow physics)
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
                    UpdateJobs(SimStep);

                    _simAccum -= SimStep;
                }
            }
            else
            {
                StarSystemLogic.UpdateCelestials(_sys, _simTime, _useKepler);
                RefreshShipOrbitCenters(_sys);
            }
        }

        private void UpdateCameraFollow(EngineContext ctx)
        {
            // Follow camera
            if (_follow)
            {
                var sel = GetSelection();
                if (sel != null)
                {
                    _cam.TargetCamWX = MathUtil.Lerp(_cam.TargetCamWX, sel.WX, _followLerp);
                    _cam.TargetCamWY = MathUtil.Lerp(_cam.TargetCamWY, sel.WY, _followLerp);
                }
            }

            // Apply smoothing once per frame, then sync camera -> legacy fields (SaveManager compatibility)
            _cam.SmoothEnabled = _smoothCam;
            _cam.Update(ctx.DeltaTime);

            _camWX = _cam.CamWX;
            _camWY = _cam.CamWY;
            _worldToScreen = _cam.WorldToScreen;
            _orbitYScale = _cam.OrbitYScale;
        }

        private void UpdateJobs(double dt)
        {
            if (_sys?.Ships == null) return;

            for (int i = 0; i < _sys.Ships.Count; i++)
            {
                var sh = _sys.Ships[i];
                if (sh == null) continue;

                var st = _jobs.GetOrCreate(sh.Name);
                if (st.Job == ShipJobType.None) continue;

                if (st.Job == ShipJobType.Mine)
                {
                    st.Accumulator += dt;
                    if (st.Accumulator >= 3.5)
                    {
                        st.Accumulator -= 3.5;
                        st.Completed += 1;
                        _credits += 25;

                        if ((st.Completed % 4) == 0)
                            _events.Add(_simTime, $"{sh.Name} mined ore (+100 cr total so far)");
                    }
                }
            }
        }

        private void SyncCameraToFields()
        {
            _cam.WorldToScreen = _worldToScreen;
            _cam.OrbitYScale = _orbitYScale;
            _cam.CamWX = _camWX;
            _cam.CamWY = _camWY;
        }

        private void DrawInteriorMapColored(ConsoleRenderer r, int x0, int y0, int w, int h)
        {
            if (_activeInterior == null) return;
            var map = _activeInterior.Map;

            int drawW = Math.Min(w, map.W);
            int drawH = Math.Min(h, map.H);

            int ox = x0 + (w - drawW) / 2;
            int oy = y0 + (h - drawH) / 2;

            _activeInterior.UpdateCamera(drawW, drawH);
            int camX = _activeInterior.CameraX;
            int camY = _activeInterior.CameraY;

            // clear panel interior
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    r.Set(x0 + x, y0 + y, ' ', Colors.BrightBlack, Colors.Black, z: RenderZ.UI_BORDER);

            for (int y = 0; y < drawH; y++)
            {
                for (int x = 0; x < drawW; x++)
                {
                    int mapX = camX + x;
                    int mapY = camY + y;
                    bool isPlayer = (mapX == _activeInterior.PlayerX && mapY == _activeInterior.PlayerY);

                    // Interior map still yields ANSI palette (for now) — convert at the boundary.
                    map.GetStyledTile(mapX, mapY, isPlayer, out char glyph, out AnsiColor fgA, out AnsiColor bgA);
                    Color fg = ToRgb(fgA);
                    Color bg = ToRgb(bgA);

                    r.Set(ox + x, oy + y, glyph, fg, bg, z: RenderZ.UI_BORDER);
                }
            }
        }

        private void DrawInteriorView(EngineContext ctx)
        {
            var r = ctx.Renderer;

            // Layout 1: interior dominates screen
            r.Clear(' ', Colors.BrightWhite, Colors.Black);
            r.DrawRect(0, 0, ctx.Width, ctx.Height, '#', Colors.BrightBlack, Colors.Black, z: RenderZ.UI_BORDER);

            _spectreUiTimer += ctx.DeltaTime;
            if (_spectreUiTimer >= SpectreUiEvery)
            {
                _spectreUiTimer = 0;

                string shipName = (_activeInterior != null) ? _activeInterior.ShipName : "Ship";

                var header = new Panel(new Text($"INTERIOR VIEW  |  ship={shipName}  |  F6 exit  |  WASD/arrows move"))
                    .Border(BoxBorder.Rounded)
                    .Header("CONTROLS", Justify.Left);

                _spectreInteriorHeaderCache = RenderSpectreToString(header, ctx.Width - 4);
            }

            int headerX = 2;
            int headerY = 1;
            int headerW = ctx.Width - 4;
            int headerH = 6;

            BlitText(r, headerX, headerY, headerW, headerH, _spectreInteriorHeaderCache,
                     Colors.BrightCyan, Colors.Black, RenderZ.UI_BORDER);

            int panelX = 2;
            int panelY = 7;
            int panelW = ctx.Width - 4;
            int panelH = ctx.Height - panelY - 2;

            r.DrawRect(panelX, panelY, panelW, panelH, '#', Colors.BrightBlack, Colors.Black, z: RenderZ.UI_BORDER);

            int innerX = panelX + 2;
            int innerY = panelY + 2;
            int innerW = panelW - 4;
            int innerH = panelH - 4;

            DrawInteriorMapColored(r, innerX, innerY, innerW, innerH);

            if (!string.IsNullOrEmpty(_msg))
                r.DrawString(2, ctx.Height - 2, _msg, Colors.BrightGreen, Colors.Black, z: RenderZ.UI_BORDER);
        }

        // =========================
        // Draw
        // =========================
        public void Draw(EngineContext ctx)
        {
            var r = ctx.Renderer;
            r.Clear(' ', Colors.BrightWhite, Colors.Black);

            if (_galaxyView)
            {
                DrawGalaxyView(ctx);
                return;
            }

            if (_interiorView)
            {
                DrawInteriorView(ctx);
                return;
            }

            if (_showStarfield)
            {
                _starfieldFrame++;

                int every = 1;
                if (_worldToScreen > 18.0) every = 2;
                if (_worldToScreen > 35.0) every = 3;
                if (_worldToScreen > 55.0) every = 4;

                if ((_starfieldFrame % every) == 0)
                    DrawStarfield(r, ctx);
            }

            int centerX = ctx.Width / 2;
            int centerY = ctx.Height / 2;

            _cam.CenterX = centerX;
            _cam.CenterY = centerY;

            int sunX = _cam.WorldToScreenX(0.0);
            int sunY = _cam.WorldToScreenY(0.0);

            // NEW: nebula background pass (behind starfield/orbits)
            DrawNebula(r, ctx, centerX, centerY);

            // Orbits only if planets exist (safe + avoids wasted loops)
            if (_showOrbits && _sys.Planets != null && _sys.Planets.Count > 0)
                DrawOrbits(r, centerX, centerY);

            // Belts + sun only if this system actually has a star
            if (_sys.HasStar)
            {
                if (_showBelts && _belt != null)
                    _belt.Draw(r, sunX, sunY, _simTime, _worldToScreen, _orbitYScale);

                bool isBH = IsBlackHoleSystem(_sys, _systemIndex);

                // If there is no belt, add some motion so the system doesn't feel dead.
                if (_showDebris && _belt == null)
                    DrawDebrisField(r, ctx);

                // Draw sun OR black hole
                if (isBH)
                    DrawBlackHole(r, sunX, sunY);
                else
                    DrawSunScaled(r, sunX, sunY);

            }

            var occluderList = OccluderBuilder.BuildForSystem(
                _sys,
                worldToScreenX: wx => _cam.WorldToScreenX(wx),
                worldToScreenY: wy => _cam.WorldToScreenY(wy),
                worldRadiusToChars: rw => (int)Math.Round(rw * _worldToScreen)
            );

            int occCount = (occluderList == null) ? 0 : occluderList.Count;
            if (occCount == 0)
            {
                _occluders = Array.Empty<PlanetDrawer.Occluder>();
            }
            else
            {
                if (_occluders.Length != occCount)
                    _occluders = new PlanetDrawer.Occluder[occCount];

                for (int i = 0; i < occCount; i++)
                    _occluders[i] = occluderList[i];
            }

            DrawStations(r, centerX, centerY);
            DrawPlanets(r, centerX, centerY, sunX, sunY, _occluders);
            DrawMoons(r, centerX, centerY, sunX, sunY, _occluders);
            DrawAsteroids(r, ctx, centerX, centerY);
            DrawShips(r, ctx, centerX, centerY);

            DrawUI(r, ctx);
            if (_fxEnabled)
            {
                int cx = ctx.Width / 2;
                int cy = ctx.Height / 2;

                _fx.Apply(ctx, r,
                    enableFlare: _fxLensFlare,
                    flareStrength: _fxFlareStrength,
                    sunX: sunX, sunY: sunY, centerX: cx, centerY: cy,
                    simTime: _simTime);
            }



        }

        // =========================
        // Orbit tracking fix (core)
        // =========================
        private void RefreshShipOrbitCenters(StarSystem sys)
        {
            if (sys == null || sys.Ships == null) return;

            for (int i = 0; i < sys.Ships.Count; i++)
            {
                var sh = sys.Ships[i];
                if (sh == null) continue;

                if (sh.Mode != ShipMode.Orbit) continue;
                if (sh.OrbitTargetKind == OrbitTargetKind.None) continue;

                if (TryGetOrbitTargetWorld(sys, sh.OrbitTargetKind, sh.OrbitTargetIndex, sh.OrbitTargetSubIndex,
                                          out double cx, out double cy))
                {
                    sh.TargetX = cx;
                    sh.TargetY = cy;
                }
            }
        }

        private bool TryGetOrbitTargetWorld(StarSystem sys, OrbitTargetKind kind, int index, int subIndex,
                                           out double wx, out double wy)
        {
            wx = 0; wy = 0;

            switch (kind)
            {
                case OrbitTargetKind.Sun:
                    wx = 0; wy = 0;
                    return true;

                case OrbitTargetKind.Planet:
                    if (sys.Planets == null || (uint)index >= (uint)sys.Planets.Count) return false;
                    wx = sys.Planets[index].WX;
                    wy = sys.Planets[index].WY;
                    return true;

                case OrbitTargetKind.Station:
                    if (sys.Stations == null || (uint)index >= (uint)sys.Stations.Count) return false;
                    wx = sys.Stations[index].WX;
                    wy = sys.Stations[index].WY;
                    return true;

                case OrbitTargetKind.Ship:
                    if (sys.Ships == null || (uint)index >= (uint)sys.Ships.Count) return false;
                    wx = sys.Ships[index].WX;
                    wy = sys.Ships[index].WY;
                    return true;

                case OrbitTargetKind.Moon:
                    return TryGetMoonWorld(sys, index, subIndex, out wx, out wy);

                default:
                    return false;
            }
        }

        private bool TryGetMoonWorld(StarSystem sys, int planetIndex, int moonIndex, out double wx, out double wy)
        {
            wx = 0; wy = 0;
            if (sys.Planets == null || (uint)planetIndex >= (uint)sys.Planets.Count) return false;

            object planet = sys.Planets[planetIndex];
            if (planet == null) return false;

            var pt = planet.GetType();
            PropertyInfo moonsProp = GetCachedProp(_moonsPropCache, pt, "Moons");
            if (moonsProp == null) return false;

            object moonsObj = moonsProp.GetValue(planet);
            if (moonsObj is not IList moons) return false;

            if ((uint)moonIndex >= (uint)moons.Count) return false;

            object moon = moons[moonIndex];
            if (moon == null) return false;

            wx = ReadDoubleCached(moon, _wxPropCache, "WX");
            wy = ReadDoubleCached(moon, _wyPropCache, "WY");
            return true;
        }

        // (kept for compatibility with your existing usage)
        private static double ReadDouble(object obj, string propName)
        {
            if (obj == null) return 0;
            PropertyInfo p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return 0;
            object v = p.GetValue(obj);
            return (v == null) ? 0 : Convert.ToDouble(v);
        }

        // =========================
        // Panning
        // =========================
        private void HandleSystemPanning(EngineContext ctx)
        {
            int ax = 0, ay = 0;

            if (ctx.Input.WasPressed(ConsoleKey.A) || ctx.Input.WasPressed(ConsoleKey.LeftArrow)) ax -= 1;
            if (ctx.Input.WasPressed(ConsoleKey.D) || ctx.Input.WasPressed(ConsoleKey.RightArrow)) ax += 1;
            if (ctx.Input.WasPressed(ConsoleKey.W) || ctx.Input.WasPressed(ConsoleKey.UpArrow)) ay -= 1;
            if (ctx.Input.WasPressed(ConsoleKey.S) || ctx.Input.WasPressed(ConsoleKey.DownArrow)) ay += 1;

            if (ax == 0 && ay == 0) return;

            _follow = false;

            double panChars = _fastPan ? 10.0 : 4.0;

            _cam.PanCharsSmooth((int)(ax * panChars), (int)(ay * panChars));
        }

        private void HandleGalaxyPanning(EngineContext ctx)
        {
            int ax = 0, ay = 0;

            if (ctx.Input.WasPressed(ConsoleKey.A) || ctx.Input.WasPressed(ConsoleKey.LeftArrow)) ax -= 1;
            if (ctx.Input.WasPressed(ConsoleKey.D) || ctx.Input.WasPressed(ConsoleKey.RightArrow)) ax += 1;
            if (ctx.Input.WasPressed(ConsoleKey.W) || ctx.Input.WasPressed(ConsoleKey.UpArrow)) ay -= 1;
            if (ctx.Input.WasPressed(ConsoleKey.S) || ctx.Input.WasPressed(ConsoleKey.DownArrow)) ay += 1;

            if (ax == 0 && ay == 0) return;

            double panChars = _fastPan ? 10.0 : 4.0;

            _galCamX += ax * (panChars / _galZoom);
            _galCamY += ay * (panChars / _galZoom);
        }

        // =========================
        // World<->Screen (kept for compatibility)
        // =========================
        private int WorldToScreenX(double wx, int centerX)
            => centerX + (int)Math.Round((wx - _camWX) * _worldToScreen);

        private int WorldToScreenY(double wy, int centerY)
            => centerY + (int)Math.Round((wy - _camWY) * _worldToScreen * _orbitYScale);

        // =========================
        // Galaxy view
        // =========================
        private void DrawGalaxyView(EngineContext ctx)
        {
            var r = ctx.Renderer;

            int w = ctx.Width;
            int h = ctx.Height;
            int cx = w / 2;
            int cy = h / 2;

            r.DrawRect(0, 0, w, h, '#', Colors.BrightBlack, Colors.Black, z: RenderZ.UI_BORDER);

            r.DrawString(2, 1,
                "GALAXY VIEW | G back | WASD/arrows pan | P fastPan | U/J zoom | Z/X select | Enter jump | F5 save | F9 load",
                Colors.BrightCyan, Colors.Black, z: RenderZ.UI_BORDER);

            HashSet<int> neighbors = new HashSet<int>();
            for (int i = 0; i < _galaxy.Links.Count; i++)
            {
                var l = _galaxy.Links[i];
                if (l.A == _systemIndex) neighbors.Add(l.B);
                if (l.B == _systemIndex) neighbors.Add(l.A);
            }

            for (int i = 0; i < _galaxy.Links.Count; i++)
            {
                var l = _galaxy.Links[i];

                var a = _galaxy.Systems[l.A];
                var b = _galaxy.Systems[l.B];

                int ax = cx + (int)Math.Round((a.GalaxyX - _galCamX) * _galZoom);
                int ay = cy + (int)Math.Round((a.GalaxyY - _galCamY) * _galZoom);

                int bx = cx + (int)Math.Round((b.GalaxyX - _galCamX) * _galZoom);
                int by = cy + (int)Math.Round((b.GalaxyY - _galCamY) * _galZoom);

                bool touchesSelected = (l.A == _systemIndex) || (l.B == _systemIndex);
                var fg = touchesSelected ? Colors.BrightCyan : Colors.BrightBlack;

                r.DrawLine(ax, ay, bx, by, '.', fg, Colors.Black, z: 25);
            }

            for (int i = 0; i < _galaxy.Systems.Count; i++)
            {
                StarSystem s = _galaxy.Systems[i];

                int sx = cx + (int)Math.Round((s.GalaxyX - _galCamX) * _galZoom);
                int sy = cy + (int)Math.Round((s.GalaxyY - _galCamY) * _galZoom);

                if (sx < 1 || sx >= w - 1 || sy < 2 || sy >= h - 2) continue;

                bool selected = (i == _systemIndex);
                bool neighbor = neighbors.Contains(i);
                bool isBH = IsBlackHoleSystem(s, i);

                char ch = selected ? '@' : (isBH ? '0' : (neighbor ? 'o' : '*'));
                var fg = selected ? Colors.BrightYellow : (isBH ? Colors.BrightCyan : (neighbor ? Colors.BrightCyan : Colors.BrightWhite));


                r.Set(sx, sy, ch, fg, Colors.Black, z: 10);

                if (w > 60)
                    r.DrawString(sx + 2, sy, s.Name,
                        selected ? Colors.BrightYellow : (neighbor ? Colors.BrightCyan : Colors.BrightBlack),
                        Colors.Black, z: 5);
            }

            r.DrawString(2, 3, $"Selected: {_galaxy.Get(_systemIndex).Name}  (Enter to jump)  Links={_galaxy.Links.Count}",
                Colors.BrightGreen, Colors.Black, z: RenderZ.UI_BORDER);

            if (!string.IsNullOrEmpty(_msg))
                r.DrawString(2, 5, _msg, Colors.BrightYellow, Colors.Black, z: RenderZ.UI_BORDER);
        }

        private IRenderable BuildSpectreHeader(EngineContext ctx)
        {
            var desc = string.IsNullOrWhiteSpace(_sys.Descriptor) ? _sys.Kind.ToString() : _sys.Descriptor;
            var title = new Markup($"[bold]System[/]  [grey]({_sys.Name})[/]  [teal]{desc}[/]  time={_simTime:0.0}  x{_timeScale:0.00}  paused={(_paused ? "YES" : "NO")}  zoom={_worldToScreen:0.0}");

            var panel = new Panel(title)
                .Border(BoxBorder.Rounded)
                .Header("CONTROLS", Justify.Left);

            return panel;
        }

        private IRenderable BuildSpectreInfo(EngineContext ctx, SelectionItem sel)
        {
            string armed = (_armedShipIndex >= 0 && _armedShipIndex < _sys.Ships.Count)
                ? _sys.Ships[_armedShipIndex].Name
                : "none";

            var grid = new Grid();
            grid.AddColumn(new GridColumn().NoWrap());
            grid.AddColumn();

            grid.AddRow("Selected", $"{sel.Kind} | {sel.Label}");
            grid.AddRow("World", $"({sel.WX:0.00},{sel.WY:0.00})");
            grid.AddRow("Follow", _follow ? "ON" : "OFF");
            grid.AddRow("FastPan", _fastPan ? "ON" : "OFF");
            grid.AddRow("ArmedShip", armed);
            grid.AddRow("Credits", $"{_credits}");

            if (sel.Kind == EntityKind.Ship && sel.Index >= 0 && sel.Index < _sys.Ships.Count)
            {
                var sh = _sys.Ships[sel.Index];
                grid.AddRow("Vel", $"({sh.VX:0.00},{sh.VY:0.00})");
                grid.AddRow("Mode", $"{sh.Mode}");

                var st = _jobs.GetOrCreate(sh.Name);
                grid.AddRow("Job", $"{st.Job}");
                if (st.Job != ShipJobType.None)
                    grid.AddRow("Done", $"{st.Completed}");
            }
            else if (sel.Kind == EntityKind.Planet && sel.Index >= 0 && sel.Index < _sys.Planets.Count)
            {
                var p = _sys.Planets[sel.Index];
                grid.AddRow("Radius", $"{p.RadiusWorld:0.00}");
                grid.AddRow("A / E", $"{p.A:0.00} / {p.E:0.00}");
                grid.AddRow("Rings", p.HasRings ? "YES" : "NO");
                grid.AddRow("Texture", $"{p.Texture}");
            }

            var panel = new Panel(grid)
                .Border(BoxBorder.Rounded)
                .Header("INFO", Justify.Left);

            return panel;
        }

        // =========================
        // Spectre -> string -> blit into our renderer
        // =========================
        private static string RenderSpectreToString(IRenderable renderable, int width)
        {
            width = Math.Max(10, width);

            var sw = new StringWriter();

            var settings = new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.TrueColor,
                Out = new AnsiConsoleOutput(sw),
            };

            var console = AnsiConsole.Create(settings);
            console.Write(renderable);

            return sw.ToString();
        }

        private static void BlitText(
            ConsoleRenderer r,
            int x0, int y0,
            int w, int h,
            string text,
            Color fg,
            Color bg,
            double z)
        {
            if (w <= 0 || h <= 0) return;
            if (string.IsNullOrEmpty(text)) return;

            int x = 0, y = 0;
            int cx = x0, cy = y0;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];

                if (ch == '\r') continue;

                if (ch == '\n')
                {
                    y++;
                    if (y >= h) break;
                    x = 0;
                    cx = x0;
                    cy = y0 + y;
                    continue;
                }

                if (x < w)
                    r.Set(cx, cy, ch, fg, bg, z: z);

                x++;
                cx++;

                if (x >= w)
                {
                    y++;
                    if (y >= h) break;
                    x = 0;
                    cx = x0;
                    cy = y0 + y;
                }
            }
        }

        // =========================
        // PostFX
        // =========================

        private sealed class FxPost
        {
            private int _w, _h;
            private Color[] _srcFg = Array.Empty<Color>();
            private Color[] _srcBg = Array.Empty<Color>();

            private float[] _lum = Array.Empty<float>();      // brightness 0..1
            private float[] _blurA = Array.Empty<float>();    // blur ping
            private float[] _blurB = Array.Empty<float>();    // blur pong

            public void Ensure(int w, int h)
            {
                if (w == _w && h == _h && _srcFg.Length == w * h) return;

                _w = w; _h = h;
                int n = w * h;

                _srcFg = new Color[n];
                _srcBg = new Color[n];
                _lum = new float[n];
                _blurA = new float[n];
                _blurB = new float[n];
            }

            public void Apply(
                EngineContext ctx,
                ConsoleRenderer r,
                bool enableFlare,
                double flareStrength,
                int sunX, int sunY,
                int centerX, int centerY,
                double simTime)
            {
                Ensure(ctx.Width, ctx.Height);

                // 1) Snapshot colors (readback)
                Capture(r);

                // 2) Compute luminance (from FG only; BG is almost always black in your scene)
                BuildLuminance();

                // 4) Lens flare (stamped on top, uses sun & center)
                if (enableFlare)
                    Flare(r, sunX, sunY, centerX, centerY, flareStrength, simTime);
            }

            private void Capture(ConsoleRenderer r)
            {
                int idx = 0;
                for (int y = 0; y < _h; y++)
                {
                    for (int x = 0; x < _w; x++)
                    {
                        if (r.TryGetColors(x, y, out var fg, out var bg))
                        {
                            _srcFg[idx] = fg;
                            _srcBg[idx] = bg;
                        }
                        else
                        {
                            _srcFg[idx] = Colors.BrightWhite;
                            _srcBg[idx] = Colors.Black;
                        }
                        idx++;
                    }
                }
            }

            private void BuildLuminance()
            {
                for (int i = 0; i < _srcFg.Length; i++)
                {
                    int v = _srcFg[i].Value;
                    int r = (v >> 16) & 255;
                    int g = (v >> 8) & 255;
                    int b = v & 255;

                    // perceptual-ish luma
                    float lum = (0.2126f * r + 0.7152f * g + 0.0722f * b) / 255f;
                    _lum[i] = lum;
                }
            }
            private void Flare(ConsoleRenderer r, int sunX, int sunY, int cx, int cy, double strength, double simTime)
            {
                strength = Clamp01(strength);
                if (strength <= 0.001) return;
                if ((uint)sunX >= (uint)_w || (uint)sunY >= (uint)_h) return;

                int dx = cx - sunX;
                int dy = cy - sunY;

                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < 1.0) dist = 1.0;

                double nx = dx / dist;
                double ny = dy / dist;

                // streak
                int len = ClampInt((int)Math.Round(40 + 90 * strength), 30, 180);
                for (int i = 0; i <= len; i += 2)
                {
                    double t = i / (double)len;
                    int x = (int)Math.Round(sunX + nx * i);
                    int y = (int)Math.Round(sunY + ny * i);
                    if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) continue;

                    double n = HashNoise.ValueNoise(unchecked((int)0x13579BDF), x * 0.07 + simTime * 0.25, y * 0.07);
                    double b = (1.0 - t) * (0.45 + 0.55 * strength) * (0.80 + 0.40 * (n - 0.5));
                    b = Clamp01(b);
                    if (b < 0.12) continue;

                    char ch = (b > 0.60) ? '-' : '.';
                    Color fg = (b > 0.65) ? Colors.BrightWhite : Colors.BrightBlack;

                    r.Set(x, y, ch, fg, Colors.Black, z: RenderZ.SUN_CORONA + 0.0008);
                }

                // a few ghosts
                for (int g = 1; g <= 4; g++)
                {
                    double gt = g / 5.0;
                    double along = dist * (0.25 + 0.22 * g);
                    int gx = (int)Math.Round(cx + (-nx) * along);
                    int gy = (int)Math.Round(cy + (-ny) * along);

                    int gr = ClampInt((int)Math.Round(2 + g * 2 * strength), 2, 10);
                    int gr2 = gr * gr;

                    for (int yy = -gr; yy <= gr; yy++)
                    {
                        for (int xx = -gr; xx <= gr; xx++)
                        {
                            if (xx * xx + yy * yy > gr2) continue;

                            int x = gx + xx;
                            int y = gy + yy;
                            if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) continue;

                            double b = (0.16 + 0.22 * strength) * (1.0 - gt);
                            if (b < 0.12) continue;

                            char ch = (b > 0.30) ? '.' : '·';
                            r.Set(x, y, ch, Colors.BrightBlack, Colors.Black, z: RenderZ.SUN_CORONA + 0.0007);
                        }
                    }
                }
            }

            private void BlurHorizontal(float[] src, float[] dst, int rad)
            {
                int w = _w;
                int h = _h;
                float inv = 1f / (rad * 2 + 1);

                for (int y = 0; y < h; y++)
                {
                    int row = y * w;

                    // prefix-ish rolling sum
                    float sum = 0;
                    for (int x = -rad; x <= rad; x++)
                    {
                        int xx = ClampInt(x, 0, w - 1);
                        sum += src[row + xx];
                    }

                    for (int x = 0; x < w; x++)
                    {
                        dst[row + x] = sum * inv;

                        int xOut = x - rad;
                        int xIn = x + rad + 1;

                        if (xOut >= 0) sum -= src[row + xOut];
                        else sum -= src[row + 0];

                        if (xIn < w) sum += src[row + xIn];
                        else sum += src[row + (w - 1)];
                    }
                }
            }

            private void BlurVertical(float[] src, float[] dst, int rad)
            {
                int w = _w;
                int h = _h;
                float inv = 1f / (rad * 2 + 1);

                for (int x = 0; x < w; x++)
                {
                    float sum = 0;

                    for (int y = -rad; y <= rad; y++)
                    {
                        int yy = ClampInt(y, 0, h - 1);
                        sum += src[yy * w + x];
                    }

                    for (int y = 0; y < h; y++)
                    {
                        dst[y * w + x] = sum * inv;

                        int yOut = y - rad;
                        int yIn = y + rad + 1;

                        if (yOut >= 0) sum -= src[yOut * w + x];
                        else sum -= src[0 * w + x];

                        if (yIn < h) sum += src[yIn * w + x];
                        else sum += src[(h - 1) * w + x];
                    }
                }
            }

            private static int ClampInt(int v, int lo, int hi) => (v < lo) ? lo : (v > hi ? hi : v);
            private static double Clamp01(double v) => (v < 0) ? 0 : (v > 1 ? 1 : v);
        }


        // =========================
        // Drawing helpers
        // =========================
        private void DrawOrbits(ConsoleRenderer r, int centerX, int centerY)
        {
            int sysSeed = _sys.Seed;

            foreach (var p in _sys.Planets)
            {
                int pSeed = sysSeed ^ (p.Name?.GetHashCode() ?? 0);

                double plane = (HashNoise.Hash01(pSeed, 101, 202) * 2.0 - 1.0) * 1.05;
                double c = Math.Cos(plane);
                double s = Math.Sin(plane);

                double inc = 0.55 + 0.45 * HashNoise.Hash01(pSeed, 303, 404);

                double co = Math.Cos(p.Omega);
                double so = Math.Sin(p.Omega);

                int stride = 2;
                if (_worldToScreen > 18.0) stride = 4;
                if (_worldToScreen > 35.0) stride = 6;
                if (_worldToScreen > 55.0) stride = 8;

                for (int i = 0; i < OrbitSteps; i += stride)
                {
                    double x, y;

                    if (_useKepler)
                    {
                        double M = i * Deg2Rad;
                        OrbitMath.Kepler2D(p.A, MathUtil.Clamp(p.E, 0.0, 0.95), 0.0, M, out x, out y);
                    }
                    else
                    {
                        var (ca, sa) = _unitCircle[i];
                        x = ca * p.A;
                        y = sa * p.A;
                    }

                    double rx = x * c - y * s;
                    double ry = x * s + y * c;

                    ry *= inc;

                    double wx = rx * co - ry * so;
                    double wy = rx * so + ry * co;

                    int sx = WorldToScreenX(wx, centerX);
                    int sy = WorldToScreenY(wy, centerY);

                    r.Set(sx, sy, '.', Colors.BrightBlack, Colors.Black, z: RenderZ.ORBITS);
                }
            }
        }

        private static bool ReadBoolCached(object obj, Dictionary<Type, PropertyInfo?> cache, string propName)
        {
            if (obj == null) return false;
            var t = obj.GetType();
            var p = GetCachedProp(cache, t, propName);
            if (p == null) return false;
            object v = p.GetValue(obj);
            if (v == null) return false;
            if (v is bool b) return b;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }

        private static string ReadAnyToStringCached(object obj, Dictionary<Type, PropertyInfo?> cache, string propName)
        {
            if (obj == null) return "";
            var t = obj.GetType();
            var p = GetCachedProp(cache, t, propName);
            if (p == null) return "";
            object v = p.GetValue(obj);
            return v?.ToString() ?? "";
        }

        private bool IsBlackHoleSystem(StarSystem s, int systemIndex)
        {
            // Guaranteed test system (so you SEE one)
            if (systemIndex == _forcedBlackHoleSystemIndex) return true;
            if (s == null) return false;

            // If your world model has something like: bool IsBlackHole
            if (ReadBoolCached(s, _isBlackHolePropCache, "IsBlackHole"))
                return true;

            // Or an enum/string like SunKind/StarKind/Type = "BlackHole"
            string kind = ReadAnyToStringCached(s, _sunKindPropCache, "SunKind");
            if (string.IsNullOrEmpty(kind)) kind = ReadAnyToStringCached(s, _sunKindPropCache, "StarKind");
            if (string.IsNullOrEmpty(kind)) kind = ReadAnyToStringCached(s, _sunKindPropCache, "Type");

            if (!string.IsNullOrEmpty(kind) && kind.IndexOf("black", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // Fallback: deterministic chance per system seed (only used if your model doesn't expose BH)
            double r01 = HashNoise.Hash01(s.Seed ^ 0x6A09E667, 123, 456);
            return r01 < BlackHoleChanceFallback;
        }

        private void EnsureDebris()
        {
            int seed = _sys.Seed ^ 0x1F3D5B79;
            if (_debrisSeedBuilt == seed && _debris.Length == DebrisCount) return;

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

                // Small drift velocities
                double vx = (rvx * 2.0 - 1.0) * DebrisMaxV;
                double vy = (rvy * 2.0 - 1.0) * DebrisMaxV;

                _debris[i] = new DebrisPt { wx = wx, wy = wy, vx = vx, vy = vy, depth = rd };
            }
        }

        private void UpdateDebris(double dt)
        {
            if (!_showDebris) return;
            EnsureDebris();

            // Deterministic drift + wrap
            for (int i = 0; i < _debris.Length; i++)
            {
                var d = _debris[i];
                d.wx += d.vx * dt;
                d.wy += d.vy * dt;

                d.wx = MathUtil.Wrap(d.wx, -DebrisSpan, DebrisSpan);
                d.wy = MathUtil.Wrap(d.wy, -DebrisSpan, DebrisSpan);

                _debris[i] = d;
            }
        }

        private void DrawDebrisField(ConsoleRenderer r, EngineContext ctx)
        {
            EnsureDebris();

            int cx = ctx.Width / 2;
            int cy = ctx.Height / 2;

            // Draw above starfield, below planets (raw numeric z so we don't need to touch RenderZ)
            const double z = 2.5;

            for (int i = 0; i < _debris.Length; i++)
            {
                var d = _debris[i];

                // mild parallax so it feels layered
                double par = 0.10 + 0.25 * d.depth;
                double wx = d.wx - _camWX * par;
                double wy = d.wy - _camWY * par;

                int sx = cx + (int)Math.Round(wx * _worldToScreen);
                int sy = cy + (int)Math.Round(wy * _worldToScreen * _orbitYScale);

                if (sx < 0 || sx >= ctx.Width || sy < 0 || sy >= ctx.Height) continue;

                // Slight variety
                char ch = (d.depth > 0.80) ? 'o' : ((d.depth > 0.55) ? '.' : ',');

                // keep subtle
                var fg = (d.depth > 0.70) ? Colors.BrightBlack : Colors.BrightBlack;

                r.Set(sx, sy, ch, fg, Colors.Black, z: z);
            }
        }

        private void DrawBlackHole(ConsoleRenderer r, int cx, int cy)
        {
            // =========
            // "Interstellar-like" BH look (Gargantua-ish):
            // - Dark shadow
            // - Bright photon ring (beamed)
            // - Thin accretion disk (beamed)
            // - Lensed upper/lower bands (single pass)
            //
            // IMPORTANT: fixed palette (no per-system color variation).
            // =========

            // ---------
            // Option C: world-scaled size (behaves like planets)
            // ---------
            double bhWorldR = 1.20; // tune this: 0.9..1.8 depending on how dominant you want BHs
            int basePx = MathUtil.ClampInt((int)Math.Round(bhWorldR * _worldToScreen), 7, 22);

            int shadowR = MathUtil.ClampInt((int)Math.Round(basePx * 1.10), 6, 26); // dark "shadow"
            int holeR = MathUtil.ClampInt((int)Math.Round(basePx * 0.85), 5, 22); // pure black core
            int ringR = MathUtil.ClampInt((int)Math.Round(basePx * 1.25), 7, 30); // photon ring

            // Disk radii
            int diskR0 = MathUtil.ClampInt((int)Math.Round(basePx * 1.60), 10, 40);
            int diskR1 = MathUtil.ClampInt((int)Math.Round(basePx * 2.35), 14, 60);

            // Visual style knobs (fixed => same look everywhere)
            double tilt = 0.32;           // radians (~18°)
            double diskFlat = 0.18;           // thin ellipse
            double rot = _simTime * 0.18; // slow rotation
            double beamPow = 2.2;            // Doppler beaming strength
            double lensLift = Math.Max(2.0, basePx * 0.55);
            double lensSquash = 0.42;

            // Palette: consistent + high contrast
            Color diskBase = Colors.BrightWhite;
            Color diskHot = Colors.BrightYellow;

            // ---- 1) Shadow + core fill ----
            int shadowR2 = shadowR * shadowR;
            int holeR2 = holeR * holeR;

            for (int y = -shadowR; y <= shadowR; y++)
            {
                for (int x = -shadowR; x <= shadowR; x++)
                {
                    int d2 = x * x + y * y;
                    if (d2 > shadowR2) continue;

                    r.Set(cx + x, cy + y, ' ', Colors.Black, Colors.Black, z: RenderZ.SUN_CORE + 0.01);
                }
            }

            for (int y = -holeR; y <= holeR; y++)
            {
                for (int x = -holeR; x <= holeR; x++)
                {
                    if (x * x + y * y > holeR2) continue;
                    r.Set(cx + x, cy + y, ' ', Colors.Black, Colors.Black, z: RenderZ.SUN_CORE + 0.02);
                }
            }

            // ---- 2) Photon ring (thin, bright, beamed) ----
            int ringSteps = Math.Max(120, ringR * 14);
            for (int si = 0; si < ringSteps; si++)
            {
                double a = (si / (double)ringSteps) * (Math.PI * 2.0);

                double ex = Math.Cos(a) * ringR;
                double ey = Math.Sin(a) * ringR * 0.88;

                int px = cx + (int)Math.Round(ex);
                int py = cy + (int)Math.Round(ey);

                double beam = 0.5 + 0.5 * Math.Cos(a + rot);
                beam = Math.Pow(beam, beamPow);

                double n = HashNoise.ValueNoise(unchecked((int)0x0BADC0DE),
                    Math.Cos(a) * 3.1 + _simTime * 0.12,
                    Math.Sin(a) * 3.1);

                double b = MathUtil.Clamp(0.35 + 0.65 * beam + 0.10 * (n - 0.5), 0.0, 1.0);

                char ch = RampCharDithered(RampBlocks, px, py, b);
                Color fg = (b > 0.78) ? diskHot : diskBase;

                r.Set(px, py, ch, fg, Colors.Black, z: RenderZ.SUN_CORONA + 0.08);
            }

            // ---- 3) Accretion disk band (main disk) ----
            double ct = Math.Cos(tilt);
            double st = Math.Sin(tilt);

            int radiiBands = 4; // reduced from 10 to avoid “snake braiding”
            int diskSteps = Math.Max(180, diskR1 * 16);

            for (int band = 0; band < radiiBands; band++)
            {
                double tBand = (radiiBands <= 1) ? 0.0 : band / (double)(radiiBands - 1);
                double rr = MathUtil.Lerp(diskR0, diskR1, tBand);

                double radial = 1.0 - tBand;
                radial = Math.Pow(radial, 0.85);

                for (int si = 0; si < diskSteps; si++)
                {
                    double a = (si / (double)diskSteps) * (Math.PI * 2.0) - rot;

                    double x0 = Math.Cos(a) * rr;
                    double y0 = Math.Sin(a) * rr * diskFlat;

                    double xr = x0 * ct - y0 * st;
                    double yr = x0 * st + y0 * ct;

                    int px = cx + (int)Math.Round(xr);
                    int py = cy + (int)Math.Round(yr);

                    double dx = px - cx;
                    double dy = py - cy;
                    double d2 = dx * dx + dy * dy;
                    if (d2 < shadowR2 * 0.92) continue;

                    double beam = 0.5 + 0.5 * Math.Cos(a + rot);
                    beam = Math.Pow(beam, beamPow);

                    double b = 0.10 + 0.65 * radial;
                    b *= (0.40 + 0.85 * beam);

                    double n = HashNoise.ValueNoise(unchecked((int)0xFEEDC0DE),
                        Math.Cos(a) * 6.0 + _simTime * 0.18,
                        Math.Sin(a) * 2.0);

                    b *= (0.80 + 0.30 * n);
                    b = MathUtil.Clamp(b, 0.0, 1.0);

                    char ch = (b > 0.80) ? '-' : ((b > 0.55) ? '-' : '.'); // avoid em-dash glyph issues
                    Color fg = (b > 0.78) ? diskHot : diskBase;

                    r.Set(px, py, ch, fg, Colors.Black, z: RenderZ.SUN_CORONA + 0.06);
                }
            }

            // ---- 4) Lensed upper/lower bands (SINGLE PASS) ----
            double rrLens = (diskR0 + diskR1) * 0.55;
            int lensSteps = Math.Max(220, diskR1 * 14);

            for (int si = 0; si < lensSteps; si++)
            {
                double a = (si / (double)lensSteps) * (Math.PI * 2.0) - rot;

                double x0 = Math.Cos(a) * rrLens;
                double y0 = Math.Sin(a) * rrLens * diskFlat;

                double xr = x0 * ct - y0 * st;
                double yr = x0 * st + y0 * ct;

                int lx = cx + (int)Math.Round(xr);

                double ly = yr * lensSquash;
                int upY = cy + (int)Math.Round(ly - lensLift);
                int dnY = cy + (int)Math.Round(ly + lensLift);

                double beam = 0.5 + 0.5 * Math.Cos(a + rot);
                beam = Math.Pow(beam, beamPow);

                double lb = MathUtil.Clamp(0.18 + 0.55 * beam, 0.0, 1.0);
                if (lb < 0.22) continue;

                char lch = (lb > 0.65) ? '-' : '.';
                Color lfg = (lb > 0.78) ? diskHot : diskBase;

                int dux = lx - cx;
                int duy = upY - cy;
                int ddx = lx - cx;
                int ddy = dnY - cy;

                double d2up = dux * dux + duy * duy;
                double d2dn = ddx * ddx + ddy * ddy;

                if (d2up > shadowR2 * 0.92)
                    r.Set(lx, upY, lch, lfg, Colors.Black, z: RenderZ.SUN_CORONA + 0.05);

                if (d2dn > shadowR2 * 0.92)
                    r.Set(lx, dnY, lch, lfg, Colors.Black, z: RenderZ.SUN_CORONA + 0.05);
            }

            // ---- 5) Thin inner rim (subtle silhouette sharpen) ----
            int rimSteps = Math.Max(90, shadowR * 12);
            for (int si = 0; si < rimSteps; si++)
            {
                double a = (si / (double)rimSteps) * (Math.PI * 2.0);
                int x = cx + (int)Math.Round(Math.Cos(a) * shadowR);
                int y = cy + (int)Math.Round(Math.Sin(a) * shadowR * 0.90);
                r.Set(x, y, '.', Colors.BrightBlack, Colors.Black, z: RenderZ.SUN_CORONA + 0.04);
            }
        }

        private void DrawNebula(ConsoleRenderer r, EngineContext ctx, int centerX, int centerY)
        {
            if (_sys.Nebulae == null || _sys.Nebulae.Count == 0) return;

            // Draw as a foggy background: low-density glyphs with screen-space noise
            // Keep it cheap: stride grows at high zoom.
            int stride = 2;
            if (_worldToScreen > 18.0) stride = 3;
            if (_worldToScreen > 35.0) stride = 4;
            if (_worldToScreen > 55.0) stride = 5;

            for (int i = 0; i < _sys.Nebulae.Count; i++)
            {
                var n = _sys.Nebulae[i];

                int sx = WorldToScreenX(n.WX, centerX);
                int sy = WorldToScreenY(n.WY, centerY);

                int rr = MathUtil.ClampInt((int)Math.Round(n.RadiusWorld * _worldToScreen), 6, 400);
                if (!CircleIntersectsScreen(sx, sy, rr, ctx.Width, ctx.Height)) continue;

                Color baseFg = ToRgb(n.Fg);

                // Sample the circle area, but skip aggressively (stride).
                int rr2 = rr * rr;
                int seed = n.NoiseSeed;

                for (int y = -rr; y <= rr; y += stride)
                {
                    int yy = y * y;
                    for (int x = -rr; x <= rr; x += stride)
                    {
                        int d2 = x * x + yy;
                        if (d2 > rr2) continue;

                        // Radial falloff
                        double d = Math.Sqrt(d2);
                        double edge = 1.0 - (d / Math.Max(1.0, rr));
                        if (edge <= 0.0) continue;

                        // Cheap noise from screen coords (stable in world space via sx/sy)
                        int px = sx + x;
                        int py = sy + y;
                        if ((uint)px >= (uint)ctx.Width || (uint)py >= (uint)ctx.Height) continue;

                        double ns = HashNoise.ValueNoise(seed, px * 0.11, py * 0.11);
                        // Convert cloud density + edge + noise into visibility
                        double dens = n.Density01 * (0.35 + 0.65 * edge);
                        double vis = dens * (0.55 + 0.90 * (ns - 0.5));

                        if (vis < 0.10) continue;

                        // Map to glyph and brightness (soft fog)
                        double b01 = MathUtil.Clamp(vis, 0.0, 1.0);

                        char ch;
                        if (PlanetDrawer.PlanetColorOnlyShading)
                        {
                            // use blocks with dithering
                            ch = RampCharDithered(RampBlocks, px, py, b01);
                        }
                        else
                        {
                            // subtle fog glyphs
                            ch = (b01 < 0.25) ? '.' : (b01 < 0.55 ? ':' : (b01 < 0.80 ? '*' : '█'));
                        }

                        // Color is base but brightness pushes it a bit
                        Color fg = ShadeFromBrightness(baseFg, b01);

                        r.Set(px, py, ch, fg, Colors.Black, z: RenderZ.STARFIELD - 1);
                    }
                }
            }
        }

        private void DrawAsteroids(ConsoleRenderer r, EngineContext ctx, int centerX, int centerY)
        {
            if (_sys.Asteroids == null || _sys.Asteroids.Count == 0) return;

            // A simple “point sprite” approach: draw each asteroid as a glyph at its projected position.
            // Optional: draw a tiny halo for large ones.
            for (int i = 0; i < _sys.Asteroids.Count; i++)
            {
                var a = _sys.Asteroids[i];

                int sx = WorldToScreenX(a.WX, centerX);
                int sy = WorldToScreenY(a.WY, centerY);

                if ((uint)sx >= (uint)ctx.Width || (uint)sy >= (uint)ctx.Height) continue;

                bool selected = IsSelected(EntityKind.Asteroid, i, -1);

                // Convert world radius into a rough “size”
                double sizeChars = a.RadiusWorld * _worldToScreen;

                // Pick glyph
                char ch = selected ? '@' : a.Glyph;

                // Convert asteroid color (Ansi) -> RGB
                Color fg = ToRgb(a.Fg);
                if (selected) fg = Colors.BrightYellow;

                // If big enough, draw a 3x3 speckle around it (cheap)
                if (sizeChars > 1.15)
                {
                    // simple random-ish sparkle pattern based on index
                    int seed = _sys.Seed ^ (i * 733);
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0) continue;

                            int px = sx + ox;
                            int py = sy + oy;
                            if ((uint)px >= (uint)ctx.Width || (uint)py >= (uint)ctx.Height) continue;

                            double n = HashNoise.Hash01(seed, px + 11, py + 17);
                            if (n < 0.82) continue;

                            r.Set(px, py, '.', Colors.BrightBlack, Colors.Black, z: RenderZ.BELT - 1);
                        }
                    }
                }

                r.Set(sx, sy, ch, fg, Colors.Black, z: RenderZ.BELT + 1);

                if (_showLabels && selected)
                    r.DrawString(sx + 2, sy, $"Asteroid-{i + 1}", Colors.BrightYellow, Colors.Black, z: RenderZ.UI_BORDER);
            }
        }



        private static bool CircleIntersectsScreen(int cx, int cy, int r, int screenW, int screenH)
        {
            return !(cx + r < 0 || cx - r >= screenW || cy + r < 0 || cy - r >= screenH);
        }

        private void DrawSunScaled(ConsoleRenderer r, int sunX, int sunY)
        {
            int sunR = MathUtil.ClampInt((int)Math.Round(_sys.SunRadiusWorld * _worldToScreen), 2, 60);

            int coronaThickness = MathUtil.ClampInt((int)Math.Round(sunR * 0.28), 2, 16);
            int coronaR = MathUtil.ClampInt(sunR + coronaThickness, sunR + 2, 80);

            int seed = _sys.Seed ^ 0x51A7BEEF;
            double t = _simTime;

            int sunR2 = sunR * sunR;
            int coronaR2 = coronaR * coronaR;
            double invBandDen = 1.0 / Math.Max(1.0, (coronaR - sunR));

            Color sunBase = ToRgb(_sys.SunColor);

            // Corona
            for (int y = -coronaR; y <= coronaR; y++)
            {
                for (int x = -coronaR; x <= coronaR; x++)
                {
                    int d2i = x * x + y * y;
                    if (d2i > coronaR2) continue;
                    if (d2i < sunR2) continue;

                    double d = Math.Sqrt(d2i);
                    double band = (d - sunR) * invBandDen;
                    if (band < 0.0 || band > 1.0) continue;

                    double edge = 1.0 - band;
                    edge = MathUtil.Clamp(edge, 0.0, 1.0);

                    double ang = Math.Atan2(y, x);
                    double n1 = HashNoise.FBm(seed + 11,
                        x * 0.08 + Math.Cos(ang) * 0.35,
                        y * 0.08 + Math.Sin(ang) * 0.35 + t * 0.35,
                        octaves: 3);

                    double n2 = HashNoise.ValueNoise(seed + 33,
                        x * 0.22 + t * 0.18,
                        y * 0.22 - t * 0.14);

                    double b = 0.10 + 0.55 * edge;
                    b += 0.20 * (n1 - 0.5);
                    b += 0.10 * (n2 - 0.5);

                    b *= MathUtil.Clamp(edge * 1.15, 0.0, 1.0);

                    if (b < 0.10) continue;

                    double bb = MathUtil.Clamp(b, 0.0, 1.0);

                    char ch = PlanetDrawer.PlanetColorOnlyShading
                        ? '█'
                        : PlanetDrawer.SunGlyph(x, y, sunX, sunY, bb);

                    Color fg = PlanetDrawer.PlanetColorOnlyShading
                        ? ShadeFromBrightness(sunBase, bb)
                        : sunBase;

                    r.Set(sunX + x, sunY + y, ch, fg, Colors.Black, z: RenderZ.SUN_CORONA);
                }
            }

            // Sun core
            for (int y = -sunR; y <= sunR; y++)
            {
                for (int x = -sunR; x <= sunR; x++)
                {
                    int d2i = x * x + y * y;
                    if (d2i > sunR2) continue;

                    double d = Math.Sqrt(d2i);
                    double u = (x / (double)sunR);
                    double v = (y / (double)sunR);

                    double tEdge = d / sunR;
                    double limb = 1.0 - 0.65 * Math.Pow(tEdge, 1.25);

                    double gran = HashNoise.FBm(seed + 101,
                        u * 9.5 + t * 0.10,
                        v * 9.5 - t * 0.08,
                        octaves: 4);

                    double ang = Math.Atan2(v, u);
                    double swirl = HashNoise.ValueNoise(seed + 202,
                        Math.Cos(ang) * 2.5 + u * 1.2 + t * 0.06,
                        Math.Sin(ang) * 2.5 + v * 1.2 - t * 0.05);

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

                    double b = 0.82 * limb;
                    b += 0.22 * (gran - 0.5);
                    b += 0.10 * (swirl - 0.5);
                    b -= 0.55 * spot;

                    b = MathUtil.Clamp(b, 0.0, 1.0);
                    double bb = MathUtil.Clamp(b, 0.0, 1.0);

                    char ch = PlanetDrawer.PlanetColorOnlyShading
                        ? '█'
                        : PlanetDrawer.SunGlyph(x, y, sunX, sunY, bb);

                    Color fg = PlanetDrawer.PlanetColorOnlyShading
                        ? ShadeFromBrightness(sunBase, bb)
                        : sunBase;

                    r.Set(sunX + x, sunY + y, ch, fg, Colors.Black, z: RenderZ.SUN_CORE);
                }
            }
        }

        private void DrawPlanets(ConsoleRenderer r, int centerX, int centerY, int sunX, int sunY, PlanetDrawer.Occluder[] occluders)
        {
            for (int i = 0; i < _sys.Planets.Count; i++)
            {
                var p = _sys.Planets[i];

                int px = WorldToScreenX(p.WX, centerX);
                int py = WorldToScreenY(p.WY, centerY);

                int pr = MathUtil.ClampInt((int)Math.Round(p.RadiusWorld * _worldToScreen), 1, 80);

                var oldTex = p.Texture;

                if (_texMode == 2 && p.Name != null && p.Name.StartsWith("Earth"))
                    p.Texture = PlanetDrawer.PlanetTexture.IceWorld;
                else if (_texMode == 3)
                    p.Texture = PlanetDrawer.PlanetTexture.GasBands;

                bool oldRings = p.HasRings;
                if (!_showRings) p.HasRings = false;

                bool inView = CircleIntersectsScreen(px, py, pr, r.Width, r.Height);
                bool allowLod = !inView;

                PlanetDrawer.DrawPlanet(r, px, py, pr, _sys.Seed, p, _simTime, sunX, sunY, occluders, allowLod);

                p.Texture = oldTex;
                p.HasRings = oldRings;

                if (_showLabels)
                {
                    bool selected = IsSelected(EntityKind.Planet, i, -1);
                    var fg = selected ? Colors.BrightYellow : Colors.BrightBlack;

                    if (selected)
                        r.DrawString(px - 1, py - pr - 1, "v", Colors.BrightYellow, Colors.Black, z: 10);

                    r.DrawString(px + pr + 1, py, p.Name, fg, Colors.Black, z: 10);
                }
            }
        }

        private void DrawMoonOrbitRing(ConsoleRenderer r, int planetSX, int planetSY, double localR)
        {
            int rx = (int)Math.Round(localR * _worldToScreen);
            int ry = (int)Math.Round(localR * _worldToScreen * _orbitYScale);

            rx = MathUtil.ClampInt(rx, 2, 400);
            ry = MathUtil.ClampInt(ry, 2, 400);

            const int steps = 96;
            for (int i = 0; i < steps; i++)
            {
                double a = (i / (double)steps) * (Math.PI * 2.0);
                int x = planetSX + (int)Math.Round(Math.Cos(a) * rx);
                int y = planetSY + (int)Math.Round(Math.Sin(a) * ry);

                r.Set(x, y, '.', Colors.BrightBlack, Colors.Black, z: RenderZ.ORBITS);
            }
        }

        private void DrawMoons(ConsoleRenderer r, int centerX, int centerY, int sunX, int sunY, PlanetDrawer.Occluder[] occluders)
        {
            for (int pi = 0; pi < _sys.Planets.Count; pi++)
            {
                var p = _sys.Planets[pi];
                if (p.Moons == null || p.Moons.Count == 0) continue;

                int psx = WorldToScreenX(p.WX, centerX);
                int psy = WorldToScreenY(p.WY, centerY);

                for (int mi = 0; mi < p.Moons.Count; mi++)
                {
                    var m = p.Moons[mi];

                    if (_showOrbits)
                        DrawMoonOrbitRing(r, psx, psy, m.LocalRadius);

                    int mx = WorldToScreenX(m.WX, centerX);
                    int my = WorldToScreenY(m.WY, centerY);

                    int mr = MathUtil.ClampInt((int)Math.Round(m.RadiusWorld * _worldToScreen), 1, 40);

                    bool inView = CircleIntersectsScreen(mx, my, mr, r.Width, r.Height);
                    bool allowLod = !inView;

                    PlanetDrawer.DrawMoon(r, mx, my, mr, _sys.Seed, m, _simTime, sunX, sunY, occluders, allowLod);

                    if (_showLabels)
                    {
                        bool selected = IsSelected(EntityKind.Moon, pi, mi);
                        if (selected)
                        {
                            r.DrawString(mx - 1, my - mr - 1, "v", Colors.BrightYellow, Colors.Black, z: 10);
                            r.DrawString(mx + mr + 1, my, m.Name, Colors.BrightYellow, Colors.Black, z: 10);
                        }
                    }
                }
            }
        }

        private void DrawStations(ConsoleRenderer r, int centerX, int centerY)
        {
            for (int i = 0; i < _sys.Stations.Count; i++)
            {
                var s = _sys.Stations[i];

                int sx = WorldToScreenX(s.WX, centerX);
                int sy = WorldToScreenY(s.WY, centerY);

                bool selected = IsSelected(EntityKind.Station, i, -1);
                char ch = selected ? '@' : '+';
                var fg = selected ? Colors.BrightGreen : Colors.BrightBlack;

                r.Set(sx, sy, ch, fg, Colors.Black, z: RenderZ.STATION);

                if (_showLabels)
                    r.DrawString(sx + 2, sy, s.Name,
                        selected ? Colors.BrightGreen : Colors.BrightBlack,
                        Colors.Black, z: 10);
            }
        }

        private void DrawShips(ConsoleRenderer r, EngineContext ctx, int centerX, int centerY)
        {
            double halfWWorld = (ctx.Width / 2.0) / _worldToScreen;
            double halfHWorld = (ctx.Height / 2.0) / (_worldToScreen * _orbitYScale);

            double minX = _camWX - halfWWorld - 2;
            double maxX = _camWX + halfWWorld + 2;
            double minY = _camWY - halfHWorld - 2;
            double maxY = _camWY + halfHWorld + 2;

            for (int i = 0; i < _sys.Ships.Count; i++)
            {
                var sh = _sys.Ships[i];
                if (sh == null) continue;

                foreach (var pt in sh.Trail.Points)
                {
                    if (pt.x < minX || pt.x > maxX || pt.y < minY || pt.y > maxY) continue;

                    int tx = WorldToScreenX(pt.x, centerX);
                    int ty = WorldToScreenY(pt.y, centerY);
                    r.Set(tx, ty, '`', Colors.BrightBlack, Colors.Black, z: RenderZ.SHIP_TRAIL);
                }

                int x = WorldToScreenX(sh.WX, centerX);
                int y = WorldToScreenY(sh.WY, centerY);

                bool selected = IsSelected(EntityKind.Ship, i, -1);
                bool armed = (_armedShipIndex == i);

                char ch = ShipGlyph(sh.VX, sh.VY, selected);

                // Ship.Fg is still AnsiColor in your world model — convert at render time.
                Color shipFg = ToRgb(sh.Fg);
                var fg = armed ? Colors.BrightCyan : (selected ? Colors.BrightYellow : shipFg);

                r.Set(x, y, ch, fg, Colors.Black, z: RenderZ.SHIP);

                if (_showLabels && selected)
                    r.DrawString(x + 2, y, sh.Name, Colors.BrightYellow, Colors.Black, z: 10);
            }
        }

        private static char ShipGlyph(double vx, double vy, bool selected)
        {
            double a2 = vx * vx + vy * vy;
            if (a2 < 0.02) return selected ? 'A' : 'a';

            if (Math.Abs(vx) > Math.Abs(vy))
                return (vx > 0) ? '>' : '<';
            else
                return (vy > 0) ? 'v' : '^';
        }

        private void EnsureStarfield()
        {
            int seed = _sys.Seed;
            if (_starSeedBuilt == seed && _starPts.Length == StarCount) return;

            _starSeedBuilt = seed;
            _starPts = new StarPt[StarCount];

            for (int i = 0; i < StarCount; i++)
            {
                double rx = MathUtil.Hash01(seed + i * 17 + 1);
                double ry = MathUtil.Hash01(seed + i * 17 + 2);
                double rd = MathUtil.Hash01(seed + i * 17 + 3);

                _starPts[i] = new StarPt
                {
                    wx = (rx * 2.0 - 1.0) * StarSpan,
                    wy = (ry * 2.0 - 1.0) * StarSpan,
                    depth = rd
                };
            }
        }

        private void DrawStarfield(ConsoleRenderer r, EngineContext ctx)
        {
            EnsureStarfield();

            int cx = ctx.Width / 2;
            int cy = ctx.Height / 2;

            for (int i = 0; i < _starPts.Length; i++)
            {
                var sp = _starPts[i];

                double depth = sp.depth;
                double par = 0.08 + 0.28 * depth;

                double relX = MathUtil.Wrap(sp.wx - _camWX * par, -StarSpan, StarSpan);
                double relY = MathUtil.Wrap(sp.wy - _camWY * par, -StarSpan, StarSpan);

                int sx = cx + (int)Math.Round(relX * _worldToScreen);
                int sy = cy + (int)Math.Round(relY * _worldToScreen * _orbitYScale);

                if (sx < 0 || sx >= ctx.Width || sy < 0 || sy >= ctx.Height) continue;

                char ch = (depth > 0.85) ? '*' : '.';
                var fg = (depth > 0.85) ? Colors.BrightWhite : Colors.BrightBlack;

                r.Set(sx, sy, ch, fg, Colors.Black, z: RenderZ.STARFIELD);
            }
        }

        private void DrawUI(ConsoleRenderer r, EngineContext ctx)
        {
            r.DrawRect(0, 0, ctx.Width, ctx.Height, '#', Colors.BrightBlack, Colors.Black, z: RenderZ.UI_BORDER);

            _spectreUiTimer += ctx.DeltaTime;
            if (_spectreUiTimer >= SpectreUiEvery)
            {
                _spectreUiTimer = 0;

                int headerW = ctx.Width - 4;
                var headerRenderable = BuildSpectreHeader(ctx);
                _spectreHeaderCache = RenderSpectreToString(headerRenderable, headerW);

                var sel = GetSelection();
                if (sel != null)
                {
                    int panelW = Math.Min(40, Math.Max(26, ctx.Width / 3));
                    var infoRenderable = BuildSpectreInfo(ctx, sel);
                    _spectreInfoCache = RenderSpectreToString(infoRenderable, panelW);
                }
                else
                {
                    _spectreInfoCache = "";
                }
            }

            int headerX = 2;
            int headerY = 1;
            int headerWBlit = ctx.Width - 4;
            int headerHBlit = 6;
            BlitText(r, headerX, headerY, headerWBlit, headerHBlit, _spectreHeaderCache,
                     Colors.BrightCyan, Colors.Black, RenderZ.UI_BORDER);

            var sel2 = GetSelection();
            if (sel2 != null)
            {
                int panelW = Math.Min(40, Math.Max(26, ctx.Width / 3));
                int panelH = Math.Min(ctx.Height - 9, 18);
                int panelX = ctx.Width - panelW - 2;
                int panelY = 7;

                BlitText(r, panelX, panelY, panelW, panelH, _spectreInfoCache,
                         Colors.BrightWhite, Colors.Black, RenderZ.UI_BORDER);
            }

            if (!string.IsNullOrEmpty(_msg))
                r.DrawString(2, 7, _msg, Colors.BrightGreen, Colors.Black, z: RenderZ.UI_BORDER);

            int line = ctx.Height - 2;
            foreach (var e in _events.GetNewestFirst(4))
            {
                r.DrawString(2, line, e, Colors.BrightBlack, Colors.Black, z: RenderZ.UI_BORDER);
                line--;
                if (line < 10) break;
            }
        }

        // =========================
        // Selection building (includes moons via reflection)
        // =========================
        private void RebuildSelection()
        {
            _selection.Clear();

            _selection.Add(new SelectionItem
            {
                Kind = EntityKind.Sun,
                Index = -1,
                SubIndex = -1,
                Label = "Sun",
                WX = 0.0,
                WY = 0.0,
                WZ = 0.0
            });

            for (int i = 0; i < _sys.Planets.Count; i++)
            {
                var p = _sys.Planets[i];
                _selection.Add(new SelectionItem
                {
                    Kind = EntityKind.Planet,
                    Index = i,
                    SubIndex = -1,
                    Label = p.Name,
                    WX = p.WX,
                    WY = p.WY,
                    WZ = p.WZ
                });

                TryAddMoonsFromPlanet(i, p);
            }

            for (int i = 0; i < _sys.Stations.Count; i++)
            {
                var s = _sys.Stations[i];
                _selection.Add(new SelectionItem
                {
                    Kind = EntityKind.Station,
                    Index = i,
                    SubIndex = -1,
                    Label = s.Name,
                    WX = s.WX,
                    WY = s.WY,
                    WZ = s.WZ
                });
            }

            for (int i = 0; i < _sys.Ships.Count; i++)
            {
                var sh = _sys.Ships[i];
                _selection.Add(new SelectionItem
                {
                    Kind = EntityKind.Ship,
                    Index = i,
                    SubIndex = -1,
                    Label = sh.Name,
                    WX = sh.WX,
                    WY = sh.WY,
                    WZ = sh.WZ
                });
            }

            // NEW: add asteroids + nebula clouds to selection (if present)
            if (_sys.Asteroids != null)
            {
                for (int i = 0; i < _sys.Asteroids.Count; i++)
                {
                    var a = _sys.Asteroids[i];
                    _selection.Add(new SelectionItem
                    {
                        Kind = EntityKind.Asteroid,
                        Index = i,
                        SubIndex = -1,
                        Label = $"Asteroid-{i + 1}",
                        WX = a.WX,
                        WY = a.WY,
                        WZ = a.WZ
                    });
                }
            }

            if (_sys.Nebulae != null)
            {
                for (int i = 0; i < _sys.Nebulae.Count; i++)
                {
                    var n = _sys.Nebulae[i];
                    _selection.Add(new SelectionItem
                    {
                        Kind = EntityKind.Nebula,
                        Index = i,
                        SubIndex = -1,
                        Label = $"Nebula-{i + 1}",
                        WX = n.WX,
                        WY = n.WY,
                        WZ = n.WZ
                    });
                }
            }

            _selIndex = MathUtil.ClampInt(_selIndex, 0, Math.Max(0, _selection.Count - 1));
        }

        private void TryAddMoonsFromPlanet(int planetIndex, object planetObj)
        {
            if (planetObj == null) return;

            var pt = planetObj.GetType();
            PropertyInfo moonsProp = GetCachedProp(_moonsPropCache, pt, "Moons");
            if (moonsProp == null) return;

            object moonsObj = moonsProp.GetValue(planetObj);
            if (moonsObj is not IList moons) return;

            for (int mi = 0; mi < moons.Count; mi++)
            {
                object moon = moons[mi];
                if (moon == null) continue;

                string name = ReadStringCached(moon, _namePropCache, "Name");
                if (string.IsNullOrWhiteSpace(name)) name = $"Moon-{planetIndex}-{mi}";

                _selection.Add(new SelectionItem
                {
                    Kind = EntityKind.Moon,
                    Index = planetIndex,
                    SubIndex = mi,
                    Label = name,
                    WX = ReadDoubleCached(moon, _wxPropCache, "WX"),
                    WY = ReadDoubleCached(moon, _wyPropCache, "WY"),
                    WZ = ReadDoubleCached(moon, _wzPropCache, "WZ")
                });
            }
        }

        private static string ReadString(object obj, string propName)
        {
            if (obj == null) return "";
            PropertyInfo p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return "";
            object v = p.GetValue(obj);
            return v?.ToString() ?? "";
        }

        private SelectionItem GetSelection()
        {
            if (_selection.Count == 0) RebuildSelection();
            if (_selection.Count == 0) return null;

            _selIndex = MathUtil.ClampInt(_selIndex, 0, _selection.Count - 1);
            var sel = _selection[_selIndex];

            if (sel.Kind == EntityKind.Sun)
            {
                sel.WX = 0; sel.WY = 0; sel.WZ = 0;
            }
            else if (sel.Kind == EntityKind.Planet && sel.Index >= 0 && sel.Index < _sys.Planets.Count)
            {
                var p = _sys.Planets[sel.Index];
                sel.WX = p.WX; sel.WY = p.WY; sel.WZ = p.WZ;
            }
            else if (sel.Kind == EntityKind.Moon)
            {
                if (TryGetMoonWorld(_sys, sel.Index, sel.SubIndex, out double mx, out double my))
                {
                    sel.WX = mx;
                    sel.WY = my;
                    sel.WZ = 0;
                }
            }
            else if (sel.Kind == EntityKind.Station && sel.Index >= 0 && sel.Index < _sys.Stations.Count)
            {
                var s = _sys.Stations[sel.Index];
                sel.WX = s.WX; sel.WY = s.WY; sel.WZ = s.WZ;
            }
            else if (sel.Kind == EntityKind.Ship && sel.Index >= 0 && sel.Index < _sys.Ships.Count)
            {
                var sh = _sys.Ships[sel.Index];
                sel.WX = sh.WX; sel.WY = sh.WY; sel.WZ = sh.WZ;
            }
            else if (sel.Kind == EntityKind.Asteroid && sel.Index >= 0 && sel.Index < _sys.Asteroids.Count)
            {
                var a = _sys.Asteroids[sel.Index];
                sel.WX = a.WX; sel.WY = a.WY; sel.WZ = a.WZ;
            }
            else if (sel.Kind == EntityKind.Nebula && sel.Index >= 0 && sel.Index < _sys.Nebulae.Count)
            {
                var n = _sys.Nebulae[sel.Index];
                sel.WX = n.WX; sel.WY = n.WY; sel.WZ = n.WZ;
            }


            _selection[_selIndex] = sel;
            return sel;
        }

        private bool IsSelected(EntityKind k, int index, int subIndex)
        {
            if (_selection.Count == 0) return false;
            var sel = _selection[MathUtil.ClampInt(_selIndex, 0, Math.Max(0, _selection.Count - 1))];
            return sel.Kind == k && sel.Index == index && sel.SubIndex == subIndex;
        }

        // =========================
        // Orders
        // =========================
        private void IssueTravelOrder(Ship ship, SelectionItem target)
        {
            switch (target.Kind)
            {
                case EntityKind.Sun:
                    ship.OrbitTargetKind = OrbitTargetKind.Sun;
                    ship.OrbitTargetIndex = -1;
                    ship.OrbitTargetSubIndex = -1;
                    break;

                case EntityKind.Planet:
                    ship.OrbitTargetKind = OrbitTargetKind.Planet;
                    ship.OrbitTargetIndex = target.Index;
                    ship.OrbitTargetSubIndex = -1;
                    break;

                case EntityKind.Station:
                    ship.OrbitTargetKind = OrbitTargetKind.Station;
                    ship.OrbitTargetIndex = target.Index;
                    ship.OrbitTargetSubIndex = -1;
                    break;

                case EntityKind.Ship:
                    ship.OrbitTargetKind = OrbitTargetKind.Ship;
                    ship.OrbitTargetIndex = target.Index;
                    ship.OrbitTargetSubIndex = -1;
                    break;

                case EntityKind.Moon:
                    ship.OrbitTargetKind = OrbitTargetKind.Moon;
                    ship.OrbitTargetIndex = target.Index;
                    ship.OrbitTargetSubIndex = target.SubIndex;
                    break;

                default:
                    ship.OrbitTargetKind = OrbitTargetKind.None;
                    ship.OrbitTargetIndex = -1;
                    ship.OrbitTargetSubIndex = -1;
                    break;
            }

            ship.TargetX = target.WX;
            ship.TargetY = target.WY;
            ship.Mode = ShipMode.TravelToPoint;
        }

        private void IssueOrbitOrder(Ship ship, SelectionItem target)
        {
            switch (target.Kind)
            {
                case EntityKind.Sun:
                    ship.OrbitTargetKind = OrbitTargetKind.Sun;
                    ship.OrbitTargetIndex = -1;
                    ship.OrbitTargetSubIndex = -1;
                    break;

                case EntityKind.Planet:
                    ship.OrbitTargetKind = OrbitTargetKind.Planet;
                    ship.OrbitTargetIndex = target.Index;
                    ship.OrbitTargetSubIndex = -1;
                    break;

                case EntityKind.Moon:
                    ship.OrbitTargetKind = OrbitTargetKind.Moon;
                    ship.OrbitTargetIndex = target.Index;
                    ship.OrbitTargetSubIndex = target.SubIndex;
                    break;

                case EntityKind.Station:
                    ship.OrbitTargetKind = OrbitTargetKind.Station;
                    ship.OrbitTargetIndex = target.Index;
                    ship.OrbitTargetSubIndex = -1;
                    break;

                case EntityKind.Ship:
                    ship.OrbitTargetKind = OrbitTargetKind.Ship;
                    ship.OrbitTargetIndex = target.Index;
                    ship.OrbitTargetSubIndex = -1;
                    break;

                default:
                    ship.OrbitTargetKind = OrbitTargetKind.None;
                    ship.OrbitTargetIndex = -1;
                    ship.OrbitTargetSubIndex = -1;
                    break;
            }

            ship.TargetX = target.WX;
            ship.TargetY = target.WY;

            if (ship.OrbitRadius <= 0.01) ship.OrbitRadius = 1.5;
            if (Math.Abs(ship.OrbitAngularSpeed) < 0.0001) ship.OrbitAngularSpeed = 0.6;

            ship.Mode = ShipMode.Orbit;
        }

        private bool TryGetArmedShip(out Ship ship)
        {
            ship = null;
            if (_armedShipIndex < 0 || _armedShipIndex >= _sys.Ships.Count) return false;
            ship = _sys.Ships[_armedShipIndex];
            return ship != null;
        }

        // =========================
        // Spawns
        // =========================
        private void SpawnShipNearSun()
        {
            int id = _sys.Ships.Count + 1;

            double ang = (id * 1.7) % (Math.PI * 2.0);
            double rr = 1.2;

            var sh = new Ship
            {
                Name = "Ship-" + id,
                WX = Math.Cos(ang) * rr,
                WY = Math.Sin(ang) * rr,
                // Ship.Fg is still AnsiColor in your world model (for now)
                Fg = AnsiColor.BrightWhite
            };

            _sys.Ships.Add(sh);
        }

        private void SpawnStationAroundPlanet(int planetIndex)
        {
            int id = _sys.Stations.Count + 1;

            _sys.Stations.Add(new Station
            {
                Name = "Station-" + id,
                ParentPlanetIndex = planetIndex,
                LocalRadius = 0.75 + 0.15 * (id % 3),
                LocalPeriod = 5.5 + 0.9 * (id % 4),
                LocalPhase = 0.3 * id
            });
        }

        // =========================
        // Systems
        // =========================
        private void SetActiveSystem(int index, bool resetSimTime)
        {
            if (_galaxy.Systems.Count == 0) return;

            _systemIndex = MathUtil.ClampInt(index, 0, _galaxy.Systems.Count - 1);
            _sys = _galaxy.Get(_systemIndex);

            if (resetSimTime) _simTime = 0.0;

            _armedShipIndex = -1;
            _follow = false;
            _interiorView = false;
            _activeInterior = null;
            _camWX = 0; _camWY = 0;
            _cam.CamWX = 0; _cam.CamWY = 0;
            _cam.TargetCamWX = 0; _cam.TargetCamWY = 0;

            BuildBeltForSystem();

            _starSeedBuilt = int.MinValue;
            _debrisSeedBuilt = int.MinValue;


            StarSystemLogic.UpdateCelestials(_sys, _simTime, _useKepler);
            RebuildSelection();
        }

        private const double BeltChance = 0.55;

        private void BuildBeltForSystem()
        {
            // Deterministic roll per system (same seed => same result)
            int seed = _sys.Seed ^ 0x4B1D_77A3;

            // If you already have HashNoise.Hash01 available, use it:
            double roll = HashNoise.Hash01(seed, 11, 22);

            if (roll > BeltChance)
            {
                _belt = null; // no belt in this system
                return;
            }

            // Optional: vary belt parameters per system so belts don’t all look identical
            double radius = 9.5 + 6.0 * HashNoise.Hash01(seed, 33, 44);  // ~9.5..15.5
            double thickness = 0.6 + 1.2 * HashNoise.Hash01(seed, 55, 66);  // ~0.6..1.8
            int count = 140 + (int)Math.Round(220 * HashNoise.Hash01(seed, 77, 88)); // ~140..360
            double speed = 0.06 + 0.14 * HashNoise.Hash01(seed, 99, 111); // ~0.06..0.20

            _belt = new Belt(seed: _sys.Seed + 77, count: count, radius: radius, thickness: thickness, baseSpeed: speed);
        }


        private void FitSystemToScreen(EngineContext ctx)
        {
            double maxA = 10.0;
            for (int i = 0; i < _sys.Planets.Count; i++)
                if (_sys.Planets[i].A > maxA) maxA = _sys.Planets[i].A;

            int w = ctx.Width;
            int h = ctx.Height;

            _worldToScreen = Math.Max(2.5, Math.Min(w, h) * 0.45 / maxA);
            _worldToScreen = MathUtil.Clamp(_worldToScreen, 1.0, 80.0);
        }

        // =========================
        // Ramp helper + message
        // =========================
        private static char RampChar(string ramp, double brightness01)
        {
            if (string.IsNullOrEmpty(ramp)) return '#';
            brightness01 = MathUtil.Clamp(brightness01, 0.0, 1.0);

            int idx = (int)(brightness01 * (ramp.Length - 1));
            if (idx < 0) idx = 0;
            if (idx >= ramp.Length) idx = ramp.Length - 1;
            return ramp[idx];
        }

        private char RampCharSmart(string ramp, int sx, int sy, double brightness01)
        {
            if (ReferenceEquals(ramp, RampBlocks) || ramp == RampBlocks)
                return RampCharDithered(ramp, sx, sy, brightness01);

            return RampChar(ramp, brightness01);
        }

        private char SunGlyphForMode(int localX, int localY, int ox, int oy, double b01)
        {
            b01 = MathUtil.Clamp(b01, 0.0, 1.0);

            if (PlanetDrawer.PlanetColorOnlyShading)
                return RampCharDithered(RampBlocks, ox + localX, oy + localY, b01);

            return PlanetDrawer.SunGlyph(localX, localY, ox, oy, b01);
        }

        private static readonly int[] _bayer4 =
        {
             0,  8,  2, 10,
            12,  4, 14,  6,
             3, 11,  1,  9,
            15,  7, 13,  5
        };

        private static char RampCharDithered(string ramp, int x, int y, double brightness01)
        {
            if (string.IsNullOrEmpty(ramp)) return '#';
            brightness01 = MathUtil.Clamp(brightness01, 0.0, 1.0);

            double t = brightness01 * (ramp.Length - 1);
            int baseIdx = (int)Math.Floor(t);
            double frac = t - baseIdx;

            if (baseIdx < 0) baseIdx = 0;
            if (baseIdx >= ramp.Length - 1) return ramp[ramp.Length - 1];

            double thresh = _bayer4[(x & 3) + ((y & 3) << 2)] / 16.0;
            if (frac > thresh) baseIdx++;

            return ramp[baseIdx];
        }

        private void EnterInterior()
        {
            int shipIndex = -1;

            if (_armedShipIndex >= 0 && _armedShipIndex < _sys.Ships.Count)
                shipIndex = _armedShipIndex;
            else
            {
                var sel = GetSelection();
                if (sel != null && sel.Kind == EntityKind.Ship && sel.Index >= 0 && sel.Index < _sys.Ships.Count)
                    shipIndex = sel.Index;
                else if (_sys.Ships.Count > 0)
                    shipIndex = 0;
            }

            if (shipIndex < 0 || shipIndex >= _sys.Ships.Count)
            {
                Flash("No ship available to enter (spawn one with Y)");
                return;
            }

            var ship = _sys.Ships[shipIndex];
            if (ship == null)
            {
                Flash("Ship was null (unexpected)");
                return;
            }

            if (!_interiorByShip.TryGetValue(ship.Name, out var session) || session == null)
            {
                int seed = _sys.Seed ^ shipIndex * 1337;
                session = ShipInteriorFactory.Create(seed, ship.Name, w: 46, h: 18);
                _interiorByShip[ship.Name] = session;
            }

            _activeInterior = session;
            _interiorView = true;

            _events.Add(_simTime, $"Entered interior: {ship.Name}");
            Flash($"Interior: {ship.Name} (F6 to exit)");
        }

        private void ExitInterior()
        {
            if (_interiorView)
                _events.Add(_simTime, $"Exited interior");

            _interiorView = false;
            _activeInterior = null;
        }

        private void UpdateInteriorControls(EngineContext ctx)
        {
            if (_activeInterior == null) return;

            int dx = 0, dy = 0;

            if (ctx.Input.WasPressed(ConsoleKey.W) || ctx.Input.WasPressed(ConsoleKey.UpArrow)) dy -= 1;
            if (ctx.Input.WasPressed(ConsoleKey.S) || ctx.Input.WasPressed(ConsoleKey.DownArrow)) dy += 1;
            if (ctx.Input.WasPressed(ConsoleKey.A) || ctx.Input.WasPressed(ConsoleKey.LeftArrow)) dx -= 1;
            if (ctx.Input.WasPressed(ConsoleKey.D) || ctx.Input.WasPressed(ConsoleKey.RightArrow)) dx += 1;

            if (dx == 0 && dy == 0) return;

            int nx = _activeInterior.PlayerX + dx;
            int ny = _activeInterior.PlayerY + dy;

            if (_activeInterior.Map.IsWalkable(nx, ny))
            {
                _activeInterior.PlayerX = nx;
                _activeInterior.PlayerY = ny;
            }
        }

        private void Flash(string msg)
        {
            _msg = msg ?? "";
            _msgTimer = 1.6;
        }

        // =========================
        // Selection item + kind
        // =========================
        private enum EntityKind
        {
            Sun,
            Planet,
            Moon,
            Station,
            Ship,

            Asteroid,
            Nebula
        }

        private sealed class SelectionItem
        {
            public EntityKind Kind;
            public int Index;
            public int SubIndex;
            public string Label;

            public double WX;
            public double WY;
            public double WZ;
        }
    }
}
