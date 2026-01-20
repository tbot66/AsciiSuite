using System.Collections.Generic;

namespace SolarSystemApp.Persistence
{
    internal sealed class SaveGame
    {
        public int Version { get; set; } = 1;

        public int GalaxySeed { get; set; }

        public int CurrentSystemIndex { get; set; }

        public double SimTime { get; set; }
        public double TimeScale { get; set; }
        public bool Paused { get; set; }

        public bool GalaxyView { get; set; }

        public double CamWX { get; set; }
        public double CamWY { get; set; }
        public double Zoom { get; set; }
        public double OrbitYScale { get; set; }

        public double GalCamX { get; set; }
        public double GalCamY { get; set; }
        public double GalZoom { get; set; }

        public bool UseKepler { get; set; }

        public List<SaveSystem> Systems { get; set; } = new List<SaveSystem>();

        // Galaxy links (hyperlanes)
        public List<SaveLink> Links { get; set; } = new List<SaveLink>();
    }

    internal sealed class SaveSystem
    {
        public int Seed { get; set; }
        public string Name { get; set; }

        public double GalaxyX { get; set; }
        public double GalaxyY { get; set; }

        public int SunColor { get; set; }
        public double SunRadiusWorld { get; set; }
        public double CoronaRadiusWorld { get; set; }

        public List<SavePlanet> Planets { get; set; } = new List<SavePlanet>();
        public List<SaveStation> Stations { get; set; } = new List<SaveStation>();
        public List<SaveShip> Ships { get; set; } = new List<SaveShip>();
    }

    internal sealed class SavePlanet
    {
        public string Name { get; set; }

        public double A { get; set; }
        public double E { get; set; }
        public double Omega { get; set; }
        public double Period { get; set; }
        public double M0 { get; set; }

        public double RadiusWorld { get; set; }
        public double SpinSpeed { get; set; }
        public bool HasRings { get; set; }

        public int Fg { get; set; }
        public int TextureId { get; set; }
    }

    internal sealed class SaveStation
    {
        public string Name { get; set; }

        public int ParentPlanetIndex { get; set; }

        public double LocalRadius { get; set; }
        public double LocalPeriod { get; set; }
        public double LocalPhase { get; set; }
    }

    internal sealed class SaveShip
    {
        public string Name { get; set; }

        public double WX { get; set; }
        public double WY { get; set; }

        public double VX { get; set; }
        public double VY { get; set; }

        public int Mode { get; set; }

        public double TargetX { get; set; }
        public double TargetY { get; set; }

        public double OrbitRadius { get; set; }
        public double OrbitAngularSpeed { get; set; }
        public double OrbitAngle { get; set; }

        public double MaxSpeed { get; set; }
        public double Accel { get; set; }
        public double ArriveRadius { get; set; }

        public int Fg { get; set; }

        // NEW: orbit target reference (backwards compatible: old saves will default to 0/0/0)
        public int OrbitTargetKind { get; set; }       // matches OrbitTargetKind enum int values
        public int OrbitTargetIndex { get; set; }
        public int OrbitTargetSubIndex { get; set; }
    }

    internal sealed class SaveLink
    {
        public int A { get; set; }
        public int B { get; set; }
    }
}
