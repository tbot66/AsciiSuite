using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace SolarSystemApp.Rendering.Gpu
{
    public sealed class GpuRenderer : IDisposable
    {
        private const int MaxOccluders = 256;
        private const int DefaultPlanetTextureSize = 256;
        private readonly FullscreenQuad _fullscreenQuad;
        private readonly TextureCache _textureCache;
        private readonly bool _supportsPersistentMapping;

        private readonly ShaderProgram _spriteShader;
        private readonly ShaderProgram _planetShader;
        private readonly ShaderProgram _ringShader;
        private readonly ShaderProgram _sunShader;
        private readonly ShaderProgram _lineShader;
        private readonly ShaderProgram _trailShader;
        // 3D shader variants added for world-space billboarding and line rendering.
        private readonly ShaderProgram _spriteShader3D;
        private readonly ShaderProgram _planetShader3D;
        private readonly ShaderProgram _ringShader3D;
        private readonly ShaderProgram _sunShader3D;
        private readonly ShaderProgram _lineShader3D;
        private readonly ShaderProgram _trailShader3D;
        private readonly ShaderProgram _bloomPrefilterShader;
        private readonly ShaderProgram _bloomBlurShader;
        private readonly ShaderProgram _compositeShader;
        private readonly ShaderProgram _blitShader;

        private int _quadVao;
        private int _quadVbo;
        private int _spriteInstanceVbo;
        private int _planetInstanceVbo;
        private int _ringInstanceVbo;
        private int _lineVao;
        private int _lineVbo;
        private int _trailVao;
        private int _trailVbo;
        private int _lineVao3D;
        private int _lineVbo3D;
        private int _trailVao3D;
        private int _trailVbo3D;
        private int _occluderSsbo;
        private int _legacyBackgroundTexture;

        private int _sceneFbo;
        private int _sceneColorTex;
        private int _sceneDepthTex;
        private int _bloomFboA;
        private int _bloomTexA;
        private int _bloomFboB;
        private int _bloomTexB;
        private int _finalFbo;
        private int _finalColorTex;
        private int _overlayTexture;

        private int _width;
        private int _height;
        private int _occluderCount;

        private DynamicBuffer _spriteBuffer;
        private DynamicBuffer _planetBuffer;
        private DynamicBuffer _ringBuffer;
        private DynamicBuffer _lineBuffer;
        private DynamicBuffer _lineBuffer3D;
        private DynamicBuffer _trailBuffer;
        private DynamicBuffer _trailBuffer3D;
        private DynamicBuffer _occluderBuffer;

        private readonly RenderFrameData _legacyFrame = new RenderFrameData();
        private readonly List<LineVertex3D> _compatOrbitVertices3D = new(4096);
        private readonly List<LineVertex3D> _compatSelectionVertices3D = new(256);
        private readonly List<TrailVertex3D> _compatTrailVertices3D = new(4096);
        private byte[]? _legacyBackground;
        private int _legacyBackgroundWidth;
        private int _legacyBackgroundHeight;
        private bool _legacyFrameActive;
        private float _legacyClearR;
        private float _legacyClearG;
        private float _legacyClearB;
        private RenderMode _renderMode = RenderMode.Ortho2D;
        private Vector3 _camRight;
        private Vector3 _camUp;

        public int OutputTextureId => _finalColorTex;

        public GpuRenderer()
        {
            _supportsPersistentMapping = CheckBufferStorageSupport();
            _fullscreenQuad = new FullscreenQuad();
            _textureCache = new TextureCache();

            CreateUnitQuad();
            CreateLineBuffers();

            _spriteShader = new ShaderProgram(SpriteVertexShader, SpriteFragmentShader);
            _planetShader = new ShaderProgram(PlanetVertexShader, PlanetFragmentShader);
            _ringShader = new ShaderProgram(RingVertexShader, RingFragmentShader);
            _sunShader = new ShaderProgram(SunVertexShader, SunFragmentShader);
            _lineShader = new ShaderProgram(LineVertexShader, LineFragmentShader);
            _trailShader = new ShaderProgram(TrailVertexShader, TrailFragmentShader);
            _spriteShader3D = new ShaderProgram(SpriteVertexShader3D, SpriteFragmentShader3D);
            _planetShader3D = new ShaderProgram(PlanetVertexShader3D, PlanetFragmentShader3D);
            _ringShader3D = new ShaderProgram(RingVertexShader3D, RingFragmentShader3D);
            _sunShader3D = new ShaderProgram(SunVertexShader3D, SunFragmentShader3D);
            _lineShader3D = new ShaderProgram(LineVertexShader3D, LineFragmentShader3D);
            _trailShader3D = new ShaderProgram(TrailVertexShader3D, TrailFragmentShader3D);
            _bloomPrefilterShader = new ShaderProgram(BloomPrefilterVertexShader, BloomPrefilterFragmentShader);
            _bloomBlurShader = new ShaderProgram(BloomBlurVertexShader, BloomBlurFragmentShader);
            _compositeShader = new ShaderProgram(CompositeVertexShader, CompositeFragmentShader);
            _blitShader = new ShaderProgram(BlitVertexShader, BlitFragmentShader);
        }

        public int GetPlanetTexture(int seed, int textureType, int size)
            => _textureCache.GetPlanetTexture(seed, textureType, size);

        public int GetRingTexture(int seed, int size)
            => _textureCache.GetRingTexture(seed, size);

        public int GetSunTexture(int seed, int size)
            => _textureCache.GetSunTexture(seed, size);

        public void BeginFrame(int width, int height, float clearR, float clearG, float clearB)
        {
            _legacyFrameActive = true;
            _legacyBackground = null;
            _legacyBackgroundWidth = 0;
            _legacyBackgroundHeight = 0;
            _legacyFrame.Clear();
            _legacyClearR = clearR;
            _legacyClearG = clearG;
            _legacyClearB = clearB;
            _width = width;
            _height = height;
        }

        public void DrawBackground(byte[] pixelBuffer, int width, int height)
        {
            if (!_legacyFrameActive)
                return;

            _legacyBackground = pixelBuffer;
            _legacyBackgroundWidth = width;
            _legacyBackgroundHeight = height;
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
            if (!_legacyFrameActive || radius <= 0)
                return;

            PlanetInstance planet = new PlanetInstance(
                new Vector2(centerX, centerY),
                radius,
                spinTurns,
                axisTilt,
                depth,
                new Vector3(lightX, lightY, lightZ),
                0,
                seed,
                1f,
                0f,
                0f,
                0f,
                textureType,
                DefaultPlanetTextureSize);

            _legacyFrame.Planets.Add(planet);
        }

        public void EndFrame()
        {
            if (!_legacyFrameActive)
                return;

            Matrix4 viewProj = Matrix4.CreateOrthographicOffCenter(0, _width, _height, 0, -1f, 1f);

            EnsureTargets(_width, _height);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFbo);
            GL.Viewport(0, 0, _width, _height);
            GL.ClearColor(_legacyClearR, _legacyClearG, _legacyClearB, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            if (_legacyBackground != null)
            {
                EnsureLegacyBackgroundTexture();
                GL.BindTexture(TextureTarget.Texture2D, _legacyBackgroundTexture);
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, _legacyBackgroundWidth, _legacyBackgroundHeight, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, _legacyBackground);

                _blitShader.Use();
                _blitShader.SetUniform("uTexture", 0);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, _legacyBackgroundTexture);
                _fullscreenQuad.Draw();
            }

            UploadOccluders(_legacyFrame);
            DrawPlanets(viewProj, 0f, _legacyFrame.Planets);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            ApplyBloom();
            CompositeFinal(0f);

            _legacyFrameActive = false;
        }

        internal void RenderFrame(
            int width,
            int height,
            Matrix4 viewProj,
            float time,
            RenderFrameData frame,
            byte[]? overlayBuffer,
            int overlayWidth,
            int overlayHeight)
        {
            RenderFrameInternal(width, height, viewProj, time, frame, overlayBuffer, overlayWidth, overlayHeight, RenderMode.Ortho2D);
        }

        internal void RenderFrame3D(
            int width,
            int height,
            Camera3D camera,
            float time,
            RenderFrameData frame,
            byte[]? overlayBuffer,
            int overlayWidth,
            int overlayHeight)
        {
            Matrix4 viewProj = camera.Projection * camera.View;
            _camRight = camera.Right;
            _camUp = camera.Up;
            RenderFrameInternal(width, height, viewProj, time, frame, overlayBuffer, overlayWidth, overlayHeight, RenderMode.Perspective3D);
        }

        private void RenderFrameInternal(
            int width,
            int height,
            Matrix4 viewProj,
            float time,
            RenderFrameData frame,
            byte[]? overlayBuffer,
            int overlayWidth,
            int overlayHeight,
            RenderMode mode)
        {
            _renderMode = mode;
            EnsureTargets(width, height);
            UpdateOverlayTexture(overlayBuffer, overlayWidth, overlayHeight);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFbo);
            GL.Viewport(0, 0, _width, _height);
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            if (mode == RenderMode.Ortho2D)
            {
                UploadOccluders(frame);

                DrawSprites(viewProj, time, frame.Stars, SpriteCategory.Star);
                DrawSprites(viewProj, time, frame.Debris, SpriteCategory.Debris);
                DrawLines(viewProj, frame.OrbitVertices);

                if (frame.HasSun)
                    DrawSun(viewProj, time, ref frame.Sun);

                DrawPlanets(viewProj, time, frame.Planets);
                DrawRings(viewProj, time, frame.Rings);

                DrawLines(viewProj, frame.SelectionVertices);
                DrawTrails(viewProj, time, frame.TrailVertices);

                DrawSprites(viewProj, time, frame.Asteroids, SpriteCategory.Asteroid);
                DrawSprites(viewProj, time, frame.Ships, SpriteCategory.Ship);
            }
            else
            {
                // 3D mode skips occluder discards until we have true 3D occluder data.
                _occluderCount = 0;

                DrawSprites3D(viewProj, time, frame.Stars3D, SpriteCategory.Star);
                DrawSprites3D(viewProj, time, frame.Debris3D, SpriteCategory.Debris);
                DrawLines3D(viewProj, EnsureLineVertices3D(frame.OrbitVertices3D, frame.OrbitVertices, _compatOrbitVertices3D));

                if (frame.HasSun3D)
                    DrawSun3D(viewProj, time, ref frame.Sun3D);

                DrawPlanets3D(viewProj, time, frame.Planets3D);
                DrawRings3D(viewProj, time, frame.Rings3D);

                DrawLines3D(viewProj, EnsureLineVertices3D(frame.SelectionVertices3D, frame.SelectionVertices, _compatSelectionVertices3D));
                DrawTrails3D(viewProj, time, EnsureTrailVertices3D(frame.TrailVertices3D, frame.TrailVertices, _compatTrailVertices3D));

                DrawSprites3D(viewProj, time, frame.Asteroids3D, SpriteCategory.Asteroid);
                DrawSprites3D(viewProj, time, frame.Ships3D, SpriteCategory.Ship);
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            ApplyBloom();
            CompositeFinal(time);
        }

        public void Dispose()
        {
            _fullscreenQuad.Dispose();
            _textureCache.Dispose();
            _spriteShader.Dispose();
            _planetShader.Dispose();
            _ringShader.Dispose();
            _sunShader.Dispose();
            _lineShader.Dispose();
            _trailShader.Dispose();
            _spriteShader3D.Dispose();
            _planetShader3D.Dispose();
            _ringShader3D.Dispose();
            _sunShader3D.Dispose();
            _lineShader3D.Dispose();
            _trailShader3D.Dispose();
            _bloomPrefilterShader.Dispose();
            _bloomBlurShader.Dispose();
            _compositeShader.Dispose();
            _blitShader.Dispose();

            DeleteBuffer(ref _quadVbo);
            DeleteVertexArray(ref _quadVao);
            DeleteBuffer(ref _spriteInstanceVbo);
            DeleteBuffer(ref _planetInstanceVbo);
            DeleteBuffer(ref _ringInstanceVbo);
            DeleteBuffer(ref _lineVbo);
            DeleteVertexArray(ref _lineVao);
            DeleteBuffer(ref _trailVbo);
            DeleteVertexArray(ref _trailVao);
            DeleteBuffer(ref _lineVbo3D);
            DeleteVertexArray(ref _lineVao3D);
            DeleteBuffer(ref _trailVbo3D);
            DeleteVertexArray(ref _trailVao3D);
            DeleteBuffer(ref _occluderSsbo);
            DeleteTexture(ref _legacyBackgroundTexture);

            DeleteFramebuffer(ref _sceneFbo);
            DeleteTexture(ref _sceneColorTex);
            DeleteTexture(ref _sceneDepthTex);
            DeleteFramebuffer(ref _bloomFboA);
            DeleteTexture(ref _bloomTexA);
            DeleteFramebuffer(ref _bloomFboB);
            DeleteTexture(ref _bloomTexB);
            DeleteFramebuffer(ref _finalFbo);
            DeleteTexture(ref _finalColorTex);
            DeleteTexture(ref _overlayTexture);
        }

        private void EnsureLegacyBackgroundTexture()
        {
            if (_legacyBackgroundTexture != 0)
                return;

            _legacyBackgroundTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _legacyBackgroundTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        private void CreateUnitQuad()
        {
            float[] vertices =
            {
                0f, 0f,
                1f, 0f,
                1f, 1f,
                0f, 0f,
                1f, 1f,
                0f, 1f
            };

            _quadVao = GL.GenVertexArray();
            _quadVbo = GL.GenBuffer();

            GL.BindVertexArray(_quadVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _quadVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);

            GL.BindVertexArray(0);
        }

        private void CreateLineBuffers()
        {
            _lineVao = GL.GenVertexArray();
            _lineVbo = GL.GenBuffer();
            _trailVao = GL.GenVertexArray();
            _trailVbo = GL.GenBuffer();
            _lineVao3D = GL.GenVertexArray();
            _lineVbo3D = GL.GenBuffer();
            _trailVao3D = GL.GenVertexArray();
            _trailVbo3D = GL.GenBuffer();

            GL.BindVertexArray(_lineVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _lineVbo);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, Marshal.SizeOf<LineVertex>(), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, Marshal.SizeOf<LineVertex>(), Marshal.OffsetOf<LineVertex>(nameof(LineVertex.Color)));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, Marshal.SizeOf<LineVertex>(), Marshal.OffsetOf<LineVertex>(nameof(LineVertex.Depth)));

            GL.BindVertexArray(_trailVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _trailVbo);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, Marshal.SizeOf<TrailVertex>(), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, Marshal.SizeOf<TrailVertex>(), Marshal.OffsetOf<TrailVertex>(nameof(TrailVertex.Age01)));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, Marshal.SizeOf<TrailVertex>(), Marshal.OffsetOf<TrailVertex>(nameof(TrailVertex.Color)));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, Marshal.SizeOf<TrailVertex>(), Marshal.OffsetOf<TrailVertex>(nameof(TrailVertex.Depth)));

            // 3D line/trail VAOs use vec3 positions without depth splits.
            GL.BindVertexArray(_lineVao3D);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _lineVbo3D);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Marshal.SizeOf<LineVertex3D>(), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, Marshal.SizeOf<LineVertex3D>(), Marshal.OffsetOf<LineVertex3D>(nameof(LineVertex3D.Color)));

            GL.BindVertexArray(_trailVao3D);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _trailVbo3D);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Marshal.SizeOf<TrailVertex3D>(), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, Marshal.SizeOf<TrailVertex3D>(), Marshal.OffsetOf<TrailVertex3D>(nameof(TrailVertex3D.Age01)));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, Marshal.SizeOf<TrailVertex3D>(), Marshal.OffsetOf<TrailVertex3D>(nameof(TrailVertex3D.Color)));

            GL.BindVertexArray(0);
        }

        private void EnsureTargets(int width, int height)
        {
            if (_width == width && _height == height && _sceneFbo != 0)
                return;

            _width = Math.Max(1, width);
            _height = Math.Max(1, height);

            EnsureFramebuffer(ref _sceneFbo, ref _sceneColorTex, ref _sceneDepthTex, _width, _height, PixelInternalFormat.Rgba16f, true);
            EnsureFramebuffer(ref _finalFbo, ref _finalColorTex, ref _sceneDepthTex, _width, _height, PixelInternalFormat.Rgba8, false);

            int bloomW = Math.Max(1, _width / 4);
            int bloomH = Math.Max(1, _height / 4);
            EnsureFramebuffer(ref _bloomFboA, ref _bloomTexA, ref _sceneDepthTex, bloomW, bloomH, PixelInternalFormat.Rgba16f, false);
            EnsureFramebuffer(ref _bloomFboB, ref _bloomTexB, ref _sceneDepthTex, bloomW, bloomH, PixelInternalFormat.Rgba16f, false);

            if (_overlayTexture == 0)
            {
                _overlayTexture = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, _overlayTexture);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            }
        }

        private void EnsureFramebuffer(ref int fbo, ref int colorTex, ref int depthTex, int width, int height, PixelInternalFormat format, bool depth)
        {
            if (fbo == 0)
                fbo = GL.GenFramebuffer();
            if (colorTex == 0)
                colorTex = GL.GenTexture();

            PixelType type = format == PixelInternalFormat.Rgba16f ? PixelType.HalfFloat : PixelType.UnsignedByte;

            GL.BindTexture(TextureTarget.Texture2D, colorTex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, format, width, height, 0, PixelFormat.Rgba, type, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, colorTex, 0);

            if (depth)
            {
                if (depthTex == 0)
                    depthTex = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, depthTex);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, width, height, 0,
                    PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                    TextureTarget.Texture2D, depthTex, 0);
            }

            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private void UpdateOverlayTexture(byte[]? overlayBuffer, int overlayWidth, int overlayHeight)
        {
            if (_overlayTexture == 0 || overlayBuffer == null || overlayBuffer.Length == 0)
                return;

            GL.BindTexture(TextureTarget.Texture2D, _overlayTexture);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, overlayWidth, overlayHeight, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, overlayBuffer);
        }

        private void UploadOccluders(RenderFrameData frame)
        {
            _occluderCount = Math.Min(frame.Occluders.Count, MaxOccluders);
            if (_occluderCount == 0)
                return;

            int size = _occluderCount * Marshal.SizeOf<OccluderData>();
            EnsureDynamicBuffer(ref _occluderBuffer, ref _occluderSsbo, BufferTarget.ShaderStorageBuffer, size);

            UploadDynamicBuffer(_occluderBuffer, _occluderSsbo, frame.Occluders.GetRange(0, _occluderCount));
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, _occluderSsbo);
        }

        private void DrawSprites(Matrix4 viewProj, float time, List<SpriteInstance> sprites, SpriteCategory category)
        {
            if (sprites.Count == 0)
                return;

            EnsureDynamicBuffer(ref _spriteBuffer, ref _spriteInstanceVbo, BufferTarget.ArrayBuffer, sprites.Count * Marshal.SizeOf<SpriteInstance>());
            UploadDynamicBuffer(_spriteBuffer, _spriteInstanceVbo, sprites);

            GL.BindVertexArray(_quadVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _spriteInstanceVbo);

            int stride = Marshal.SizeOf<SpriteInstance>();
            EnableInstancedAttrib(1, 2, stride, Marshal.OffsetOf<SpriteInstance>(nameof(SpriteInstance.Position)));
            EnableInstancedAttrib(2, 1, stride, Marshal.OffsetOf<SpriteInstance>(nameof(SpriteInstance.Size)));
            EnableInstancedAttrib(3, 4, stride, Marshal.OffsetOf<SpriteInstance>(nameof(SpriteInstance.Color)));
            EnableInstancedAttrib(4, 1, stride, Marshal.OffsetOf<SpriteInstance>(nameof(SpriteInstance.Depth)));
            EnableInstancedAttrib(5, 1, stride, Marshal.OffsetOf<SpriteInstance>(nameof(SpriteInstance.Seed)));
            EnableInstancedAttrib(6, 1, stride, Marshal.OffsetOf<SpriteInstance>(nameof(SpriteInstance.Rotation)));
            EnableInstancedAttrib(7, 1, stride, Marshal.OffsetOf<SpriteInstance>(nameof(SpriteInstance.Type)));

            _spriteShader.Use();
            _spriteShader.SetUniform("uViewProj", viewProj);
            _spriteShader.SetUniform("uTime", time);
            _spriteShader.SetUniform("uCategory", (int)category);

            GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, sprites.Count);
        }

        // 3D sprite drawing uses world-space positions and camera-facing billboards.
        private void DrawSprites3D(Matrix4 viewProj, float time, List<SpriteInstance3D> sprites, SpriteCategory category)
        {
            if (sprites.Count == 0)
                return;

            EnsureDynamicBuffer(ref _spriteBuffer, ref _spriteInstanceVbo, BufferTarget.ArrayBuffer, sprites.Count * Marshal.SizeOf<SpriteInstance3D>());
            UploadDynamicBuffer(_spriteBuffer, _spriteInstanceVbo, sprites);

            GL.BindVertexArray(_quadVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _spriteInstanceVbo);

            int stride = Marshal.SizeOf<SpriteInstance3D>();
            EnableInstancedAttrib(1, 3, stride, Marshal.OffsetOf<SpriteInstance3D>(nameof(SpriteInstance3D.Position)));
            EnableInstancedAttrib(2, 1, stride, Marshal.OffsetOf<SpriteInstance3D>(nameof(SpriteInstance3D.Size)));
            EnableInstancedAttrib(3, 4, stride, Marshal.OffsetOf<SpriteInstance3D>(nameof(SpriteInstance3D.Color)));
            EnableInstancedAttrib(4, 1, stride, Marshal.OffsetOf<SpriteInstance3D>(nameof(SpriteInstance3D.Seed)));
            EnableInstancedAttrib(5, 1, stride, Marshal.OffsetOf<SpriteInstance3D>(nameof(SpriteInstance3D.Rotation)));
            EnableInstancedAttrib(6, 1, stride, Marshal.OffsetOf<SpriteInstance3D>(nameof(SpriteInstance3D.Type)));

            _spriteShader3D.Use();
            _spriteShader3D.SetUniform("uViewProj", viewProj);
            _spriteShader3D.SetUniform("uTime", time);
            _spriteShader3D.SetUniform("uCategory", (int)category);
            _spriteShader3D.SetUniform("uCamRight", _camRight);
            _spriteShader3D.SetUniform("uCamUp", _camUp);

            GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, sprites.Count);
        }

        private void DrawLines(Matrix4 viewProj, List<LineVertex> vertices)
        {
            if (vertices.Count == 0)
                return;

            EnsureDynamicBuffer(ref _lineBuffer, ref _lineVbo, BufferTarget.ArrayBuffer, vertices.Count * Marshal.SizeOf<LineVertex>());
            UploadDynamicBuffer(_lineBuffer, _lineVbo, vertices);

            GL.BindVertexArray(_lineVao);
            _lineShader.Use();
            _lineShader.SetUniform("uViewProj", viewProj);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertices.Count);
        }

        // 3D line drawing uses vec3 positions for orbit/selection geometry.
        private void DrawLines3D(Matrix4 viewProj, List<LineVertex3D> vertices)
        {
            if (vertices.Count == 0)
                return;

            EnsureDynamicBuffer(ref _lineBuffer3D, ref _lineVbo3D, BufferTarget.ArrayBuffer, vertices.Count * Marshal.SizeOf<LineVertex3D>());
            UploadDynamicBuffer(_lineBuffer3D, _lineVbo3D, vertices);

            GL.BindVertexArray(_lineVao3D);
            _lineShader3D.Use();
            _lineShader3D.SetUniform("uViewProj", viewProj);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertices.Count);
        }

        private void DrawTrails(Matrix4 viewProj, float time, List<TrailVertex> vertices)
        {
            if (vertices.Count == 0)
                return;

            EnsureDynamicBuffer(ref _trailBuffer, ref _trailVbo, BufferTarget.ArrayBuffer, vertices.Count * Marshal.SizeOf<TrailVertex>());
            UploadDynamicBuffer(_trailBuffer, _trailVbo, vertices);

            GL.BindVertexArray(_trailVao);
            _trailShader.Use();
            _trailShader.SetUniform("uViewProj", viewProj);
            _trailShader.SetUniform("uTime", time);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertices.Count);
        }

        // 3D trail drawing uses vec3 positions and the same age/color shading.
        private void DrawTrails3D(Matrix4 viewProj, float time, List<TrailVertex3D> vertices)
        {
            if (vertices.Count == 0)
                return;

            EnsureDynamicBuffer(ref _trailBuffer3D, ref _trailVbo3D, BufferTarget.ArrayBuffer, vertices.Count * Marshal.SizeOf<TrailVertex3D>());
            UploadDynamicBuffer(_trailBuffer3D, _trailVbo3D, vertices);

            GL.BindVertexArray(_trailVao3D);
            _trailShader3D.Use();
            _trailShader3D.SetUniform("uViewProj", viewProj);
            _trailShader3D.SetUniform("uTime", time);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertices.Count);
        }

        private void DrawPlanets(Matrix4 viewProj, float time, List<PlanetInstance> planets)
        {
            if (planets.Count == 0)
                return;

            for (int i = 0; i < planets.Count; i++)
            {
                PlanetInstance planet = planets[i];
                if (planet.TextureId == 0)
                {
                    planet.TextureId = GetPlanetTexture(planet.Seed, planet.TextureType, planet.TextureSize);
                    planets[i] = planet;
                }
            }

            planets.Sort((a, b) => a.TextureId.CompareTo(b.TextureId));

            EnsureDynamicBuffer(ref _planetBuffer, ref _planetInstanceVbo, BufferTarget.ArrayBuffer, planets.Count * Marshal.SizeOf<PlanetInstance>());
            UploadDynamicBuffer(_planetBuffer, _planetInstanceVbo, planets);

            GL.BindVertexArray(_quadVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _planetInstanceVbo);

            int stride = Marshal.SizeOf<PlanetInstance>();
            EnableInstancedAttrib(1, 2, stride, Marshal.OffsetOf<PlanetInstance>(nameof(PlanetInstance.Position)));
            EnableInstancedAttrib(2, 1, stride, Marshal.OffsetOf<PlanetInstance>(nameof(PlanetInstance.Radius)));
            EnableInstancedAttrib(3, 1, stride, Marshal.OffsetOf<PlanetInstance>(nameof(PlanetInstance.Spin)));
            EnableInstancedAttrib(4, 1, stride, Marshal.OffsetOf<PlanetInstance>(nameof(PlanetInstance.AxisTilt)));
            EnableInstancedAttrib(5, 1, stride, Marshal.OffsetOf<PlanetInstance>(nameof(PlanetInstance.Depth)));
            EnableInstancedAttrib(6, 3, stride, Marshal.OffsetOf<PlanetInstance>(nameof(PlanetInstance.LightDir)));
            EnableInstancedAttrib(7, 1, stride, Marshal.OffsetOf<PlanetInstance>(nameof(PlanetInstance.Lod)));
            EnableInstancedAttrib(8, 1, stride, Marshal.OffsetOf<PlanetInstance>(nameof(PlanetInstance.RingInner)));
            EnableInstancedAttrib(9, 1, stride, Marshal.OffsetOf<PlanetInstance>(nameof(PlanetInstance.RingOuter)));
            EnableInstancedAttrib(10, 1, stride, Marshal.OffsetOf<PlanetInstance>(nameof(PlanetInstance.RingTilt)));

            _planetShader.Use();
            _planetShader.SetUniform("uViewProj", viewProj);
            _planetShader.SetUniform("uTime", time);
            _planetShader.SetUniform("uOccluderCount", _occluderCount);

            int start = 0;
            while (start < planets.Count)
            {
                int textureId = planets[start].TextureId;
                int count = 1;
                for (int i = start + 1; i < planets.Count; i++)
                {
                    if (planets[i].TextureId != textureId)
                        break;
                    count++;
                }

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, textureId);
                _planetShader.SetUniform("uSurface", 0);
                GL.DrawArraysInstancedBaseInstance(PrimitiveType.Triangles, 0, 6, count, start);
                start += count;
            }
        }

        // 3D planet drawing uses world positions with camera-facing billboards.
        private void DrawPlanets3D(Matrix4 viewProj, float time, List<PlanetInstance3D> planets)
        {
            if (planets.Count == 0)
                return;

            for (int i = 0; i < planets.Count; i++)
            {
                PlanetInstance3D planet = planets[i];
                if (planet.TextureId == 0)
                {
                    planet.TextureId = GetPlanetTexture(planet.Seed, planet.TextureType, planet.TextureSize);
                    planets[i] = planet;
                }
            }

            planets.Sort((a, b) => a.TextureId.CompareTo(b.TextureId));

            EnsureDynamicBuffer(ref _planetBuffer, ref _planetInstanceVbo, BufferTarget.ArrayBuffer, planets.Count * Marshal.SizeOf<PlanetInstance3D>());
            UploadDynamicBuffer(_planetBuffer, _planetInstanceVbo, planets);

            GL.BindVertexArray(_quadVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _planetInstanceVbo);

            int stride = Marshal.SizeOf<PlanetInstance3D>();
            EnableInstancedAttrib(1, 3, stride, Marshal.OffsetOf<PlanetInstance3D>(nameof(PlanetInstance3D.Position)));
            EnableInstancedAttrib(2, 1, stride, Marshal.OffsetOf<PlanetInstance3D>(nameof(PlanetInstance3D.Radius)));
            EnableInstancedAttrib(3, 1, stride, Marshal.OffsetOf<PlanetInstance3D>(nameof(PlanetInstance3D.Spin)));
            EnableInstancedAttrib(4, 1, stride, Marshal.OffsetOf<PlanetInstance3D>(nameof(PlanetInstance3D.AxisTilt)));
            EnableInstancedAttrib(5, 3, stride, Marshal.OffsetOf<PlanetInstance3D>(nameof(PlanetInstance3D.LightDir)));
            EnableInstancedAttrib(6, 1, stride, Marshal.OffsetOf<PlanetInstance3D>(nameof(PlanetInstance3D.Lod)));
            EnableInstancedAttrib(7, 1, stride, Marshal.OffsetOf<PlanetInstance3D>(nameof(PlanetInstance3D.RingInner)));
            EnableInstancedAttrib(8, 1, stride, Marshal.OffsetOf<PlanetInstance3D>(nameof(PlanetInstance3D.RingOuter)));
            EnableInstancedAttrib(9, 1, stride, Marshal.OffsetOf<PlanetInstance3D>(nameof(PlanetInstance3D.RingTilt)));

            _planetShader3D.Use();
            _planetShader3D.SetUniform("uViewProj", viewProj);
            _planetShader3D.SetUniform("uTime", time);
            _planetShader3D.SetUniform("uCamRight", _camRight);
            _planetShader3D.SetUniform("uCamUp", _camUp);

            int start = 0;
            while (start < planets.Count)
            {
                int textureId = planets[start].TextureId;
                int count = 1;
                for (int i = start + 1; i < planets.Count; i++)
                {
                    if (planets[i].TextureId != textureId)
                        break;
                    count++;
                }

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, textureId);
                _planetShader3D.SetUniform("uSurface", 0);
                GL.DrawArraysInstancedBaseInstance(PrimitiveType.Triangles, 0, 6, count, start);
                start += count;
            }
        }

        private void DrawRings(Matrix4 viewProj, float time, List<RingInstance> rings)
        {
            if (rings.Count == 0)
                return;

            for (int i = 0; i < rings.Count; i++)
            {
                RingInstance ring = rings[i];
                if (ring.TextureId == 0)
                {
                    ring.TextureId = GetRingTexture(ring.Seed, ring.TextureSize);
                    rings[i] = ring;
                }
            }

            rings.Sort((a, b) => a.TextureId.CompareTo(b.TextureId));

            EnsureDynamicBuffer(ref _ringBuffer, ref _ringInstanceVbo, BufferTarget.ArrayBuffer, rings.Count * Marshal.SizeOf<RingInstance>());
            UploadDynamicBuffer(_ringBuffer, _ringInstanceVbo, rings);

            GL.BindVertexArray(_quadVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _ringInstanceVbo);

            int stride = Marshal.SizeOf<RingInstance>();
            EnableInstancedAttrib(1, 2, stride, Marshal.OffsetOf<RingInstance>(nameof(RingInstance.Position)));
            EnableInstancedAttrib(2, 1, stride, Marshal.OffsetOf<RingInstance>(nameof(RingInstance.InnerRadius)));
            EnableInstancedAttrib(3, 1, stride, Marshal.OffsetOf<RingInstance>(nameof(RingInstance.OuterRadius)));
            EnableInstancedAttrib(4, 1, stride, Marshal.OffsetOf<RingInstance>(nameof(RingInstance.Tilt)));
            EnableInstancedAttrib(5, 1, stride, Marshal.OffsetOf<RingInstance>(nameof(RingInstance.Rotation)));
            EnableInstancedAttrib(6, 1, stride, Marshal.OffsetOf<RingInstance>(nameof(RingInstance.Depth)));
            EnableInstancedAttrib(7, 3, stride, Marshal.OffsetOf<RingInstance>(nameof(RingInstance.LightDir)));
            EnableInstancedAttrib(8, 1, stride, Marshal.OffsetOf<RingInstance>(nameof(RingInstance.PlanetRadius)));

            _ringShader.Use();
            _ringShader.SetUniform("uViewProj", viewProj);
            _ringShader.SetUniform("uTime", time);
            _ringShader.SetUniform("uOccluderCount", _occluderCount);

            int start = 0;
            while (start < rings.Count)
            {
                int textureId = rings[start].TextureId;
                int count = 1;
                for (int i = start + 1; i < rings.Count; i++)
                {
                    if (rings[i].TextureId != textureId)
                        break;
                    count++;
                }

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, textureId);
                _ringShader.SetUniform("uRingTex", 0);
                GL.DrawArraysInstancedBaseInstance(PrimitiveType.Triangles, 0, 6, count, start);
                start += count;
            }
        }

        // 3D ring drawing uses world positions and camera-facing billboards.
        private void DrawRings3D(Matrix4 viewProj, float time, List<RingInstance3D> rings)
        {
            if (rings.Count == 0)
                return;

            for (int i = 0; i < rings.Count; i++)
            {
                RingInstance3D ring = rings[i];
                if (ring.TextureId == 0)
                {
                    ring.TextureId = GetRingTexture(ring.Seed, ring.TextureSize);
                    rings[i] = ring;
                }
            }

            rings.Sort((a, b) => a.TextureId.CompareTo(b.TextureId));

            EnsureDynamicBuffer(ref _ringBuffer, ref _ringInstanceVbo, BufferTarget.ArrayBuffer, rings.Count * Marshal.SizeOf<RingInstance3D>());
            UploadDynamicBuffer(_ringBuffer, _ringInstanceVbo, rings);

            GL.BindVertexArray(_quadVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _ringInstanceVbo);

            int stride = Marshal.SizeOf<RingInstance3D>();
            EnableInstancedAttrib(1, 3, stride, Marshal.OffsetOf<RingInstance3D>(nameof(RingInstance3D.Position)));
            EnableInstancedAttrib(2, 1, stride, Marshal.OffsetOf<RingInstance3D>(nameof(RingInstance3D.InnerRadius)));
            EnableInstancedAttrib(3, 1, stride, Marshal.OffsetOf<RingInstance3D>(nameof(RingInstance3D.OuterRadius)));
            EnableInstancedAttrib(4, 1, stride, Marshal.OffsetOf<RingInstance3D>(nameof(RingInstance3D.Tilt)));
            EnableInstancedAttrib(5, 1, stride, Marshal.OffsetOf<RingInstance3D>(nameof(RingInstance3D.Rotation)));
            EnableInstancedAttrib(6, 3, stride, Marshal.OffsetOf<RingInstance3D>(nameof(RingInstance3D.LightDir)));
            EnableInstancedAttrib(7, 1, stride, Marshal.OffsetOf<RingInstance3D>(nameof(RingInstance3D.PlanetRadius)));

            _ringShader3D.Use();
            _ringShader3D.SetUniform("uViewProj", viewProj);
            _ringShader3D.SetUniform("uTime", time);
            _ringShader3D.SetUniform("uCamRight", _camRight);
            _ringShader3D.SetUniform("uCamUp", _camUp);

            int start = 0;
            while (start < rings.Count)
            {
                int textureId = rings[start].TextureId;
                int count = 1;
                for (int i = start + 1; i < rings.Count; i++)
                {
                    if (rings[i].TextureId != textureId)
                        break;
                    count++;
                }

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, textureId);
                _ringShader3D.SetUniform("uRingTex", 0);
                GL.DrawArraysInstancedBaseInstance(PrimitiveType.Triangles, 0, 6, count, start);
                start += count;
            }
        }

        private void DrawSun(Matrix4 viewProj, float time, ref SunInstance sun)
        {
            if (sun.TextureId == 0)
                sun.TextureId = GetSunTexture(sun.Seed, sun.TextureSize);

            GL.BindVertexArray(_quadVao);
            _sunShader.Use();
            _sunShader.SetUniform("uViewProj", viewProj);
            _sunShader.SetUniform("uTime", time);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, sun.TextureId);
            _sunShader.SetUniform("uSurface", 0);

            _sunShader.SetUniform("uCenter", sun.Position.X, sun.Position.Y);
            _sunShader.SetUniform("uRadius", sun.Radius);
            _sunShader.SetUniform("uDepth", sun.Depth);
            _sunShader.SetUniform("uSeed", sun.Seed);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }

        // 3D sun rendering uses a billboard quad centered at the world-space sun position.
        private void DrawSun3D(Matrix4 viewProj, float time, ref SunInstance3D sun)
        {
            if (sun.TextureId == 0)
                sun.TextureId = GetSunTexture(sun.Seed, sun.TextureSize);

            GL.BindVertexArray(_quadVao);
            _sunShader3D.Use();
            _sunShader3D.SetUniform("uViewProj", viewProj);
            _sunShader3D.SetUniform("uTime", time);
            _sunShader3D.SetUniform("uCamRight", _camRight);
            _sunShader3D.SetUniform("uCamUp", _camUp);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, sun.TextureId);
            _sunShader3D.SetUniform("uSurface", 0);

            _sunShader3D.SetUniform("uCenter", sun.Position);
            _sunShader3D.SetUniform("uRadius", sun.Radius);
            _sunShader3D.SetUniform("uSeed", sun.Seed);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }

        private void ApplyBloom()
        {
            int bloomW = Math.Max(1, _width / 4);
            int bloomH = Math.Max(1, _height / 4);

            GL.Disable(EnableCap.DepthTest);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFboA);
            GL.Viewport(0, 0, bloomW, bloomH);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            _bloomPrefilterShader.Use();
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _sceneColorTex);
            _bloomPrefilterShader.SetUniform("uScene", 0);
            _fullscreenQuad.Draw();

            for (int i = 0; i < 2; i++)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFboB);
                _bloomBlurShader.Use();
                _bloomBlurShader.SetUniform("uHorizontal", 1);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, _bloomTexA);
                _bloomBlurShader.SetUniform("uSource", 0);
                _fullscreenQuad.Draw();

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFboA);
                _bloomBlurShader.SetUniform("uHorizontal", 0);
                GL.BindTexture(TextureTarget.Texture2D, _bloomTexB);
                _fullscreenQuad.Draw();
            }
        }

        private void CompositeFinal(float time)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _finalFbo);
            GL.Viewport(0, 0, _width, _height);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            _compositeShader.Use();
            _compositeShader.SetUniform("uTime", time);
            _compositeShader.SetUniform("uOverlayKey", 1.0f, 0.0f, 1.0f);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _sceneColorTex);
            _compositeShader.SetUniform("uScene", 0);

            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, _bloomTexA);
            _compositeShader.SetUniform("uBloom", 1);

            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, _overlayTexture);
            _compositeShader.SetUniform("uOverlay", 2);

            _fullscreenQuad.Draw();

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private void EnableInstancedAttrib(int index, int size, int stride, IntPtr offset)
        {
            GL.EnableVertexAttribArray(index);
            GL.VertexAttribPointer(index, size, VertexAttribPointerType.Float, false, stride, offset);
            GL.VertexAttribDivisor(index, 1);
        }

        private void EnsureDynamicBuffer(ref DynamicBuffer buffer, ref int handle, BufferTarget target, int size)
        {
            if (handle == 0)
                handle = GL.GenBuffer();

            if (!buffer.IsInitialized)
                buffer.Initialize(handle, target, size, _supportsPersistentMapping);
            else if (size > buffer.Capacity)
                buffer.Resize(size, _supportsPersistentMapping);
        }

        private void UploadDynamicBuffer<T>(DynamicBuffer buffer, int handle, List<T> data) where T : struct
        {
            if (data.Count == 0)
                return;

            int size = data.Count * Marshal.SizeOf<T>();
            T[] arr = data.ToArray();
            GL.BindBuffer(buffer.Target, handle);
            if (buffer.MappedPtr != IntPtr.Zero)
            {

            }
            else
            {
                GL.BufferSubData(buffer.Target, IntPtr.Zero, size, arr);
            }
        }

        private bool CheckBufferStorageSupport()
        {
            string? ext = GL.GetString(StringName.Extensions);
            return ext != null && ext.Contains("GL_ARB_buffer_storage", StringComparison.OrdinalIgnoreCase);
        }

        private static List<LineVertex3D> EnsureLineVertices3D(List<LineVertex3D> source3D, List<LineVertex> source2D, List<LineVertex3D> compatTarget)
        {
            if (source3D.Count > 0)
                return source3D;

            compatTarget.Clear();
            if (source2D.Count == 0)
                return compatTarget;

            // TODO: Apply orbit plane tilt here once upstream provides the tilt values per orbit.
            for (int i = 0; i < source2D.Count; i++)
            {
                LineVertex v = source2D[i];
                compatTarget.Add(new LineVertex3D(new Vector3(v.Position.X, v.Position.Y, 0f), v.Color));
            }

            return compatTarget;
        }

        private static List<TrailVertex3D> EnsureTrailVertices3D(List<TrailVertex3D> source3D, List<TrailVertex> source2D, List<TrailVertex3D> compatTarget)
        {
            if (source3D.Count > 0)
                return source3D;

            compatTarget.Clear();
            if (source2D.Count == 0)
                return compatTarget;

            for (int i = 0; i < source2D.Count; i++)
            {
                TrailVertex v = source2D[i];
                compatTarget.Add(new TrailVertex3D(new Vector3(v.Position.X, v.Position.Y, 0f), v.Age01, v.Color));
            }

            return compatTarget;
        }

        private enum SpriteCategory
        {
            Star,
            Debris,
            Asteroid,
            Ship
        }

        internal enum RenderMode
        {
            Ortho2D,
            Perspective3D
        }

        private struct DynamicBuffer
        {
            public int Handle;
            public int Capacity;
            public BufferTarget Target;
            public IntPtr MappedPtr;
            public bool IsInitialized;

            public void Initialize(int handle, BufferTarget target, int size, bool persistent)
            {
                Handle = handle;
                Target = target;
                Resize(size, persistent);
                IsInitialized = true;
            }

            public void Resize(int size, bool persistent)
            {
                Capacity = Math.Max(256, size);
                GL.BindBuffer(Target, Handle);
                if (persistent)
                {
                    GL.BufferStorage(Target, Capacity, IntPtr.Zero,
                        BufferStorageFlags.MapWriteBit | BufferStorageFlags.MapPersistentBit | BufferStorageFlags.MapCoherentBit);
                    MappedPtr = GL.MapBufferRange(Target, IntPtr.Zero, Capacity,
                        BufferAccessMask.MapWriteBit | BufferAccessMask.MapPersistentBit | BufferAccessMask.MapCoherentBit);
                }
                else
                {
                    GL.BufferData(Target, Capacity, IntPtr.Zero, BufferUsageHint.StreamDraw);
                    MappedPtr = IntPtr.Zero;
                }
            }
        }

        private static void DeleteBuffer(ref int buffer)
        {
            if (buffer != 0)
                GL.DeleteBuffer(buffer);
            buffer = 0;
        }

        private static void DeleteVertexArray(ref int vao)
        {
            if (vao != 0)
                GL.DeleteVertexArray(vao);
            vao = 0;
        }

        private static void DeleteFramebuffer(ref int fbo)
        {
            if (fbo != 0)
                GL.DeleteFramebuffer(fbo);
            fbo = 0;
        }

        private static void DeleteTexture(ref int tex)
        {
            if (tex != 0)
                GL.DeleteTexture(tex);
            tex = 0;
        }

        private const string SpriteVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 iPos;
