# Texture Caching System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace per-frame procedural texture generation with cached equirectangular maps using progressive background-threaded refinement and disk persistence, achieving ~15-20x speedup at close zoom.

**Architecture:** Three new files in `Rendering/` — `EquirectMap.cs` (data structure + bilinear sampling), `TextureCache.cs` (cache manager with tier selection), `TextureGenerator.cs` (background thread worker with priority queue). Modified files: `PlanetDrawer.cs` (swap noise for cache lookup), `SolarSystemScene.cs` (wire up cache + generator lifecycle), `PlanetTextures.cs` (extract generation logic for reuse).

**Tech Stack:** .NET 10.0, `System.Threading.Channels`, `System.Collections.Concurrent`, `System.IO` for binary serialization.

**Spec:** `docs/superpowers/specs/2026-05-03-texture-caching-design.md`

---

### Task 1: EquirectMap Data Structure

**Files:**
- Create: `Rendering/EquirectMap.cs`

This is the foundational data structure. Everything else depends on it.

- [ ] **Step 1: Create EquirectMap.cs with core structure**

```csharp
// Rendering/EquirectMap.cs
using System;
using System.IO;
using AsciiEngine;
using SolarSystemApp.Util;

namespace SolarSystemApp.Rendering
{
    internal sealed class EquirectMap
    {
        public readonly int Width;
        public readonly int Height;
        private readonly byte[] _rgb;     // flat RGB array, row-major, 3 bytes per texel
        private readonly char[] _glyphs;  // ASCII glyph per texel
        private readonly double[] _emissive; // emissive intensity per texel (0..1)
        private readonly byte[] _emissiveRgb; // emissive color per texel, 3 bytes

        public EquirectMap(int width, int height)
        {
            Width = width;
            Height = height;
            _rgb = new byte[width * height * 3];
            _glyphs = new char[width * height];
            _emissive = new double[width * height];
            _emissiveRgb = new byte[width * height * 3];
        }

        public void SetTexel(int x, int y, byte r, byte g, byte b, char glyph,
            double emissive01, byte emR, byte emG, byte emB)
        {
            int i = (y * Width + x);
            int i3 = i * 3;
            _rgb[i3] = r;
            _rgb[i3 + 1] = g;
            _rgb[i3 + 2] = b;
            _glyphs[i] = glyph;
            _emissive[i] = emissive01;
            _emissiveRgb[i3] = emR;
            _emissiveRgb[i3 + 1] = emG;
            _emissiveRgb[i3 + 2] = emB;
        }

        public void Sample(double u, double v,
            out byte r, out byte g, out byte b,
            out char glyph,
            out double emissive01,
            out byte emR, out byte emG, out byte emB)
        {
            // Wrap u to [0,1), clamp v to [0,1]
            u = u - Math.Floor(u);
            v = MathUtil.Clamp(v, 0.0, 1.0 - 1e-9);

            double fx = u * Width;
            double fy = v * Height;

            int x0 = (int)fx;
            int y0 = (int)fy;
            int x1 = (x0 + 1) % Width; // wrap horizontally
            int y1 = Math.Min(y0 + 1, Height - 1);

            double tx = fx - x0;
            double ty = fy - y0;

            // Bilinear interpolation for RGB
            int i00 = (y0 * Width + x0) * 3;
            int i10 = (y0 * Width + x1) * 3;
            int i01 = (y1 * Width + x0) * 3;
            int i11 = (y1 * Width + x1) * 3;

            double invTx = 1.0 - tx;
            double invTy = 1.0 - ty;
            double w00 = invTx * invTy;
            double w10 = tx * invTy;
            double w01 = invTx * ty;
            double w11 = tx * ty;

            r = (byte)MathUtil.Clamp(
                _rgb[i00] * w00 + _rgb[i10] * w10 + _rgb[i01] * w01 + _rgb[i11] * w11, 0, 255);
            g = (byte)MathUtil.Clamp(
                _rgb[i00 + 1] * w00 + _rgb[i10 + 1] * w10 + _rgb[i01 + 1] * w01 + _rgb[i11 + 1] * w11, 0, 255);
            b = (byte)MathUtil.Clamp(
                _rgb[i00 + 2] * w00 + _rgb[i10 + 2] * w10 + _rgb[i01 + 2] * w01 + _rgb[i11 + 2] * w11, 0, 255);

            // Nearest-neighbor for glyph (can't interpolate characters)
            int nearest = (ty < 0.5) ? (y0 * Width + ((tx < 0.5) ? x0 : x1))
                                     : (y1 * Width + ((tx < 0.5) ? x0 : x1));
            glyph = _glyphs[nearest];

            // Bilinear for emissive intensity
            int e00 = y0 * Width + x0;
            int e10 = y0 * Width + x1;
            int e01 = y1 * Width + x0;
            int e11 = y1 * Width + x1;
            emissive01 = _emissive[e00] * w00 + _emissive[e10] * w10
                       + _emissive[e01] * w01 + _emissive[e11] * w11;

            // Bilinear for emissive color
            emR = (byte)MathUtil.Clamp(
                _emissiveRgb[i00] * w00 + _emissiveRgb[i10] * w10 + _emissiveRgb[i01] * w01 + _emissiveRgb[i11] * w11, 0, 255);
            emG = (byte)MathUtil.Clamp(
                _emissiveRgb[i00 + 1] * w00 + _emissiveRgb[i10 + 1] * w10 + _emissiveRgb[i01 + 1] * w01 + _emissiveRgb[i11 + 1] * w11, 0, 255);
            emB = (byte)MathUtil.Clamp(
                _emissiveRgb[i00 + 2] * w00 + _emissiveRgb[i10 + 2] * w10 + _emissiveRgb[i01 + 2] * w01 + _emissiveRgb[i11 + 2] * w11, 0, 255);
        }
    }
}
```

