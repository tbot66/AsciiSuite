# Engine Fixes, Z-Layer & Viewport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix UI z-layer occlusion, relax canvas size clamping for widescreen terminals, fix sub-pixel jitter at low time scales, and set a slower default simulation speed.

**Architecture:** Minimal engine changes (canvas clamp values), rendering layer constants and blit behavior in SolarSystemApp, and a rounding fix in Camera2D. No new classes — just targeted edits to existing files.

**Tech Stack:** C# / .NET 10.0, AsciiEngine (custom), SolarSystemApp

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `../AsciiEngine/AsciiEngine/AsciiRunner.cs` | Modify lines 32-33 | Raise initial canvas clamp |
| `../AsciiEngine/AsciiEngine/TerminalSession.cs` | Modify lines 99-100 | Raise resize canvas clamp |
| `Rendering/RenderZ.cs` | Modify | Add UI_BG and UI_TEXT z-layer constants |
| `SolarSystemScene.cs` | Modify lines 57, 1277-1322, 2451-2463 | Time scale default, BlitText background fill, update blit z-values |
| `Rendering/Camera2D.cs` | Modify lines 40-43 | Fix rounding for jitter |

---

### Task 1: Relax Canvas Size Clamps

**Files:**
- Modify: `../AsciiEngine/AsciiEngine/AsciiRunner.cs:32-33`
- Modify: `../AsciiEngine/AsciiEngine/TerminalSession.cs:99-100`

- [ ] **Step 1: Update AsciiRunner initial clamp**

In `AsciiRunner.cs`, change lines 32-33 from:

```csharp
int w = Math.Max(20, Math.Min(160, ww));
int h = Math.Max(10, Math.Min(60, wh));
```

to:

```csharp
int w = Math.Max(20, Math.Min(320, ww));
int h = Math.Max(10, Math.Min(100, wh));
```

- [ ] **Step 2: Update TerminalSession resize clamp**

In `TerminalSession.cs`, change lines 99-100 from:

```csharp
newW = Math.Max(20, Math.Min(200, w));
newH = Math.Max(10, Math.Min(80, h));
```

to:

```csharp
newW = Math.Max(20, Math.Min(320, w));
newH = Math.Max(10, Math.Min(100, h));
```

- [ ] **Step 3: Build and verify**

Run:
```powershell
dotnet build ../AsciiSuite.slnx
```
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Commit**

```powershell
git add ../AsciiEngine/AsciiEngine/AsciiRunner.cs ../AsciiEngine/AsciiEngine/TerminalSession.cs
git commit -m "feat: raise canvas size clamp to 320x100 for widescreen support"
```

---

### Task 2: Add UI Z-Layer Constants

**Files:**
- Modify: `Rendering/RenderZ.cs`

- [ ] **Step 1: Add UI_BG and UI_TEXT constants**

In `RenderZ.cs`, replace the existing `UI_BORDER` constant:

```csharp
public const double UI_BORDER = 0;
```

with:

```csharp
public const double UI_BORDER = 0;
public const double UI_BG = -10.0;
public const double UI_TEXT = -11.0;
```

- [ ] **Step 2: Build and verify**

Run:
```powershell
dotnet build
```
Expected: Build succeeds with 0 errors.

- [ ] **Step 3: Commit**

```powershell
git add Rendering/RenderZ.cs
git commit -m "feat: add UI_BG and UI_TEXT z-layer constants for panel occlusion"
```

---

### Task 3: Update BlitText to Fill Opaque Background

**Files:**
- Modify: `SolarSystemScene.cs:1277-1322` (BlitText method)
- Modify: `SolarSystemScene.cs:2421-2476` (DrawUI method)

- [ ] **Step 1: Add background fill to BlitText**

In `SolarSystemScene.cs`, replace the `BlitText` method (lines 1277-1323) with this version that fills the panel background first:

```csharp
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

    // Fill the entire panel region with opaque background
    for (int fy = 0; fy < h; fy++)
        for (int fx = 0; fx < w; fx++)
            r.Set(x0 + fx, y0 + fy, ' ', bg, bg, z: RenderZ.UI_BG);

    if (string.IsNullOrEmpty(text)) return;

    // Blit text on top at UI_TEXT layer
    double textZ = RenderZ.UI_TEXT;
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
            r.Set(cx, cy, ch, fg, bg, z: textZ);

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
```

