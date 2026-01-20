using System;
using AsciiEngine;
using SolarSystemApp.Util;

namespace SolarSystemApp.World
{
    internal static class StarSystemLogic
    {
        // ============================================================
        // NEW: Kind selection + naming
        // ============================================================

        public static SystemKind PickSystemKind(int galaxySeed, int index)
        {
            // Deterministic “global” roll per system index
            int s = galaxySeed ^ (index * 0x1f123bb5);
            double r = HashNoise.Hash01(s, 101, 202);

            // Weights tuned so most are normal star systems.
            // Adjust freely.
            if (r < 0.70) return SystemKind.StarSystem;       // 70%
            if (r < 0.80) return SystemKind.AsteroidField;    // 10%
            if (r < 0.88) return SystemKind.Nebula;           // 8%
            if (r < 0.92) return SystemKind.DebrisDisk;       // 4%
            if (r < 0.95) return SystemKind.BrownDwarf;       // 3%
            if (r < 0.97) return SystemKind.Protostar;        // 2%
            if (r < 0.985) return SystemKind.NeutronStar;     // 1.5%
            if (r < 0.993) return SystemKind.BlackHole;       // 0.8%
            if (r < 0.998) return SystemKind.SupernovaRemnant;// 0.5%
            return SystemKind.DarkNebula;                     // 0.2%
        }

        public static string MakeSystemName(SystemKind kind, int ordinal)
        {
            return kind switch
            {
                SystemKind.StarSystem => $"System-{ordinal}",
                SystemKind.AsteroidField => $"Field-{ordinal}",
                SystemKind.Nebula => $"Nebula-{ordinal}",
                SystemKind.DarkNebula => $"Dark-{ordinal}",
                SystemKind.DebrisDisk => $"Disk-{ordinal}",
                SystemKind.Protostar => $"Proto-{ordinal}",
                SystemKind.BrownDwarf => $"Dwarf-{ordinal}",
                SystemKind.NeutronStar => $"Pulsar-{ordinal}",
                SystemKind.BlackHole => $"Horizon-{ordinal}",
                SystemKind.SupernovaRemnant => $"Remnant-{ordinal}",
                _ => $"System-{ordinal}"
            };
        }

        // ============================================================
        // NEW: Single entry point used by Galaxy.Build
        // ============================================================

