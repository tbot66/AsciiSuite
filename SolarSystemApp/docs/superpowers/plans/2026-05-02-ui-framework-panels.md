# UI Framework & Panels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a structured UI panel system that replaces the ad-hoc Spectre blit code in SolarSystemScene, with a UIManager, edge-anchored panels, modal support, keyboard-first input routing, and a viewport rectangle that adapts to visible panels.

**Architecture:** New `UI/` folder with UIManager as coordinator, UIPanel base class, and individual panel implementations. Panels build Spectre IRenderable widgets, which are rendered to string and blitted with opaque backgrounds at UI z-layers. IGameContext interface decouples panels from scene internals. Scene delegates input to UIManager first, then handles residual input for camera/selection.

**Tech Stack:** C# / .NET 10.0, Spectre.Console 0.54.0, AsciiEngine (custom)

**Depends on:** Plan 1 (Engine Fixes, Z-Layer & Viewport) must be completed first — this plan uses `RenderZ.UI_BG` and `RenderZ.UI_TEXT` constants and the updated `BlitText` behavior.

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `UI/SpectreBlitter.cs` | Create | Static utilities: RenderSpectreToString + BlitPanel (blit with opaque bg) |
| `UI/UIPanel.cs` | Create | Base class for all panels: Bounds, Visible, Modal, Anchor, input, rendering |
| `UI/IGameContext.cs` | Create | Read-only interface exposing game state to panels |
| `UI/UIManager.cs` | Create | Panel list, modal stack, layout, input routing, render orchestration |
| `UI/Panels/HeaderPanel.cs` | Create | Top bar: system info, time, zoom, pause |
| `UI/Panels/InfoPanel.cs` | Create | Right panel: selected object details |
| `UI/Panels/EventLogPanel.cs` | Create | Bottom bar: recent events |
| `UI/Panels/FleetListPanel.cs` | Create | Left panel (toggled): ships/stations list |
| `UI/Panels/CommandMenuPanel.cs` | Create | Modal overlay: cascading key-command menus |
| `SolarSystemScene.cs` | Modify | Implement IGameContext, wire UIManager, remove old blit code |

---

### Task 1: SpectreBlitter Utility

**Files:**
- Create: `UI/SpectreBlitter.cs`

- [ ] **Step 1: Create the UI directory**

Run:
```powershell
New-Item -ItemType Directory -Path "UI" -Force
New-Item -ItemType Directory -Path "UI/Panels" -Force
```

- [ ] **Step 2: Write SpectreBlitter.cs**

```csharp
using AsciiEngine;
using SolarSystemApp.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.IO;

namespace SolarSystemApp.UI
{
    internal static class SpectreBlitter
    {
        public static string RenderToString(IRenderable renderable, int width)
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

        public static void BlitPanel(
            ConsoleRenderer r,
            int x0, int y0,
            int w, int h,
            string text,
            Color fg,
            Color bg)
        {
            if (w <= 0 || h <= 0) return;

            r.FillRect(x0, y0, w, h, ' ', bg, bg, RenderZ.UI_BG);

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
                    r.Set(cx, cy, ch, fg, bg, z: RenderZ.UI_TEXT);

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
git add UI/SpectreBlitter.cs
git commit -m "feat: add SpectreBlitter utility for rendering Spectre widgets to ConsoleRenderer"
```

---

### Task 2: UIPanel Base Class

**Files:**
- Create: `UI/UIPanel.cs`

- [ ] **Step 1: Write UIPanel.cs**

