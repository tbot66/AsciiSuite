using AsciiEngine;
using SolarSystemApp.Gameplay;
using SolarSystemApp.Interiors;
using SolarSystemApp.Persistence;
using SolarSystemApp.Util;
using System;

namespace SolarSystemApp
{
    public sealed partial class SolarSystemScene
    {
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

        private void UpdateInteriorControls(EngineContext ctx)
        {
            if (_activeInterior == null) return;

            if (_activeInterior.CurrentInteractionMode != InteriorSession.InteractionMode.None)
            {
                if (ctx.Input.WasPressed(ConsoleKey.Escape) || ctx.Input.WasPressed(ConsoleKey.Backspace) || ctx.Input.WasPressed(ConsoleKey.E))
                {
                    _activeInterior.CancelInteraction();
                    return;
                }

                if (_activeInterior.CurrentInteractionMode == InteriorSession.InteractionMode.Picker)
                {
                    int pick = GetNumericKeyPress(ctx);
                    if (pick >= 0 && _activeInterior.TrySelectInteraction(pick))
                        return;
                }
                else if (_activeInterior.CurrentInteractionMode == InteriorSession.InteractionMode.SleepScreen)
                {
                    if (ctx.Input.WasPressed(ConsoleKey.LeftArrow) || ctx.Input.WasPressed(ConsoleKey.A))
                        _activeInterior.AdjustSleepSelection(-1);
                    if (ctx.Input.WasPressed(ConsoleKey.RightArrow) || ctx.Input.WasPressed(ConsoleKey.D))
                        _activeInterior.AdjustSleepSelection(1);
                    if (ctx.Input.WasPressed(ConsoleKey.UpArrow) || ctx.Input.WasPressed(ConsoleKey.W))
                        _activeInterior.AdjustSleepSelection(4);
                    if (ctx.Input.WasPressed(ConsoleKey.DownArrow) || ctx.Input.WasPressed(ConsoleKey.S))
                        _activeInterior.AdjustSleepSelection(-4);

                    if (ctx.Input.WasPressed(ConsoleKey.Enter))
                    {
                        _activeInterior.StartSleep();
                        if (!_activeInterior.IsSleeping && _activeInterior.SleepSelectedHours == 0)
                            Flash("Wait time set to 0 hours.");
                    }

                    if (_activeInterior.IsSleeping)
                    {
                        bool finished = _activeInterior.TickSleep(ctx.DeltaTime / 3600.0);
                        if (finished)
                            Flash("Rest complete.");
                    }
                }

                return;
            }

            if (ctx.Input.WasPressed(ConsoleKey.E))
            {
                if (!_activeInterior.BeginInteractionPicker(radius: 2))
                    Flash("No interactables within 2 tiles.");
                return;
            }

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

        private int GetNumericKeyPress(EngineContext ctx)
        {
            if (ctx.Input.WasPressed(ConsoleKey.D1) || ctx.Input.WasPressed(ConsoleKey.NumPad1)) return 0;
            if (ctx.Input.WasPressed(ConsoleKey.D2) || ctx.Input.WasPressed(ConsoleKey.NumPad2)) return 1;
            if (ctx.Input.WasPressed(ConsoleKey.D3) || ctx.Input.WasPressed(ConsoleKey.NumPad3)) return 2;
            if (ctx.Input.WasPressed(ConsoleKey.D4) || ctx.Input.WasPressed(ConsoleKey.NumPad4)) return 3;
            if (ctx.Input.WasPressed(ConsoleKey.D5) || ctx.Input.WasPressed(ConsoleKey.NumPad5)) return 4;
            if (ctx.Input.WasPressed(ConsoleKey.D6) || ctx.Input.WasPressed(ConsoleKey.NumPad6)) return 5;
            if (ctx.Input.WasPressed(ConsoleKey.D7) || ctx.Input.WasPressed(ConsoleKey.NumPad7)) return 6;
            if (ctx.Input.WasPressed(ConsoleKey.D8) || ctx.Input.WasPressed(ConsoleKey.NumPad8)) return 7;
            if (ctx.Input.WasPressed(ConsoleKey.D9) || ctx.Input.WasPressed(ConsoleKey.NumPad9)) return 8;

            return -1;
        }
    }
}