layout(location = 2) in float iSize;
layout(location = 3) in vec4 iColor;
layout(location = 4) in float iDepth;
layout(location = 5) in float iSeed;
layout(location = 6) in float iRotation;
layout(location = 7) in float iType;

uniform mat4 uViewProj;

out vec2 vLocal;
out vec4 vColor;
out float vSeed;
out float vType;

void main()
{
    vec2 local = aPos * 2.0 - 1.0;
    float c = cos(iRotation);
    float s = sin(iRotation);
    vec2 rotated = vec2(local.x * c - local.y * s, local.x * s + local.y * c);
    vec2 worldPos = iPos + rotated * iSize;
    gl_Position = uViewProj * vec4(worldPos, iDepth, 1.0);
    vLocal = local;
    vColor = iColor;
    vSeed = iSeed;
    vType = iType;
}";

        private const string SpriteFragmentShader = @"
#version 330 core
in vec2 vLocal;
in vec4 vColor;
in float vSeed;
in float vType;

uniform float uTime;

out vec4 FragColor;

void main()
{
    float dist = length(vLocal);
    if (dist > 1.0)
        discard;

    float alpha = 1.0 - smoothstep(0.6, 1.0, dist);
    float twinkle = 0.75 + 0.25 * sin(uTime * 2.5 + vSeed * 12.3);
    if (vType > 2.5)
    {
        float shipShape = smoothstep(0.1, 0.0, abs(vLocal.x)) * step(0.0, vLocal.y);
        alpha = max(alpha, shipShape);
        twinkle = 1.0;
    }

    vec3 color = vColor.rgb;
    FragColor = vec4(color, alpha * twinkle * vColor.a);
}";

        // 3D sprite vertex shader adds camera-facing billboard basis vectors.
        private const string SpriteVertexShader3D = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec3 iPos;
