# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

This is a .NET 10.0 console app in a multi-project solution (`../AsciiSuite.slnx`).

```powershell
# Build
dotnet build

# Run (30 FPS ASCII solar system explorer)
dotnet run

# Build the full solution (includes AsciiEngine + CircleApp)
dotnet build ../AsciiSuite.slnx
```

No test projects or linter configurations exist currently.

## Architecture

### Engine Layer (AsciiEngine — sibling project)

SolarSystemApp is built on a custom ASCII game engine at `../AsciiEngine/`. The engine provides:

- **`AsciiRunner.Run(IAsciiApp, fpsCap)`** — Main loop with frame timing, resize detection, input polling
- **`IAsciiApp`** — Scene interface: `Init(ctx)`, `Update(ctx)`, `Draw(ctx)`. Init is re-called on terminal resize.
- **`EngineContext`** — Frame context with `Renderer`, `Input`, `DeltaTime`, `Time`, `Width`, `Height`
- **`ConsoleRenderer`** — Z-buffered character drawing. All draw calls take a `z` depth parameter (lower z = closer). `Present()` flushes dirty regions only.
- **`InputState`** — Per-frame input: `WasPressed()`, `IsDown()`, `GetDirectional()`. Escape auto-exits.
- **`Color`** — RGB-first (`Color.FromRgb(r,g,b)`). Pre-defined constants in `Colors` static class.

### Application Structure

**Entry:** `Program.cs` → `AsciiRunner.Run(new SolarSystemScene(), fpsCap: 30)`

**`SolarSystemScene.cs` (~2900 lines)** — The main scene implementing `IAsciiApp`. Contains the game loop, state management, camera control, input handling, and all view-mode rendering. This is the central file; most features flow through it.

**Key subsystems:**

- **World/** — Data model and procedural generation. `Galaxy.cs` generates seed-based graph of star systems. `StarSystem.cs` builds each system by kind (10 types: StarSystem, AsteroidField, Nebula, BlackHole, etc.) via `BuildByKind()`. `Entities.cs` defines all world objects (Planet, Moon, Ship, Station). `OrbitMath.cs` handles Kepler orbit calculations.

- **Render/ + Rendering/** — Two rendering folders (legacy and modern). `SolarRender.cs` has static helpers for orbits/suns/trails. `Camera2D.cs` handles world-to-screen transforms with smooth zoom/pan and orbit Y-scale squash. `RenderZ.cs` and `ZLayers.cs` define depth ordering constants.

- **PlanetDrawer.cs / PlanetTextures.cs** — Planet rendering with 16 texture types, ASCII ramp shading, atmosphere halos, and a palette system (4 color variants per texture with Dark/Mid/Light RGB values).

- **Persistence/** — JSON save/load via `SaveManager.cs`. Saves galaxy seed, camera state, simulation time, and per-system entity state to `savegame.json`.

- **Interiors/** — Procedural ship interior generation. `ShipInteriorFactory.cs` stamps 7×7 room prefabs. `InteriorMap.cs` is a tile grid (VOID/WALL/FLOOR/DOOR/WINDOW) with walkability checks.

- **Gameplay/** — `ShipJobs.cs` for ship task/action system.

- **Util/** — `HashNoise.cs` (deterministic noise, FBm), `MathUtil.cs` (clamp, lerp, wrap, ramp).

### Key Design Patterns

- **Deterministic procedural generation** — Everything seeds from galaxy seed + index offset. Same seed = identical universe. Uses hash-based RNG, not `System.Random`.
- **Fixed-step simulation** — Physics runs at 60 Hz via accumulator pattern in Update, independent of render framerate. TimeScale multiplier for fast-forward.
- **Z-layered rendering** — All draw calls include z-depth. Negative z for close-up views (planet surfaces).
- **Camera smoothing** — Exponential lerp with separate pan/zoom responsiveness parameters.

### Dependencies

- **Spectre.Console** 0.54.0 — Console rendering enhancements
- **AsciiEngine** (project reference) — Custom ASCII game engine with no external dependencies
