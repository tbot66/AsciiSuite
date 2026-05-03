using AsciiEngine;
using SolarSystemApp.Util;
using SolarSystemApp.World;
using System;
using System.IO;
using System.Text.Json;

namespace SolarSystemApp.Persistence
{
    internal static class SaveManager
    {
        private const string FileName = "savegame.json";

        public static bool Save(Galaxy galaxy, int systemIndex, double simTime, double timeScale, bool paused,
                                bool galaxyView, double camWX, double camWY, double zoom, double orbitYScale,
                                double galCamX, double galCamY, double galZoom,
                                bool useKepler)
        {
            try
            {
                var sg = new SaveGame
                {
                    GalaxySeed = galaxy.Seed,
                    CurrentSystemIndex = systemIndex,
                    SimTime = simTime,
                    TimeScale = timeScale,
                    Paused = paused,
                    GalaxyView = galaxyView,
                    CamWX = camWX,
                    CamWY = camWY,
                    Zoom = zoom,
                    OrbitYScale = orbitYScale,
                    GalCamX = galCamX,
                    GalCamY = galCamY,
                    GalZoom = galZoom,
                    UseKepler = useKepler
                };

                sg.Version = 1;

                for (int i = 0; i < galaxy.Systems.Count; i++)
                {
                    StarSystem sys = galaxy.Systems[i];
                    var ss = new SaveSystem
                    {
                        Seed = sys.Seed,
                        Name = sys.Name,
                        GalaxyX = sys.GalaxyX,
                        GalaxyY = sys.GalaxyY,
                        SunColor = (int)sys.SunColor,
                        SunRadiusWorld = sys.SunRadiusWorld,
                        CoronaRadiusWorld = sys.CoronaRadiusWorld
                    };

                    foreach (var p in sys.Planets)
                    {
                        ss.Planets.Add(new SavePlanet
                        {
                            Name = p.Name,
                            A = p.A,
                            E = p.E,
                            Omega = p.Omega,
                            Period = p.Period,
                            M0 = p.M0,
                            RadiusWorld = p.RadiusWorld,
                            SpinSpeed = p.SpinSpeed,
                            HasRings = p.HasRings,
                            Fg = (int)p.Fg,
                            TextureId = TextureToId(p.Texture)
                        });
                    }

                    foreach (var st in sys.Stations)
                    {
                        ss.Stations.Add(new SaveStation
                        {
                            Name = st.Name,
                            ParentPlanetIndex = st.ParentPlanetIndex,
                            LocalRadius = st.LocalRadius,
                            LocalPeriod = st.LocalPeriod,
                            LocalPhase = st.LocalPhase
                        });
                    }

                    foreach (var sh in sys.Ships)
                    {
                        ss.Ships.Add(new SaveShip
                        {
                            Name = sh.Name,
                            WX = sh.WX,
                            WY = sh.WY,
                            VX = sh.VX,
                            VY = sh.VY,
                            Mode = (int)sh.Mode,
                            TargetX = sh.TargetX,
                            TargetY = sh.TargetY,
                            OrbitRadius = sh.OrbitRadius,
                            OrbitAngularSpeed = sh.OrbitAngularSpeed,
                            OrbitAngle = sh.OrbitAngle,
                            MaxSpeed = sh.MaxSpeed,
                            Accel = sh.Accel,
                            ArriveRadius = sh.ArriveRadius,
                            Fg = (int)sh.Fg,

                            // FIX: persist orbit target references (backwards compatible)
                            OrbitTargetKind = (int)sh.OrbitTargetKind,
                            OrbitTargetIndex = sh.OrbitTargetIndex,
                            OrbitTargetSubIndex = sh.OrbitTargetSubIndex
                        });
                    }

                    sg.Systems.Add(ss);
                }

                sg.Links.Clear();
                for (int i = 0; i < galaxy.Links.Count; i++)
                {
                    var l = galaxy.Links[i];
                    sg.Links.Add(new SaveLink { A = l.A, B = l.B });
                }

                var opts = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(sg, opts);
                WriteAtomicWithBackup(FileName, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool Load(out SaveGame sg)
        {
            sg = null;
            try
            {
                if (!File.Exists(FileName)) return false;
                string json = File.ReadAllText(FileName);
                sg = JsonSerializer.Deserialize<SaveGame>(json);
                if (sg != null && sg.Version <= 0) sg.Version = 0; // old save
                return (sg != null);
            }
            catch
            {
                return false;
            }
        }

        public static void ApplyToGalaxy(SaveGame sg, Galaxy galaxy)
        {
            galaxy.Seed = sg.GalaxySeed;
            galaxy.Systems.Clear();
            galaxy.Links.Clear();

            foreach (var ss in sg.Systems)
            {
                var sys = new StarSystem
                {
                    Seed = ss.Seed,
                    Name = ss.Name,
                    GalaxyX = ss.GalaxyX,
                    GalaxyY = ss.GalaxyY,
                    SunColor = (AnsiColor)ss.SunColor,
                    SunRadiusWorld = ss.SunRadiusWorld,
                    CoronaRadiusWorld = ss.CoronaRadiusWorld
                };

                sys.Planets.Clear();
                foreach (var sp in ss.Planets)
                {
                    sys.Planets.Add(new Planet
                    {
                        Name = sp.Name,
                        A = sp.A,
                        E = sp.E,
                        Omega = sp.Omega,
                        Period = sp.Period,
                        M0 = sp.M0,
                        RadiusWorld = sp.RadiusWorld,
                        SpinSpeed = sp.SpinSpeed,
                        HasRings = sp.HasRings,
                        Fg = (AnsiColor)sp.Fg,
                        Texture = IdToTexture(sp.TextureId)
                    });
                }

                sys.Stations.Clear();
                foreach (var st in ss.Stations)
                {
                    sys.Stations.Add(new Station
                    {
                        Name = st.Name,
                        ParentPlanetIndex = st.ParentPlanetIndex,
                        LocalRadius = st.LocalRadius,
                        LocalPeriod = st.LocalPeriod,
                        LocalPhase = st.LocalPhase
                    });
                }

                sys.Ships.Clear();
                foreach (var sh in ss.Ships)
                {
                    sys.Ships.Add(new Ship
                    {
                        Name = sh.Name,
                        WX = sh.WX,
                        WY = sh.WY,
                        VX = sh.VX,
                        VY = sh.VY,
                        Mode = (ShipMode)sh.Mode,
                        TargetX = sh.TargetX,
                        TargetY = sh.TargetY,
                        OrbitRadius = sh.OrbitRadius,
                        OrbitAngularSpeed = sh.OrbitAngularSpeed,
                        OrbitAngle = sh.OrbitAngle,
                        MaxSpeed = sh.MaxSpeed,
                        Accel = sh.Accel,
                        ArriveRadius = sh.ArriveRadius,
                        Fg = (AnsiColor)sh.Fg,

                        // FIX: restore orbit target references (old saves default to 0)
                        OrbitTargetKind = (OrbitTargetKind)sh.OrbitTargetKind,
                        OrbitTargetIndex = sh.OrbitTargetIndex,
                        OrbitTargetSubIndex = sh.OrbitTargetSubIndex
                    });
                }

                galaxy.Systems.Add(sys);
            }

            // Restore links once (AFTER systems are built)
            galaxy.Links.Clear();

            if (sg.Links != null && sg.Links.Count > 0)
            {
                for (int i = 0; i < sg.Links.Count; i++)
                {
                    var l = sg.Links[i];
                    int a = MathUtil.ClampInt(l.A, 0, galaxy.Systems.Count - 1);
                    int b = MathUtil.ClampInt(l.B, 0, galaxy.Systems.Count - 1);
                    if (a != b)
                        galaxy.Links.Add(new Galaxy.Link(a, b));
                }
            }
            else
            {
                // Fewer total connections now
                galaxy.RebuildConnections(neighborsPerNode: 2);
            }
        }

        private static void WriteAtomicWithBackup(string path, string contents)
        {
            string tmp = path + ".tmp";
            string bak = path + ".bak";

            File.WriteAllText(tmp, contents);

            try
            {
                if (File.Exists(path))
                    File.Copy(path, bak, overwrite: true);
            }
            catch { /* ignore */ }

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
                    return;
                }
                catch { /* fallback below */ }
            }

            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static int TextureToId(PlanetDrawer.PlanetTexture t)
        {
            if (t == PlanetDrawer.PlanetTexture.Rocky) return 1;
            if (t == PlanetDrawer.PlanetTexture.Continents) return 2;
            if (t == PlanetDrawer.PlanetTexture.GasBands) return 3;
            if (t == PlanetDrawer.PlanetTexture.IceWorld) return 4;

            if (t == PlanetDrawer.PlanetTexture.Barren) return 10;
            if (t == PlanetDrawer.PlanetTexture.Cratered) return 11;
            if (t == PlanetDrawer.PlanetTexture.Metallic) return 12;
            if (t == PlanetDrawer.PlanetTexture.Desert) return 13;
            if (t == PlanetDrawer.PlanetTexture.Jungle) return 14;
            if (t == PlanetDrawer.PlanetTexture.Oceanic) return 15;
            if (t == PlanetDrawer.PlanetTexture.EarthLike) return 16;
            if (t == PlanetDrawer.PlanetTexture.Lava) return 17;
            if (t == PlanetDrawer.PlanetTexture.Toxic) return 18;
            if (t == PlanetDrawer.PlanetTexture.IceCracked) return 19;

            if (t == PlanetDrawer.PlanetTexture.GasSwirl) return 30;
            if (t == PlanetDrawer.PlanetTexture.GasStorm) return 31;

            return 1;
        }

        private static PlanetDrawer.PlanetTexture IdToTexture(int id)
        {
            switch (id)
            {
                case 1: return PlanetDrawer.PlanetTexture.Rocky;
                case 2: return PlanetDrawer.PlanetTexture.Continents;
                case 3: return PlanetDrawer.PlanetTexture.GasBands;
                case 4: return PlanetDrawer.PlanetTexture.IceWorld;

                case 10: return PlanetDrawer.PlanetTexture.Barren;
                case 11: return PlanetDrawer.PlanetTexture.Cratered;
                case 12: return PlanetDrawer.PlanetTexture.Metallic;
                case 13: return PlanetDrawer.PlanetTexture.Desert;
                case 14: return PlanetDrawer.PlanetTexture.Jungle;
                case 15: return PlanetDrawer.PlanetTexture.Oceanic;
                case 16: return PlanetDrawer.PlanetTexture.EarthLike;
                case 17: return PlanetDrawer.PlanetTexture.Lava;
                case 18: return PlanetDrawer.PlanetTexture.Toxic;
                case 19: return PlanetDrawer.PlanetTexture.IceCracked;

                case 30: return PlanetDrawer.PlanetTexture.GasSwirl;
                case 31: return PlanetDrawer.PlanetTexture.GasStorm;

                default: return PlanetDrawer.PlanetTexture.Rocky;
            }
        }
    }
}