        public static void BuildByKind(StarSystem sys)
        {
            // Clear everything deterministically every time we rebuild
            ClearAll(sys);

            // Deterministic RNG based on system seed
            Random rng = new Random(sys.Seed ^ 0x6C8E9CF1);

            // Always keep at least one ship so your existing “visit system” flow still works.
            // (Matches your current behavior.)
            void AddDefaultShip()
            {
                sys.Ships.Add(new Ship
                {
                    Name = "Courier-1",
                    WX = 0.0,
                    WY = -2.0,
                    Glyph = '>',
                    Fg = AnsiColor.BrightWhite,
                    Mode = ShipMode.Idle
                });
            }

            switch (sys.Kind)
            {
                case SystemKind.StarSystem:
                    sys.HasStar = true;
                    sys.Descriptor = "Star System";

                    // Let the existing generator decide planets/moons/rings.
                    // (Sun properties can be slightly varied too.)
                    PickStarStyle(rng, out sys.SunColor, out sys.SunRadiusWorld, out sys.CoronaRadiusWorld);

                    BuildRandomSystem(sys);
                    AddDefaultShip();
                    return;

                case SystemKind.AsteroidField:
                    sys.HasStar = false;
                    sys.Descriptor = "Asteroid Field";
                    sys.SunRadiusWorld = 0.0;
                    sys.CoronaRadiusWorld = 0.0;
                    sys.SunColor = AnsiColor.Black;

                    BuildAsteroidField(sys, rng, radiusWorld: 22.0, countMin: 280, countMax: 650);
                    AddDefaultShip();
                    return;

                case SystemKind.Nebula:
                    sys.HasStar = false;
                    sys.Descriptor = "Nebula";
                    sys.SunRadiusWorld = 0.0;
                    sys.CoronaRadiusWorld = 0.0;
                    sys.SunColor = AnsiColor.Black;

                    BuildNebula(sys, rng, cloudsMin: 6, cloudsMax: 14, dark: false);
                    // A few rocks inside looks cool later when you render
                    BuildAsteroidField(sys, rng, radiusWorld: 18.0, countMin: 60, countMax: 160);
                    AddDefaultShip();
                    return;

                case SystemKind.DarkNebula:
                    sys.HasStar = false;
                    sys.Descriptor = "Dark Nebula";
                    sys.SunRadiusWorld = 0.0;
                    sys.CoronaRadiusWorld = 0.0;
                    sys.SunColor = AnsiColor.Black;

                    BuildNebula(sys, rng, cloudsMin: 8, cloudsMax: 18, dark: true);
                    BuildAsteroidField(sys, rng, radiusWorld: 16.0, countMin: 80, countMax: 220);
                    AddDefaultShip();
                    return;

                case SystemKind.DebrisDisk:
                    sys.HasStar = true;
                    sys.Descriptor = "Debris Disk";
                    PickStarStyle(rng, out sys.SunColor, out sys.SunRadiusWorld, out sys.CoronaRadiusWorld);

                    // No planets, just a dense disk (you can add planets later if you want).
                    BuildDebrisDisk(sys, rng, inner: 5.5, outer: 18.0, countMin: 420, countMax: 920);
                    AddDefaultShip();
                    return;

                case SystemKind.Protostar:
                    sys.HasStar = true;
                    sys.Descriptor = "Protostar";
                    sys.SunColor = AnsiColor.BrightYellow;
                    sys.SunRadiusWorld = 0.95;
                    sys.CoronaRadiusWorld = 1.65;

                    BuildNebula(sys, rng, cloudsMin: 10, cloudsMax: 20, dark: false);
                    BuildDebrisDisk(sys, rng, inner: 4.0, outer: 14.0, countMin: 260, countMax: 520);
                    AddDefaultShip();
                    return;

                case SystemKind.BrownDwarf:
                    sys.HasStar = true;
                    sys.Descriptor = "Brown Dwarf";
                    sys.SunColor = AnsiColor.BrightRed;
                    sys.SunRadiusWorld = 0.55;
                    sys.CoronaRadiusWorld = 0.90;

                    // Sparse: 0–3 planets
                    BuildSparsePlanets(sys, rng, planetMax: 3);
                    BuildAsteroidField(sys, rng, radiusWorld: 20.0, countMin: 120, countMax: 280);
                    AddDefaultShip();
                    return;

                case SystemKind.NeutronStar:
                    sys.HasStar = true;
                    sys.Descriptor = "Neutron Star";
                    sys.SunColor = AnsiColor.BrightCyan;
                    sys.SunRadiusWorld = 0.28;
                    sys.CoronaRadiusWorld = 0.85;

                    BuildSparsePlanets(sys, rng, planetMax: 2);
                    BuildDebrisDisk(sys, rng, inner: 3.5, outer: 10.0, countMin: 160, countMax: 320);
                    AddDefaultShip();
                    return;

                case SystemKind.BlackHole:
                    sys.HasStar = false;
                    sys.Descriptor = "Black Hole";
                    sys.SunRadiusWorld = 0.0;
                    sys.CoronaRadiusWorld = 0.0;
                    sys.SunColor = AnsiColor.Black;

                    // Accretion debris: tighter, denser ring
                    BuildDebrisDisk(sys, rng, inner: 2.8, outer: 11.5, countMin: 520, countMax: 1200);
                    BuildNebula(sys, rng, cloudsMin: 4, cloudsMax: 8, dark: true);
                    AddDefaultShip();
                    return;

                case SystemKind.SupernovaRemnant:
                    sys.HasStar = false;
                    sys.Descriptor = "Supernova Remnant";
                    sys.SunRadiusWorld = 0.0;
                    sys.CoronaRadiusWorld = 0.0;
                    sys.SunColor = AnsiColor.Black;

                    BuildNebulaShell(sys, rng, shellRadius: 15.0, shellThickness: 3.5);
                    BuildAsteroidField(sys, rng, radiusWorld: 20.0, countMin: 140, countMax: 360);
                    AddDefaultShip();
                    return;

                default:
                    // Safe fallback
                    sys.Kind = SystemKind.StarSystem;
                    sys.HasStar = true;
                    sys.Descriptor = "Star System";
                    PickStarStyle(rng, out sys.SunColor, out sys.SunRadiusWorld, out sys.CoronaRadiusWorld);
                    BuildRandomSystem(sys);
                    AddDefaultShip();
                    return;
            }
        }