- [ ] **Step 2: Add disk serialization methods**

Add these methods to the `EquirectMap` class, after the `Sample` method:

```csharp
        private const byte FormatVersion = 1;

        public void WriteToDisk(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536);
            // Header: version (1) + width (2) + height (2) = 5 bytes
            fs.WriteByte(FormatVersion);
            fs.WriteByte((byte)(Width & 0xFF));
            fs.WriteByte((byte)((Width >> 8) & 0xFF));
            fs.WriteByte((byte)(Height & 0xFF));
            fs.WriteByte((byte)((Height >> 8) & 0xFF));
            // Pixel data
            fs.Write(_rgb, 0, _rgb.Length);
            // Glyph data (as UTF-16 LE, 2 bytes per char)
            for (int i = 0; i < _glyphs.Length; i++)
            {
                fs.WriteByte((byte)(_glyphs[i] & 0xFF));
                fs.WriteByte((byte)((_glyphs[i] >> 8) & 0xFF));
            }
            // Emissive intensity (as 1 byte each, mapped 0..1 → 0..255)
            for (int i = 0; i < _emissive.Length; i++)
                fs.WriteByte((byte)MathUtil.Clamp(_emissive[i] * 255.0, 0, 255));
            // Emissive RGB
            fs.Write(_emissiveRgb, 0, _emissiveRgb.Length);
        }

        public static EquirectMap? ReadFromDisk(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
                int ver = fs.ReadByte();
                if (ver != FormatVersion) return null;

                int wLo = fs.ReadByte(), wHi = fs.ReadByte();
                int hLo = fs.ReadByte(), hHi = fs.ReadByte();
                int w = wLo | (wHi << 8);
                int h = hLo | (hHi << 8);

                var map = new EquirectMap(w, h);

                if (fs.Read(map._rgb, 0, map._rgb.Length) != map._rgb.Length) return null;

                for (int i = 0; i < map._glyphs.Length; i++)
                {
                    int lo = fs.ReadByte(), hi = fs.ReadByte();
                    if (lo < 0 || hi < 0) return null;
                    map._glyphs[i] = (char)(lo | (hi << 8));
                }

                for (int i = 0; i < map._emissive.Length; i++)
                {
                    int b = fs.ReadByte();
                    if (b < 0) return null;
                    map._emissive[i] = b / 255.0;
                }

                if (fs.Read(map._emissiveRgb, 0, map._emissiveRgb.Length) != map._emissiveRgb.Length) return null;

                return map;
            }
            catch (IOException)
            {
                return null;
            }
        }
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build`
Expected: Build succeeded, no errors.

- [ ] **Step 4: Commit**

```bash
git add Rendering/EquirectMap.cs
git commit -m "feat(texture-cache): add EquirectMap data structure with bilinear sampling and disk persistence"
```

---

### Task 2: TextureCache Manager

**Files:**
- Create: `Rendering/TextureCache.cs`

The cache manager that owns in-memory tier storage and provides the lookup API.

- [ ] **Step 1: Create TextureCache.cs**

