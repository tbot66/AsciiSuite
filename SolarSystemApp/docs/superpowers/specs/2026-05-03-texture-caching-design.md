# Texture Caching System — Design Spec

**Date:** 2026-05-03
**Scope:** Priority 1 of 3 rendering upgrades (texture caching → camera controls → rendering fidelity)
**Goal:** Eliminate per-frame procedural texture computation by caching equirectangular maps with progressive background refinement and disk persistence.

## Problem

All celestial body textures are procedurally generated every frame via `SamplePlanetEx()` in `PlanetDrawer.cs`. Each visible pixel triggers multiple FBm noise evaluations (14–22 octaves depending on texture type, ~56–88 hash operations per pixel). At close zoom with stepSize=1, a single EarthLike planet computes ~25,000 pixels × ~3,000 ops = ~75 million operations per frame. This is the primary cause of frame rate degradation at high zoom levels and the main bottleneck preventing future resolution increases.

## Solution Overview

Replace per-pixel noise computation with a texture lookup from a pre-computed equirectangular map. Textures are generated progressively in the background using a tiered mip-chain approach, cached in memory during play, and persisted to disk for instant loading on revisit.

Planet rotation is handled by offsetting the U coordinate during sampling — the cached map remains valid regardless of rotation angle. Lighting (Lambert shading from sun direction) and atmosphere halos are applied at render time since they depend on the body's orbital position relative to the sun.

## Cache Data Model

### Equirectangular Map

Each body's texture is stored as an equirectangular projection (longitude × latitude) in a flat RGB byte array (3 bytes per texel). This format:

- Maps naturally to sphere projection (existing `u, v` math in `SamplePlanetEx()` is directly usable)
- Supports rotation via U-offset with seamless wrapping
- Is resolution-agnostic — same format at any size

### Mip-Chain Tiers

Three resolution tiers defined as fractions of a configurable max resolution:

| Tier | Fraction | Size at max=1024 | Size at max=2048 | Use Case |
|------|----------|-------------------|-------------------|----------|
| 0 | 1/16 | 64×32 | 128×64 | Distant bodies, instant generation |
| 1 | 1/4 | 256×128 | 512×256 | Mid-zoom, background-generated |
| 2 | 1/1 | 1024×512 | 2048×1024 | Close-up, background-generated |

The max resolution is a single configurable value. Changing it scales all tiers proportionally with zero code changes. This ensures the system is ready for future resolution increases (e.g., half-block rendering doubling effective vertical resolution).

### Cache Key

```
{galaxySeed}-{systemIndex}-{bodyIndex}-{textureType}-{paletteSeed}
```

Deterministic — same seed always produces the same texture. Disk caches are valid across sessions.

## Progressive Generation Pipeline

### Synchronous Phase (on system entry or body becoming visible)

1. Check disk cache for this body's key.
   - **HIT:** Load the highest available tier file into memory. Queue any missing higher tiers for background generation.
   - **MISS:** Generate Tier 0 synchronously. At ~2,048 texels this completes in <1ms — imperceptible even for 10–15 bodies in a system.

### Asynchronous Phase (background thread)

Generation runs on a **dedicated background thread**, completely off the main game loop. The main thread never pays any generation cost — it only does cheap texture lookups.

**Thread architecture:**

- `TextureGenerator` spawns a single long-lived background thread on startup.
- The main thread submits generation requests via a `Channel<T>` (lock-free concurrent queue). Each request specifies the body key, target tier, and priority.
- The background thread pulls requests in priority order and generates full `EquirectMap` objects. No chunking needed — the background thread can take as long as it wants without affecting framerate.

**Thread safety via atomic reference swap:**

- The background thread generates into a **new `EquirectMap` instance** — it never mutates a texture the renderer is currently reading.
- When a tier completes, the background thread writes the new reference into the cache slot. In .NET, reference assignment is atomic — the renderer will either see the old tier or the new tier on any given frame, never a half-written texture.
- The renderer reads whatever reference is in the cache at frame start. Worst case: it uses the old tier for one additional frame before picking up the upgrade. No locks or mutexes required.

**Disk writes** also happen on the background thread, after the in-memory swap. The renderer never depends on disk state.

### Dynamic Re-prioritization

- The main thread signals priority changes (zoom level, camera target) via a lightweight shared priority map (e.g., `ConcurrentDictionary<string, int>`).
- The background thread checks priorities when picking its next request. No need for the main thread to reorder the queue — the background thread reads current priorities at dequeue time.
- Player zooms toward a planet → its priority value increases → background thread picks it up next.
- Player zooms away → priority drops; Tier 2 generation may be deferred indefinitely until the body is viewed up close.

### Shutdown

`TextureGenerator.Dispose()` signals the background thread to drain and exit via a `CancellationToken`. Any in-progress tier generation is abandoned (it will be regenerated on next launch if needed).

## Disk Persistence

### Directory Structure

