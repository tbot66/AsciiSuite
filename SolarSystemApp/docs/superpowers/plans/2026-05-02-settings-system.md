# Settings System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a persistent settings system with Display, Camera, Simulation, and Theme categories. Settings are edited via a SettingsPanel (modal overlay in the UIManager) and persisted to `settings.json`.

**Architecture:** `GameSettings` data class holds all settings with defaults. `SettingsManager` handles JSON load/save. `SettingsPanel` is a modal UIPanel registered with UIManager. Scene fields that were hardcoded toggles become backed by GameSettings. Existing hotkey toggles continue to work and update in-memory settings.

**Tech Stack:** C# / .NET 10.0, Spectre.Console 0.54.0, System.Text.Json

**Depends on:** Plan 2 (UI Framework & Panels) must be completed first — SettingsPanel is a UIPanel managed by UIManager.

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `UI/Settings/GameSettings.cs` | Create | Data model with all settings and defaults |
| `UI/Settings/SettingsManager.cs` | Create | JSON load/save to settings.json |
| `UI/Settings/SettingsPanel.cs` | Create | Modal settings UI with category tabs |
| `SolarSystemScene.cs` | Modify | Load settings on init, apply to fields, wire SettingsPanel |

---

### Task 1: GameSettings Data Model

**Files:**
- Create: `UI/Settings/GameSettings.cs`

- [ ] **Step 1: Create the Settings directory**

Run:
```powershell
New-Item -ItemType Directory -Path "UI/Settings" -Force
```

- [ ] **Step 2: Write GameSettings.cs**

```csharp
namespace SolarSystemApp.UI.Settings
{
    internal enum CameraSpeed { Slow, Medium, Fast }
    internal enum SmoothingLevel { Low, Medium, High }
    internal enum ThemePreset { Classic, Monochrome, HighContrast, CoolBlue }
    internal enum UIBorderStyle { Single, Double, Rounded, Heavy }

    internal sealed class GameSettings
    {
        // Display
        public bool ShowOrbits { get; set; } = true;
        public bool ShowLabels { get; set; } = true;
        public bool ShowStarfield { get; set; } = true;
        public bool ShowBelts { get; set; } = true;
        public bool ShowRings { get; set; } = true;
        public bool ShowDebris { get; set; } = true;
        public bool EnableBloom { get; set; } = true;
        public bool EnableLensFlare { get; set; } = true;

        // Camera
        public CameraSpeed ZoomSpeed { get; set; } = CameraSpeed.Medium;
        public CameraSpeed PanSpeed { get; set; } = CameraSpeed.Medium;
        public SmoothingLevel CameraSmoothing { get; set; } = SmoothingLevel.Medium;
        public double OrbitYScale { get; set; } = 0.55;

        // Simulation
        public double DefaultTimeScale { get; set; } = 0.25;
        public bool PauseOnSystemEntry { get; set; } = false;

        // Theme
        public ThemePreset ColorTheme { get; set; } = ThemePreset.Classic;
        public UIBorderStyle BorderStyle { get; set; } = UIBorderStyle.Rounded;

        // Enum-to-value mappings
        public static double GetZoomResponsiveness(CameraSpeed speed) => speed switch
        {
            CameraSpeed.Slow => 8.0,
            CameraSpeed.Medium => 18.0,
            CameraSpeed.Fast => 30.0,
            _ => 18.0
        };

        public static double GetPanResponsiveness(CameraSpeed speed) => speed switch
        {
            CameraSpeed.Slow => 6.0,
            CameraSpeed.Medium => 14.0,
            CameraSpeed.Fast => 24.0,
            _ => 14.0
        };

        public static readonly double[] OrbitYScalePresets = { 0.3, 0.45, 0.55, 0.7, 0.85 };
        public static readonly double[] TimeScalePresets = { 0.25, 0.5, 1.0, 2.0, 4.0 };

        public static Spectre.Console.BoxBorder GetBoxBorder(UIBorderStyle style) => style switch
        {
            UIBorderStyle.Single => Spectre.Console.BoxBorder.Square,
            UIBorderStyle.Double => Spectre.Console.BoxBorder.Double,
            UIBorderStyle.Rounded => Spectre.Console.BoxBorder.Rounded,
            UIBorderStyle.Heavy => Spectre.Console.BoxBorder.Heavy,
            _ => Spectre.Console.BoxBorder.Rounded
        };
    }
}
```