```csharp
// Rendering/TextureCache.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace SolarSystemApp.Rendering
{
    internal sealed class TextureCache
    {
        public const int TierCount = 3;
        private static readonly double[] TierFractions = { 1.0 / 16.0, 1.0 / 4.0, 1.0 };

        private int _maxWidth;
        private int _maxHeight;

        // bodyKey → EquirectMap[TierCount] (slots may be null)
        private readonly ConcurrentDictionary<string, EquirectMap?[]> _cache = new();

        private readonly string _diskRoot;

        public TextureCache(int maxWidth, string diskCacheRoot)
        {
            _maxWidth = maxWidth;
            _maxHeight = maxWidth / 2;
            _diskRoot = diskCacheRoot;
        }

        public int MaxWidth => _maxWidth;
        public int MaxHeight => _maxHeight;

        public (int w, int h) TierSize(int tier)
        {
            double f = TierFractions[tier];
            int w = Math.Max(4, (int)(_maxWidth * f));
            int h = Math.Max(2, (int)(_maxHeight * f));
            return (w, h);
        }

        public static string MakeKey(int galaxySeed, int systemIndex, int bodyIndex,
            int textureType, int paletteSeed)
        {
            return $"{galaxySeed}-{systemIndex}-{bodyIndex}-{textureType}-{paletteSeed}";
        }

        /// <summary>
        /// Returns the best cached tier whose width >= minWidth, or the highest
        /// available tier if none meets the threshold. Returns null if nothing cached.
        /// </summary>
        public EquirectMap? GetBestTier(string bodyKey, int minWidth)
        {
            if (!_cache.TryGetValue(bodyKey, out var tiers))
                return null;

            // Walk tiers from lowest to highest, pick the first that meets minWidth
            EquirectMap? best = null;
            for (int i = 0; i < TierCount; i++)
            {
                var t = tiers[i];
                if (t != null)
                {
                    best = t;
                    if (t.Width >= minWidth) return t;
                }
            }
            return best; // highest available even if below minWidth
        }

        /// <summary>
        /// Returns the highest tier index currently cached for this body, or -1 if none.
        /// </summary>
        public int GetHighestCachedTier(string bodyKey)
        {
            if (!_cache.TryGetValue(bodyKey, out var tiers))
                return -1;
            for (int i = TierCount - 1; i >= 0; i--)
                if (tiers[i] != null) return i;
            return -1;
        }

        /// <summary>
        /// Atomically installs a completed tier. Called from the background thread.
        /// Reference assignment in .NET is atomic so this is safe without locks.
        /// </summary>
        public void InstallTier(string bodyKey, int tier, EquirectMap map)
        {
            var tiers = _cache.GetOrAdd(bodyKey, _ => new EquirectMap?[TierCount]);
            tiers[tier] = map;
        }

        /// <summary>
        /// Try to load tiers from disk for a body. Returns the highest tier loaded, or -1.
        /// Called on the main thread during system entry.
        /// </summary>
        public int LoadFromDisk(string bodyKey, int galaxySeed, int systemIndex, int bodyIndex)
        {
            int highestLoaded = -1;
            var tiers = _cache.GetOrAdd(bodyKey, _ => new EquirectMap?[TierCount]);

            for (int tier = 0; tier < TierCount; tier++)
            {
                string path = GetDiskPath(galaxySeed, systemIndex, bodyIndex, tier);
                var map = EquirectMap.ReadFromDisk(path);
                if (map != null)
                {
                    tiers[tier] = map;
                    highestLoaded = tier;
                }
            }
            return highestLoaded;
        }

        /// <summary>
        /// Write a single tier to disk. Safe to call from any thread.
        /// </summary>
        public void SaveTierToDisk(string bodyKey, int tier, EquirectMap map,
            int galaxySeed, int systemIndex, int bodyIndex)
        {
            string path = GetDiskPath(galaxySeed, systemIndex, bodyIndex, tier);
            map.WriteToDisk(path);
        }

        public string GetDiskPath(int galaxySeed, int systemIndex, int bodyIndex, int tier)
        {
            return Path.Combine(_diskRoot, galaxySeed.ToString(),
                $"sys-{systemIndex}", $"body-{bodyIndex}-t{tier}.bin");
        }

        public void Clear()
        {
            _cache.Clear();
        }
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build`
Expected: Build succeeded, no errors.

- [ ] **Step 3: Commit**

```bash
git add Rendering/TextureCache.cs
git commit -m "feat(texture-cache): add TextureCache manager with tier selection and disk persistence"
```

---

### Task 3: TextureGenerator Background Worker

**Files:**
- Create: `Rendering/TextureGenerator.cs`
- Reference: `PlanetDrawer.cs` (for `SamplePlanetEx` — will be refactored in Task 5, but generator uses its own fill method for now)

The background thread that generates texture tiers off the main thread.

- [ ] **Step 1: Create TextureGenerator.cs with core structure**

