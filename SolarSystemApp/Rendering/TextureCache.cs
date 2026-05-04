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

        internal int _galaxySeed;
        internal int _systemIndex;

        public void SetContext(int galaxySeed, int systemIndex)
        {
            _galaxySeed = galaxySeed;
            _systemIndex = systemIndex;
        }

        public void Clear()
        {
            _cache.Clear();
        }
    }
}
