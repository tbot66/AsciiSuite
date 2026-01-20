using System;
using System.Collections.Generic;
using AsciiEngine;

namespace SolarSystemApp.World
{
    public enum EntityKind
    {
        Sun,
        Planet,
        Station,
        Ship
    }

    // NEW: What kind of "system" this node is.
    public enum SystemKind
    {
        StarSystem = 0,        // normal star + planets (your existing generator)
        AsteroidField = 1,     // no star, just asteroids
        Nebula = 2,            // no star, colored fog clouds
        DarkNebula = 3,        // darker/murkier nebula
        DebrisDisk = 4,        // star + dense disk/ring of debris
        Protostar = 5,         // star + lots of nebula/dust
        BrownDwarf = 6,        // dim star, sparse
        NeutronStar = 7,       // tiny hot star, sparse
        BlackHole = 8,         // starless “center” + accretion debris
        SupernovaRemnant = 9   // shell-like nebula + debris
    }

    // NEW: lightweight “scenery” objects for non-planet systems
    public sealed class Asteroid
    {
        public double WX, WY, WZ;
        public double RadiusWorld;
        public AnsiColor Fg;
        public char Glyph;
    }

    public sealed class NebulaCloud
    {
        public double WX, WY, WZ;
        public double RadiusWorld;
        public double Density01;     // 0..1
        public AnsiColor Fg;
        public int NoiseSeed;        // deterministic per-cloud
    }

    public sealed class Planet
    {
        public string Name;

        public double A;
        public double E;
        public double Omega;
        public double Period;
        public double M0;

        public double RadiusWorld;
        public double SpinSpeed;
        public bool HasRings;

        public double AxisTilt;

        public AnsiColor Fg;

        internal PlanetDrawer.PlanetTexture Texture;

        public double WX, WY, WZ;

        internal TrailBuffer Trail = new TrailBuffer(maxPoints: 40);

        public List<Moon> Moons = new List<Moon>();
    }

    public sealed class Moon
    {
        public string Name;

        public double LocalRadius;
        public double LocalPeriod;
        public double LocalPhase;

        public double RadiusWorld;
        public double SpinSpeed;

        public AnsiColor Fg;

        internal PlanetDrawer.PlanetTexture Texture;

        public double WX, WY, WZ;

        internal TrailBuffer Trail = new TrailBuffer(maxPoints: 18);
    }

    public sealed class Station
    {
        public string Name;

        public int ParentPlanetIndex;

        public double LocalRadius;
        public double LocalPeriod;
        public double LocalPhase;

        public double WX, WY, WZ;
    }

    public enum ShipMode
    {
        Idle = 0,
        TravelToPoint = 1,
        Orbit = 2
    }

    public enum OrbitTargetKind
    {
        None = 0,
        Sun = 1,
        Planet = 2,
        Station = 3,
        Ship = 4,
        Moon = 5
    }

    public sealed class Ship
    {
        public string Name { get; set; } = "Ship";

        public double WX { get; set; }
        public double WY { get; set; }
        public double WZ { get; set; }

        public double VX { get; set; }
        public double VY { get; set; }

        public ShipMode Mode { get; set; } = ShipMode.Idle;

        public double TargetX { get; set; }
        public double TargetY { get; set; }

        public double OrbitRadius { get; set; } = 1.0;
        public double OrbitAngularSpeed { get; set; } = 0.6;
        public double OrbitAngle { get; set; } = 0.0;

        public double MaxSpeed { get; set; } = 4.0;
        public double Accel { get; set; } = 6.0;
        public double ArriveRadius { get; set; } = 0.65;

        public OrbitTargetKind OrbitTargetKind { get; set; } = OrbitTargetKind.None;

        public int OrbitTargetIndex { get; set; } = -1;
        public int OrbitTargetSubIndex { get; set; } = -1;

        public AnsiColor Fg { get; set; } = AnsiColor.BrightWhite;

        public double TrailAccum { get; set; } = 0.0;

        public char Glyph = '>';

        internal TrailBuffer Trail { get; } = new TrailBuffer(maxPoints: 80);
    }

    public sealed class StarSystem
    {
        public int Seed;
        public string Name;

        public double GalaxyX, GalaxyY;

        // NEW: kind + flags for starless systems
        public SystemKind Kind = SystemKind.StarSystem;

        // If false, your renderer can skip drawing the sun (set SunRadiusWorld = 0 too).
        public bool HasStar = true;

        // Simple UI descriptor (“Nebula”, “Asteroid Field”, etc.)
        public string Descriptor = "Star System";

        public AnsiColor SunColor = AnsiColor.BrightYellow;

        public double SunRadiusWorld = 0.70;
        public double CoronaRadiusWorld = 1.20;

        // NEW: scenery for non-planet systems
        public readonly List<Asteroid> Asteroids = new List<Asteroid>();
        public readonly List<NebulaCloud> Nebulae = new List<NebulaCloud>();

        public readonly List<Planet> Planets = new List<Planet>();
        public readonly List<Station> Stations = new List<Station>();
        public readonly List<Ship> Ships = new List<Ship>();
    }

    internal sealed class SelectionItem
    {
        public EntityKind Kind;
        public int Index;
        public string Label;

        public double WX, WY, WZ;
    }
}