```csharp
// Rendering/TextureGenerator.cs
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using AsciiEngine;
using SolarSystemApp.Util;
using SolarSystemApp.World;

namespace SolarSystemApp.Rendering
{
    internal sealed class TextureGenerator : IDisposable
    {
        private readonly TextureCache _cache;
        private readonly Channel<GenRequest> _requests;
        private readonly ConcurrentDictionary<string, int> _priorities = new();
        private readonly Thread _worker;
        private readonly CancellationTokenSource _cts = new();

        // Galaxy-level context needed for generation
        private int _galaxySeed;
        private int _systemIndex;

        internal struct GenRequest
        {
            public string BodyKey;
            public int Tier;
            public int BodySeed;
            public PlanetDrawer.PlanetTexture Texture;
            public double SpinTurns; // 0 for cache (rotation applied at sample time)
            public int BodyIndex;
            public int GalaxySeed;
            public int SystemIndex;
            public double AxisTilt;
        }

        public TextureGenerator(TextureCache cache)
        {
            _cache = cache;
            _requests = Channel.CreateUnbounded<GenRequest>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "TextureGen",
                Priority = ThreadPriority.BelowNormal
            };
            _worker.Start();
        }

        public void SetContext(int galaxySeed, int systemIndex)
        {
            _galaxySeed = galaxySeed;
            _systemIndex = systemIndex;
        }

        public void SetPriority(string bodyKey, int priority)
        {
            _priorities[bodyKey] = priority;
        }

        public void RequestGeneration(GenRequest request)
        {
            _requests.Writer.TryWrite(request);
        }

        /// <summary>
        /// Requests generation for all missing tiers of a body.
        /// Call from main thread on system entry.
        /// </summary>
        public void RequestAllMissing(string bodyKey, int highestCached,
            int bodySeed, PlanetDrawer.PlanetTexture texture, int bodyIndex,
            int galaxySeed, int systemIndex, double axisTilt)
        {
            for (int tier = highestCached + 1; tier < TextureCache.TierCount; tier++)
            {
                RequestGeneration(new GenRequest
                {
                    BodyKey = bodyKey,
                    Tier = tier,
                    BodySeed = bodySeed,
                    Texture = texture,
                    SpinTurns = 0,
                    BodyIndex = bodyIndex,
                    GalaxySeed = galaxySeed,
                    SystemIndex = systemIndex,
                    AxisTilt = axisTilt
                });
            }
        }

        private void WorkerLoop()
        {
            var token = _cts.Token;
            var pending = new System.Collections.Generic.List<GenRequest>();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Block until at least one request arrives
                    if (!_requests.Reader.TryRead(out var first))
                    {
                        // Wait for a request with cancellation support
                        var waitTask = _requests.Reader.WaitToReadAsync(token);
                        if (!waitTask.AsTask().GetAwaiter().GetResult())
                            break;
                        if (!_requests.Reader.TryRead(out first))
                            continue;
                    }

                    // Drain all pending requests to pick the highest priority
                    pending.Clear();
                    pending.Add(first);
                    while (_requests.Reader.TryRead(out var extra))
                        pending.Add(extra);

                    // Sort by priority (higher = more urgent)
                    pending.Sort((a, b) =>
                    {
                        _priorities.TryGetValue(a.BodyKey, out int pa);
                        _priorities.TryGetValue(b.BodyKey, out int pb);
                        int cmp = pb.CompareTo(pa); // descending
                        if (cmp != 0) return cmp;
                        return a.Tier.CompareTo(b.Tier); // lower tier first
                    });

                    // Process highest priority request
                    var req = pending[0];

                    // Re-queue the rest
                    for (int i = 1; i < pending.Count; i++)
                        _requests.Writer.TryWrite(pending[i]);

                    // Skip if this tier is already cached
                    if (_cache.GetHighestCachedTier(req.BodyKey) >= req.Tier)
                        continue;

                    GenerateTier(req, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void GenerateTier(GenRequest req, CancellationToken token)
        {
            var (w, h) = _cache.TierSize(req.Tier);
            var map = new EquirectMap(w, h);

            for (int py = 0; py < h; py++)
            {
                if (token.IsCancellationRequested) return;

                for (int px = 0; px < w; px++)
                {
                    double u = (px + 0.5) / w;
                    double v = (py + 0.5) / h;

                    // Convert equirectangular UV to sphere normal for SamplePlanetEx
                    double lon = (u - 0.5) * Math.PI * 2.0;
                    double lat = (v - 0.5) * Math.PI;

                    double cosLat = Math.Cos(lat);
                    double nx = Math.Sin(lon) * cosLat;
                    double ny = Math.Sin(lat);
                    double nz = Math.Cos(lon) * cosLat;

                    // Apply axis tilt (same rotation as PlanetDrawer lines 193-202)
                    double tilt = req.AxisTilt;
                    if (Math.Abs(tilt) > 1e-6)
                    {
                        double ct = Math.Cos(tilt);
                        double st = Math.Sin(tilt);
                        double x2 = nx * ct - ny * st;
                        double y2 = nx * st + ny * ct;
                        nx = x2;
                        ny = y2;
                    }

                    // Call existing texture sampling (spinTurns=0, rotation applied at render time)
                    PlanetDrawer.SamplePlanetForCache(req.BodySeed, req.Texture, nx, ny, nz, 0.0,
                        out Color fg, out char glyph,
                        out double emissive01, out Color emissiveColor);

                    // Extract RGB from Color
                    int packed = fg.Value;
                    byte r = (byte)((packed >> 16) & 0xFF);
                    byte g = (byte)((packed >> 8) & 0xFF);
                    byte b = (byte)(packed & 0xFF);

                    int emPacked = emissiveColor.Value;
                    byte emR = (byte)((emPacked >> 16) & 0xFF);
                    byte emG = (byte)((emPacked >> 8) & 0xFF);
                    byte emB = (byte)(emPacked & 0xFF);

                    map.SetTexel(px, py, r, g, b, glyph, emissive01, emR, emG, emB);
                }
            }

            // Install into live cache (atomic reference swap)
            _cache.InstallTier(req.BodyKey, req.Tier, map);

            // Write to disk asynchronously
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    _cache.SaveTierToDisk(req.BodyKey, req.Tier, map,
                        req.GalaxySeed, req.SystemIndex, req.BodyIndex);
                }
                catch (IOException) { }
            });
        }

        public void Dispose()
        {
            _cts.Cancel();
            _requests.Writer.Complete();
            _worker.Join(timeout: TimeSpan.FromSeconds(2));
            _cts.Dispose();
        }
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build`
Expected: Build error — `PlanetDrawer.SamplePlanetForCache` doesn't exist yet. That's expected; Task 4 will expose it.