        private static void ClearAll(StarSystem sys)
        {
            sys.Planets.Clear();
            sys.Stations.Clear();
            sys.Ships.Clear();

            sys.Asteroids.Clear();
            sys.Nebulae.Clear();
        }

        private static void PickStarStyle(Random rng, out AnsiColor c, out double rStar, out double rCorona)
        {
            // Super simple star variety (enough for now).
            // Later you can map to O/B/A/F/G/K/M classes.
            double roll = rng.NextDouble();

            if (roll < 0.15) { c = AnsiColor.BrightRed; rStar = 0.60; rCorona = 1.05; return; }     // M/K-ish
            if (roll < 0.45) { c = AnsiColor.BrightYellow; rStar = 0.72; rCorona = 1.22; return; }  // G-ish
            if (roll < 0.75) { c = AnsiColor.BrightWhite; rStar = 0.78; rCorona = 1.28; return; }   // F/A-ish
            { c = AnsiColor.BrightCyan; rStar = 0.88; rCorona = 1.45; return; }                      // hot/blue-ish
        }

        // ============================================================
        // NEW: Builders for non-standard systems
        // ============================================================

        private static void BuildAsteroidField(StarSystem sys, Random rng, double radiusWorld, int countMin, int countMax)
        {
            int n = Range(rng, countMin, countMax);

            for (int i = 0; i < n; i++)
            {
                // Cluster-ish distribution: more density toward center + some clumps
                double a = rng.NextDouble() * Math.PI * 2.0;
                double u = rng.NextDouble();
                double r = radiusWorld * Math.Sqrt(u);

                // occasional clump bias
                if (rng.NextDouble() < 0.15)
                {
                    r *= 0.45 + 0.35 * rng.NextDouble();
                    a += (rng.NextDouble() * 2.0 - 1.0) * 0.25;
                }

                double x = Math.Cos(a) * r;
                double y = Math.Sin(a) * r;

                // thin-ish z layer
                double z = (rng.NextDouble() * 2.0 - 1.0) * 0.35;

                // Sizes + glyphs
                double size = 0.05 + rng.NextDouble() * 0.22;

                char g =
                    (size < 0.10) ? '.' :
                    (size < 0.16) ? 'o' :
                    (size < 0.22) ? 'O' : '@';

                AnsiColor fg =
                    (rng.NextDouble() < 0.75) ? AnsiColor.BrightBlack : AnsiColor.BrightWhite;

                sys.Asteroids.Add(new Asteroid
                {
                    WX = x,
                    WY = y,
                    WZ = z,
                    RadiusWorld = size,
                    Glyph = g,
                    Fg = fg
                });
            }
        }

        private static void BuildNebula(StarSystem sys, Random rng, int cloudsMin, int cloudsMax, bool dark)
        {
            int clouds = Range(rng, cloudsMin, cloudsMax);

            for (int i = 0; i < clouds; i++)
            {
                double x = (rng.NextDouble() * 2.0 - 1.0) * 14.0;
                double y = (rng.NextDouble() * 2.0 - 1.0) * 9.0;
                double z = (rng.NextDouble() * 2.0 - 1.0) * 0.25;

                double rad = 4.5 + rng.NextDouble() * 9.0;
                double dens = dark
                    ? (0.40 + rng.NextDouble() * 0.50)
                    : (0.25 + rng.NextDouble() * 0.55);

                AnsiColor fg;
                if (dark)
                {
                    fg = (rng.NextDouble() < 0.55) ? AnsiColor.BrightBlack : AnsiColor.BrightBlue;
                }
                else
                {
                    double roll = rng.NextDouble();
                    fg =
                        (roll < 0.33) ? AnsiColor.BrightMagenta :
                        (roll < 0.66) ? AnsiColor.BrightCyan :
                                        AnsiColor.BrightBlue;
                }

                sys.Nebulae.Add(new NebulaCloud
                {
                    WX = x,
                    WY = y,
                    WZ = z,
                    RadiusWorld = rad,
                    Density01 = MathUtil.Clamp(dens, 0.0, 1.0),
                    Fg = fg,
                    NoiseSeed = sys.Seed ^ (i * 7919)
                });
            }
        }