layout(location = 2) in float iSize;
layout(location = 3) in vec4 iColor;
layout(location = 4) in float iSeed;
layout(location = 5) in float iRotation;
layout(location = 6) in float iType;

uniform mat4 uViewProj;
uniform vec3 uCamRight;
uniform vec3 uCamUp;

out vec2 vLocal;
out vec4 vColor;
out float vSeed;
out float vType;

void main()
{
    vec2 local = aPos * 2.0 - 1.0;
    float c = cos(iRotation);
    float s = sin(iRotation);
    vec2 rotated = vec2(local.x * c - local.y * s, local.x * s + local.y * c);
    vec3 worldPos = iPos + (uCamRight * rotated.x + uCamUp * rotated.y) * iSize;
    gl_Position = uViewProj * vec4(worldPos, 1.0);
    vLocal = rotated;
    vColor = iColor;
    vSeed = iSeed;
    vType = iType;
}";

        private const string SpriteFragmentShader3D = SpriteFragmentShader;

        private const string PlanetVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 iPos;
layout(location = 2) in float iRadius;
layout(location = 3) in float iSpin;
layout(location = 4) in float iAxisTilt;
layout(location = 5) in float iDepth;
layout(location = 6) in vec3 iLightDir;
layout(location = 7) in float iLod;
layout(location = 8) in float iRingInner;
layout(location = 9) in float iRingOuter;
layout(location = 10) in float iRingTilt;