- [ ] **Step 3: Commit (work in progress)**

```bash
git add Rendering/TextureGenerator.cs
git commit -m "feat(texture-cache): add TextureGenerator background worker (WIP, needs PlanetDrawer bridge)"
```

---

### Task 4: Expose SamplePlanetEx for Cache Generation

**Files:**
- Modify: `PlanetDrawer.cs` (lines 701-710)

Make the existing `SamplePlanetEx` callable from `TextureGenerator` by adding a public static wrapper. The original method stays private — we add a thin `internal` bridge.

- [ ] **Step 1: Add SamplePlanetForCache bridge method**

Add this method to `PlanetDrawer`, right before the existing `SamplePlanetEx` at line 701:

```csharp
        /// <summary>
        /// Bridge for TextureGenerator to call the existing texture sampling logic.
        /// Same as SamplePlanetEx but with internal visibility.
        /// </summary>
        internal static void SamplePlanetForCache(
            int seed,
            PlanetTexture tex,
            double nx, double ny, double nz,
            double spinTurns,
            out Color fg,
            out char glyph,
            out double emissive01,
            out Color emissiveColor)
        {
            SamplePlanetEx(seed, tex, nx, ny, nz, spinTurns,
                out fg, out glyph, out emissive01, out emissiveColor);
        }
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build`
Expected: Build succeeded. TextureGenerator can now call `PlanetDrawer.SamplePlanetForCache`.

- [ ] **Step 3: Commit**

```bash
git add PlanetDrawer.cs
git commit -m "feat(texture-cache): expose SamplePlanetForCache bridge for TextureGenerator"
```

---

### Task 5: Integrate TextureCache into PlanetDrawer Render Path

**Files:**
- Modify: `PlanetDrawer.cs` (lines 7, 51, 168-260)

This is the core performance change. Replace the per-pixel `SamplePlanetEx` call with a cache lookup when a cached texture is available.

- [ ] **Step 1: Add static TextureCache field to PlanetDrawer**

Add at line 52 (after the existing `_moonStepCache` declaration):

```csharp
        internal static TextureCache? Cache;
```

- [ ] **Step 2: Add using directive**

Add at the top of `PlanetDrawer.cs` (line 3, after existing usings):

```csharp
using SolarSystemApp.Rendering;
```

- [ ] **Step 3: Replace SamplePlanetEx call with cache lookup in DrawPlanet**

Replace the `SamplePlanetEx` call site at lines 219-221 with a cache-first lookup. The existing code at that location is:

```csharp
                    SamplePlanetEx(pSeed, p.Texture, txN, tyN, tzN, spinTurns,
                        out Color fg, out char texGlyph,
                        out double emissive01, out Color emissiveColor);
```

Replace with:

```csharp
                    Color fg;
                    char texGlyph;
                    double emissive01;
                    Color emissiveColor;

                    bool usedCache = false;
                    if (Cache != null)
                    {
                        string bodyKey = TextureCache.MakeKey(
                            Cache._galaxySeed, Cache._systemIndex, _currentBodyIndex,
                            (int)p.Texture, pSeed);
                        var tier = Cache.GetBestTier(bodyKey, radChars * 2);
                        if (tier != null)
                        {
                            // Reconstruct u,v from the tilted normal (same math as SamplePlanetEx lines 714-718)
                            double lon = Math.Atan2(txN, tzN);
                            double cacheU = lon / (Math.PI * 2.0) + 0.5;
                            cacheU = Frac(cacheU);
                            // Apply rotation offset
                            cacheU = Frac(cacheU + spinTurns);
                            double cacheV = Math.Asin(MathUtil.Clamp(tyN, -1.0, 1.0)) / Math.PI + 0.5;

                            tier.Sample(cacheU, cacheV,
                                out byte cr, out byte cg, out byte cb,
                                out texGlyph,
                                out emissive01,
                                out byte emR, out byte emG, out byte emB);
                            fg = Color.FromRgb(cr, cg, cb);
                            emissiveColor = Color.FromRgb(emR, emG, emB);
                            usedCache = true;
                        }
                    }

                    if (!usedCache)
                    {
                        SamplePlanetEx(pSeed, p.Texture, txN, tyN, tzN, spinTurns,
                            out fg, out texGlyph,
                            out emissive01, out emissiveColor);
                    }
```

**Note:** This requires passing galaxy/system/body context into the draw methods. We need a lightweight way to thread this through. Add a static field for the current body index:

Add after the `Cache` field (around line 53):

```csharp
        internal static int _currentBodyIndex;
```

The `_galaxySeed` and `_systemIndex` fields will need to be added to `TextureCache` as well (see step below).

- [ ] **Step 4: Add context fields to TextureCache**

In `Rendering/TextureCache.cs`, add public fields for the current galaxy/system context. Add after the `_diskRoot` field:

```csharp
        internal int _galaxySeed;
        internal int _systemIndex;

        public void SetContext(int galaxySeed, int systemIndex)
        {
            _galaxySeed = galaxySeed;
            _systemIndex = systemIndex;
        }
```

- [ ] **Step 5: Apply the same cache lookup to DrawMoon**

In `DrawMoon`, the existing moon sampling call at line 350 is:

```csharp
                    SamplePlanet(mSeed, m.Texture, nx, ny, nz, spinTurns, out Color fg, out char texGlyph);
```

Note: moons don't apply axis tilt in their render loop — `nx, ny, nz` are used directly (no tilt rotation like planets have). Replace line 350 with:

```csharp
                    Color fg;
                    char texGlyph;

                    bool usedCache = false;
                    if (Cache != null)
                    {
                        string bodyKey = TextureCache.MakeKey(
                            Cache._galaxySeed, Cache._systemIndex, _currentBodyIndex,
                            (int)m.Texture, mSeed);
                        var tier = Cache.GetBestTier(bodyKey, radChars * 2);
                        if (tier != null)
                        {
                            double lon = Math.Atan2(nx, nz);
                            double cacheU = lon / (Math.PI * 2.0) + 0.5;
                            cacheU = Frac(cacheU);
                            cacheU = Frac(cacheU + spinTurns);
                            double cacheV = Math.Asin(MathUtil.Clamp(ny, -1.0, 1.0)) / Math.PI + 0.5;

                            tier.Sample(cacheU, cacheV,
                                out byte cr, out byte cg, out byte cb,
                                out texGlyph,
                                out _, out _, out _, out _);
                            fg = Color.FromRgb(cr, cg, cb);
                            usedCache = true;
                        }
                    }

                    if (!usedCache)
                    {
                        SamplePlanet(mSeed, m.Texture, nx, ny, nz, spinTurns,
                            out fg, out texGlyph);
                    }
```

