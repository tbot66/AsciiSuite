# Spec 2: UI Framework and Panels

## Overview

Build a panel-based UI framework in `SolarSystemApp/UI/` that manages screen layout, input routing, and Spectre-rendered panel content. Replace the ad-hoc Spectre blit code in `SolarSystemScene` with a structured panel system designed for fleet management.

## Visual Style

DOS/sci-fi console hybrid: bordered panels using ASCII box-drawing characters (`╔═╗║╚╝` or `┌─┐│└┘`), labeled headers like `[ NAVIGATION ]`, `[ SCAN DATA ]`, color-coded readouts. Spectre.Console's `Panel`, `Grid`, `Table`, and `Markup` widgets handle this naturally.

## Architecture

### UIManager

Central coordinator class. Responsibilities:

- Owns a list of `UIPanel` instances
- On each frame: recalculates layout if needed, routes input, triggers panel rendering
- Exposes `ViewportRect` (the screen region available for world rendering after subtracting visible panels)
- Lives in `UI/UIManager.cs`

```
UIManager
├── Panels: List<UIPanel>
├── ModalStack: Stack<UIPanel>      // settings, command menus
├── ViewportRect: (x, y, w, h)     // computed from visible panels
│
├── Layout(screenW, screenH)        // recalculate panel positions
├── HandleInput(InputState) → bool  // returns true if UI consumed the key
├── Render(ConsoleRenderer)         // blit all visible panels
```

### UIPanel (Base Class)

```
UIPanel
├── Bounds: (x, y, w, h)           // screen region
├── Visible: bool
├── Modal: bool                     // captures all input when active
├── Anchor: Edge enum               // Top, Bottom, Left, Right, Center
├── ZLayer: double                  // defaults to RenderZ.UI_BG
│
├── HandleInput(InputState) → bool  // returns true if key consumed
├── BuildContent(ctx) → IRenderable // returns Spectre widget to render
├── OnShow() / OnHide()             // lifecycle hooks
```

### Input Routing

Priority order each frame:

1. If `ModalStack` is non-empty, top modal gets all input. If it consumes the key, stop. If the modal closes itself (e.g., Esc), pop it and continue.
2. Check global toggle keys against all panels (e.g., F1 toggles FleetList, F10 toggles Settings). These are registered with UIManager, not hardcoded.
3. Check non-modal visible panels in front-to-back order.
4. If no panel consumed the input, pass through to `SolarSystemScene` for camera/selection/game controls.

The scene's `Update()` method calls `_uiManager.HandleInput(ctx.Input)` first. If it returns `true`, the scene skips its own input processing for that frame.

### Layout Calculation

UIManager computes panel positions based on screen size and which panels are visible:

```
┌──────────────────────────────────────────────────┐
│  HeaderPanel (Top, full width, ~3 rows)          │
├────────┬────────────────────────────┬────────────┤
│ Fleet  │                            │   Info     │
│ List   │      VIEWPORT              │   Panel    │
│ (Left  │   (world rendering)        │  (Right    │
│  ~20   │                            │   ~30      │
│  cols) │                            │   cols)    │
│ toggle │                            │ on-select  │
├────────┴────────────────────────────┴────────────┤
│  EventLogPanel (Bottom, full width, ~5 rows)     │
└──────────────────────────────────────────────────┘
```

- Edge-anchored panels have fixed or proportional sizes (e.g., InfoPanel = `min(30, screenW/3)`)
- Viewport = remaining space after subtracting all visible edge panels
- When a toggleable panel hides, the viewport expands to fill its space
- Layout recalculates on: terminal resize, panel show/hide

### Rendering

Each frame in `Draw()`:

1. Scene renders world content within `ViewportRect` bounds
2. `_uiManager.Render(renderer)` iterates visible panels:
   a. Fill panel's `Bounds` rectangle at `RenderZ.UI_BG` with opaque background
   b. Call `panel.BuildContent(ctx)` to get Spectre `IRenderable`
   c. Render to string via `RenderSpectreToString(renderable, panel.Bounds.w)`
   d. Blit text at `RenderZ.UI_TEXT`
3. Spectre rendering is throttled to 10Hz per panel (existing `SpectreUiEvery` pattern)

## Panel Specifications

### HeaderPanel

- **Anchor:** Top, full width, 3 rows
- **Always visible**
- **Content:** System name, system type, simulation time, time scale, pause state, zoom level
- **Replaces:** Current `BuildSpectreHeader()` and its blit code
- **Style:** Single `Panel` with `Markup` content, rounded border, `[ CONTROLS ]` header

