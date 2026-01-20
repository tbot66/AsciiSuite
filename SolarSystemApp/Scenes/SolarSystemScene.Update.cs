using AsciiEngine;
using SolarSystemApp.Gameplay;
using SolarSystemApp.Persistence;
using SolarSystemApp.Rendering;
using SolarSystemApp.Util;
using System;

namespace SolarSystemApp
{
    /// <summary>
    /// Update-loop helpers for <see cref="SolarSystemScene"/>.
    /// Split into a partial to keep the main scene file focused on state and rendering.
    /// </summary>
    public sealed partial class SolarSystemScene
    {
        // =========================
        // Update
        // =========================
        /// <summary>
        /// Per-frame update entry point for the scene. This orchestrates input handling,
        /// simulation stepping, and camera follow updates in a consistent order.
        /// </summary>
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

        /// <summary>
        /// Counts down transient UI message timers and clears the message when expired.
        /// </summary>
        private void UpdateMessageTimer(double dt)
        {
            if (_msgTimer <= 0) return;
            _msgTimer -= dt;
            if (_msgTimer <= 0) _msg = "";
        }

        /// <summary>
        /// Handles save/load hotkeys and applies save data into world + camera state.
        /// </summary>
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

        /// <summary>
        /// Handles post-processing toggles (FX enable + lens flare).
        /// </summary>
        private void HandleFxToggles(EngineContext ctx)
        {
            if (ctx.Input.WasPressed(ConsoleKey.F2)) _fxEnabled = !_fxEnabled;
            if (ctx.Input.WasPressed(ConsoleKey.F3)) _fxLensFlare = !_fxLensFlare;
        }

        /// <summary>
        /// Manages entering and exiting the interior view and its input handling.
        /// </summary>
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

        /// <summary>
        /// Toggles galaxy view and updates camera to the current system location.
        /// </summary>
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

        /// <summary>
        /// Toggles fast panning in system/galaxy camera controls.
        /// </summary>
        private void HandleFastPanToggle(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.P)) return;

            _fastPan = !_fastPan;
            Flash(_fastPan ? "Fast pan ON" : "Fast pan OFF");
        }

        /// <summary>
        /// Cycles planet shading glyph modes (ramps/solid).
        /// </summary>
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

        /// <summary>
        /// Applies the current glyph mode to the planet renderer.
        /// </summary>
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

        /// <summary>
        /// Switches between planet texture modes (1-3).
        /// </summary>
        private void HandleTextureMode(EngineContext ctx)
        {
            if (ctx.Input.WasPressed(ConsoleKey.D1)) _texMode = 1;
            if (ctx.Input.WasPressed(ConsoleKey.D2)) _texMode = 2;
            if (ctx.Input.WasPressed(ConsoleKey.D3)) _texMode = 3;
        }

        /// <summary>
        /// Toggles system rendering layers such as orbits, belts, labels, and starfield.
        /// </summary>
        private void HandleDisplayToggles(EngineContext ctx)
        {
            if (ctx.Input.WasPressed(ConsoleKey.O)) _showOrbits = !_showOrbits;
            if (ctx.Input.WasPressed(ConsoleKey.B)) _showBelts = !_showBelts;
            if (ctx.Input.WasPressed(ConsoleKey.H)) _showRings = !_showRings;
            if (ctx.Input.WasPressed(ConsoleKey.L)) _showLabels = !_showLabels;
            if (ctx.Input.WasPressed(ConsoleKey.F1)) _showStarfield = !_showStarfield;
        }

        /// <summary>
        /// Toggles between Kepler and circular orbit models.
        /// </summary>
        private void HandleOrbitModelToggle(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.T)) return;

            _useKepler = !_useKepler;
            Flash(_useKepler ? "Orbit model: KEPLER" : "Orbit model: CIRCULAR");
            _events.Add(_simTime, _useKepler ? "Orbit model: KEPLER" : "Orbit model: CIRCULAR");
        }

        /// <summary>
        /// Toggles the simulation pause state (system view only).
        /// </summary>
        private void HandlePauseToggle(EngineContext ctx)
        {
            if (ctx.Input.WasPressed(ConsoleKey.Spacebar) && !_galaxyView && !_interiorView)
                _paused = !_paused;
        }

        /// <summary>
        /// Adjusts simulation time scale using +/-.
        /// </summary>
        private void HandleTimeScale(EngineContext ctx)
        {
            if (ctx.Input.WasPressed(ConsoleKey.OemPlus) || ctx.Input.WasPressed(ConsoleKey.Add))
                _timeScale *= 1.25;
            if (ctx.Input.WasPressed(ConsoleKey.OemMinus) || ctx.Input.WasPressed(ConsoleKey.Subtract))
                _timeScale /= 1.25;
            _timeScale = MathUtil.Clamp(_timeScale, 0.05, 10.0);
        }

        /// <summary>
        /// Applies zoom controls for galaxy and system views.
        /// </summary>
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

        /// <summary>
        /// Adjusts orbit squash for system view only.
        /// </summary>
        private void HandleOrbitSquash(EngineContext ctx)
        {
            if (_galaxyView) return;

            if (ctx.Input.WasPressed(ConsoleKey.I)) _orbitYScale += 0.03;
            if (ctx.Input.WasPressed(ConsoleKey.K)) _orbitYScale -= 0.03;
            _orbitYScale = MathUtil.Clamp(_orbitYScale, 0.20, 1.20);
            _cam.TargetOrbitYScale = _orbitYScale;
        }

        /// <summary>
        /// Handles selection cycling in system view and galaxy selection/jumps when in galaxy view.
        /// Returns true when the galaxy view input consumes the update flow.
        /// </summary>
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

        /// <summary>
        /// Toggles camera-following of the current selection.
        /// </summary>
        private void HandleFollowToggle(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.F)) return;

            _follow = !_follow;
            Flash(_follow ? "Follow ON" : "Follow OFF");
        }

        /// <summary>
        /// Centers the system camera on the sun and disables follow.
        /// </summary>
        private void HandleCameraCenter(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.C)) return;

            _follow = false;
            _cam.TargetCamWX = 0.0;
            _cam.TargetCamWY = 0.0;
            _cam.CamWX = 0.0;
            _cam.CamWY = 0.0; // optional: instant snap instead of easing
        }

        /// <summary>
        /// Spawns a ship near the sun and refreshes selection.
        /// </summary>
        private void HandleSpawnShip(EngineContext ctx)
        {
            if (!ctx.Input.WasPressed(ConsoleKey.Y)) return;

            SpawnShipNearSun();
            RebuildSelection();
            Flash("Spawned ship");
            _events.Add(_simTime, "Spawned ship near sun");
        }

        /// <summary>
        /// Spawns a station around the currently selected planet.
        /// </summary>
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

        /// <summary>
        /// Arms the currently selected ship for issuing orders.
        /// </summary>
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

        /// <summary>
        /// Issues a travel order to the selected target for the armed ship.
        /// </summary>
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

        /// <summary>
        /// Issues an orbit order to the selected target for the armed ship.
        /// </summary>
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

        /// <summary>
        /// Assigns the mining job to the armed ship.
        /// </summary>
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

        /// <summary>
        /// Advances fixed-step simulation timing and updates orbit/ship/debris systems.
        /// </summary>
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

        /// <summary>
        /// Updates camera follow targets and synchronizes legacy camera fields for save compatibility.
        /// </summary>
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
    }
}