```csharp
using AsciiEngine;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI
{
    internal enum PanelAnchor
    {
        Top,
        Bottom,
        Left,
        Right,
        Center
    }

    internal struct PanelBounds
    {
        public int X, Y, W, H;

        public PanelBounds(int x, int y, int w, int h)
        {
            X = x; Y = y; W = w; H = h;
        }
    }

    internal abstract class UIPanel
    {
        public PanelBounds Bounds { get; set; }
        public bool Visible { get; set; } = true;
        public bool Modal { get; set; } = false;
        public PanelAnchor Anchor { get; set; }
        public ConsoleKey? ToggleKey { get; set; }

        public abstract IRenderable BuildContent(IGameContext ctx);
        public virtual bool HandleInput(InputState input, IGameContext ctx) => false;
        public virtual void OnShow() { }
        public virtual void OnHide() { }

        public int RequestedWidth { get; set; } = 0;
        public int RequestedHeight { get; set; } = 0;
    }
}
```

- [ ] **Step 2: Build and verify**

Run:
```powershell
dotnet build
```
Expected: Build fails — `IGameContext` doesn't exist yet. This is expected; we'll fix it in Task 3.

- [ ] **Step 3: Commit**

```powershell
git add UI/UIPanel.cs
git commit -m "feat: add UIPanel base class with anchor, bounds, and modal support"
```

---

### Task 3: IGameContext Interface

**Files:**
- Create: `UI/IGameContext.cs`

- [ ] **Step 1: Write IGameContext.cs**

This interface exposes the subset of scene state that panels need. It references types from the World and Rendering namespaces.

```csharp
using SolarSystemApp.Rendering;
using SolarSystemApp.World;
using SolarSystemApp.Gameplay;

namespace SolarSystemApp.UI
{
    internal interface IGameContext
    {
        // System
        StarSystem CurrentSystem { get; }
        string SystemDescriptor { get; }
        int SystemIndex { get; }

        // Simulation
        double SimTime { get; }
        double TimeScale { get; }
        bool Paused { get; }

        // Camera
        double WorldToScreen { get; }
        Camera2D Camera { get; }

        // Selection
        int SelectedIndex { get; }
        int SelectionCount { get; }
        SelectionInfo GetSelection(int index);
        SelectionInfo GetCurrentSelection();

        // Fleet
        int ArmedShipIndex { get; }
        long Credits { get; }
        ShipJobs Jobs { get; }

        // Events
        EventLog Events { get; }

        // Display toggles (read-only from panels)
        bool Follow { get; }
        bool FastPan { get; }
    }

    internal struct SelectionInfo
    {
        public string Kind;
        public int Index;
        public int SubIndex;
        public string Label;
        public double WX, WY;

        // Optional detail fields (populated depending on kind)
        public double Radius;
        public double A, E;
        public bool HasRings;
        public string Texture;
        public double VX, VY;
        public string Mode;
        public string Job;
        public bool JobCompleted;
        public bool HasDetails;
    }
}
```

- [ ] **Step 2: Make SelectionItem accessible**

The `SelectionItem` and `EntityKind` types are currently `private` nested types inside `SolarSystemScene`. We need them accessible for `IGameContext`. However, rather than extracting the private types, we use `SelectionInfo` (a new struct above) as a DTO that panels consume. The scene maps its internal `SelectionItem` to `SelectionInfo` when implementing `IGameContext`.

No code changes needed for this step — the mapping happens in Task 9 when we wire up the scene.

- [ ] **Step 3: Build and verify**

Run:
```powershell
dotnet build
```
Expected: Build succeeds (UIPanel now has its IGameContext dependency).

- [ ] **Step 4: Commit**

```powershell
git add UI/IGameContext.cs
git commit -m "feat: add IGameContext interface for decoupled panel data access"
```

---

### Task 4: UIManager

**Files:**
- Create: `UI/UIManager.cs`

- [ ] **Step 1: Write UIManager.cs**