- [ ] **Step 6: Verify it builds**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add PlanetDrawer.cs Rendering/TextureCache.cs
git commit -m "feat(texture-cache): integrate cache lookup into PlanetDrawer render path with noise fallback"
```

---

### Task 6: Wire Up Cache + Generator in SolarSystemScene

**Files:**
- Modify: `SolarSystemScene.cs` (lines ~126, ~362-405, ~532, ~2787-2812)

Connect the TextureCache and TextureGenerator to the scene lifecycle.

- [ ] **Step 1: Add using directive and fields**

Add the using at the top of `SolarSystemScene.cs`:

```csharp
using SolarSystemApp.Rendering;
```

Add fields in the field declaration area (around line 126):

```csharp
        private TextureCache? _textureCache;
        private TextureGenerator? _textureGenerator;
        private const int TextureMaxWidth = 1024;
```

- [ ] **Step 2: Initialize cache and generator in Init()**

In `Init()`, after the existing subsystem initialization (around line 405, before the event log entry), add:

```csharp
            // Texture cache
            string cacheRoot = Path.Combine(AppContext.BaseDirectory, "cache", "textures");
            _textureCache = new TextureCache(TextureMaxWidth, cacheRoot);
            _textureGenerator = new TextureGenerator(_textureCache);
            PlanetDrawer.Cache = _textureCache;

            // Preload textures for initial system
            PreloadSystemTextures();
```

Add the `using System.IO;` directive at the top if not already present.

- [ ] **Step 3: Add PreloadSystemTextures method**

Add this method to `SolarSystemScene`:

```csharp
        private void PreloadSystemTextures()
        {
            if (_textureCache == null || _textureGenerator == null || _sys == null)
                return;

            _textureCache.SetContext(_galaxy.Seed, _systemIndex);
            _textureGenerator.SetContext(_galaxy.Seed, _systemIndex);

            int bodyIdx = 0;
            foreach (var planet in _sys.Planets)
            {
                int pSeed = _sys.Seed ^ (planet.OrbitIndex * 7919);
                string key = TextureCache.MakeKey(_galaxy.Seed, _systemIndex, bodyIdx,
                    (int)planet.Texture, pSeed);

                // Try disk first
                int highest = _textureCache.LoadFromDisk(key, _galaxy.Seed, _systemIndex, bodyIdx);

                if (highest < 0)
                {
                    // No disk cache — generate Tier 0 synchronously
                    GenerateTier0Sync(key, pSeed, planet.Texture, bodyIdx, planet.AxisTilt);
                    highest = 0;
                }

                // Queue remaining tiers for background generation
                _textureGenerator.RequestAllMissing(key, highest,
                    pSeed, planet.Texture, bodyIdx,
                    _galaxy.Seed, _systemIndex, planet.AxisTilt);

                bodyIdx++;

                // Also preload moons for this planet
                foreach (var moon in planet.Moons)
                {
                    int mSeed = _sys.Seed ^ (moon.OrbitIndex * 7919 + 3);
                    string mKey = TextureCache.MakeKey(_galaxy.Seed, _systemIndex, bodyIdx,
                        (int)moon.Texture, mSeed);

                    int mHighest = _textureCache.LoadFromDisk(mKey, _galaxy.Seed, _systemIndex, bodyIdx);

                    if (mHighest < 0)
                    {
                        GenerateTier0Sync(mKey, mSeed, moon.Texture, bodyIdx, 0.0);
                        mHighest = 0;
                    }

                    _textureGenerator.RequestAllMissing(mKey, mHighest,
                        mSeed, moon.Texture, bodyIdx,
                        _galaxy.Seed, _systemIndex, 0.0);

                    bodyIdx++;
                }
            }
        }

        private void GenerateTier0Sync(string bodyKey, int bodySeed,
            PlanetDrawer.PlanetTexture texture, int bodyIndex, double axisTilt)
        {
            var (w, h) = _textureCache!.TierSize(0);
            var map = new EquirectMap(w, h);

            for (int py = 0; py < h; py++)
            {
                for (int px = 0; px < w; px++)
                {
                    double u = (px + 0.5) / w;
                    double v = (py + 0.5) / h;

                    double lon = (u - 0.5) * Math.PI * 2.0;
                    double lat = (v - 0.5) * Math.PI;
                    double cosLat = Math.Cos(lat);
                    double nx = Math.Sin(lon) * cosLat;
                    double ny = Math.Sin(lat);
                    double nz = Math.Cos(lon) * cosLat;

                    if (Math.Abs(axisTilt) > 1e-6)
                    {
                        double ct = Math.Cos(axisTilt);
                        double st = Math.Sin(axisTilt);
                        double x2 = nx * ct - ny * st;
                        double y2 = nx * st + ny * ct;
                        nx = x2;
                        ny = y2;
                    }

                    PlanetDrawer.SamplePlanetForCache(bodySeed, texture, nx, ny, nz, 0.0,
                        out Color fg, out char glyph,
                        out double emissive01, out Color emissiveColor);

                    int packed = fg.Value;
                    byte r = (byte)((packed >> 16) & 0xFF);
                    byte g = (byte)((packed >> 8) & 0xFF);
                    byte b = (byte)(packed & 0xFF);

                    int emPacked = emissiveColor.Value;
                    byte emR = (byte)((emPacked >> 16) & 0xFF);
                    byte emG = (byte)((emPacked >> 8) & 0xFF);
                    byte emB = (byte)(emPacked & 0xFF);

                    map.SetTexel(px, py, r, g, b, glyph, emissive01, emR, emG, emB);
                }
            }

            _textureCache.InstallTier(bodyKey, 0, map);
        }