        private static void BuildDebrisDisk(StarSystem sys, Random rng, double inner, double outer, int countMin, int countMax)
        {
            int n = Range(rng, countMin, countMax);

            for (int i = 0; i < n; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2.0;

                // Ring radius: uniform in area within [inner, outer]
                double u = rng.NextDouble();
                double r = Math.Sqrt(inner * inner + u * (outer * outer - inner * inner));

                double x = Math.Cos(a) * r;
                double y = Math.Sin(a) * r;

                // Flattened disk
                y *= (0.55 + rng.NextDouble() * 0.25);

                double z = (rng.NextDouble() * 2.0 - 1.0) * 0.18;

                double size = 0.04 + rng.NextDouble() * 0.18;
                char g =
                    (size < 0.08) ? '.' :
                    (size < 0.14) ? 'o' : 'O';

                AnsiColor fg =
                    (rng.NextDouble() < 0.80) ? AnsiColor.BrightBlack : AnsiColor.BrightWhite;

                sys.Asteroids.Add(new Asteroid
                {
                    WX = x,
                    WY = y,
                    WZ = z,
                    RadiusWorld = size,
                    Glyph = g,
                    Fg = fg
                });
            }
        }

        private static void BuildNebulaShell(StarSystem sys, Random rng, double shellRadius, double shellThickness)
        {
            // Shell = many clouds placed roughly on a ring/sphere projection.
            int clouds = 14 + rng.Next(0, 10);

            for (int i = 0; i < clouds; i++)
            {
                double ang = rng.NextDouble() * Math.PI * 2.0;
                double r = shellRadius + (rng.NextDouble() * 2.0 - 1.0) * shellThickness;

                double x = Math.Cos(ang) * r;
                double y = Math.Sin(ang) * r;
                double z = (rng.NextDouble() * 2.0 - 1.0) * 0.25;

                double rad = 3.0 + rng.NextDouble() * 6.5;
                double dens = 0.30 + rng.NextDouble() * 0.55;

                AnsiColor fg = (rng.NextDouble() < 0.5) ? AnsiColor.BrightMagenta : AnsiColor.BrightCyan;

                sys.Nebulae.Add(new NebulaCloud
                {
                    WX = x,
                    WY = y,
                    WZ = z,
                    RadiusWorld = rad,
                    Density01 = MathUtil.Clamp(dens, 0.0, 1.0),
                    Fg = fg,
                    NoiseSeed = sys.Seed ^ (i * 104729)
                });
            }
        }

        private static void BuildSparsePlanets(StarSystem sys, Random rng, int planetMax)
        {
            // Minimal planet generator (keeps your orbit/renderer logic happy)
            int planetCount = rng.Next(0, planetMax + 1);

            double a = 8.0 + rng.NextDouble() * 2.0;
            for (int i = 0; i < planetCount; i++)
            {
                bool gas = rng.NextDouble() < 0.35;

                var p = new Planet
                {
                    Name = $"Planet-{i + 1}",
                    A = a,
                    E = rng.NextDouble() * 0.06,
                    Omega = rng.NextDouble() * Math.PI * 2.0,
                    Period = 14.0 + a * (2.0 + rng.NextDouble() * 1.0),
                    M0 = rng.NextDouble() * Math.PI * 2.0,
                    RadiusWorld = gas ? (0.38 + rng.NextDouble() * 0.35) : (0.16 + rng.NextDouble() * 0.22),
                    SpinSpeed = gas ? (1.5 + rng.NextDouble() * 2.2) : (0.7 + rng.NextDouble() * 1.6),
                    Fg = gas ? AnsiColor.BrightYellow : AnsiColor.BrightWhite,
                    Texture = gas ? PlanetDrawer.PlanetTexture.GasBands : PlanetDrawer.PlanetTexture.Rocky,
                    HasRings = gas && (rng.NextDouble() < 0.45),
                    AxisTilt = 0.0
                };

                sys.Planets.Add(p);

                a += 6.5 + rng.NextDouble() * 3.0;
            }

            ApplyDeterministicAxisTilts(sys);
        }

        // ============================================================
        // YOUR EXISTING FUNCTIONS (kept, only lightly adjusted where needed)
        // ============================================================