```csharp
using AsciiEngine;
using SolarSystemApp.Rendering;

namespace SolarSystemApp.UI
{
    internal sealed class UIManager
    {
        private readonly List<UIPanel> _panels = new List<UIPanel>();
        private readonly Stack<UIPanel> _modalStack = new Stack<UIPanel>();

        public PanelBounds ViewportRect { get; private set; }

        private double _renderTimer = 0.0;
        private const double RenderInterval = 0.10; // 10 Hz Spectre rebuild
        private readonly Dictionary<UIPanel, string> _renderCache = new Dictionary<UIPanel, string>();

        public void AddPanel(UIPanel panel)
        {
            _panels.Add(panel);
            _renderCache[panel] = "";
        }

        public void PushModal(UIPanel panel)
        {
            panel.Visible = true;
            panel.OnShow();
            _modalStack.Push(panel);
        }

        public void PopModal()
        {
            if (_modalStack.Count > 0)
            {
                var panel = _modalStack.Pop();
                panel.Visible = false;
                panel.OnHide();
            }
        }

        public bool IsModalOpen => _modalStack.Count > 0;

        public void Layout(int screenW, int screenH)
        {
            int top = 0, bottom = 0, left = 0, right = 0;

            foreach (var p in _panels)
            {
                if (!p.Visible || p.Modal) continue;

                switch (p.Anchor)
                {
                    case PanelAnchor.Top:
                        int th = p.RequestedHeight > 0 ? p.RequestedHeight : 3;
                        p.Bounds = new PanelBounds(0, top, screenW, th);
                        top += th;
                        break;

                    case PanelAnchor.Bottom:
                        int bh = p.RequestedHeight > 0 ? p.RequestedHeight : 5;
                        p.Bounds = new PanelBounds(0, screenH - bottom - bh, screenW, bh);
                        bottom += bh;
                        break;

                    case PanelAnchor.Left:
                        int lw = p.RequestedWidth > 0 ? p.RequestedWidth : 20;
                        int lh = screenH - top - bottom;
                        p.Bounds = new PanelBounds(left, top, lw, lh);
                        left += lw;
                        break;

                    case PanelAnchor.Right:
                        int rw = p.RequestedWidth > 0 ? Math.Min(p.RequestedWidth, screenW / 3) : Math.Min(30, screenW / 3);
                        int rh = screenH - top - bottom;
                        p.Bounds = new PanelBounds(screenW - right - rw, top, rw, rh);
                        right += rw;
                        break;

                    case PanelAnchor.Center:
                        int cw = p.RequestedWidth > 0 ? p.RequestedWidth : 50;
                        int ch = p.RequestedHeight > 0 ? p.RequestedHeight : 20;
                        p.Bounds = new PanelBounds(
                            (screenW - cw) / 2,
                            (screenH - ch) / 2,
                            cw, ch);
                        break;
                }
            }

            ViewportRect = new PanelBounds(left, top, screenW - left - right, screenH - top - bottom);
        }

        public bool HandleInput(InputState input, IGameContext ctx)
        {
            // Modal gets all input
            if (_modalStack.Count > 0)
            {
                var modal = _modalStack.Peek();
                modal.HandleInput(input, ctx);
                return true;
            }

            // Check toggle keys
            foreach (var p in _panels)
            {
                if (p.ToggleKey.HasValue && input.WasPressed(p.ToggleKey.Value))
                {
                    if (p.Modal)
                    {
                        if (p.Visible)
                        {
                            PopModal();
                        }
                        else
                        {
                            PushModal(p);
                            Layout(ViewportRect.W + GetHorizontalPanelWidth(), ViewportRect.H + GetVerticalPanelHeight());
                        }
                    }
                    else
                    {
                        p.Visible = !p.Visible;
                        if (p.Visible) p.OnShow(); else p.OnHide();
                        // Relayout is needed — caller should call Layout()
                    }
                    return true;
                }
            }

            // Check non-modal visible panels
            foreach (var p in _panels)
            {
                if (p.Visible && !p.Modal && p.HandleInput(input, ctx))
                    return true;
            }

            return false;
        }

        public void Render(ConsoleRenderer r, IGameContext ctx, double dt)
        {
            _renderTimer += dt;
            bool rebuild = _renderTimer >= RenderInterval;
            if (rebuild) _renderTimer = 0;

            foreach (var p in _panels)
            {
                if (!p.Visible) continue;

                var b = p.Bounds;
                if (b.W <= 0 || b.H <= 0) continue;

                if (rebuild)
                {
                    var renderable = p.BuildContent(ctx);
                    _renderCache[p] = SpectreBlitter.RenderToString(renderable, b.W);
                }

                string cached = _renderCache.TryGetValue(p, out var c) ? c : "";
                SpectreBlitter.BlitPanel(r, b.X, b.Y, b.W, b.H, cached,
                    Colors.BrightWhite, Colors.Black);
            }
        }

        public bool NeedsRelayout { get; set; } = true;

        private int GetHorizontalPanelWidth()
        {
            int w = 0;
            foreach (var p in _panels)
                if (p.Visible && !p.Modal && (p.Anchor == PanelAnchor.Left || p.Anchor == PanelAnchor.Right))
                    w += p.Bounds.W;
            return w;
        }

        private int GetVerticalPanelHeight()
        {
            int h = 0;
            foreach (var p in _panels)
                if (p.Visible && !p.Modal && (p.Anchor == PanelAnchor.Top || p.Anchor == PanelAnchor.Bottom))
                    h += p.Bounds.H;
            return h;
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
git add UI/UIManager.cs
git commit -m "feat: add UIManager with layout, input routing, and render orchestration"
```