### InfoPanel

- **Anchor:** Right, 30 columns (or `min(30, screenW/3)`), variable height
- **Visible when:** An object is selected
- **Content:** Selected object details — type, name, world position, orbital parameters, radius, texture, rings, etc.
- **Replaces:** Current `BuildSpectreInfo()` and its blit code
- **Style:** `Panel` with `Grid` content, `[ INFO ]` header
- **Future:** Will expand to show fleet orders, cargo, status for ships/stations

### EventLogPanel

- **Anchor:** Bottom, full width, 5 rows
- **Always visible**
- **Content:** Most recent events from `EventLog`, newest at bottom
- **Replaces:** Current inline event rendering (~line 2470)
- **Style:** `Panel` with stacked `Text` lines, `[ LOG ]` header, dim timestamps

### FleetListPanel

- **Anchor:** Left, 20 columns, fills vertical space between header and event log
- **Toggled:** F1 key
- **Content (initial/placeholder):** List of ships and stations in current system, with name and status. Selected item highlighted.
- **Navigation:** Up/Down arrows to scroll when panel is focused, Enter to select/focus on object
- **Style:** `Table` with Name and Status columns, `[ FLEET ]` header
- **Future:** Ship groups, order queues, fleet-wide commands

### CommandMenuPanel

- **Anchor:** Center (overlay), sized to content
- **Modal:** Yes — captures all input while open
- **Trigger:** Context key (e.g., Space or Enter) when a selectable object is focused
- **Content:** Cascading key menus:
  - Level 1: Top-level commands (e.g., `[M] Move  [D] Dock  [P] Patrol  [I] Inspect  [Esc] Cancel`)
  - Level 2: Sub-options depending on command (e.g., Move → list of destinations)
  - Level 3+: Further refinement as needed
- **Behavior:** Each keypress either navigates deeper or executes the command and closes the menu. Esc goes back one level (or closes if at top).
- **Style:** `Panel` with vertical list of `Markup` options, `[ COMMAND ]` header, highlighted current selection

### SettingsPanel

- Defined in Spec 3. UIManager treats it as another modal panel.

## Integration with SolarSystemScene

### What Moves Out

- `_spectreHeaderCache`, `_spectreInfoCache`, `_spectreUiTimer` fields → managed by UIManager/panels
- `BuildSpectreHeader()`, `BuildSpectreInfo()` methods → become panel `BuildContent()` implementations
- `BlitText()` → moves to UIManager's render method (shared utility)
- `RenderSpectreToString()` → shared utility in `UI/` folder
- Event log rendering code → EventLogPanel

### What Stays

- Game state (`_simTime`, `_timeScale`, `_paused`, `_galaxy`, etc.) stays in the scene
- Camera control and world rendering stay in the scene
- Selection logic stays in the scene

### Data Flow

Panels need access to game state to render their content. Options:

- Panels receive a reference to `SolarSystemScene` (or a read-only interface exposing the data they need)
- A lightweight `GameState` struct/record passed each frame with current values

**Recommended:** Define an `IGameContext` interface that `SolarSystemScene` implements, exposing read-only properties panels need (selected object, system data, sim time, time scale, camera state, events, fleet data). Panels receive this in `BuildContent()`. This keeps panels decoupled from the scene's internals.

## New Files

```
UI/
├── UIManager.cs
├── UIPanel.cs           (base class)
├── SpectreBlitter.cs    (RenderSpectreToString + BlitText utilities)
├── IGameContext.cs      (interface for panel data access)
├── Panels/
│   ├── HeaderPanel.cs
│   ├── InfoPanel.cs
│   ├── EventLogPanel.cs
│   ├── FleetListPanel.cs
│   └── CommandMenuPanel.cs
```

## Implementation Order

1. `UIPanel` base class + `SpectreBlitter` utilities
2. `IGameContext` interface
3. `UIManager` with layout and input routing
4. `HeaderPanel` — migrate `BuildSpectreHeader()` 
5. `InfoPanel` — migrate `BuildSpectreInfo()`
6. `EventLogPanel` — migrate event log rendering
7. `FleetListPanel` — new, placeholder content
8. `CommandMenuPanel` — new, cascading key menu system
9. Wire UIManager into `SolarSystemScene.Init/Update/Draw`, remove old blit code
