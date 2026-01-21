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

        // 3D lists added so the perspective path can operate on world-space positions.
        public readonly List<SpriteInstance3D> Stars3D = new(4096);
        public readonly List<SpriteInstance3D> Debris3D = new(2048);
        public readonly List<SpriteInstance3D> Asteroids3D = new(1024);
        public readonly List<SpriteInstance3D> Ships3D = new(256);
        public readonly List<LineVertex3D> OrbitVertices3D = new(4096);
        public readonly List<LineVertex3D> SelectionVertices3D = new(256);
        public readonly List<TrailVertex3D> TrailVertices3D = new(4096);
        public readonly List<PlanetInstance3D> Planets3D = new(128);
        public readonly List<RingInstance3D> Rings3D = new(128);

        public SunInstance Sun;
        public bool HasSun;

        public SunInstance3D Sun3D;
        public bool HasSun3D;

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
            Stars3D.Clear();
            Debris3D.Clear();
            Asteroids3D.Clear();
            Ships3D.Clear();
            OrbitVertices3D.Clear();
            SelectionVertices3D.Clear();
            TrailVertices3D.Clear();
            Planets3D.Clear();
            Rings3D.Clear();
            HasSun = false;
            HasSun3D = false;
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

    // 3D instance variant: world-space positions replace the 2D position + depth split.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SpriteInstance3D
    {
        public Vector3 Position;
        public float Size;
        public Vector4 Color;
        public float Seed;
        public float Rotation;
        public float Type;

        public SpriteInstance3D(Vector3 position, float size, Vector4 color, float seed, float rotation, float type)
        {
            Position = position;
            Size = size;
            Color = color;
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

    // 3D line vertex variant for orbit/selection rendering in world space.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct LineVertex3D
    {
        public Vector3 Position;
        public Vector4 Color;

        public LineVertex3D(Vector3 position, Vector4 color)
        {
            Position = position;
            Color = color;
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

    // 3D trail vertex variant for world-space motion paths.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct TrailVertex3D
    {
        public Vector3 Position;
        public float Age01;
        public Vector4 Color;

        public TrailVertex3D(Vector3 position, float age01, Vector4 color)
        {
            Position = position;
            Age01 = age01;
            Color = color;
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

    // 3D planet instance variant for world-space billboard rendering.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PlanetInstance3D
    {
        public Vector3 Position;
        public float Radius;
        public float Spin;
        public float AxisTilt;
        public Vector3 LightDir;
        public float Lod;
        public float RingInner;
        public float RingOuter;
        public float RingTilt;
        public int TextureId;
        public int Seed;
        public int TextureType;
        public int TextureSize;

        public PlanetInstance3D(
            Vector3 position,
            float radius,
            float spin,
            float axisTilt,
            Vector3 lightDir,
            float lod,
            float ringInner,
            float ringOuter,
            float ringTilt,
            int textureId,
            int seed,
            int textureType,
            int textureSize)
        {
            Position = position;
            Radius = radius;
            Spin = spin;
            AxisTilt = axisTilt;
            LightDir = lightDir;
            Lod = lod;
            RingInner = ringInner;
            RingOuter = ringOuter;
            RingTilt = ringTilt;
            TextureId = textureId;
            Seed = seed;
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

    // 3D ring instance variant for world-space billboards.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct RingInstance3D
    {
        public Vector3 Position;
        public float InnerRadius;
        public float OuterRadius;
        public float Tilt;
        public float Rotation;
        public Vector3 LightDir;
        public float PlanetRadius;
        public int TextureId;
        public int Seed;
        public int TextureSize;

        public RingInstance3D(
            Vector3 position,
            float innerRadius,
            float outerRadius,
            float tilt,
            float rotation,
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

    // 3D sun instance variant for world-space placement.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SunInstance3D
    {
        public Vector3 Position;
        public float Radius;
        public int TextureId;
        public int Seed;
        public int TextureSize;

        public SunInstance3D(Vector3 position, float radius, int textureId, int seed, int textureSize)
        {
            Position = position;
            Radius = radius;
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