        public static void BuildBasicSystem(StarSystem sys)
        {
            sys.Planets.Clear();
            sys.Stations.Clear();
            sys.Ships.Clear();

            sys.Planets.Add(new Planet
            {
                Name = "Mercury-ish",
                A = 5.0,
                E = 0.06,
                Omega = 0.2,
                Period = 14.0,
                M0 = 0.3,
                RadiusWorld = 0.22,
                SpinSpeed = 1.8,
                Fg = AnsiColor.BrightWhite,
                Texture = PlanetDrawer.PlanetTexture.Cratered,
                HasRings = false,
                AxisTilt = 0.0
            });

            sys.Planets.Add(new Planet
            {
                Name = "Earth-ish",
                A = 9.0,
                E = 0.02,
                Omega = 1.1,
                Period = 22.0,
                M0 = 1.2,
                RadiusWorld = 0.32,
                SpinSpeed = 1.0,
                Fg = AnsiColor.BrightCyan,
                Texture = PlanetDrawer.PlanetTexture.EarthLike,
                HasRings = false,
                AxisTilt = 0.0
            });

            sys.Planets[1].Moons.Add(new Moon
            {
                Name = "Moon",
                LocalRadius = (sys.Planets[1].RadiusWorld * 2.4) + 0.35,
                LocalPeriod = 6.0,
                LocalPhase = 0.0,
                RadiusWorld = 0.12,
                SpinSpeed = 0.0,
                Fg = AnsiColor.BrightWhite,
                Texture = PlanetDrawer.PlanetTexture.Rocky
            });

            sys.Planets.Add(new Planet
            {
                Name = "Saturn-ish",
                A = 15.0,
                E = 0.05,
                Omega = 2.0,
                Period = 36.0,
                M0 = 2.4,
                RadiusWorld = 0.52,
                SpinSpeed = 2.4,
                Fg = AnsiColor.BrightYellow,
                Texture = PlanetDrawer.PlanetTexture.GasBands,
                HasRings = true,
                AxisTilt = 0.0
            });

            sys.Stations.Add(new Station
            {
                Name = "Station-Alpha",
                ParentPlanetIndex = 1,
                LocalRadius = 0.75,
                LocalPeriod = 6.0,
                LocalPhase = 0.0
            });

            sys.Ships.Add(new Ship
            {
                Name = "Courier-1",
                WX = 0.0,
                WY = -2.0,
                Glyph = '>',
                Fg = AnsiColor.BrightWhite,
                Mode = ShipMode.Idle
            });

            ApplyDeterministicAxisTilts(sys);
        }

        public static void BuildRandomSystem(StarSystem sys)
        {
            sys.Planets.Clear();
            sys.Stations.Clear();
            sys.Ships.Clear();

            int seed = sys.Seed;
            Random rng = new Random(seed);

            int planetCount = Range(rng, 4, 12);

            double a = 11.0 + rng.NextDouble() * 2.5;
            double aStep = 7.6 + rng.NextDouble() * 2.0;

            for (int i = 0; i < planetCount; i++)
            {
                bool outer = (i > planetCount * 0.55);
                bool coldZone = outer && (rng.NextDouble() < 0.65);

                bool gas = outer ? (rng.NextDouble() < 0.60) : (rng.NextDouble() < 0.20);

                double e = rng.NextDouble() * 0.08;
                double omega = rng.NextDouble() * Math.PI * 2.0;
                double period = 12.0 + a * (2.2 + rng.NextDouble() * 1.2);
                double m0 = rng.NextDouble() * Math.PI * 2.0;

                double radiusWorld = gas
                    ? (0.40 + rng.NextDouble() * 0.45)
                    : (0.16 + rng.NextDouble() * 0.24);

                double spinSpeed = gas
                    ? (1.5 + rng.NextDouble() * 3.0)
                    : (0.6 + rng.NextDouble() * 2.0);

                var tex = PickTexture(rng, gas, coldZone);

                bool hasRings = gas && (rng.NextDouble() < 0.55);

                var p = new Planet
                {
                    Name = $"Planet-{i + 1}",
                    A = a,
                    E = e,
                    Omega = omega,
                    Period = period,
                    M0 = m0,
                    RadiusWorld = radiusWorld,
                    SpinSpeed = spinSpeed,
                    Fg = gas ? AnsiColor.BrightYellow : AnsiColor.BrightWhite,
                    Texture = tex,
                    HasRings = hasRings,
                    AxisTilt = 0.0
                };

                int moonCount = gas ? Range(rng, 2, 8) : Range(rng, 0, 2);
                if (!gas && moonCount == 2 && rng.NextDouble() < 0.55) moonCount = 1;

                for (int m = 0; m < moonCount; m++)
                {
                    double minR = (p.RadiusWorld * 2.4) + 0.25;
                    double spacing = 0.55 + rng.NextDouble() * 0.35;

                    double localR = minR + m * spacing + rng.NextDouble() * 0.25;

                    double localPeriod = 4.5 + rng.NextDouble() * 8.0 + m * 0.9;
                    double localPhase = rng.NextDouble() * Math.PI * 2.0;

                    double moonRadius = gas
                        ? (0.06 + rng.NextDouble() * 0.10)
                        : (0.05 + rng.NextDouble() * 0.08);

                    var mTex = PickMoonTexture(rng, coldZone);

                    p.Moons.Add(new Moon
                    {
                        Name = $"Moon-{m + 1}",
                        LocalRadius = localR,
                        LocalPeriod = localPeriod,
                        LocalPhase = localPhase,
                        RadiusWorld = moonRadius,
                        SpinSpeed = 0.0,
                        Fg = AnsiColor.BrightWhite,
                        Texture = mTex
                    });
                }

                sys.Planets.Add(p);

                a += aStep + rng.NextDouble() * 1.3;
                aStep *= (1.04 + rng.NextDouble() * 0.08);
            }

            if (sys.Planets.Count > 0)
            {
                int stationPlanet = MathUtil.ClampInt(sys.Planets.Count / 2, 0, sys.Planets.Count - 1);
                sys.Stations.Add(new Station
                {
                    Name = "Station-Alpha",
                    ParentPlanetIndex = stationPlanet,
                    LocalRadius = 0.75,
                    LocalPeriod = 6.0,
                    LocalPhase = 0.0
                });
            }

            sys.Ships.Add(new Ship
            {
                Name = "Courier-1",
                WX = 0.0,
                WY = -2.0,
                Glyph = '>',
                Fg = AnsiColor.BrightWhite,
                Mode = ShipMode.Idle
            });

            ApplyDeterministicAxisTilts(sys);
        }