---

### Task 5: HeaderPanel

**Files:**
- Create: `UI/Panels/HeaderPanel.cs`

- [ ] **Step 1: Write HeaderPanel.cs**

Migrates the logic from `SolarSystemScene.BuildSpectreHeader()`.

```csharp
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI.Panels
{
    internal sealed class HeaderPanel : UIPanel
    {
        public HeaderPanel()
        {
            Anchor = PanelAnchor.Top;
            RequestedHeight = 3;
            Visible = true;
        }

        public override IRenderable BuildContent(IGameContext ctx)
        {
            var desc = ctx.SystemDescriptor;
            var sys = ctx.CurrentSystem;
            var title = new Markup(
                $"[bold]System[/]  [grey]({sys.Name})[/]  [teal]{desc}[/]  " +
                $"time={ctx.SimTime:0.0}  x{ctx.TimeScale:0.00}  " +
                $"paused={( ctx.Paused ? "YES" : "NO")}  " +
                $"zoom={ctx.WorldToScreen:0.0}");

            return new Panel(title)
                .Border(BoxBorder.Rounded)
                .Header("CONTROLS", Justify.Left);
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
git add UI/Panels/HeaderPanel.cs
git commit -m "feat: add HeaderPanel — migrated from BuildSpectreHeader"
```

---

### Task 6: InfoPanel

**Files:**
- Create: `UI/Panels/InfoPanel.cs`

- [ ] **Step 1: Write InfoPanel.cs**

Migrates the logic from `SolarSystemScene.BuildSpectreInfo()`.

