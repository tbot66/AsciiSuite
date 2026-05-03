# Spec 3: Settings System

## Overview

A persistent settings system with categories for Display, Camera, Simulation, and Theme. Settings are edited through a `SettingsPanel` (modal overlay) and persisted to `settings.json`. The time step default change (0.25x) is part of this spec.

## Settings Data Model

### GameSettings Class

Located in `UI/Settings/GameSettings.cs`. Plain C# class with properties, serializable to JSON.

```csharp
class GameSettings
{
    // Display
    bool ShowOrbits = true;
    bool ShowLabels = true;
    bool ShowStarfield = true;
    bool ShowBelts = true;
    bool ShowRings = true;
    bool ShowDebris = true;
    bool EnableBloom = true;
    bool EnableLensFlare = true;

    // Camera
    CameraSpeed ZoomSpeed = CameraSpeed.Medium;      // enum: Slow, Medium, Fast
    CameraSpeed PanSpeed = CameraSpeed.Medium;
    SmoothingLevel CameraSmoothing = SmoothingLevel.Medium;  // enum: Low, Medium, High
    double OrbitYScale = 0.55;                        // preset values: 0.3, 0.45, 0.55, 0.7, 0.85

    // Simulation
    double DefaultTimeScale = 0.25;
    bool PauseOnSystemEntry = false;

    // Theme
    ThemePreset ColorTheme = ThemePreset.Classic;     // enum: Classic, Monochrome, HighContrast, CoolBlue
    BorderStyle UIBorderStyle = BorderStyle.Rounded;  // enum: Single, Double, Rounded, Heavy
}
```

### Enum Definitions

```csharp
enum CameraSpeed { Slow, Medium, Fast }
enum SmoothingLevel { Low, Medium, High }
enum ThemePreset { Classic, Monochrome, HighContrast, CoolBlue }
enum BorderStyle { Single, Double, Rounded, Heavy }
```

### Mapping Enums to Values

Each enum maps to concrete values used by the engine:

| Setting | Slow/Low | Medium | Fast/High |
|---------|----------|--------|-----------|
| ZoomSpeed | `ZoomResponsiveness = 8.0` | `18.0` (current) | `30.0` |
| PanSpeed | `PanResponsiveness = 6.0` | `14.0` (current) | `24.0` |
| CameraSmoothing | `SmoothEnabled = true, responsiveness × 2.0` | `current values` | `responsiveness × 0.5` |

OrbitYScale presets: `[0.3, 0.45, 0.55, 0.7, 0.85]` — cycled with left/right in settings.

## Persistence

### File: `settings.json`

- Location: same directory as `savegame.json` (current working directory)
- Format: JSON via `System.Text.Json`
- Loaded on startup in `SolarSystemScene.Init()`. If the file doesn't exist, defaults are used and the file is created on first save.
- Saved whenever a setting changes (immediate write-through) and on game exit.

### SettingsManager Class

Located in `UI/Settings/SettingsManager.cs`.

```
SettingsManager
├── Current: GameSettings           // active settings
├── Load() → GameSettings           // read from settings.json, return defaults on failure
├── Save(GameSettings)              // write to settings.json
├── ApplyTo(SolarSystemScene)       // push settings values into scene fields
```

`ApplyTo()` maps `GameSettings` properties to the scene's existing fields: toggle booleans (`_showOrbits`, `_showBelts`, etc.), camera responsiveness values, time scale default, etc.

## SettingsPanel

### UI Layout

Modal overlay, centered on screen. Sized to ~50 columns × 20 rows (or proportional to screen).

```
╔══════════[ SETTINGS ]══════════╗
║                                ║
║  ◄ Display ►  Camera  Sim  Theme
║  ─────────────────────────────  ║
║  [x] Orbit lines               ║
║  [x] Labels                    ║
║  [x] Starfield                 ║
║  [x] Asteroid belts            ║
║  [x] Rings                     ║
║  [x] Debris                    ║
║  [x] Bloom effect              ║
║  [ ] Lens flare                ║
║                                ║
║  ───────────────────────────── ║
║  ↑↓ Navigate  Enter Toggle     ║
║  ←→ Category  Esc Close        ║
╚════════════════════════════════╝
```

### Navigation

- **Left/Right arrows:** Switch between categories (Display, Camera, Simulation, Theme)
- **Up/Down arrows:** Navigate options within a category
- **Enter:** Toggle boolean values, cycle enum values, or step through preset values
- **Left/Right on a value row:** For non-boolean values (enums, presets), cycle through options
- **Esc:** Close settings panel, save to disk

### Category Screens

**Display:**
- Toggle list: Orbits, Labels, Starfield, Belts, Rings, Debris, Bloom, Lens Flare
- Each row: `[x]` or `[ ]` + label

**Camera:**
- Zoom Speed: `◄ Medium ►`
- Pan Speed: `◄ Medium ►`
- Smoothing: `◄ Medium ►`
- Orbit Squash: `◄ 0.55 ►`

**Simulation:**
- Default Time Scale: `◄ 0.25x ►` (presets: 0.25x, 0.5x, 1x, 2x, 4x)
- Pause on System Entry: `[x]` / `[ ]`

**Theme:**
- Color Palette: `◄ Classic ►`
- Border Style: `◄ Rounded ►`

### Spectre Rendering

Each category builds a Spectre `Grid` or `Table` widget. The selected row is highlighted with `[bold yellow]` markup. Category tabs use `Markup` with the active tab highlighted.

## Integration with Existing Code

### Scene Fields That Become Settings-Driven

These fields in `SolarSystemScene` currently exist as hardcoded booleans or values. They become backed by `GameSettings`:

- `_showOrbits`, `_showBelts`, `_showRings`, `_showLabels`, `_showStarfield`, `_showDebris` — Display toggles
- `_enableBloom`, `_enableFlare` — FX toggles  
- `_timeScale` default (initial value) — Simulation
- `_cam.PanResponsiveness`, `_cam.ZoomResponsiveness`, `_cam.OrbitYScale` — Camera
- `_cam.SmoothEnabled` — Camera smoothing

### Existing Hotkey Toggles

The scene has existing hotkeys for toggling orbits, labels, etc. These should continue to work and update the `GameSettings` in memory (but not auto-save — only the settings panel save triggers persistence). This keeps hotkey toggling fast and avoids disk writes every keypress.

### Theme Application

`ThemePreset` maps to a set of colors used by UI panels (border color, header color, text color, highlight color). The panel base class reads from `GameSettings.ColorTheme` when choosing Spectre markup colors.

`BorderStyle` maps to Spectre's `BoxBorder` variants: `BoxBorder.Ascii`, `BoxBorder.Double`, `BoxBorder.Rounded`, `BoxBorder.Heavy`.

## New Files

```
UI/Settings/
├── GameSettings.cs         // data model
├── SettingsManager.cs      // load/save/apply
├── SettingsPanel.cs        // modal UI panel (registered with UIManager)
```

## Implementation Order

1. `GameSettings` data model with defaults
2. `SettingsManager` with JSON load/save
3. `SettingsPanel` UI — Display category first
4. Wire `ApplyTo()` to push settings into scene fields
5. Add remaining categories (Camera, Simulation, Theme)
6. Set default time scale to 0.25x in `GameSettings`
7. Hook up existing toggle hotkeys to update `GameSettings` in memory
8. Test persistence: change settings, restart, verify they load