        private static void ApplyDeterministicAxisTilts(StarSystem sys)
        {
            int sysSeed = sys.Seed;

            for (int i = 0; i < sys.Planets.Count; i++)
            {
                Planet p = sys.Planets[i];
                int pSeed = sysSeed ^ (p.Name?.GetHashCode() ?? i * 1337);

                double u = HashNoise.Hash01(pSeed, 9101, 9202);
                double v = HashNoise.Hash01(pSeed, 9303, 9404);

                double deg;
                if (u < 0.78) deg = 0.0 + 35.0 * v;
                else if (u < 0.95) deg = 35.0 + 35.0 * v;
                else deg = 70.0 + 40.0 * v;

                if (p.Texture == PlanetDrawer.PlanetTexture.IceWorld || p.Texture == PlanetDrawer.PlanetTexture.IceCracked)
                    deg *= 1.12;

                if (deg > 110.0) deg = 110.0;

                p.AxisTilt = deg * (Math.PI / 180.0);
            }
        }

        public static void UpdateCelestials(StarSystem sys, double t, bool useKepler)
        {
            int sysSeed = sys.Seed;

            for (int i = 0; i < sys.Planets.Count; i++)
            {
                var p = sys.Planets[i];

                double x, y;
                if (useKepler)
                {
                    double n = 2.0 * Math.PI / Math.Max(0.001, p.Period);
                    double M = p.M0 + n * t;
                    OrbitMath.Kepler2D(p.A, MathUtil.Clamp(p.E, 0.0, 0.95), 0.0, M, out x, out y);
                }
                else
                {
                    double n = 2.0 * Math.PI / Math.Max(0.001, p.Period);
                    double ang = p.M0 + n * t;
                    x = Math.Cos(ang) * p.A;
                    y = Math.Sin(ang) * p.A;
                }

                int pSeed = sysSeed ^ p.Name.GetHashCode();

                double plane = (HashNoise.Hash01(pSeed, 101, 202) * 2.0 - 1.0) * 1.05;
                double c = Math.Cos(plane);
                double s = Math.Sin(plane);

                double rx = x * c - y * s;
                double ry = x * s + y * c;

                double inc = 0.55 + 0.45 * HashNoise.Hash01(pSeed, 303, 404);
                ry *= inc;

                double co = Math.Cos(p.Omega);
                double so = Math.Sin(p.Omega);
                double fx = rx * co - ry * so;
                double fy = rx * so + ry * co;

                p.WX = fx;
                p.WY = fy;

                p.WZ = Math.Sin((p.M0 + t * 0.35)) * 0.6;
            }

            for (int i = 0; i < sys.Stations.Count; i++)
            {
                var s = sys.Stations[i];
                int pi = MathUtil.ClampInt(s.ParentPlanetIndex, 0, sys.Planets.Count - 1);
                var p = sys.Planets[pi];

                double n = 2.0 * Math.PI / Math.Max(0.001, s.LocalPeriod);
                double ang = s.LocalPhase + n * t;

                double sx = Math.Cos(ang) * s.LocalRadius;
                double sy = Math.Sin(ang) * s.LocalRadius;

                s.WX = p.WX + sx;
                s.WY = p.WY + sy;
                s.WZ = p.WZ + 0.1;
            }

            for (int i = 0; i < sys.Planets.Count; i++)
            {
                var p = sys.Planets[i];
                for (int m = 0; m < p.Moons.Count; m++)
                {
                    var moon = p.Moons[m];

                    double n = 2.0 * Math.PI / Math.Max(0.001, moon.LocalPeriod);
                    double ang = moon.LocalPhase + n * t;

                    double mx = Math.Cos(ang) * moon.LocalRadius;
                    double my = Math.Sin(ang) * moon.LocalRadius;

                    moon.WX = p.WX + mx;
                    moon.WY = p.WY + my;
                    moon.WZ = p.WZ + 0.05;
                }
            }
        }

