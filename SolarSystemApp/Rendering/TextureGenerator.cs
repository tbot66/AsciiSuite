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

                    // Tilt is NOT applied here — the renderer applies it at sample time
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