uniform mat4 uViewProj;

out vec2 vWorldPos;
flat out vec2 vCenter;
flat out float vRadius;
flat out float vSpin;
flat out float vAxisTilt;
flat out float vDepth;
flat out vec3 vLightDir;
flat out float vLod;
flat out float vRingInner;
flat out float vRingOuter;
flat out float vRingTilt;

void main()
{
    vec2 local = aPos * 2.0 - 1.0;
    vec2 worldPos = iPos + local * iRadius;
    gl_Position = uViewProj * vec4(worldPos, iDepth, 1.0);
    vWorldPos = worldPos;
    vCenter = iPos;
    vRadius = iRadius;
    vSpin = iSpin;
    vAxisTilt = iAxisTilt;
    vDepth = iDepth;
    vLightDir = iLightDir;
    vLod = iLod;
    vRingInner = iRingInner;
    vRingOuter = iRingOuter;
    vRingTilt = iRingTilt;
}";

        private const string PlanetFragmentShader = @"
#version 330 core
in vec2 vWorldPos;
flat in vec2 vCenter;
flat in float vRadius;
flat in float vSpin;
flat in float vAxisTilt;
flat in float vDepth;
flat in vec3 vLightDir;
flat in float vLod;
flat in float vRingInner;
flat in float vRingOuter;
flat in float vRingTilt;

