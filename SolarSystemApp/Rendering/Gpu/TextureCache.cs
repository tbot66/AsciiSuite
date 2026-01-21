using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;

namespace SolarSystemApp.Rendering.Gpu
{
    internal sealed class TextureCache : IDisposable
    {
        private readonly Dictionary<TextureKey, int> _textures = new();
        private readonly FullscreenQuad _quad = new();
        private readonly bool _supportsCompute;
        private readonly ShaderProgram _planetCompute;
        private readonly ShaderProgram _ringCompute;
        private readonly ShaderProgram _sunCompute;
        private readonly ShaderProgram _planetFbo;
        private readonly ShaderProgram _ringFbo;
        private readonly ShaderProgram _sunFbo;
        private int _fbo;

        public TextureCache()
        {
            GL.GetInteger(GetPName.MajorVersion, out int major);
            GL.GetInteger(GetPName.MinorVersion, out int minor);
            _supportsCompute = major > 4 || (major == 4 && minor >= 3);

            _planetCompute = new ShaderProgram(PlanetComputeShader);
            _ringCompute = new ShaderProgram(RingComputeShader);
            _sunCompute = new ShaderProgram(SunComputeShader);
            _planetFbo = new ShaderProgram(FullscreenVertexShader, PlanetFragmentShader);
            _ringFbo = new ShaderProgram(FullscreenVertexShader, RingFragmentShader);
            _sunFbo = new ShaderProgram(FullscreenVertexShader, SunFragmentShader);
        }

        public int GetPlanetTexture(int seed, int textureType, int size)
            => GetTexture(new TextureKey(TextureKind.Planet, seed, textureType, size), PixelInternalFormat.Rgba8);

        public int GetRingTexture(int seed, int size)
            => GetTexture(new TextureKey(TextureKind.Ring, seed, 0, size), PixelInternalFormat.R8);

        public int GetSunTexture(int seed, int size)
            => GetTexture(new TextureKey(TextureKind.Sun, seed, 0, size), PixelInternalFormat.Rgba8);

        public void Dispose()
        {
            foreach (int id in _textures.Values)
                GL.DeleteTexture(id);
            _textures.Clear();

            _quad.Dispose();
            _planetCompute.Dispose();
            _ringCompute.Dispose();
            _sunCompute.Dispose();
            _planetFbo.Dispose();
            _ringFbo.Dispose();
            _sunFbo.Dispose();

            if (_fbo != 0)
                GL.DeleteFramebuffer(_fbo);
        }

        private int GetTexture(TextureKey key, PixelInternalFormat format)
        {
            if (_textures.TryGetValue(key, out int textureId))
                return textureId;

            int id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);
            GL.TexImage2D(TextureTarget.Texture2D, 0, format, key.Size, key.Size, 0,
                format == PixelInternalFormat.R8 ? PixelFormat.Red : PixelFormat.Rgba,
                PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            GenerateTexture(key, id);

            _textures[key] = id;
            return id;
        }

        private void GenerateTexture(TextureKey key, int textureId)
        {
            if (_supportsCompute)
            {
                ShaderProgram program = key.Kind switch
                {
                    TextureKind.Ring => _ringCompute,
                    TextureKind.Sun => _sunCompute,
                    _ => _planetCompute
                };

                program.Use();
                program.SetUniform("uSeed", key.Seed);
                program.SetUniform("uType", key.Type);
                program.SetUniform("uSize", key.Size);
                GL.BindImageTexture(0, textureId, 0, false, 0, TextureAccess.WriteOnly,
                    key.Kind == TextureKind.Ring ? SizedInternalFormat.R8 : SizedInternalFormat.Rgba8);
                int groups = (key.Size + 7) / 8;
                GL.DispatchCompute(groups, groups, 1);
                GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderImageAccessBarrierBit);
                return;
            }

            if (_fbo == 0)
                _fbo = GL.GenFramebuffer();

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, textureId, 0);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.Viewport(0, 0, key.Size, key.Size);

            ShaderProgram program = key.Kind switch
            {
                TextureKind.Ring => _ringFbo,
                TextureKind.Sun => _sunFbo,
                _ => _planetFbo
            };

            program.Use();
            program.SetUniform("uSeed", (float)key.Seed);
            program.SetUniform("uType", key.Type);
            program.SetUniform("uSize", key.Size);
            _quad.Draw();

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private readonly struct TextureKey : IEquatable<TextureKey>
        {
            public readonly TextureKind Kind;
            public readonly int Seed;
            public readonly int Type;
            public readonly int Size;

            public TextureKey(TextureKind kind, int seed, int type, int size)
            {
                Kind = kind;
                Seed = seed;
                Type = type;
                Size = size;
            }

            public bool Equals(TextureKey other)
                => Kind == other.Kind && Seed == other.Seed && Type == other.Type && Size == other.Size;

            public override bool Equals(object? obj)
                => obj is TextureKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine((int)Kind, Seed, Type, Size);
        }