- [ ] **Step 2: Update the border draw in DrawUI to use UI_BG z-layer**

In `SolarSystemScene.cs`, in the `DrawUI` method, change line 2423 from:

```csharp
r.DrawRect(0, 0, ctx.Width, ctx.Height, '#', Colors.BrightBlack, Colors.Black, z: RenderZ.UI_BORDER);
```

to:

```csharp
r.DrawRect(0, 0, ctx.Width, ctx.Height, '#', Colors.BrightBlack, Colors.Black, z: RenderZ.UI_BG);
```

- [ ] **Step 3: Update event log rendering z-layer**

In the `DrawUI` method, change the event log rendering (around line 2472) from:

```csharp
r.DrawString(2, line, e, Colors.BrightBlack, Colors.Black, z: RenderZ.UI_BORDER);
```

to:

```csharp
r.DrawString(2, line, e, Colors.BrightBlack, Colors.Black, z: RenderZ.UI_TEXT);
```

Also update the flash message line (around line 2467) from:

```csharp
r.DrawString(2, 7, _msg, Colors.BrightGreen, Colors.Black, z: RenderZ.UI_BORDER);
```

to:

```csharp
r.DrawString(2, 7, _msg, Colors.BrightGreen, Colors.Black, z: RenderZ.UI_TEXT);
```

- [ ] **Step 4: Build and verify**

Run:
```powershell
dotnet build
```
Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Manual test**

Run:
```powershell
dotnet run
```
Verify: Navigate to a system with planets. Zoom in so a planet is near the INFO panel or header. Confirm the UI panels have opaque black backgrounds and planet/ring content does not bleed through.

- [ ] **Step 6: Commit**

```powershell
git add SolarSystemScene.cs
git commit -m "fix: UI panels now render with opaque backgrounds above world content"
```

---

### Task 4: Change Default Time Scale

**Files:**
- Modify: `SolarSystemScene.cs:57`

- [ ] **Step 1: Change the default value**

In `SolarSystemScene.cs`, change line 57 from:

```csharp
private double _timeScale = 1.0;
```

to:

```csharp
private double _timeScale = 0.25;
```

- [ ] **Step 2: Build and verify**

Run:
```powershell
dotnet build
```
Expected: Build succeeds.

- [ ] **Step 3: Manual test**

Run:
```powershell
dotnet run
```
Verify: The header bar shows `x0.25`. Planets orbit noticeably slower than before. Speed up/down keys (`+`/`-`) still work to adjust.

- [ ] **Step 4: Commit**

```powershell
git add SolarSystemScene.cs
git commit -m "feat: slow default time scale to 0.25x for gameplay pacing"
```

---

### Task 5: Fix Sub-Pixel Jitter in Camera2D

**Files:**
- Modify: `Rendering/Camera2D.cs:39-43`

- [ ] **Step 1: Change rounding to consistent Floor**

In `Camera2D.cs`, replace lines 39-43:

```csharp
public int WorldToScreenX(double wx)
    => CenterX + (int)Math.Round((wx - CamWX) * WorldToScreen);

public int WorldToScreenY(double wy)
    => CenterY + (int)Math.Round((wy - CamWY) * WorldToScreen * OrbitYScale);
```

with:

```csharp
public int WorldToScreenX(double wx)
    => CenterX + (int)Math.Floor((wx - CamWX) * WorldToScreen);

public int WorldToScreenY(double wy)
    => CenterY + (int)Math.Floor((wy - CamWY) * WorldToScreen * OrbitYScale);
```

- [ ] **Step 2: Build and verify**

Run:
```powershell
dotnet build
```
Expected: Build succeeds.

- [ ] **Step 3: Manual test**

Run:
```powershell
dotnet run
```
Verify: At low time scales (0.25x or lower), watch objects moving on diagonal or curved paths. They should move smoothly without flickering between adjacent cells. If jitter persists, the hysteresis approach from the spec should be explored as a follow-up.

- [ ] **Step 4: Commit**

```powershell
git add Rendering/Camera2D.cs
git commit -m "fix: use Floor instead of Round in Camera2D to prevent sub-pixel jitter"
```