uniform sampler2D uSurface;
uniform float uTime;
uniform int uOccluderCount;

layout(std430, binding = 0) buffer Occluders
{
    vec4 occ[];
};

out vec4 FragColor;

void main()
{
    vec2 p = (vWorldPos - vCenter) / vRadius;
    float r2 = dot(p, p);
    if (r2 > 1.0)
        discard;

    float nz = sqrt(max(0.0, 1.0 - r2));
    vec3 n = normalize(vec3(p.x, p.y, nz));

    float ct = cos(vAxisTilt);
    float st = sin(vAxisTilt);
    float tx = n.x * ct - n.y * st;
    float ty = n.x * st + n.y * ct;
    float tz = n.z;

    float lon = atan(tx, tz);
    float u = lon / (6.2831853) + 0.5 + vSpin;
    float v = asin(clamp(ty, -1.0, 1.0)) / 3.1415926 + 0.5;
    vec2 uv = vec2(fract(u), v);

    vec3 baseColor = texture(uSurface, uv).rgb;

    float ndotlRaw = dot(n, normalize(vLightDir));
    float ndotl = max(ndotlRaw, 0.0);
    float limb = 0.78 + 0.22 * n.z;
    float light = clamp(ndotl * limb, 0.0, 1.0);
    vec3 litColor = baseColor * light;

    if (vRingOuter > 0.0)
    {
        float ctR = cos(vRingTilt);
        float y = p.y / max(0.01, ctR);
        float ringDist = length(vec2(p.x, y));
        float ringShadow = smoothstep(vRingInner / vRadius, vRingOuter / vRadius, ringDist);
        litColor *= mix(1.0, 0.82, ringShadow * 0.6);
    }

    vec2 fragWorld = vWorldPos;
    for (int i = 0; i < uOccluderCount; i++)
    {
        vec4 o = occ[i];
        float occDepth = o.w;
        if (occDepth < vDepth)
        {
            float dist = length(fragWorld - o.xy);
            if (dist < o.z)
                discard;
        }
    }

    if (vLod < 0.5)
        FragColor = vec4(baseColor, 1.0);
    else
        FragColor = vec4(litColor, 1.0);
}";

        // 3D planet vertex shader uses camera-facing billboards in world space.
        private const string PlanetVertexShader3D = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec3 iPos;