        private enum TextureKind
        {
            Planet,
            Ring,
            Sun
        }

        private const string FullscreenVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vUv;
void main()
{
    vUv = aPos;
    vec2 ndc = aPos * 2.0 - 1.0;
    gl_Position = vec4(ndc, 0.0, 1.0);
}";

        private const string PlanetFragmentShader = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;
uniform float uSeed;
uniform int uType;

float hash(vec2 p, float seed)
{
    return fract(sin(dot(p, vec2(127.1, 311.7)) + seed) * 43758.5453);
}

float noise(vec2 p, float seed)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i, seed);
    float b = hash(i + vec2(1.0, 0.0), seed);
    float c = hash(i + vec2(0.0, 1.0), seed);
    float d = hash(i + vec2(1.0, 1.0), seed);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

float fbm(vec2 p, float seed)
{
    float value = 0.0;
    float amp = 0.5;
    for (int i = 0; i < 5; i++)
    {
        value += amp * noise(p, seed);
        p *= 2.02;
        amp *= 0.5;
    }
    return value;
}

vec3 palette(float t, vec3 a, vec3 b, vec3 c)
{
    return mix(a, b, smoothstep(0.2, 0.8, t)) + c * t * 0.15;
}

void main()
{
    float seed = uSeed * 0.01;
    float n = fbm(vUv * 4.0 + seed, seed);
    float bands = sin((vUv.y + n * 0.25) * 18.0) * 0.5 + 0.5;
    float lava = fbm(vUv * 7.0 + seed * 3.0, seed + 2.2);

    float t = n;
    if (uType == 3 || uType == 30 || uType == 31)
        t = mix(n, bands, 0.6);
    else if (uType == 17)
        t = mix(n, lava, 0.65);

    vec3 dark = vec3(0.08, 0.10, 0.16);
    vec3 mid = vec3(0.28, 0.42, 0.6);
    vec3 light = vec3(0.68, 0.78, 0.92);
    if (uType == 17)
    {
        dark = vec3(0.08, 0.02, 0.01);
        mid = vec3(0.62, 0.22, 0.07);
        light = vec3(1.0, 0.58, 0.16);
    }

    vec3 color = palette(t, dark, mid, light);
    FragColor = vec4(color, 1.0);
}";

        private const string RingFragmentShader = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;
uniform float uSeed;

float hash(vec2 p, float seed)
{
    return fract(sin(dot(p, vec2(41.7, 127.1)) + seed) * 43758.5453);
}

float noise(vec2 p, float seed)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i, seed);
    float b = hash(i + vec2(1.0, 0.0), seed);
    float c = hash(i + vec2(0.0, 1.0), seed);
    float d = hash(i + vec2(1.0, 1.0), seed);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

void main()
{
    float seed = uSeed * 0.02;
    float n = noise(vUv * 64.0, seed);
    float bands = smoothstep(0.2, 0.8, sin(vUv.x * 60.0 + n * 3.0) * 0.5 + 0.5);
    FragColor = vec4(vec3(bands), 1.0);
}";

        private const string SunFragmentShader = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;
uniform float uSeed;

float hash(vec2 p, float seed)
{
    return fract(sin(dot(p, vec2(12.7, 78.1)) + seed) * 43758.5453);
}

float noise(vec2 p, float seed)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i, seed);
    float b = hash(i + vec2(1.0, 0.0), seed);
    float c = hash(i + vec2(0.0, 1.0), seed);
    float d = hash(i + vec2(1.0, 1.0), seed);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

float fbm(vec2 p, float seed)
{
    float value = 0.0;
    float amp = 0.6;
    for (int i = 0; i < 5; i++)
    {
        value += amp * noise(p, seed);
        p *= 2.1;
        amp *= 0.5;
    }
    return value;
}

void main()
{
    float seed = uSeed * 0.01;
    float n = fbm(vUv * 6.0 + seed, seed);
    float spots = smoothstep(0.45, 0.8, fbm(vUv * 15.0 + seed * 3.0, seed + 4.2));
    vec3 base = mix(vec3(1.0, 0.68, 0.2), vec3(1.0, 0.42, 0.05), n);
    base = mix(base, vec3(0.55, 0.16, 0.02), spots * 0.35);
    FragColor = vec4(base, 1.0);
}";

        private const string PlanetComputeShader = @"
#version 430 core
layout(local_size_x = 8, local_size_y = 8) in;
layout(rgba8, binding = 0) writeonly uniform image2D destTex;
uniform int uSeed;
uniform int uType;
uniform int uSize;

float hash(vec2 p, float seed)
{
    return fract(sin(dot(p, vec2(127.1, 311.7)) + seed) * 43758.5453);
}

