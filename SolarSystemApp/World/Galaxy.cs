using System;
using System.Collections.Generic;
using AsciiEngine;
using SolarSystemApp.Util;

namespace SolarSystemApp.World
{
    internal sealed class Galaxy
    {
        public int Seed;

        public readonly List<StarSystem> Systems = new List<StarSystem>();

        public readonly List<Link> Links = new List<Link>();

        public struct Link
        {
            public int A;
            public int B;

            public Link(int a, int b)
            {
                A = a;
                B = b;
            }
        }

        public void Build(int seed, int count)
        {
            Seed = seed;
            Systems.Clear();
            Links.Clear();

            var rng = new Random(seed);

            for (int i = 0; i < count; i++)
            {
                var sys = new StarSystem();
                sys.Seed = seed + i * 999;

                // Position in galaxy map
                sys.GalaxyX = (rng.NextDouble() * 2.0 - 1.0) * 40.0;
                sys.GalaxyY = (rng.NextDouble() * 2.0 - 1.0) * 20.0;

                // NEW: choose system kind (weighted)
                sys.Kind = StarSystemLogic.PickSystemKind(seed, i);

                // Build content based on kind (StarSystem uses your existing random system logic)
                StarSystemLogic.BuildByKind(sys);

                // Name based on kind (keeps names readable in UI)
                sys.Name = StarSystemLogic.MakeSystemName(sys.Kind, i + 1);

                Systems.Add(sys);
            }

            RebuildConnections(neighborsPerNode: 3);
        }

        public StarSystem Get(int index)
        {
            if (Systems.Count == 0) return null;
            index = MathUtil.ClampInt(index, 0, Systems.Count - 1);
            return Systems[index];
        }

        public void RebuildConnections(int neighborsPerNode = 3)
        {
            Links.Clear();

            int n = Systems.Count;
            if (n <= 1) return;

            neighborsPerNode = MathUtil.ClampInt(neighborsPerNode, 1, Math.Max(1, n - 1));

            var set = new HashSet<long>();

            for (int i = 0; i < n; i++)
            {
                var dists = new List<(double d2, int j)>(n - 1);
                double ix = Systems[i].GalaxyX;
                double iy = Systems[i].GalaxyY;

                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    double dx = ix - Systems[j].GalaxyX;
                    double dy = iy - Systems[j].GalaxyY;
                    double d2 = dx * dx + dy * dy;
                    dists.Add((d2, j));
                }

                dists.Sort((a, b) => a.d2.CompareTo(b.d2));

                int take = Math.Min(neighborsPerNode, dists.Count);
                for (int k = 0; k < take; k++)
                {
                    int a = i;
                    int b = dists[k].j;
                    if (a == b) continue;

                    if (a > b) { int tmp = a; a = b; b = tmp; }

                    long key = ((long)a << 32) | (uint)b;
                    if (set.Add(key))
                        Links.Add(new Link(a, b));
                }
            }
        }

        public bool AreLinked(int a, int b)
        {
            if (a == b) return false;
            if (a > b) { int tmp = a; a = b; b = tmp; }

            for (int i = 0; i < Links.Count; i++)
            {
                var L = Links[i];
                if (L.A == a && L.B == b) return true;
            }
            return false;
        }

        public IEnumerable<int> NeighborsOf(int index)
        {
            for (int i = 0; i < Links.Count; i++)
            {
                var L = Links[i];
                if (L.A == index) yield return L.B;
                else if (L.B == index) yield return L.A;
            }
        }
    }
}