- [ ] **Step 3: Build and verify**

Run:
```powershell
dotnet build
```
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```powershell
git add UI/Settings/GameSettings.cs
git commit -m "feat: add GameSettings data model with defaults and enum mappings"
```

---

### Task 2: SettingsManager (Load/Save)

**Files:**
- Create: `UI/Settings/SettingsManager.cs`

- [ ] **Step 1: Write SettingsManager.cs**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolarSystemApp.UI.Settings
{
    internal static class SettingsManager
    {
        private const string FileName = "settings.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static GameSettings Load()
        {
            try
            {
                if (!File.Exists(FileName))
                    return new GameSettings();

                string json = File.ReadAllText(FileName);
                return JsonSerializer.Deserialize<GameSettings>(json, JsonOptions) ?? new GameSettings();
            }
            catch
            {
                return new GameSettings();
            }
        }

        public static bool Save(GameSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(FileName, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

    }
}
```

- [ ] **Step 2: Build and verify**

Run:
```powershell
dotnet build
```
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```powershell
git add UI/Settings/SettingsManager.cs
git commit -m "feat: add SettingsManager with JSON load/save and scene application"
```

---

### Task 3: SettingsPanel UI

**Files:**
- Create: `UI/Settings/SettingsPanel.cs`

- [ ] **Step 1: Write SettingsPanel.cs**

```csharp
using AsciiEngine;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI.Settings
{
    internal sealed class SettingsPanel : UIPanel
    {
        private enum Category { Display, Camera, Simulation, Theme }

        private static readonly Category[] Categories =
            { Category.Display, Category.Camera, Category.Simulation, Category.Theme };

        private int _categoryIndex = 0;
        private int _optionIndex = 0;

        public GameSettings Settings { get; set; } = new GameSettings();
        public Action? OnClose { get; set; }
        public Action<GameSettings>? OnSettingsChanged { get; set; }

        public SettingsPanel()
        {
            Anchor = PanelAnchor.Center;
            RequestedWidth = 50;
            RequestedHeight = 22;
            Modal = true;
            Visible = false;
            ToggleKey = ConsoleKey.F10;
        }

        private Category CurrentCategory => Categories[_categoryIndex];

        private int OptionCount => CurrentCategory switch
        {
            Category.Display => 8,
            Category.Camera => 4,
            Category.Simulation => 2,
            Category.Theme => 2,
            _ => 0
        };

        public override bool HandleInput(InputState input, IGameContext ctx)
        {
            if (input.WasPressed(ConsoleKey.Escape))
            {
                SettingsManager.Save(Settings);
                OnClose?.Invoke();
                return true;
            }

            if (input.WasPressed(ConsoleKey.LeftArrow) && _optionIndex == -1)
            {
                _categoryIndex = (_categoryIndex - 1 + Categories.Length) % Categories.Length;
                _optionIndex = 0;
                return true;
            }
            if (input.WasPressed(ConsoleKey.RightArrow) && _optionIndex == -1)
            {
                _categoryIndex = (_categoryIndex + 1) % Categories.Length;
                _optionIndex = 0;
                return true;
            }

            // Tab key to switch categories from anywhere
            if (input.WasPressed(ConsoleKey.Tab))
            {
                _categoryIndex = (_categoryIndex + 1) % Categories.Length;
                _optionIndex = 0;
                return true;
            }

            int count = OptionCount;
            if (input.WasPressed(ConsoleKey.UpArrow))
            {
                _optionIndex = Math.Max(0, _optionIndex - 1);
                return true;
            }
            if (input.WasPressed(ConsoleKey.DownArrow))
            {
                _optionIndex = Math.Min(count - 1, _optionIndex + 1);
                return true;
            }

            // Enter or Left/Right to change values
            bool enter = input.WasPressed(ConsoleKey.Enter);
            bool left = input.WasPressed(ConsoleKey.LeftArrow);
            bool right = input.WasPressed(ConsoleKey.RightArrow);

            if (enter || left || right)
            {
                int dir = right ? 1 : (left ? -1 : 1);
                ApplyChange(dir);
                OnSettingsChanged?.Invoke(Settings);
                return true;
            }

            return true; // modal
        }

        private void ApplyChange(int dir)
        {
            switch (CurrentCategory)
            {
                case Category.Display:
                    switch (_optionIndex)
                    {
                        case 0: Settings.ShowOrbits = !Settings.ShowOrbits; break;
                        case 1: Settings.ShowLabels = !Settings.ShowLabels; break;
                        case 2: Settings.ShowStarfield = !Settings.ShowStarfield; break;
                        case 3: Settings.ShowBelts = !Settings.ShowBelts; break;
                        case 4: Settings.ShowRings = !Settings.ShowRings; break;
                        case 5: Settings.ShowDebris = !Settings.ShowDebris; break;
                        case 6: Settings.EnableBloom = !Settings.EnableBloom; break;
                        case 7: Settings.EnableLensFlare = !Settings.EnableLensFlare; break;
                    }
                    break;

                case Category.Camera:
                    switch (_optionIndex)
                    {
                        case 0: Settings.ZoomSpeed = CycleEnum(Settings.ZoomSpeed, dir); break;
                        case 1: Settings.PanSpeed = CycleEnum(Settings.PanSpeed, dir); break;
                        case 2: Settings.CameraSmoothing = CycleEnum(Settings.CameraSmoothing, dir); break;
                        case 3:
                            var presets = GameSettings.OrbitYScalePresets;
                            int idx = Array.IndexOf(presets, Settings.OrbitYScale);
                            if (idx < 0) idx = 2;
                            idx = (idx + dir + presets.Length) % presets.Length;
                            Settings.OrbitYScale = presets[idx];
                            break;
                    }
                    break;

                case Category.Simulation:
                    switch (_optionIndex)
                    {
                        case 0:
                            var tsPresets = GameSettings.TimeScalePresets;
                            int tsIdx = Array.IndexOf(tsPresets, Settings.DefaultTimeScale);
                            if (tsIdx < 0) tsIdx = 0;
                            tsIdx = (tsIdx + dir + tsPresets.Length) % tsPresets.Length;
                            Settings.DefaultTimeScale = tsPresets[tsIdx];
                            break;
                        case 1: Settings.PauseOnSystemEntry = !Settings.PauseOnSystemEntry; break;
                    }
                    break;

                case Category.Theme:
                    switch (_optionIndex)
                    {
                        case 0: Settings.ColorTheme = CycleEnum(Settings.ColorTheme, dir); break;
                        case 1: Settings.BorderStyle = CycleEnum(Settings.BorderStyle, dir); break;
                    }
                    break;
            }
        }

        private static T CycleEnum<T>(T current, int dir) where T : struct, Enum
        {
            var values = Enum.GetValues<T>();
            int idx = Array.IndexOf(values, current);
            if (idx < 0) idx = 0;
            idx = (idx + dir + values.Length) % values.Length;
            return values[idx];
        }

        public override IRenderable BuildContent(IGameContext ctx)
        {
            var lines = new List<IRenderable>();

            // Category tabs
            var tabs = new List<string>();
            for (int i = 0; i < Categories.Length; i++)
            {
                string name = Categories[i].ToString();
                tabs.Add(i == _categoryIndex ? $"[bold yellow] {name} [/]" : $"[grey] {name} [/]");
            }
            lines.Add(new Markup(string.Join("  ", tabs)));
            lines.Add(new Rule().RuleStyle(Style.Parse("grey")));

            // Options
            switch (CurrentCategory)
            {
                case Category.Display:
                    AddToggle(lines, 0, "Orbit lines", Settings.ShowOrbits);
                    AddToggle(lines, 1, "Labels", Settings.ShowLabels);
                    AddToggle(lines, 2, "Starfield", Settings.ShowStarfield);
                    AddToggle(lines, 3, "Asteroid belts", Settings.ShowBelts);
                    AddToggle(lines, 4, "Rings", Settings.ShowRings);
                    AddToggle(lines, 5, "Debris", Settings.ShowDebris);
                    AddToggle(lines, 6, "Bloom effect", Settings.EnableBloom);
                    AddToggle(lines, 7, "Lens flare", Settings.EnableLensFlare);
                    break;

                case Category.Camera:
                    AddValue(lines, 0, "Zoom Speed", Settings.ZoomSpeed.ToString());
                    AddValue(lines, 1, "Pan Speed", Settings.PanSpeed.ToString());
                    AddValue(lines, 2, "Smoothing", Settings.CameraSmoothing.ToString());
                    AddValue(lines, 3, "Orbit Squash", $"{Settings.OrbitYScale:0.00}");
                    break;

                case Category.Simulation:
                    AddValue(lines, 0, "Default Time Scale", $"{Settings.DefaultTimeScale}x");
                    AddToggle(lines, 1, "Pause on System Entry", Settings.PauseOnSystemEntry);
                    break;

                case Category.Theme:
                    AddValue(lines, 0, "Color Palette", Settings.ColorTheme.ToString());
                    AddValue(lines, 1, "Border Style", Settings.BorderStyle.ToString());
                    break;
            }

            // Footer
            lines.Add(new Text(""));
            lines.Add(new Rule().RuleStyle(Style.Parse("grey")));
            lines.Add(new Markup("[grey]↑↓ Navigate  Enter/←→ Change  Tab Category  Esc Close[/]"));

            return new Panel(new Rows(lines))
                .Border(GameSettings.GetBoxBorder(Settings.BorderStyle))
                .Header("SETTINGS", Justify.Left);
        }

        private void AddToggle(List<IRenderable> lines, int index, string label, bool value)
        {
            string check = value ? "[green]x[/]" : " ";
            string style = index == _optionIndex ? "[bold yellow]" : "[white]";
            lines.Add(new Markup($"  {style}[{check}] {Markup.Escape(label)}[/]"));
        }

        private void AddValue(List<IRenderable> lines, int index, string label, string value)
        {
            string style = index == _optionIndex ? "[bold yellow]" : "[white]";
            string arrows = index == _optionIndex ? "◄ " : "  ";
            string arrowsR = index == _optionIndex ? " ►" : "  ";
            lines.Add(new Markup($"  {style}{Markup.Escape(label)}: {arrows}{Markup.Escape(value)}{arrowsR}[/]"));
        }

        public override void OnShow()
        {
            _categoryIndex = 0;
            _optionIndex = 0;
        }
    }
}
```

- [ ] **Step 2: Build and verify**

Run:
```powershell
dotnet build
```
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```powershell
git add UI/Settings/SettingsPanel.cs
git commit -m "feat: add SettingsPanel with category tabs, toggles, and value cycling"
```

---

### Task 4: Wire Settings into SolarSystemScene

**Files:**
- Modify: `SolarSystemScene.cs`

- [ ] **Step 1: Add settings field and load on init**

Add a using statement at the top of the file:

```csharp
using SolarSystemApp.UI.Settings;
```

Add a field near the other UI fields:

```csharp
private GameSettings _settings;
```

In the `Init()` method, before UIManager setup, add:

```csharp
if (_settings == null)
{
    _settings = SettingsManager.Load();
    _timeScale = _settings.DefaultTimeScale;
    ApplySettings();
}
```

- [ ] **Step 2: Add ApplySettings helper method**

Add this method to SolarSystemScene:

```csharp
private void ApplySettings()
{
    _showOrbits = _settings.ShowOrbits;
    _showLabels = _settings.ShowLabels;
    _showStarfield = _settings.ShowStarfield;
    _showBelts = _settings.ShowBelts;
    _showRings = _settings.ShowRings;
    _showDebris = _settings.ShowDebris;
    _fxEnabled = _settings.EnableBloom;
    _fxLensFlare = _settings.EnableLensFlare;

    _cam.ZoomResponsiveness = GameSettings.GetZoomResponsiveness(_settings.ZoomSpeed);
    _cam.PanResponsiveness = GameSettings.GetPanResponsiveness(_settings.PanSpeed);
    _cam.TargetOrbitYScale = _settings.OrbitYScale;
}
```

- [ ] **Step 3: Register SettingsPanel with UIManager**

In the `Init()` method, in the UIManager setup block (where panels are added), add the SettingsPanel:

```csharp
var settingsPanel = new SettingsPanel();
settingsPanel.Settings = _settings;
settingsPanel.OnClose = () => _uiManager.PopModal();
settingsPanel.OnSettingsChanged = (s) => ApplySettings();
_uiManager.AddPanel(settingsPanel);
```

- [ ] **Step 4: Update existing hotkey toggles to sync with settings**

In the input handling section of `Update()`, after the existing toggle lines (around lines 462-466, 380-381), update them to also write to `_settings`:

Replace:
```csharp
if (ctx.Input.WasPressed(ConsoleKey.O)) _showOrbits = !_showOrbits;
if (ctx.Input.WasPressed(ConsoleKey.B)) _showBelts = !_showBelts;
if (ctx.Input.WasPressed(ConsoleKey.H)) _showRings = !_showRings;
if (ctx.Input.WasPressed(ConsoleKey.L)) _showLabels = !_showLabels;
if (ctx.Input.WasPressed(ConsoleKey.F1)) _showStarfield = !_showStarfield;
```

with:

```csharp
if (ctx.Input.WasPressed(ConsoleKey.O)) { _showOrbits = !_showOrbits; _settings.ShowOrbits = _showOrbits; }
if (ctx.Input.WasPressed(ConsoleKey.B)) { _showBelts = !_showBelts; _settings.ShowBelts = _showBelts; }
if (ctx.Input.WasPressed(ConsoleKey.H)) { _showRings = !_showRings; _settings.ShowRings = _showRings; }
if (ctx.Input.WasPressed(ConsoleKey.L)) { _showLabels = !_showLabels; _settings.ShowLabels = _showLabels; }
```

Note: F1 is now used for FleetListPanel toggle, so starfield toggle needs a new key. Change it to F4:

```csharp
if (ctx.Input.WasPressed(ConsoleKey.F4)) { _showStarfield = !_showStarfield; _settings.ShowStarfield = _showStarfield; }
```

And for FX toggles:

Replace:
```csharp
if (ctx.Input.WasPressed(ConsoleKey.F2)) _fxEnabled = !_fxEnabled;
if (ctx.Input.WasPressed(ConsoleKey.F3)) _fxLensFlare = !_fxLensFlare;
```

with:

```csharp
if (ctx.Input.WasPressed(ConsoleKey.F2)) { _fxEnabled = !_fxEnabled; _settings.EnableBloom = _fxEnabled; }
if (ctx.Input.WasPressed(ConsoleKey.F3)) { _fxLensFlare = !_fxLensFlare; _settings.EnableLensFlare = _fxLensFlare; }
```

- [ ] **Step 5: Remove the hardcoded `_timeScale = 0.25` default**

Since settings now provide the default time scale, change line 57 back to being settings-driven:

```csharp
private double _timeScale = 1.0; // overwritten by GameSettings.DefaultTimeScale on init
```

The `Init()` method already sets `_timeScale = _settings.DefaultTimeScale` which defaults to 0.25.

- [ ] **Step 6: Build and verify**

Run:
```powershell
dotnet build
```
Expected: Build succeeds.

- [ ] **Step 7: Manual test**

Run:
```powershell
dotnet run
```
Verify:
- F10 opens Settings panel centered on screen
- Left/Right or Tab switches categories
- Up/Down navigates options
- Enter toggles booleans, Left/Right cycles values
- Esc closes and saves
- After closing, check that `settings.json` was created in the working directory
- Kill and restart the app — verify settings are loaded from file
- Hotkey toggles (O, B, H, L, F2, F3, F4) still work and values match settings

- [ ] **Step 8: Commit**

```powershell
git add SolarSystemScene.cs
git commit -m "feat: wire settings system — load/save, SettingsPanel, hotkey sync"
```