float noise(vec2 p, float seed)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i, seed);
    float b = hash(i + vec2(1.0, 0.0), seed);
    float c = hash(i + vec2(0.0, 1.0), seed);
    float d = hash(i + vec2(1.0, 1.0), seed);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

float fbm(vec2 p, float seed)
{
    float value = 0.0;
    float amp = 0.5;
    for (int i = 0; i < 5; i++)
    {
        value += amp * noise(p, seed);
        p *= 2.02;
        amp *= 0.5;
    }
    return value;
}

vec3 palette(float t, vec3 a, vec3 b, vec3 c)
{
    return mix(a, b, smoothstep(0.2, 0.8, t)) + c * t * 0.15;
}

void main()
{
    ivec2 id = ivec2(gl_GlobalInvocationID.xy);
    if (id.x >= uSize || id.y >= uSize)
        return;

    vec2 uv = (vec2(id) + 0.5) / float(uSize);
    float seed = float(uSeed) * 0.01;
    float n = fbm(uv * 4.0 + seed, seed);
    float bands = sin((uv.y + n * 0.25) * 18.0) * 0.5 + 0.5;
    float lava = fbm(uv * 7.0 + seed * 3.0, seed + 2.2);

    float t = n;
    if (uType == 3 || uType == 30 || uType == 31)
        t = mix(n, bands, 0.6);
    else if (uType == 17)
        t = mix(n, lava, 0.65);

    vec3 dark = vec3(0.08, 0.10, 0.16);
    vec3 mid = vec3(0.28, 0.42, 0.6);
    vec3 light = vec3(0.68, 0.78, 0.92);
    if (uType == 17)
    {
        dark = vec3(0.08, 0.02, 0.01);
        mid = vec3(0.62, 0.22, 0.07);
        light = vec3(1.0, 0.58, 0.16);
    }

    vec3 color = palette(t, dark, mid, light);
    imageStore(destTex, id, vec4(color, 1.0));
}";

        private const string RingComputeShader = @"
#version 430 core
layout(local_size_x = 8, local_size_y = 8) in;
layout(r8, binding = 0) writeonly uniform image2D destTex;
uniform int uSeed;
uniform int uSize;

float hash(vec2 p, float seed)
{
    return fract(sin(dot(p, vec2(41.7, 127.1)) + seed) * 43758.5453);
}

float noise(vec2 p, float seed)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i, seed);
    float b = hash(i + vec2(1.0, 0.0), seed);
    float c = hash(i + vec2(0.0, 1.0), seed);
    float d = hash(i + vec2(1.0, 1.0), seed);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

void main()
{
    ivec2 id = ivec2(gl_GlobalInvocationID.xy);
    if (id.x >= uSize || id.y >= uSize)
        return;

    vec2 uv = (vec2(id) + 0.5) / float(uSize);
    float seed = float(uSeed) * 0.02;
    float n = noise(uv * 64.0, seed);
    float bands = smoothstep(0.2, 0.8, sin(uv.x * 60.0 + n * 3.0) * 0.5 + 0.5);
    imageStore(destTex, id, vec4(bands, 0.0, 0.0, 1.0));
}";

        private const string SunComputeShader = @"
#version 430 core
layout(local_size_x = 8, local_size_y = 8) in;
layout(rgba8, binding = 0) writeonly uniform image2D destTex;
uniform int uSeed;
uniform int uSize;

float hash(vec2 p, float seed)
{
    return fract(sin(dot(p, vec2(12.7, 78.1)) + seed) * 43758.5453);
}

float noise(vec2 p, float seed)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i, seed);
    float b = hash(i + vec2(1.0, 0.0), seed);
    float c = hash(i + vec2(0.0, 1.0), seed);
    float d = hash(i + vec2(1.0, 1.0), seed);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

float fbm(vec2 p, float seed)
{
    float value = 0.0;
    float amp = 0.6;
    for (int i = 0; i < 5; i++)
    {
        value += amp * noise(p, seed);
        p *= 2.1;
        amp *= 0.5;
    }
    return value;
}

void main()
{
    ivec2 id = ivec2(gl_GlobalInvocationID.xy);
    if (id.x >= uSize || id.y >= uSize)
        return;

    vec2 uv = (vec2(id) + 0.5) / float(uSize);
    float seed = float(uSeed) * 0.01;
    float n = fbm(uv * 6.0 + seed, seed);
    float spots = smoothstep(0.45, 0.8, fbm(uv * 15.0 + seed * 3.0, seed + 4.2));
    vec3 base = mix(vec3(1.0, 0.68, 0.2), vec3(1.0, 0.42, 0.05), n);
    base = mix(base, vec3(0.55, 0.16, 0.02), spots * 0.35);
    imageStore(destTex, id, vec4(base, 1.0));
}";
    }
}