```

- [ ] **Step 4: Update priority in the render loop**

In the `DrawPlanets` method (around line 2231), before the `PlanetDrawer.DrawPlanet` call, set the body index and update priority:

```csharp
                PlanetDrawer._currentBodyIndex = bodyIdx;
                if (_textureGenerator != null)
                {
                    // Priority based on screen-space radius — larger = more urgent
                    int priority = pr; // pr is the planet's screen radius in chars
                    string key = TextureCache.MakeKey(_galaxy.Seed, _systemIndex, bodyIdx,
                        (int)p.Texture, _sys.Seed ^ (p.OrbitIndex * 7919));
                    _textureGenerator.SetPriority(key, priority);
                }
```

Apply the same pattern in `DrawMoons` (around line 2293) for moon bodies.

- [ ] **Step 5: Trigger preload on system change**

In `SetActiveSystem()` (around line 2787), after the existing system setup, trigger texture preloading:

```csharp
            // After existing code at ~line 2810
            PreloadSystemTextures();
```

- [ ] **Step 6: Dispose generator on scene teardown**

If there's any cleanup path (check for `IDisposable` implementation or a cleanup method), add:

```csharp
            _textureGenerator?.Dispose();
```

If `SolarSystemScene` doesn't implement `IDisposable`, this can be handled in the `AsciiRunner` exit path or left to process termination (the background thread is marked `IsBackground = true` so it will die with the process).

- [ ] **Step 7: Verify it builds**

Run: `dotnet build`
Expected: Build succeeded. May need to adjust exact field names and method access based on the actual `Planet`/`Moon` property names (check `Entities.cs` for `OrbitIndex`, `Texture`, `AxisTilt`, `Moons`).

- [ ] **Step 8: Commit**

```bash
git add SolarSystemScene.cs
git commit -m "feat(texture-cache): wire up TextureCache and TextureGenerator in scene lifecycle"
```

---

### Task 7: End-to-End Smoke Test

**Files:**
- No new files. Manual verification.

Run the game and verify the texture caching system works correctly.

- [ ] **Step 1: Clean build**

Run: `dotnet build --no-incremental`
Expected: Build succeeded, no warnings related to texture cache code.

- [ ] **Step 2: First run (cold cache)**

Run: `dotnet run`

Verify:
- Game starts and renders planets as before (Tier 0 generated synchronously)
- No visual glitches or missing textures
- Planets rotate correctly
- Check that `cache/textures/` directory is created with `.bin` files appearing

- [ ] **Step 3: Zoom test**

While the game is running:
- Zoom into a planet (scroll wheel or zoom keys)
- Verify that the planet texture sharpens as higher tiers become available
- Verify that frame rate stays stable during zoom (no stuttering from per-frame noise)
- Compare perceived smoothness vs. the old code at similar zoom levels

- [ ] **Step 4: Second run (warm cache)**

Exit and restart:

Run: `dotnet run`

Verify:
- System loads faster (disk-cached textures loaded instead of regenerated)
- Textures look identical to the first run
- `cache/textures/` directory contains tier files from previous run

- [ ] **Step 5: System change test**

Navigate to a different star system:
- Verify textures for the new system generate without hitches
- Return to the original system — textures should load instantly from disk cache

- [ ] **Step 6: Commit any fixes**

If any issues were found and fixed during testing:

```bash
git add -A
git commit -m "fix(texture-cache): fixes from smoke testing"
```

---

### Task 8: Add .gitignore Entry for Cache Directory

**Files:**
- Modify: `.gitignore` (create if it doesn't exist)

- [ ] **Step 1: Add cache directory to .gitignore**

Add these lines to `.gitignore`:

```
# Texture cache (generated, machine-specific)
cache/
.superpowers/
```

- [ ] **Step 2: Commit**

```bash
git add .gitignore
git commit -m "chore: add texture cache and .superpowers to .gitignore"
```
