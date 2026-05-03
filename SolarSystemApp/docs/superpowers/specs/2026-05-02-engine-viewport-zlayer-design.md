# Spec 1: Engine Fixes, Z-Layer, and Viewport

## Overview

Fix three foundational rendering issues: UI z-layer occlusion, canvas size clamping, and sub-pixel jitter at low time scales. These changes unblock all subsequent UI work.

## 1. Z-Layer UI Occlusion Fix

### Problem

The current `BlitText()` method in `SolarSystemScene.cs` (~line 1277) stamps Spectre-rendered text character-by-character at `RenderZ.UI_BORDER` (z=0). However, planets use `PLANET_SURFACE = -0.5` and rings use `RINGS = -0.55`, which are *lower* (closer) than the UI z-value. World content renders in front of UI panels.

### Solution

- Add new z-layer constants in `RenderZ`:
  - `UI_BG = -10.0` — opaque background fill for UI panel regions
  - `UI_TEXT = -11.0` — text content on top of panel backgrounds
- Modify `BlitText()` (or add a wrapper) to first fill the entire panel rectangle with space characters at `UI_BG` z-level using the panel's background color. This creates an opaque mask.
- Then blit the Spectre text at `UI_TEXT`.
- Update all `BlitText()` call sites to use the new z constants instead of `RenderZ.UI_BORDER`.

### Files Changed

- `Rendering/RenderZ.cs` — Add `UI_BG` and `UI_TEXT` constants
- `SolarSystemScene.cs` — Update `BlitText()` to fill background, update call sites

### Notes

- `Core/ZLayers.cs` also has a `UI = 0` constant. This file appears to be a parallel/older set of z-values. Both files should be kept consistent, but `RenderZ` is the one actively used in blit calls. Consider consolidating to one file in a future cleanup, but not in this spec's scope.

## 2. Canvas Size Clamp Relaxation

### Problem

`AsciiRunner.cs` line 32-33 clamps canvas dimensions to 160×60:
```csharp
int w = Math.Max(20, Math.Min(160, ww));
int h = Math.Max(10, Math.Min(60, wh));
```

`TerminalSession.BeginFrame()` line 99-100 uses a wider clamp of 200×80 for resize events. The initial creation uses the tighter clamp, so the canvas starts narrow even on wide terminals.

### Solution

- In `AsciiRunner.cs`, raise the initial clamp to match or exceed TerminalSession:
  - Width: `Math.Min(320, ww)` (supports ultrawide terminals)
  - Height: `Math.Min(100, wh)`
- In `TerminalSession.BeginFrame()`, match the same maximums: 320×100.
- Minimum stays at 20×10 (unchanged).

### Files Changed

- `AsciiEngine/AsciiRunner.cs` — Update initial clamp values
- `AsciiEngine/TerminalSession.cs` — Update resize clamp values

### Performance Note

`ConsoleRenderer` allocates per-cell buffers. At 320×100 that's 32,000 cells × 2 buffers (front/back) with color data — still negligible memory. The dirty-region presentation system means only changed cells are written to the console, so wider canvases don't proportionally increase render cost.

## 3. Viewport Dynamic Sizing

### Problem

Currently the viewport (where world content renders) doesn't account for UI panel regions. World content renders across the full canvas, and panels blit on top. With the z-fix above, panels will properly occlude, but rendering world content behind panels wastes cycles and the viewport center won't be correctly positioned.

### Solution

- Track a "viewport rectangle" that represents the world-rendering area: the canvas minus any visible edge-anchored panel regions.
- `Camera2D.CenterX` and `CenterY` should be set to the center of this viewport, not the center of the full canvas.
- World rendering (orbits, planets, labels, starfield, etc.) should use the viewport bounds for culling and positioning.
- This will be fully implemented as part of Spec 2 (UI Framework) since the `UIManager` will own panel layout. In this spec, we just ensure the engine doesn't block it — the canvas size changes are sufficient.

### No Additional Files Changed

This is an architectural note for Spec 2. No implementation in this spec beyond the canvas clamp fix.

## 4. Sub-Pixel Jitter Fix

### Problem

At low time scales, objects move slowly and their world-to-screen position hovers on cell boundaries. `Math.Round()` in `Camera2D.WorldToScreenX/Y()` causes objects to flicker between adjacent cells frame-to-frame, especially on curved/diagonal paths.

### Solution

- Change `Camera2D.WorldToScreenX()` and `WorldToScreenY()` to use `Math.Floor()` instead of `Math.Round()`. This biases consistently in one direction, eliminating the round-up/round-down flicker at boundaries.
- If `Floor` alone isn't sufficient (objects may shift by 1 char vs. their "expected" position), add a half-cell offset before flooring:
  ```csharp
  => CenterX + (int)Math.Floor((wx - CamWX) * WorldToScreen + 0.5);
  ```
  This is equivalent to rounding but uses floor internally, which behaves more predictably when values land exactly on 0.5 boundaries due to IEEE 754 behavior.
- If jitter persists, add position hysteresis: cache each rendered object's last screen position and only update it when the new position differs by more than 0.6 cells. This prevents oscillation at boundaries. Implement this in the rendering loop, not in Camera2D (the camera should report the true position; hysteresis is a rendering concern).

### Files Changed

- `Rendering/Camera2D.cs` — Update `WorldToScreenX()` and `WorldToScreenY()`
- Possibly `SolarSystemScene.cs` — Add hysteresis cache if needed (deferred until testing)

## 5. Time Step Default

### Change

In `SolarSystemScene.cs` line 57:
```csharp
// Current:
private double _timeScale = 1.0;

// New:
private double _timeScale = 0.25;
```

The existing speed controls (`OemPlus`/`OemMinus` cycling by 1.25× factor) and clamp range (0.05–10.0) remain unchanged.

**Note:** This hardcoded default is superseded by `GameSettings.DefaultTimeScale` once Spec 3 (Settings System) is implemented. This change provides the correct default immediately, before the settings system exists.

## Implementation Order

1. Canvas clamp relaxation (engine changes)
2. Z-layer constants + `BlitText()` background fill
3. Time scale default change
4. Jitter fix in Camera2D
5. Test all together — verify panels occlude, wide terminals work, slow time is smooth