```csharp
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI.Panels
{
    internal sealed class InfoPanel : UIPanel
    {
        public InfoPanel()
        {
            Anchor = PanelAnchor.Right;
            RequestedWidth = 30;
            Visible = true;
        }

        public override IRenderable BuildContent(IGameContext ctx)
        {
            var sel = ctx.GetCurrentSelection();

            if (!sel.HasDetails && string.IsNullOrEmpty(sel.Label))
            {
                return new Panel(new Text("No selection"))
                    .Border(BoxBorder.Rounded)
                    .Header("INFO", Justify.Left);
            }

            var armed = ctx.ArmedShipIndex >= 0
                ? ctx.CurrentSystem.Ships.Count > ctx.ArmedShipIndex
                    ? ctx.CurrentSystem.Ships[ctx.ArmedShipIndex].Name
                    : "none"
                : "none";

            var grid = new Grid();
            grid.AddColumn(new GridColumn().NoWrap());
            grid.AddColumn();

            grid.AddRow("Selected", $"{sel.Kind} | {sel.Label}");
            grid.AddRow("World", $"({sel.WX:0.00},{sel.WY:0.00})");
            grid.AddRow("Follow", ctx.Follow ? "ON" : "OFF");
            grid.AddRow("FastPan", ctx.FastPan ? "ON" : "OFF");
            grid.AddRow("ArmedShip", armed);
            grid.AddRow("Credits", $"{ctx.Credits}");

            if (sel.Kind == "Ship" && sel.HasDetails)
            {
                grid.AddRow("Vel", $"({sel.VX:0.00},{sel.VY:0.00})");
                grid.AddRow("Mode", sel.Mode ?? "");

                grid.AddRow("Job", sel.Job ?? "None");
                if (sel.Job != null && sel.Job != "None")
                    grid.AddRow("Done", $"{sel.JobCompleted}");
            }
            else if (sel.Kind == "Planet" && sel.HasDetails)
            {
                grid.AddRow("Radius", $"{sel.Radius:0.00}");
                grid.AddRow("A / E", $"{sel.A:0.00} / {sel.E:0.00}");
                grid.AddRow("Rings", sel.HasRings ? "YES" : "NO");
                grid.AddRow("Texture", sel.Texture ?? "");
            }

            return new Panel(grid)
                .Border(BoxBorder.Rounded)
                .Header("INFO", Justify.Left);
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
git add UI/Panels/InfoPanel.cs
git commit -m "feat: add InfoPanel — migrated from BuildSpectreInfo"
```

---

### Task 7: EventLogPanel

**Files:**
- Create: `UI/Panels/EventLogPanel.cs`

- [ ] **Step 1: Write EventLogPanel.cs**

```csharp
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI.Panels
{
    internal sealed class EventLogPanel : UIPanel
    {
        public EventLogPanel()
        {
            Anchor = PanelAnchor.Bottom;
            RequestedHeight = 7;
            Visible = true;
        }

        public override IRenderable BuildContent(IGameContext ctx)
        {
            var rows = new Rows();
            int maxLines = Bounds.H - 2; // account for panel border
            if (maxLines < 1) maxLines = 1;

            var events = new List<string>();
            foreach (var e in ctx.Events.GetNewestFirst(maxLines))
                events.Add(e);

            // Reverse so oldest is on top, newest at bottom
            events.Reverse();

            var lines = new List<IRenderable>();
            foreach (var e in events)
                lines.Add(new Markup($"[grey]{Markup.Escape(e)}[/]"));

            // Pad empty lines if fewer events than available space
            while (lines.Count < maxLines)
                lines.Insert(0, new Text(""));

            return new Panel(new Rows(lines))
                .Border(BoxBorder.Rounded)
                .Header("LOG", Justify.Left);
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
git add UI/Panels/EventLogPanel.cs
git commit -m "feat: add EventLogPanel — migrated from inline event rendering"
```

---

### Task 8: FleetListPanel (Placeholder)

**Files:**
- Create: `UI/Panels/FleetListPanel.cs`

- [ ] **Step 1: Write FleetListPanel.cs**

