using System;
using OpenTK.Graphics.OpenGL4;

namespace SolarSystemApp.Rendering.Gpu
{
    public sealed class GpuRenderer : IDisposable
    {
        private const int BytesPerPixel = 4;

        private readonly ShaderProgram _sphereShader;
        private readonly ShaderProgram _blitShader;
        private readonly FullscreenQuad _quad;

        private int _framebuffer;
        private int _colorTexture;
        private int _depthBuffer;
        private int _backgroundTexture;

        private int _width;
        private int _height;

        public int OutputTextureId => _colorTexture;

        public GpuRenderer()
        {
            _quad = new FullscreenQuad();
            _sphereShader = new ShaderProgram(SphereVertexShader, SphereFragmentShader);
            _blitShader = new ShaderProgram(BlitVertexShader, BlitFragmentShader);
        }

        public void BeginFrame(int width, int height, float clearR, float clearG, float clearB)
        {
            EnsureTargets(width, height);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            GL.Viewport(0, 0, width, height);
            GL.ClearColor(clearR, clearG, clearB, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.Disable(EnableCap.Blend);
        }

        public void EndFrame()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        public void DrawBackground(byte[] pixelBuffer, int width, int height)
        {
            if (_backgroundTexture == 0)
                return;

            GL.BindTexture(TextureTarget.Texture2D, _backgroundTexture);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixelBuffer);

            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);

            _blitShader.Use();
            _blitShader.SetUniform("uTexture", 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _backgroundTexture);

            _quad.Draw();

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        public void DrawSphere(
            int centerX,
            int centerY,
            int radius,
            int seed,
            int textureType,
            float spinTurns,
            float axisTilt,
            float lightX,
            float lightY,
            float lightZ,
            float depth,
            PlanetPalette palette,
            PlanetPalette emissive)
        {
            if (radius <= 0)
                return;

            _sphereShader.Use();
            _sphereShader.SetUniform("uResolution", _width, _height);
            _sphereShader.SetUniform("uCenter", centerX, centerY);
            _sphereShader.SetUniform("uRadius", radius);
            _sphereShader.SetUniform("uSeed", seed * 1f);
            _sphereShader.SetUniform("uSpin", spinTurns);
            _sphereShader.SetUniform("uAxisTilt", axisTilt);
            _sphereShader.SetUniform("uLightDir", lightX, lightY, lightZ);
            _sphereShader.SetUniform("uDepth", depth);
            _sphereShader.SetUniform("uTextureType", textureType);
            _sphereShader.SetUniform("uColorDark", palette.DarkR, palette.DarkG, palette.DarkB);
            _sphereShader.SetUniform("uColorMid", palette.MidR, palette.MidG, palette.MidB);
            _sphereShader.SetUniform("uColorLight", palette.LightR, palette.LightG, palette.LightB);
            _sphereShader.SetUniform("uEmissive", emissive.MidR, emissive.MidG, emissive.MidB);

            _quad.Draw();
        }

        public void Dispose()
        {
            _sphereShader.Dispose();
            _blitShader.Dispose();
            _quad.Dispose();

            if (_framebuffer != 0)
                GL.DeleteFramebuffer(_framebuffer);
            if (_colorTexture != 0)
                GL.DeleteTexture(_colorTexture);
            if (_depthBuffer != 0)
                GL.DeleteRenderbuffer(_depthBuffer);
            if (_backgroundTexture != 0)
                GL.DeleteTexture(_backgroundTexture);
        }

        private void EnsureTargets(int width, int height)
        {
            if (_width == width && _height == height && _framebuffer != 0)
                return;

            _width = Math.Max(1, width);
            _height = Math.Max(1, height);

            if (_framebuffer == 0)
                _framebuffer = GL.GenFramebuffer();
            if (_colorTexture == 0)
                _colorTexture = GL.GenTexture();
            if (_depthBuffer == 0)
                _depthBuffer = GL.GenRenderbuffer();
            if (_backgroundTexture == 0)
                _backgroundTexture = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, _colorTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, _width, _height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthBuffer);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, _width, _height);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _colorTexture, 0);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depthBuffer);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);

            GL.BindTexture(TextureTarget.Texture2D, _backgroundTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, _width, _height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private const string BlitVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vUv;
void main()
{
    vUv = vec2(aPos.x, 1.0 - aPos.y);
    vec2 ndc = aPos * 2.0 - 1.0;
    gl_Position = vec4(ndc.x, ndc.y, 0.0, 1.0);
}";

        private const string BlitFragmentShader = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;
uniform sampler2D uTexture;
void main()
{
    FragColor = texture(uTexture, vUv);
}";

        private const string SphereVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
uniform vec2 uResolution;
uniform vec2 uCenter;
uniform float uRadius;
void main()
{
    vec2 screenPos = uCenter + (aPos * 2.0 - 1.0) * uRadius;
    vec2 ndc = vec2(
        (screenPos.x / uResolution.x) * 2.0 - 1.0,
        (screenPos.y / uResolution.y) * 2.0 - 1.0
    );
    gl_Position = vec4(ndc.x, ndc.y, 0.0, 1.0);
}";

        private const string SphereFragmentShader = @"
#version 330 core
out vec4 FragColor;
uniform vec2 uResolution;
uniform vec2 uCenter;
uniform float uRadius;
uniform float uSpin;
uniform float uAxisTilt;
uniform vec3 uLightDir;
uniform float uDepth;
uniform int uTextureType;
uniform vec3 uColorDark;
uniform vec3 uColorMid;
uniform vec3 uColorLight;
uniform vec3 uEmissive;
uniform float uSeed;

const float PI = 3.14159265359;

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
    for (int i = 0; i < 4; i++)
    {
        value += amp * noise(p);
        p *= 2.02;
        amp *= 0.5;
    }
    return value;
}

