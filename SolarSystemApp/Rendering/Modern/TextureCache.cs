using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using SolarSystemApp.Rendering.Gpu;

namespace SolarSystemApp.Rendering.Modern
{
    internal enum ProceduralTextureKind
    {
        Planet = 0,
        Ring = 1,
        Sun = 2
    }

    internal sealed class TextureCache : IDisposable
    {
        private readonly Dictionary<TextureKey, TextureEntry> _cache = new Dictionary<TextureKey, TextureEntry>();
        private readonly ShaderProgram _generator;
        private readonly FullscreenQuad _quad = new FullscreenQuad();
        private int _fbo;

        public TextureCache()
        {
            _generator = new ShaderProgram(GeneratorVertexShader, GeneratorFragmentShader);
        }

        public int GetTexture(ProceduralTextureKind kind, int seed, int size)
        {
            TextureKey key = new TextureKey(kind, seed, size);
            if (_cache.TryGetValue(key, out TextureEntry entry))
                return entry.TextureId;

            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, size, size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            if (_fbo == 0)
                _fbo = GL.GenFramebuffer();

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, tex, 0);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.Viewport(0, 0, size, size);

            _generator.Use();
            _generator.SetUniform("uSeed", seed * 1f);
            _generator.SetUniform("uType", (int)kind);
            _generator.SetUniform("uSize", size * 1f);
            _quad.Draw();

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            entry = new TextureEntry(tex, size);
            _cache[key] = entry;
            return tex;
        }

        public void Dispose()
        {
            foreach (TextureEntry entry in _cache.Values)
                GL.DeleteTexture(entry.TextureId);
            _cache.Clear();

            if (_fbo != 0)
                GL.DeleteFramebuffer(_fbo);

            _generator.Dispose();
            _quad.Dispose();
        }

        private readonly struct TextureKey : IEquatable<TextureKey>
        {
            private readonly ProceduralTextureKind _kind;
            private readonly int _seed;
            private readonly int _size;

            public TextureKey(ProceduralTextureKind kind, int seed, int size)
            {
                _kind = kind;
                _seed = seed;
                _size = size;
            }

            public bool Equals(TextureKey other) => _kind == other._kind && _seed == other._seed && _size == other._size;
            public override bool Equals(object obj) => obj is TextureKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine((int)_kind, _seed, _size);
        }

        private readonly struct TextureEntry
        {
            public readonly int TextureId;
            public readonly int Size;

            public TextureEntry(int textureId, int size)
            {
                TextureId = textureId;
                Size = size;
            }
        }

        private const string GeneratorVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vUv;
void main()
{
    vUv = aPos;
    vec2 ndc = aPos * 2.0 - 1.0;
    gl_Position = vec4(ndc.x, ndc.y, 0.0, 1.0);
}";

        private const string GeneratorFragmentShader = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;
uniform float uSeed;
uniform int uType;
uniform float uSize;

float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7)) + uSeed) * 43758.5453);
}

float noise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

float fbm(vec2 p)
{
    float value = 0.0;
    float amp = 0.5;
    for (int i = 0; i < 5; i++)
    {
        value += amp * noise(p);
        p *= 2.02;
        amp *= 0.5;
    }
    return value;
}

void main()
{
    vec2 uv = vUv;
    float n = fbm(uv * 6.0 + uSeed * 0.01);

    if (uType == 1)
    {
        float bands = smoothstep(0.35, 0.75, abs(uv.y - 0.5) * 2.0);
        float ring = mix(0.2, 1.0, bands) * (0.5 + 0.5 * n);
        FragColor = vec4(vec3(ring), 1.0);
        return;
    }

    if (uType == 2)
    {
        float swirl = fbm(uv * 10.0 + vec2(uSeed * 0.03, uSeed * 0.05));
        vec3 sun = vec3(1.0, 0.6, 0.2) * (0.65 + 0.35 * n) + vec3(0.4, 0.1, 0.0) * swirl;
        FragColor = vec4(sun, 1.0);
        return;
    }

    vec3 base = mix(vec3(0.12, 0.18, 0.28), vec3(0.5, 0.5, 0.4), n);
    FragColor = vec4(base, 1.0);
}";
    }
}