```csharp
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI.Panels
{
    internal sealed class FleetListPanel : UIPanel
    {
        private int _scrollIndex = 0;

        public FleetListPanel()
        {
            Anchor = PanelAnchor.Left;
            RequestedWidth = 22;
            Visible = false; // toggled by F1
            ToggleKey = ConsoleKey.F1;
        }

        public override bool HandleInput(InputState input, IGameContext ctx)
        {
            int totalItems = ctx.CurrentSystem.Ships.Count + ctx.CurrentSystem.Stations.Count;
            if (totalItems == 0) return false;

            if (input.WasPressed(ConsoleKey.UpArrow))
            {
                _scrollIndex = Math.Max(0, _scrollIndex - 1);
                return true;
            }
            if (input.WasPressed(ConsoleKey.DownArrow))
            {
                _scrollIndex = Math.Min(totalItems - 1, _scrollIndex + 1);
                return true;
            }

            return false;
        }

        public override IRenderable BuildContent(IGameContext ctx)
        {
            var table = new Table();
            table.AddColumn("Name");
            table.AddColumn("Type");
            table.Border(TableBorder.None);
            table.Expand();

            int row = 0;

            foreach (var ship in ctx.CurrentSystem.Ships)
            {
                string name = row == _scrollIndex
                    ? $"[bold yellow]{Markup.Escape(ship.Name)}[/]"
                    : Markup.Escape(ship.Name);
                table.AddRow(new Markup(name), new Text("Ship"));
                row++;
            }

            foreach (var station in ctx.CurrentSystem.Stations)
            {
                string name = row == _scrollIndex
                    ? $"[bold yellow]{Markup.Escape(station.Name)}[/]"
                    : Markup.Escape(station.Name);
                table.AddRow(new Markup(name), new Text("Stn"));
                row++;
            }

            if (row == 0)
                table.AddRow(new Text("(empty)"), new Text(""));

            return new Panel(table)
                .Border(BoxBorder.Rounded)
                .Header("FLEET", Justify.Left);
        }

        public override void OnShow()
        {
            _scrollIndex = 0;
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
git add UI/Panels/FleetListPanel.cs
git commit -m "feat: add FleetListPanel with scrollable ship/station list"
```

---

### Task 9: CommandMenuPanel

**Files:**
- Create: `UI/Panels/CommandMenuPanel.cs`

- [ ] **Step 1: Write CommandMenuPanel.cs**

```csharp
using AsciiEngine;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI.Panels
{
    internal sealed class CommandMenuPanel : UIPanel
    {
        public sealed class MenuOption
        {
            public ConsoleKey Key { get; init; }
            public string Label { get; init; } = "";
            public Action<IGameContext>? Execute { get; init; }
            public List<MenuOption>? SubMenu { get; init; }
        }

        private readonly Stack<List<MenuOption>> _menuStack = new Stack<List<MenuOption>>();
        private List<MenuOption> _currentMenu = new List<MenuOption>();

        public Action? OnClose { get; set; }

        public CommandMenuPanel()
        {
            Anchor = PanelAnchor.Center;
            RequestedWidth = 40;
            RequestedHeight = 16;
            Modal = true;
            Visible = false;
        }

        public void Open(List<MenuOption> rootMenu)
        {
            _menuStack.Clear();
            _currentMenu = rootMenu;
        }

        public override bool HandleInput(InputState input, IGameContext ctx)
        {
            if (input.WasPressed(ConsoleKey.Escape))
            {
                if (_menuStack.Count > 0)
                {
                    _currentMenu = _menuStack.Pop();
                }
                else
                {
                    OnClose?.Invoke();
                }
                return true;
            }

            foreach (var opt in _currentMenu)
            {
                if (input.WasPressed(opt.Key))
                {
                    if (opt.SubMenu != null && opt.SubMenu.Count > 0)
                    {
                        _menuStack.Push(_currentMenu);
                        _currentMenu = opt.SubMenu;
                    }
                    else if (opt.Execute != null)
                    {
                        opt.Execute(ctx);
                        OnClose?.Invoke();
                    }
                    return true;
                }
            }

            return true; // modal — consume all input
        }

        public override IRenderable BuildContent(IGameContext ctx)
        {
            var lines = new List<IRenderable>();

            foreach (var opt in _currentMenu)
            {
                string keyName = opt.Key.ToString();
                // Strip "Oem" prefix and common key name cleanup
                if (keyName.StartsWith("Oem"))
                    keyName = keyName.Substring(3);

                string hasSubmenu = opt.SubMenu != null ? " >" : "";
                lines.Add(new Markup($"[yellow][[{keyName}]][/] {Markup.Escape(opt.Label)}{hasSubmenu}"));
            }

            lines.Add(new Text(""));

            string backLabel = _menuStack.Count > 0 ? "Back" : "Close";
            lines.Add(new Markup($"[grey][[Esc]] {backLabel}[/]"));

            return new Panel(new Rows(lines))
                .Border(BoxBorder.Rounded)
                .Header("COMMAND", Justify.Left);
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
git add UI/Panels/CommandMenuPanel.cs
git commit -m "feat: add CommandMenuPanel with cascading key-menu system"
```