layout(location = 2) in float iRadius;
layout(location = 3) in float iSpin;
layout(location = 4) in float iAxisTilt;
layout(location = 5) in vec3 iLightDir;
layout(location = 6) in float iLod;
layout(location = 7) in float iRingInner;
layout(location = 8) in float iRingOuter;
layout(location = 9) in float iRingTilt;

uniform mat4 uViewProj;
uniform vec3 uCamRight;
uniform vec3 uCamUp;

out vec2 vLocal;
flat out float vSpin;
flat out float vAxisTilt;
flat out vec3 vLightDir;
flat out float vLod;
flat out float vRingInner;
flat out float vRingOuter;
flat out float vRingTilt;

void main()
{
    vec2 local = aPos * 2.0 - 1.0;
    vec3 worldPos = iPos + (uCamRight * local.x + uCamUp * local.y) * iRadius;
    gl_Position = uViewProj * vec4(worldPos, 1.0);
    vLocal = local;
    vSpin = iSpin;
    vAxisTilt = iAxisTilt;
    vLightDir = iLightDir;
    vLod = iLod;
    vRingInner = iRingInner;
    vRingOuter = iRingOuter;
    vRingTilt = iRingTilt;
}";

        private const string PlanetFragmentShader3D = @"
#version 330 core
in vec2 vLocal;
flat in float vSpin;
flat in float vAxisTilt;
flat in vec3 vLightDir;
flat in float vLod;
flat in float vRingInner;
flat in float vRingOuter;
flat in float vRingTilt;