        public static void UpdateShips(StarSystem sys, double dt)
        {
            for (int i = 0; i < sys.Ships.Count; i++)
            {
                Ship sh = sys.Ships[i];

                sh.TrailAccum += dt;
                if (sh.TrailAccum >= 0.06)
                {
                    sh.TrailAccum = 0.0;
                    sh.Trail.Add(sh.WX, sh.WY);
                }

                switch (sh.Mode)
                {
                    case ShipMode.Idle:
                        sh.VX *= 0.92;
                        sh.VY *= 0.92;
                        sh.WX += sh.VX * dt;
                        sh.WY += sh.VY * dt;
                        break;

                    case ShipMode.TravelToPoint:
                        StepTravelTo(sys, sh, dt);
                        break;

                    case ShipMode.Orbit:
                        StepOrbitPoint(sys, sh, dt);
                        break;
                }

                sh.WZ = -0.25;
            }
        }

        private static bool TryGetOrbitTargetPosition(StarSystem sys, Ship sh, out double tx, out double ty)
        {
            tx = 0.0; ty = 0.0;

            if (sh.OrbitTargetKind == OrbitTargetKind.None)
                return false;

            switch (sh.OrbitTargetKind)
            {
                case OrbitTargetKind.Sun:
                    tx = 0.0; ty = 0.0;
                    return true;

                case OrbitTargetKind.Planet:
                    if (sh.OrbitTargetIndex >= 0 && sh.OrbitTargetIndex < sys.Planets.Count)
                    {
                        var p = sys.Planets[sh.OrbitTargetIndex];
                        tx = p.WX; ty = p.WY;
                        return true;
                    }
                    return false;

                case OrbitTargetKind.Station:
                    if (sh.OrbitTargetIndex >= 0 && sh.OrbitTargetIndex < sys.Stations.Count)
                    {
                        var s = sys.Stations[sh.OrbitTargetIndex];
                        tx = s.WX; ty = s.WY;
                        return true;
                    }
                    return false;

                case OrbitTargetKind.Ship:
                    if (sh.OrbitTargetIndex >= 0 && sh.OrbitTargetIndex < sys.Ships.Count)
                    {
                        var other = sys.Ships[sh.OrbitTargetIndex];
                        tx = other.WX; ty = other.WY;
                        return true;
                    }
                    return false;

                case OrbitTargetKind.Moon:
                    if (sh.OrbitTargetIndex >= 0 && sh.OrbitTargetIndex < sys.Planets.Count)
                    {
                        var p = sys.Planets[sh.OrbitTargetIndex];
                        int mi = sh.OrbitTargetSubIndex;
                        if (mi >= 0 && mi < p.Moons.Count)
                        {
                            var moon = p.Moons[mi];
                            tx = moon.WX; ty = moon.WY;
                            return true;
                        }
                    }
                    return false;

                default:
                    return false;
            }
        }