---

### Task 10: Wire UIManager into SolarSystemScene

**Files:**
- Modify: `SolarSystemScene.cs`

This is the integration task. It implements `IGameContext`, creates and wires the UIManager, and removes old blit code.

- [ ] **Step 1: Add IGameContext implementation to SolarSystemScene**

Add `using SolarSystemApp.UI;` and `using SolarSystemApp.UI.Panels;` to the top of the file.

Change the class declaration from:

```csharp
public sealed class SolarSystemScene : IAsciiApp
```

to:

```csharp
public sealed class SolarSystemScene : IAsciiApp, IGameContext
```

Add the IGameContext property implementations at the end of the field declarations section (after the existing fields, around line 155):

```csharp
// =========================
// IGameContext implementation
// =========================
StarSystem IGameContext.CurrentSystem => _sys;
string IGameContext.SystemDescriptor => string.IsNullOrWhiteSpace(_sys.Descriptor) ? _sys.Kind.ToString() : _sys.Descriptor;
int IGameContext.SystemIndex => _systemIndex;
double IGameContext.SimTime => _simTime;
double IGameContext.TimeScale => _timeScale;
bool IGameContext.Paused => _paused;
double IGameContext.WorldToScreen => _worldToScreen;
Camera2D IGameContext.Camera => _cam;
int IGameContext.SelectedIndex => _selIndex;
int IGameContext.SelectionCount => _selection.Count;
int IGameContext.ArmedShipIndex => _armedShipIndex;
long IGameContext.Credits => _credits;
ShipJobs IGameContext.Jobs => _jobs;
EventLog IGameContext.Events => _events;
bool IGameContext.Follow => _follow;
bool IGameContext.FastPan => _fastPan;

SelectionInfo IGameContext.GetCurrentSelection()
{
    var sel = GetSelection();
    if (sel == null) return default;
    return MapSelection(sel);
}

SelectionInfo IGameContext.GetSelection(int index)
{
    if (index < 0 || index >= _selection.Count) return default;
    return MapSelection(_selection[index]);
}

private SelectionInfo MapSelection(SelectionItem sel)
{
    var info = new SelectionInfo
    {
        Kind = sel.Kind.ToString(),
        Index = sel.Index,
        SubIndex = sel.SubIndex,
        Label = sel.Label ?? "",
        WX = sel.WX,
        WY = sel.WY,
    };

    if (sel.Kind == EntityKind.Ship && sel.Index >= 0 && sel.Index < _sys.Ships.Count)
    {
        var sh = _sys.Ships[sel.Index];
        info.VX = sh.VX;
        info.VY = sh.VY;
        info.Mode = sh.Mode.ToString();
        var st = _jobs.GetOrCreate(sh.Name);
        info.Job = st.Job.ToString();
        info.JobCompleted = st.Completed;
        info.HasDetails = true;
    }
    else if (sel.Kind == EntityKind.Planet && sel.Index >= 0 && sel.Index < _sys.Planets.Count)
    {
        var p = _sys.Planets[sel.Index];
        info.Radius = p.RadiusWorld;
        info.A = p.A;
        info.E = p.E;
        info.HasRings = p.HasRings;
        info.Texture = p.Texture.ToString();
        info.HasDetails = true;
    }

    return info;
}
```

- [ ] **Step 2: Add UIManager field and initialization**

Add a new field near the other UI fields (around line 145):