uniform sampler2D uSurface;
uniform float uTime;

out vec4 FragColor;

void main()
{
    vec2 p = vLocal;
    float r2 = dot(p, p);
    if (r2 > 1.0)
        discard;

    float nz = sqrt(max(0.0, 1.0 - r2));
    vec3 n = normalize(vec3(p.x, p.y, nz));

    float ct = cos(vAxisTilt);
    float st = sin(vAxisTilt);
    float tx = n.x * ct - n.y * st;
    float ty = n.x * st + n.y * ct;
    float tz = n.z;

    float lon = atan(tx, tz);
    float u = lon / (6.2831853) + 0.5 + vSpin;
    float v = asin(clamp(ty, -1.0, 1.0)) / 3.1415926 + 0.5;
    vec2 uv = vec2(fract(u), v);

    vec3 baseColor = texture(uSurface, uv).rgb;

    float ndotlRaw = dot(n, normalize(vLightDir));
    float ndotl = max(ndotlRaw, 0.0);
    float limb = 0.78 + 0.22 * n.z;
    float light = clamp(ndotl * limb, 0.0, 1.0);
    vec3 litColor = baseColor * light;

    if (vRingOuter > 0.0)
    {
        float ctR = cos(vRingTilt);
        float y = p.y / max(0.01, ctR);
        float ringDist = length(vec2(p.x, y));
        float ringShadow = smoothstep(vRingInner, vRingOuter, ringDist);
        litColor *= mix(1.0, 0.82, ringShadow * 0.6);
    }

    if (vLod < 0.5)
        FragColor = vec4(baseColor, 1.0);
    else
        FragColor = vec4(litColor, 1.0);
}";

        private const string RingVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 iPos;
layout(location = 2) in float iInner;
layout(location = 3) in float iOuter;
layout(location = 4) in float iTilt;
layout(location = 5) in float iRotation;
layout(location = 6) in float iDepth;
layout(location = 7) in vec3 iLightDir;
layout(location = 8) in float iPlanetRadius;

uniform mat4 uViewProj;

out vec2 vWorldPos;
flat out vec2 vCenter;
flat out float vInner;
flat out float vOuter;
flat out float vTilt;
flat out float vRotation;
flat out float vDepth;
flat out vec3 vLightDir;
flat out float vPlanetRadius;

void main()
{
    vec2 local = aPos * 2.0 - 1.0;
    vec2 worldPos = iPos + local * iOuter;
    gl_Position = uViewProj * vec4(worldPos, iDepth, 1.0);
    vWorldPos = worldPos;
    vCenter = iPos;
    vInner = iInner;
    vOuter = iOuter;
    vTilt = iTilt;
    vRotation = iRotation;
    vDepth = iDepth;
    vLightDir = iLightDir;
    vPlanetRadius = iPlanetRadius;
}";

        private const string RingFragmentShader = @"
#version 330 core
in vec2 vWorldPos;
flat in vec2 vCenter;
flat in float vInner;
flat in float vOuter;
flat in float vTilt;
flat in float vRotation;
flat in float vDepth;
flat in vec3 vLightDir;
flat in float vPlanetRadius;

uniform sampler2D uRingTex;
uniform float uTime;
uniform int uOccluderCount;

layout(std430, binding = 0) buffer Occluders
{
    vec4 occ[];
};

out vec4 FragColor;

void main()
{
    vec2 local = vWorldPos - vCenter;
    float ct = cos(vTilt);
    float st = sin(vTilt);
    vec2 rotated = vec2(local.x * ct - local.y * st, local.x * st + local.y * ct);
    float dist = length(vec2(rotated.x, rotated.y / max(0.25, ct)));
    if (dist < vInner || dist > vOuter)
        discard;

    float angle = atan(rotated.y, rotated.x) / 6.2831853 + 0.5 + vRotation * 0.05;
    float uvx = fract(angle * 8.0 + uTime * 0.01);
    float uvy = (dist - vInner) / max(0.0001, vOuter - vInner);
    float density = texture(uRingTex, vec2(uvx, uvy)).r;
    vec3 base = mix(vec3(0.22, 0.22, 0.26), vec3(0.55, 0.52, 0.45), density);

    vec3 normal = normalize(vec3(0.0, st, ct));
    float light = max(dot(normalize(vLightDir), normal), 0.2);
    base *= light;

    if (dist < vPlanetRadius)
        base *= 0.3;

    for (int i = 0; i < uOccluderCount; i++)
    {
        vec4 o = occ[i];
        float occDepth = o.w;
        if (occDepth < vDepth)
        {
            float distOcc = length(vWorldPos - o.xy);
            if (distOcc < o.z)
                discard;
        }
    }

    FragColor = vec4(base, 0.85);
}";

        // 3D ring vertex shader uses camera-facing billboards in world space.
        private const string RingVertexShader3D = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec3 iPos;
layout(location = 2) in float iInner;
layout(location = 3) in float iOuter;
layout(location = 4) in float iTilt;
layout(location = 5) in float iRotation;
layout(location = 6) in vec3 iLightDir;
layout(location = 7) in float iPlanetRadius;

uniform mat4 uViewProj;
uniform vec3 uCamRight;
uniform vec3 uCamUp;

out vec2 vLocal;
flat out float vInner;
flat out float vOuter;
flat out float vTilt;
flat out float vRotation;
flat out vec3 vLightDir;
flat out float vPlanetRadius;

void main()
{
    vec2 local = aPos * 2.0 - 1.0;
    vec3 worldPos = iPos + (uCamRight * local.x + uCamUp * local.y) * iOuter;
    gl_Position = uViewProj * vec4(worldPos, 1.0);
    vLocal = local;
    vInner = iInner;
    vOuter = iOuter;
    vTilt = iTilt;
    vRotation = iRotation;
    vLightDir = iLightDir;
    vPlanetRadius = iPlanetRadius;
}";

        private const string RingFragmentShader3D = @"
#version 330 core
in vec2 vLocal;
flat in float vInner;
flat in float vOuter;
flat in float vTilt;
flat in float vRotation;
flat in vec3 vLightDir;
flat in float vPlanetRadius;

uniform sampler2D uRingTex;
uniform float uTime;

out vec4 FragColor;