void main()
{
    vec2 delta = gl_FragCoord.xy - uCenter;
    vec2 p = vec2(delta.x, -delta.y) / uRadius;
    float r2 = dot(p, p);
    if (r2 > 1.0)
        discard;

    float nz = sqrt(max(0.0, 1.0 - r2));
    vec3 n = normalize(vec3(p.x, p.y, nz));

    float ct = cos(uAxisTilt);
    float st = sin(uAxisTilt);
    float tx = n.x * ct - n.y * st;
    float ty = n.x * st + n.y * ct;
    float tz = n.z;

    float lon = atan(tx, tz);
    float u = lon / (2.0 * PI) + 0.5 + uSpin;
    float v = asin(clamp(ty, -1.0, 1.0)) / PI + 0.5;
    vec2 uv = vec2(fract(u), v);

    float baseNoise = fbm(uv * 4.0 + uSeed * 0.01);
    float pattern = baseNoise;

    if (uTextureType == 3 || uTextureType == 30 || uTextureType == 31)
    {
        float bands = sin((uv.y + baseNoise * 0.25) * 18.0) * 0.5 + 0.5;
        float swirl = fbm(vec2(uv.x * 3.0, uv.y * 8.0) + uSeed * 0.02);
        pattern = mix(baseNoise, bands, 0.65);
        if (uTextureType != 3)
            pattern = mix(pattern, swirl, 0.35);
    }
    else if (uTextureType == 17)
    {
        float lava = fbm(uv * 7.0 + uSeed * 0.04);
        pattern = mix(baseNoise, lava, 0.6);
    }
    else if (uTextureType == 19)
    {
        float cracks = abs(sin(uv.x * 35.0 + baseNoise * 6.0)) * abs(sin(uv.y * 30.0 + baseNoise * 5.0));
        pattern = mix(baseNoise, 1.0 - cracks, 0.55);
    }

    float midT = smoothstep(0.25, 0.75, pattern);
    float lightT = smoothstep(0.65, 0.95, pattern);

    vec3 baseColor = mix(uColorDark, uColorMid, midT);
    baseColor = mix(baseColor, uColorLight, lightT);

    float ndotlRaw = dot(n, uLightDir);
    float ndotl = max(ndotlRaw, 0.0);
    float limb = 0.78 + 0.22 * n.z;
    float light = clamp(ndotl * limb, 0.0, 1.0);
    vec3 litColor = baseColor * light;

    if (uTextureType == 17)
    {
        float glow = smoothstep(0.6, 1.0, pattern);
        float night = clamp((0.22 - ndotlRaw) / 0.70, 0.0, 1.0);
        litColor = mix(litColor, uEmissive, glow * night * 0.65);
    }

    gl_FragDepth = clamp(uDepth, 0.0, 1.0);
    FragColor = vec4(litColor, 1.0);
}";
    }

    public readonly struct PlanetPalette
    {
        public readonly float DarkR;
        public readonly float DarkG;
        public readonly float DarkB;
        public readonly float MidR;
        public readonly float MidG;
        public readonly float MidB;
        public readonly float LightR;
        public readonly float LightG;
        public readonly float LightB;

        public PlanetPalette(
            float darkR, float darkG, float darkB,
            float midR, float midG, float midB,
            float lightR, float lightG, float lightB)
        {
            DarkR = darkR;
            DarkG = darkG;
            DarkB = darkB;
            MidR = midR;
            MidG = midG;
            MidB = midB;
            LightR = lightR;
            LightG = lightG;
            LightB = lightB;
        }
    }
}