        private static void StepTravelTo(StarSystem sys, Ship sh, double dt)
        {
            double targetX = sh.TargetX;
            double targetY = sh.TargetY;

            if (TryGetOrbitTargetPosition(sys, sh, out double movingTX, out double movingTY))
            {
                targetX = movingTX;
                targetY = movingTY;
                sh.TargetX = targetX;
                sh.TargetY = targetY;
            }

            double dx = targetX - sh.WX;
            double dy = targetY - sh.WY;
            double d2 = dx * dx + dy * dy;

            if (d2 <= sh.ArriveRadius * sh.ArriveRadius)
            {
                sh.Mode = ShipMode.Orbit;
                sh.OrbitAngle = Math.Atan2(sh.WY - targetY, sh.WX - targetX);
                sh.VX *= 0.25;
                sh.VY *= 0.25;
                return;
            }

            double d = Math.Sqrt(d2);
            double ux = dx / (d + 1e-9);
            double uy = dy / (d + 1e-9);

            double desiredSpeed = sh.MaxSpeed;
            double brake = MathUtil.Clamp(d / (sh.ArriveRadius * 6.0), 0.15, 1.0);
            desiredSpeed *= brake;

            double dvx = ux * desiredSpeed - sh.VX;
            double dvy = uy * desiredSpeed - sh.VY;

            double dv = Math.Sqrt(dvx * dvx + dvy * dvy);
            double maxDv = sh.Accel * dt;
            if (dv > maxDv)
            {
                dvx = dvx / (dv + 1e-9) * maxDv;
                dvy = dvy / (dv + 1e-9) * maxDv;
            }

            sh.VX += dvx;
            sh.VY += dvy;

            sh.WX += sh.VX * dt;
            sh.WY += sh.VY * dt;
        }

        private static void StepOrbitPoint(StarSystem sys, Ship sh, double dt)
        {
            double cx = sh.TargetX;
            double cy = sh.TargetY;

            if (TryGetOrbitTargetPosition(sys, sh, out double movingTX, out double movingTY))
            {
                cx = movingTX;
                cy = movingTY;
                sh.TargetX = cx;
                sh.TargetY = cy;
            }

            sh.OrbitAngle += sh.OrbitAngularSpeed * dt;

            sh.WX = cx + Math.Cos(sh.OrbitAngle) * sh.OrbitRadius;
            sh.WY = cy + Math.Sin(sh.OrbitAngle) * sh.OrbitRadius;

            sh.VX *= 0.0;
            sh.VY *= 0.0;
        }

        private static int Range(Random rng, int minInclusive, int maxInclusive)
            => rng.Next(minInclusive, maxInclusive + 1);

        private static PlanetDrawer.PlanetTexture PickTexture(Random rng, bool isGas, bool isColdZone)
        {
            if (isGas)
            {
                int r = rng.Next(0, 100);
                if (r < 55) return PlanetDrawer.PlanetTexture.GasBands;
                if (r < 75) return PlanetDrawer.PlanetTexture.GasSwirl;
                return PlanetDrawer.PlanetTexture.GasStorm;
            }

            if (isColdZone)
            {
                return rng.NextDouble() < 0.5
                    ? PlanetDrawer.PlanetTexture.IceWorld
                    : PlanetDrawer.PlanetTexture.IceCracked;
            }

            PlanetDrawer.PlanetTexture[] pool =
            {
                PlanetDrawer.PlanetTexture.Rocky,
                PlanetDrawer.PlanetTexture.Cratered,
                PlanetDrawer.PlanetTexture.Barren,
                PlanetDrawer.PlanetTexture.Desert,
                PlanetDrawer.PlanetTexture.Jungle,
                PlanetDrawer.PlanetTexture.Oceanic,
                PlanetDrawer.PlanetTexture.EarthLike,
                PlanetDrawer.PlanetTexture.Lava,
                PlanetDrawer.PlanetTexture.Toxic,
                PlanetDrawer.PlanetTexture.Metallic
            };

            return pool[rng.Next(pool.Length)];
        }

        private static PlanetDrawer.PlanetTexture PickMoonTexture(Random rng, bool isColdZone)
        {
            if (isColdZone && rng.NextDouble() < 0.55)
                return PlanetDrawer.PlanetTexture.IceCracked;

            PlanetDrawer.PlanetTexture[] pool =
            {
                PlanetDrawer.PlanetTexture.Rocky,
                PlanetDrawer.PlanetTexture.Cratered,
                PlanetDrawer.PlanetTexture.Barren,
                PlanetDrawer.PlanetTexture.Metallic,
                PlanetDrawer.PlanetTexture.IceWorld
            };

            return pool[rng.Next(pool.Length)];
        }
    }
}