```
SolarSystemApp/
  cache/
    textures/
      {galaxySeed}/
        sys-{systemIndex}/
          body-{bodyIndex}-t{tier}.bin
```

### File Format

Raw binary with a minimal header:

| Field | Size | Description |
|-------|------|-------------|
| Version | 1 byte | Format version for cache invalidation |
| Width | 2 bytes (uint16) | Texture width in pixels |
| Height | 2 bytes (uint16) | Texture height in pixels |
| Pixel data | W×H×3 bytes | Flat RGB array, row-major |

No compression. A max-res Tier 2 at 1024×512 is ~1.5MB. Lower tiers are negligible. Total disk usage for a full galaxy of hundreds of systems: a few hundred MB at most.

### Cache Invalidation

The format version byte allows invalidating all caches when the texture generation algorithm changes (e.g., noise parameters are tweaked). Bump the version → old files are ignored and regenerated on next visit.

### Lifecycle

- Tiers are written to disk as they complete. Each tier is its own file, so partial generation (only Tier 0 and 1 exist) is naturally handled.
- On system entry, the loader checks which tier files exist for each body and loads the best one.
- No explicit eviction policy — disk space is trivially small for this use case.

## Renderer Integration

### Sampling Path (replaces hot loop in PlanetDrawer.cs)

For each visible pixel of a body:

1. **Sphere projection** (unchanged): compute `u` (longitude, 0–1) and `v` (latitude, 0–1) from screen position.
2. **Texture lookup** (new): `equirectMap.Sample(u + rotationOffset, v)` with bilinear interpolation. The `rotationOffset` is `rotationAngle / (2π)`, wrapping seamlessly.
3. **Lighting** (unchanged): Lambert shading from sun direction, atmosphere halos — computed per-frame as today.

### Tier Selection

The renderer picks the lowest-resolution tier whose width exceeds the body's current screen-space diameter. A body 20 chars wide uses Tier 0 (64px wide); a body 80 chars wide needs Tier 2.

### Fallback

If no cached tier is available (defensive case — shouldn't happen since Tier 0 is synchronous), fall back to the existing `SamplePlanetEx()` noise path. The old code is preserved as both the generation source for cache tiers and the emergency fallback.

### What Moves Out of the Hot Path

- All FBm noise evaluation
- Seamless wrapping math
- Per-pixel texture-type branching (EarthLike vs Rocky vs GasGiant etc.)

These move to `TextureGenerator` (background worker). The per-frame render cost drops from ~3,000 ops/pixel (noise) to ~100–150 ops/pixel (array lookup + lighting). Estimated **15–20× speedup** at close zoom.

## Code Structure

### New Files (all in `Rendering/`)

| File | Responsibility |
|------|---------------|
| `EquirectMap.cs` | Data structure: flat RGB array, `Sample(u, v)` with bilinear interpolation, disk serialization (header + raw bytes) |
| `TextureCache.cs` | Cache manager: `bodyKey → EquirectMap[]` dictionary, `GetBestTier(bodyKey, minWidth)` API, disk load on miss |
| `TextureGenerator.cs` | Background thread worker: `Channel<T>` request queue, priority-ordered generation, atomic reference swap into cache, disk persistence. Owns its own thread lifecycle via `CancellationToken`. |

### Modified Files

| File | Changes |
|------|---------|
| `PlanetDrawer.cs` | `SamplePlanetEx()` hot loop: check TextureCache first, fall back to noise. Remove per-pixel FBm from render path. |
| `SolarSystemScene.cs` | `Init()`: create TextureCache + TextureGenerator (spawns background thread). `Update()`: update priority map on zoom/camera changes. System entry: submit generation requests for all bodies. No per-frame tick cost. |
| `PlanetTextures.cs` | Extract texture-type generation logic into methods that fill an `EquirectMap` (iterate pixel grid). No algorithm changes — restructure for reuse by `TextureGenerator`. |

### Unchanged Files

- `Util/HashNoise.cs` — noise functions stay as-is (called by TextureGenerator)
- `World/*` — no changes to data model or world generation
- `Persistence/SaveManager.cs` — texture cache is independent of save system
- `Render/SolarRender.cs` — orbit/trail rendering unaffected

## Performance Summary

| Metric | Before | After |
|--------|--------|-------|
| Per-pixel cost (close zoom) | ~3,000 ops (FBm noise) | ~100–150 ops (lookup + lighting) |
| Frame cost for 1 EarthLike planet at radius 60 | ~75M ops | ~3.5M ops |
| Speedup at close zoom | — | ~15–20× |
| Main thread generation cost per frame | 100% of generation work | 0 (offloaded to background thread) |
| Memory per system (10 bodies, all tiers) | 0 (computed on fly) | ~15MB worst case |
| Disk per system (all tiers) | 0 | ~15MB |
| Tier 0 generation time | — | <1ms per body (synchronous) |
| System entry latency (cold) | 0 | <15ms (Tier 0 for all bodies) |
| System entry latency (cached) | 0 | ~5–10ms (disk load) |
