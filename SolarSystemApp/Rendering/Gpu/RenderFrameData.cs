using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace SolarSystemApp.Rendering.Gpu
{
    internal sealed class RenderFrameData
    {
        public readonly List<SpriteInstance> Stars = new(4096);
        public readonly List<SpriteInstance> Debris = new(2048);
        public readonly List<SpriteInstance> Asteroids = new(1024);
        public readonly List<SpriteInstance> Ships = new(256);
        public readonly List<LineVertex> OrbitVertices = new(4096);
        public readonly List<LineVertex> SelectionVertices = new(256);
        public readonly List<TrailVertex> TrailVertices = new(4096);
        public readonly List<PlanetInstance> Planets = new(128);
        public readonly List<RingInstance> Rings = new(128);
        public readonly List<OccluderData> Occluders = new(128);

        public SunInstance Sun;
        public bool HasSun;

        public void Clear()
        {
            Stars.Clear();
            Debris.Clear();
            Asteroids.Clear();
            Ships.Clear();
            OrbitVertices.Clear();
            SelectionVertices.Clear();
            TrailVertices.Clear();
            Planets.Clear();
            Rings.Clear();
            Occluders.Clear();
            HasSun = false;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SpriteInstance
    {
        public Vector2 Position;
        public float Size;
        public Vector4 Color;
        public float Depth;
        public float Seed;
        public float Rotation;
        public float Type;

        public SpriteInstance(Vector2 position, float size, Vector4 color, float depth, float seed, float rotation, float type)
        {
            Position = position;
            Size = size;
            Color = color;
            Depth = depth;
            Seed = seed;
            Rotation = rotation;
            Type = type;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct LineVertex
    {
        public Vector2 Position;
        public Vector4 Color;
        public float Depth;

        public LineVertex(Vector2 position, Vector4 color, float depth)
        {
            Position = position;
            Color = color;
            Depth = depth;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct TrailVertex
    {
        public Vector2 Position;
        public float Age01;
        public Vector4 Color;
        public float Depth;

        public TrailVertex(Vector2 position, float age01, Vector4 color, float depth)
        {
            Position = position;
            Age01 = age01;
            Color = color;
            Depth = depth;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PlanetInstance
    {
        public Vector2 Position;
        public float Radius;
        public float Spin;
        public float AxisTilt;
        public float Depth;
        public Vector3 LightDir;
        public int TextureId;
        public int Seed;
        public float Lod;
        public float RingInner;
        public float RingOuter;
        public float RingTilt;
        public int TextureType;
        public int TextureSize;

        public PlanetInstance(
            Vector2 position,
            float radius,
            float spin,
            float axisTilt,
            float depth,
            Vector3 lightDir,
            int textureId,
            int seed,
            float lod,
            float ringInner,
            float ringOuter,
            float ringTilt,
            int textureType,
            int textureSize)
        {
            Position = position;
            Radius = radius;
            Spin = spin;
            AxisTilt = axisTilt;
            Depth = depth;
            LightDir = lightDir;
            TextureId = textureId;
            Seed = seed;
            Lod = lod;
            RingInner = ringInner;
            RingOuter = ringOuter;
            RingTilt = ringTilt;
            TextureType = textureType;
            TextureSize = textureSize;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct RingInstance
    {
        public Vector2 Position;
        public float InnerRadius;
        public float OuterRadius;
        public float Tilt;
        public float Rotation;
        public float Depth;
        public Vector3 LightDir;
        public float PlanetRadius;
        public int TextureId;
        public int Seed;
        public int TextureSize;

        public RingInstance(
            Vector2 position,
            float innerRadius,
            float outerRadius,
            float tilt,
            float rotation,
            float depth,
            Vector3 lightDir,
            float planetRadius,
            int textureId,
            int seed,
            int textureSize)
        {
            Position = position;
            InnerRadius = innerRadius;
            OuterRadius = outerRadius;
            Tilt = tilt;
            Rotation = rotation;
            Depth = depth;
            LightDir = lightDir;
            PlanetRadius = planetRadius;
            TextureId = textureId;
            Seed = seed;
            TextureSize = textureSize;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SunInstance
    {
        public Vector2 Position;
        public float Radius;
        public float Depth;
        public int TextureId;
        public int Seed;
        public int TextureSize;

        public SunInstance(Vector2 position, float radius, float depth, int textureId, int seed, int textureSize)
        {
            Position = position;
            Radius = radius;
            Depth = depth;
            TextureId = textureId;
            Seed = seed;
            TextureSize = textureSize;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct OccluderData
    {
        public Vector2 Position;
        public float Radius;
        public float Depth;

        public OccluderData(Vector2 position, float radius, float depth)
        {
            Position = position;
            Radius = radius;
            Depth = depth;
        }
    }
}