```csharp
private UIManager _uiManager;
private CommandMenuPanel _commandMenu;
```

In the `Init()` method, after existing initialization (or at the end of `Init`), add:

```csharp
if (_uiManager == null)
{
    _uiManager = new UIManager();
    _commandMenu = new CommandMenuPanel();
    _commandMenu.OnClose = () => _uiManager.PopModal();

    _uiManager.AddPanel(new HeaderPanel());
    _uiManager.AddPanel(new EventLogPanel());
    _uiManager.AddPanel(new FleetListPanel());
    _uiManager.AddPanel(new InfoPanel());
    _uiManager.AddPanel(_commandMenu);
}
_uiManager.Layout(ctx.Width, ctx.Height);

// Update camera center to viewport instead of screen center
var vp = _uiManager.ViewportRect;
_cam.CenterX = vp.X + vp.W / 2;
_cam.CenterY = vp.Y + vp.H / 2;
```

- [ ] **Step 3: Route input through UIManager**

In the `Update()` method, at the very beginning of the system-view input handling (not interior view), add a flag:

```csharp
bool uiConsumedInput = _uiManager.HandleInput(ctx.Input, this);
if (uiConsumedInput)
{
    _uiManager.Layout(ctx.Width, ctx.Height);
    var vp = _uiManager.ViewportRect;
    _cam.CenterX = vp.X + vp.W / 2;
    _cam.CenterY = vp.Y + vp.H / 2;
}
```

Then wrap the existing scene input handling block (camera, selection, hotkeys) with:

```csharp
if (!uiConsumedInput)
{
    // ... existing input handling code ...
}
```

**Important:** Do NOT skip simulation stepping (the `_simAccum` accumulator loop). Only scene input handling is guarded — simulation and rendering always run regardless of UI input state.

- [ ] **Step 4: Replace DrawUI with UIManager rendering**

In the `Draw()` method, replace the call to `DrawUI(r, ctx)` (or the inline DrawUI code) with:

```csharp
_uiManager.Render(ctx.Renderer, this, ctx.DeltaTime);
```

- [ ] **Step 5: Remove old Spectre blit code**

Remove these methods and fields from SolarSystemScene (they are now handled by panels):

- Remove field: `_spectreHeaderCache` (line 147)
- Remove field: `_spectreInfoCache` (line 148)
- Remove field: `_spectreUiTimer` (line 149)
- Remove constant: `SpectreUiEvery` (line 150)
- Remove method: `BuildSpectreHeader()` (lines 1199-1209)
- Remove method: `BuildSpectreInfo()` (lines 1211-1253)
- Remove method: `RenderSpectreToString()` (lines 1258-1275)
- Remove method: `BlitText()` (lines 1277-1323)
- Remove method: `DrawUI()` (lines 2421-2476) — but keep any non-UI code that was in there (like the flash message, which can stay as a direct `DrawString` call in `Draw()`)

Keep the flash message rendering inline in `Draw()`:

```csharp
if (!string.IsNullOrEmpty(_msg))
    r.DrawString(2, _uiManager.ViewportRect.Y + 1, _msg, Colors.BrightGreen, Colors.Black, z: RenderZ.UI_TEXT);
```

- [ ] **Step 6: Build and verify**

Run:
```powershell
dotnet build
```
Expected: Build succeeds with 0 errors.

- [ ] **Step 7: Manual test**

Run:
```powershell
dotnet run
```
Verify:
- Header panel shows at top with system info
- Info panel shows on right when object is selected
- Event log shows at bottom
- F1 toggles fleet list on left
- UI panels have opaque backgrounds — no bleed-through from planets/rings
- Camera is centered in the viewport (not the full screen)
- Resizing the terminal relayouts panels correctly

- [ ] **Step 8: Commit**

```powershell
git add SolarSystemScene.cs
git commit -m "feat: wire UIManager into scene — panels replace ad-hoc Spectre blit code"
```