void main()
{
    vec2 local = vLocal * vOuter;
    float ct = cos(vTilt);
    float st = sin(vTilt);
    vec2 rotated = vec2(local.x * ct - local.y * st, local.x * st + local.y * ct);
    float dist = length(vec2(rotated.x, rotated.y / max(0.25, ct)));
    if (dist < vInner || dist > vOuter)
        discard;

    float angle = atan(rotated.y, rotated.x) / 6.2831853 + 0.5 + vRotation * 0.05;
    float uvx = fract(angle * 8.0 + uTime * 0.01);
    float uvy = (dist - vInner) / max(0.0001, vOuter - vInner);
    float density = texture(uRingTex, vec2(uvx, uvy)).r;
    vec3 base = mix(vec3(0.22, 0.22, 0.26), vec3(0.55, 0.52, 0.45), density);

    vec3 normal = normalize(vec3(0.0, st, ct));
    float light = max(dot(normalize(vLightDir), normal), 0.2);
    base *= light;

    if (dist < vPlanetRadius)
        base *= 0.3;

    FragColor = vec4(base, 0.85);
}";

        private const string SunVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;

uniform mat4 uViewProj;
uniform vec2 uCenter;
uniform float uRadius;
uniform float uDepth;

out vec2 vWorldPos;

void main()
{
    vec2 local = aPos * 2.0 - 1.0;
    vec2 worldPos = uCenter + local * uRadius;
    gl_Position = uViewProj * vec4(worldPos, uDepth, 1.0);
    vWorldPos = worldPos;
}";

        private const string SunFragmentShader = @"
#version 330 core
in vec2 vWorldPos;

uniform sampler2D uSurface;
uniform vec2 uCenter;
uniform float uRadius;
uniform float uTime;
uniform float uSeed;

out vec4 FragColor;

void main()
{
    vec2 p = (vWorldPos - uCenter) / uRadius;
    float r2 = dot(p, p);
    if (r2 > 1.0)
        discard;

    float nz = sqrt(max(0.0, 1.0 - r2));
    vec3 n = normalize(vec3(p.x, p.y, nz));
    float lon = atan(n.x, n.z);
    float u = lon / (6.2831853) + 0.5 + uTime * 0.01;
    float v = asin(clamp(n.y, -1.0, 1.0)) / 3.1415926 + 0.5;
    vec3 base = texture(uSurface, vec2(fract(u), v)).rgb;
    float flare = smoothstep(0.9, 1.0, nz) * (0.7 + 0.3 * sin(uTime * 1.2 + uSeed));
    vec3 color = base + flare * vec3(1.0, 0.6, 0.2);
    FragColor = vec4(color, 1.0);
}";

        // 3D sun shader uses camera-facing billboards for world-space placement.
        private const string SunVertexShader3D = @"
#version 330 core
layout(location = 0) in vec2 aPos;

uniform mat4 uViewProj;
uniform vec3 uCenter;
uniform float uRadius;
uniform vec3 uCamRight;
uniform vec3 uCamUp;

out vec2 vLocal;

void main()
{
    vec2 local = aPos * 2.0 - 1.0;
    vec3 worldPos = uCenter + (uCamRight * local.x + uCamUp * local.y) * uRadius;
    gl_Position = uViewProj * vec4(worldPos, 1.0);
    vLocal = local;
}";

        private const string SunFragmentShader3D = @"
#version 330 core
in vec2 vLocal;

uniform sampler2D uSurface;
uniform float uRadius;
uniform float uTime;
uniform float uSeed;

out vec4 FragColor;

void main()
{
    vec2 p = vLocal;
    float r2 = dot(p, p);
    if (r2 > 1.0)
        discard;

    float nz = sqrt(max(0.0, 1.0 - r2));
    vec3 n = normalize(vec3(p.x, p.y, nz));
    float lon = atan(n.x, n.z);
    float u = lon / (6.2831853) + 0.5 + uTime * 0.01;
    float v = asin(clamp(n.y, -1.0, 1.0)) / 3.1415926 + 0.5;
    vec3 base = texture(uSurface, vec2(fract(u), v)).rgb;
    float flare = smoothstep(0.9, 1.0, nz) * (0.7 + 0.3 * sin(uTime * 1.2 + uSeed));
    vec3 color = base + flare * vec3(1.0, 0.6, 0.2);
    FragColor = vec4(color, 1.0);
}";

        private const string LineVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec4 aColor;
layout(location = 2) in float aDepth;

uniform mat4 uViewProj;

out vec4 vColor;

void main()
{
    gl_Position = uViewProj * vec4(aPos, aDepth, 1.0);
    vColor = aColor;
}";

        private const string LineFragmentShader = @"
#version 330 core
in vec4 vColor;
out vec4 FragColor;
void main()
{
    FragColor = vColor;
}";

        // 3D line shader uses full vec3 positions in world space.
        private const string LineVertexShader3D = @"
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec4 aColor;

uniform mat4 uViewProj;

out vec4 vColor;

void main()
{
    gl_Position = uViewProj * vec4(aPos, 1.0);
    vColor = aColor;
}";

        private const string LineFragmentShader3D = LineFragmentShader;

        private const string TrailVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in float aAge;
layout(location = 2) in vec4 aColor;
layout(location = 3) in float aDepth;

uniform mat4 uViewProj;

out float vAge;
out vec4 vColor;

void main()
{
    gl_Position = uViewProj * vec4(aPos, aDepth, 1.0);
    vAge = aAge;
    vColor = aColor;
}";

        private const string TrailFragmentShader = @"
#version 330 core
in float vAge;
in vec4 vColor;
out vec4 FragColor;
void main()
{
    float alpha = (1.0 - vAge) * vColor.a;
    FragColor = vec4(vColor.rgb, alpha);
}";

        // 3D trail shader uses vec3 positions for world-space trails.
        private const string TrailVertexShader3D = @"
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in float aAge;
layout(location = 2) in vec4 aColor;

uniform mat4 uViewProj;

out float vAge;
out vec4 vColor;

void main()
{
    gl_Position = uViewProj * vec4(aPos, 1.0);
    vAge = aAge;
    vColor = aColor;
}";

        private const string TrailFragmentShader3D = TrailFragmentShader;

        private const string BlitVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vUv;
void main()
{
    vUv = vec2(aPos.x, 1.0 - aPos.y);
    vec2 ndc = aPos * 2.0 - 1.0;
    gl_Position = vec4(ndc, 0.0, 1.0);
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

        private const string BloomPrefilterVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vUv;
void main()
{
    vUv = aPos;
    vec2 ndc = aPos * 2.0 - 1.0;
    gl_Position = vec4(ndc, 0.0, 1.0);
}";

        private const string BloomPrefilterFragmentShader = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;
uniform sampler2D uScene;
void main()
{
    vec3 color = texture(uScene, vUv).rgb;
    float brightness = max(max(color.r, color.g), color.b);
    float mask = smoothstep(0.6, 1.1, brightness);
    FragColor = vec4(color * mask, 1.0);
}";

        private const string BloomBlurVertexShader = BloomPrefilterVertexShader;

        private const string BloomBlurFragmentShader = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;
uniform sampler2D uSource;
uniform int uHorizontal;
void main()
{
    vec2 texel = 1.0 / vec2(textureSize(uSource, 0));
    vec3 result = texture(uSource, vUv).rgb * 0.36;
    for (int i = 1; i < 4; i++)
    {
        vec2 offset = (uHorizontal == 1) ? vec2(texel.x * i, 0.0) : vec2(0.0, texel.y * i);
        result += texture(uSource, vUv + offset).rgb * 0.16;
        result += texture(uSource, vUv - offset).rgb * 0.16;
    }
    FragColor = vec4(result, 1.0);
}";

        private const string CompositeVertexShader = BloomPrefilterVertexShader;

        private const string CompositeFragmentShader = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;

uniform sampler2D uScene;
uniform sampler2D uBloom;
uniform sampler2D uOverlay;
uniform vec3 uOverlayKey;
uniform float uTime;

void main()
{
    vec3 scene = texture(uScene, vUv).rgb;
    vec3 bloom = texture(uBloom, vUv).rgb;
    vec4 overlay = texture(uOverlay, vUv);
    float key = step(0.01, distance(overlay.rgb, uOverlayKey));
    vec3 color = scene + bloom * 0.8;
    color = mix(color, overlay.rgb, key * overlay.a);

    float vignette = smoothstep(1.2, 0.6, distance(vUv, vec2(0.5)));
    color *= mix(0.9, 1.0, vignette);

    FragColor = vec4(color, 1.0);
}";
    }
}
