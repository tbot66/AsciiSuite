using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using SolarSystemApp.Rendering.Gpu;
using SolarSystemApp.Util;
using SolarSystemApp.World;

namespace SolarSystemApp.Rendering.Modern
{
    internal sealed class ModernSystemRenderer : IDisposable
    {
        private const int MaxOccluders = 128;
        private const int PlanetTextureSize = 256;
        private const int RingTextureSize = 256;
        private const int SunTextureSize = 256;

        private readonly CameraOrtho _camera = new CameraOrtho();
        private readonly SceneFbo _sceneFbo = new SceneFbo();
        private readonly SceneFbo _brightFbo = new SceneFbo();
        private readonly SceneFbo _blurPing = new SceneFbo();
        private readonly SceneFbo _blurPong = new SceneFbo();
        private readonly SceneFbo _finalFbo = new SceneFbo();

        private readonly FullscreenQuad _quad = new FullscreenQuad();
        private readonly TextureCache _textureCache = new TextureCache();

        private readonly ShaderProgram _spriteShader;
        private readonly ShaderProgram _planetShader;
        private readonly ShaderProgram _ringShader;
        private readonly ShaderProgram _sunShader;
        private readonly ShaderProgram _orbitShader;
        private readonly ShaderProgram _trailShader;
        private readonly ShaderProgram _brightShader;
        private readonly ShaderProgram _blurShader;
        private readonly ShaderProgram _compositeShader;

        private readonly int _unitVao;
        private readonly int _unitVbo;
        private readonly InstanceBuffer<SpriteInstance> _spriteBuffer;
        private readonly InstanceBuffer<OrbitInstance> _orbitBuffer;
        private readonly InstanceBuffer<TrailInstance> _trailBuffer;

        private readonly int _occluderBuffer;

        private FrameData _frameFront = new FrameData();
        private FrameData _frameBack = new FrameData();
        private readonly object _frameLock = new object();
        private Thread? _worker;
        private bool _workerRunning;
        private volatile bool _hasNewFrame;
        private readonly object _prepLock = new object();
        private FramePrepRequest _prepRequest = new FramePrepRequest();

        private int _outputTexture;
        private int _outputWidth;
        private int _outputHeight;

        public int OutputTextureId => _outputTexture;

        public ModernSystemRenderer()
        {
            _spriteShader = new ShaderProgram(SpriteVertexShader, SpriteFragmentShader);
            _planetShader = new ShaderProgram(PlanetVertexShader, PlanetFragmentShader);
            _ringShader = new ShaderProgram(RingVertexShader, RingFragmentShader);
            _sunShader = new ShaderProgram(SunVertexShader, SunFragmentShader);
            _orbitShader = new ShaderProgram(OrbitVertexShader, OrbitFragmentShader);
            _trailShader = new ShaderProgram(TrailVertexShader, TrailFragmentShader);
            _brightShader = new ShaderProgram(FullscreenVertexShader, BrightFragmentShader);
            _blurShader = new ShaderProgram(FullscreenVertexShader, BlurFragmentShader);
            _compositeShader = new ShaderProgram(FullscreenVertexShader, CompositeFragmentShader);

            float[] unitVerts =
            {
                0f, 0f,
                1f, 0f,
                1f, 1f,
                0f, 0f,
                1f, 1f,
                0f, 1f
            };

            _unitVao = GL.GenVertexArray();
            _unitVbo = GL.GenBuffer();

            GL.BindVertexArray(_unitVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _unitVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, unitVerts.Length * sizeof(float), unitVerts, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);

            bool persistent = SupportsPersistentMapping();
            _spriteBuffer = new InstanceBuffer<SpriteInstance>(4096, persistent);
            _orbitBuffer = new InstanceBuffer<OrbitInstance>(4096, persistent);
            _trailBuffer = new InstanceBuffer<TrailInstance>(16384, persistent);

            int strideSprite = SpriteInstance.Stride;
            GL.BindBuffer(BufferTarget.ArrayBuffer, _spriteBuffer.BufferId);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, strideSprite, 0);
            GL.VertexAttribDivisor(1, 1);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, strideSprite, 2 * sizeof(float));
            GL.VertexAttribDivisor(2, 1);
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, strideSprite, 3 * sizeof(float));
            GL.VertexAttribDivisor(3, 1);
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, strideSprite, 7 * sizeof(float));
            GL.VertexAttribDivisor(4, 1);

            int strideOrbit = OrbitInstance.Stride;
            GL.BindBuffer(BufferTarget.ArrayBuffer, _orbitBuffer.BufferId);
            GL.EnableVertexAttribArray(5);
            GL.VertexAttribPointer(5, 2, VertexAttribPointerType.Float, false, strideOrbit, 0);
            GL.VertexAttribDivisor(5, 1);
            GL.EnableVertexAttribArray(6);
            GL.VertexAttribPointer(6, 3, VertexAttribPointerType.Float, false, strideOrbit, 2 * sizeof(float));
            GL.VertexAttribDivisor(6, 1);

            int strideTrail = TrailInstance.Stride;
            GL.BindBuffer(BufferTarget.ArrayBuffer, _trailBuffer.BufferId);
            GL.EnableVertexAttribArray(7);
            GL.VertexAttribPointer(7, 2, VertexAttribPointerType.Float, false, strideTrail, 0);
            GL.VertexAttribDivisor(7, 1);
            GL.EnableVertexAttribArray(8);
            GL.VertexAttribPointer(8, 2, VertexAttribPointerType.Float, false, strideTrail, 2 * sizeof(float));
            GL.VertexAttribDivisor(8, 1);

            GL.BindVertexArray(0);

            _occluderBuffer = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _occluderBuffer);
            GL.BufferData(BufferTarget.ShaderStorageBuffer, MaxOccluders * 4 * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);
        }

        public void Dispose()
        {
            StopWorker();
            _spriteShader.Dispose();
            _planetShader.Dispose();
            _ringShader.Dispose();
            _sunShader.Dispose();
            _orbitShader.Dispose();
            _trailShader.Dispose();
            _brightShader.Dispose();
            _blurShader.Dispose();
            _compositeShader.Dispose();
            _spriteBuffer.Dispose();
            _orbitBuffer.Dispose();
            _trailBuffer.Dispose();
            _textureCache.Dispose();
            _sceneFbo.Dispose();
            _brightFbo.Dispose();
            _blurPing.Dispose();
            _blurPong.Dispose();
            _finalFbo.Dispose();

            GL.DeleteBuffer(_unitVbo);
            GL.DeleteVertexArray(_unitVao);
            GL.DeleteBuffer(_occluderBuffer);
        }

        public void BeginFrame(int width, int height, Vector2 cameraPosition, float zoom, float orbitScale)
        {
            _camera.ViewportWidth = width;
            _camera.ViewportHeight = height;
            _camera.Position = cameraPosition;
            _camera.Zoom = zoom;
            _camera.OrbitYScale = orbitScale;
            _camera.UpdateMatrices();

            _sceneFbo.Ensure(width, height);
            _brightFbo.Ensure(width / 2, height / 2);
            _blurPing.Ensure(width / 2, height / 2);
            _blurPong.Ensure(width / 2, height / 2);
            _finalFbo.Ensure(width, height);

            _outputTexture = _finalFbo.ColorTexture;
            _outputWidth = width;
            _outputHeight = height;
        }

        public void QueueFramePrep(StarSystem system, double simTime, bool showOrbits, bool showBelts, bool showRings, bool showDebris, bool showStarfield, bool showNebula)
        {
            lock (_prepLock)
            {
                _prepRequest.System = system;
                _prepRequest.SimTime = simTime;
                _prepRequest.ShowOrbits = showOrbits;
                _prepRequest.ShowBelts = showBelts;
                _prepRequest.ShowRings = showRings;
                _prepRequest.ShowDebris = showDebris;
                _prepRequest.ShowStarfield = showStarfield;
                _prepRequest.ShowNebula = showNebula;
            }

            if (_workerRunning)
                return;

            _workerRunning = true;
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true
            };
            _worker.Start();
        }

        private void WorkerLoop()
        {
            while (_workerRunning)
            {
                FramePrepRequest request;
                lock (_prepLock)
                {
                    request = _prepRequest;
                }

                FrameData target = _frameBack;
                target.Clear();

                Vector4 bounds = _camera.WorldBounds;
                float minX = bounds.X - 2f;
                float minY = bounds.Y - 2f;
                float maxX = bounds.Z + 2f;
                float maxY = bounds.W + 2f;

                if (request.ShowStarfield)
                    BuildStars(request.System, target, minX, minY, maxX, maxY, request.SimTime);
                if (request.ShowDebris)
                    BuildDebris(request.System, target, minX, minY, maxX, maxY, request.SimTime);
                if (request.ShowNebula)
                    BuildNebula(request.System, target, minX, minY, maxX, maxY, request.SimTime);

                BuildPlanets(request.System, target, request.SimTime, request.ShowRings);
                BuildShips(request.System, target, minX, minY, maxX, maxY, request.SimTime);
                BuildStations(request.System, target, minX, minY, maxX, maxY);
                BuildAsteroids(request.System, target, minX, minY, maxX, maxY);
                if (request.ShowOrbits)
                    BuildOrbits(request.System, target);
                BuildTrails(request.System, target, request.SimTime);

                lock (_frameLock)
                {
                    (_frameFront, _frameBack) = (_frameBack, _frameFront);
                    _hasNewFrame = true;
                }

                Thread.Sleep(1);
            }
        }

        public void RenderWorld(double simTime)
        {
            if (_hasNewFrame)
            {
                lock (_frameLock)
                {
                    _hasNewFrame = false;
                }
            }

            FrameData data = _frameFront;

            _sceneFbo.Bind();
            GL.ClearColor(0.02f, 0.03f, 0.06f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.DepthTest);

            _spriteBuffer.Update(data.Sprites);
            _orbitBuffer.Update(data.Orbits);
            _trailBuffer.Update(data.Trails);

            UploadOccluders(data.Occluders);

            DrawSprites(simTime, data.Sprites.Count);
            DrawOrbits(simTime, data.Orbits.Count);
            DrawTrails(simTime, data.Trails.Count);

            DrawPlanets(simTime, data.Planets);
            DrawRings(simTime, data.Rings);
            DrawSun(simTime, data.Sun);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        public void ApplyPostProcess(int overlayTexture)
        {
            ExtractBrightPass();
            BlurBloom();

            _finalFbo.Bind();
            GL.Viewport(0, 0, _outputWidth, _outputHeight);

            _compositeShader.Use();
            _compositeShader.SetUniform("uScene", 0);
            _compositeShader.SetUniform("uBloom", 1);
            _compositeShader.SetUniform("uOverlay", 2);
            _compositeShader.SetUniform("uVignette", 0.25f);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _sceneFbo.ColorTexture);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, _blurPong.ColorTexture);
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, overlayTexture);

            _quad.Draw();
        }

        public void StopWorker()
        {
            _workerRunning = false;
            if (_worker != null && _worker.IsAlive)
                _worker.Join();
            _worker = null;
        }

        private void DrawSprites(double simTime, int count)
        {
            if (count <= 0)
                return;

            GL.BindVertexArray(_unitVao);
            _spriteShader.Use();
            _spriteShader.SetUniform("uViewProj", _camera.ViewProjection);
            _spriteShader.SetUniform("uTime", (float)simTime);
            GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, count);
            GL.BindVertexArray(0);
        }

        private void DrawOrbits(double simTime, int count)
        {
            if (count <= 0)
                return;

            GL.BindVertexArray(_unitVao);
            _orbitShader.Use();
            _orbitShader.SetUniform("uViewProj", _camera.ViewProjection);
            _orbitShader.SetUniform("uTime", (float)simTime);
            GL.DrawArraysInstanced(PrimitiveType.Points, 0, 1, count);
            GL.BindVertexArray(0);
        }

        private void DrawTrails(double simTime, int count)
        {
            if (count <= 0)
                return;

            GL.BindVertexArray(_unitVao);
            _trailShader.Use();
            _trailShader.SetUniform("uViewProj", _camera.ViewProjection);
            _trailShader.SetUniform("uTime", (float)simTime);
            GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, count);
            GL.BindVertexArray(0);
        }

        private void DrawPlanets(double simTime, List<PlanetInstance> planets)
        {
            if (planets.Count == 0)
                return;

            GL.BindVertexArray(_unitVao);
            _planetShader.Use();
            _planetShader.SetUniform("uViewProj", _camera.ViewProjection);
            _planetShader.SetUniform("uTime", (float)simTime);
            _planetShader.SetUniform("uOccluderCount", _frameFront.Occluders.Count);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, _occluderBuffer);

            foreach (PlanetInstance planet in planets)
            {
                _planetShader.SetUniform("uCenter", planet.Position.X, planet.Position.Y);
                _planetShader.SetUniform("uRadius", planet.Radius);
                _planetShader.SetUniform("uSpin", planet.Spin);
                _planetShader.SetUniform("uAxisTilt", planet.AxisTilt);
                _planetShader.SetUniform("uLightDir", planet.LightDir);
                _planetShader.SetUniform("uDepth", planet.Depth);
                _planetShader.SetUniform("uSeed", planet.Seed);
                _planetShader.SetUniform("uTextureType", planet.TextureType);

                int tex = _textureCache.GetTexture(ProceduralTextureKind.Planet, planet.Seed, PlanetTextureSize);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, tex);
                _planetShader.SetUniform("uTexture", 0);

                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            }

            GL.BindVertexArray(0);
        }

        private void DrawRings(double simTime, List<RingInstance> rings)
        {
            if (rings.Count == 0)
                return;

            GL.BindVertexArray(_unitVao);
            _ringShader.Use();
            _ringShader.SetUniform("uViewProj", _camera.ViewProjection);
            _ringShader.SetUniform("uTime", (float)simTime);
            _ringShader.SetUniform("uOccluderCount", _frameFront.Occluders.Count);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, _occluderBuffer);

            foreach (RingInstance ring in rings)
            {
                _ringShader.SetUniform("uCenter", ring.Position.X, ring.Position.Y);
                _ringShader.SetUniform("uInner", ring.InnerRadius);
                _ringShader.SetUniform("uOuter", ring.OuterRadius);
                _ringShader.SetUniform("uAxisTilt", ring.AxisTilt);
                _ringShader.SetUniform("uPattern", ring.Pattern);
                _ringShader.SetUniform("uDepth", ring.Depth);
                _ringShader.SetUniform("uSeed", ring.Seed);

                int tex = _textureCache.GetTexture(ProceduralTextureKind.Ring, ring.Seed, RingTextureSize);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, tex);
                _ringShader.SetUniform("uTexture", 0);

                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            }

            GL.BindVertexArray(0);
        }

        private void DrawSun(double simTime, SunInstance? sun)
        {
            if (!sun.HasValue)
                return;

            SunInstance data = sun.Value;
            GL.BindVertexArray(_unitVao);
            _sunShader.Use();
            _sunShader.SetUniform("uViewProj", _camera.ViewProjection);
            _sunShader.SetUniform("uTime", (float)simTime);
            _sunShader.SetUniform("uCenter", data.Position.X, data.Position.Y);
            _sunShader.SetUniform("uRadius", data.Radius);
            _sunShader.SetUniform("uCorona", data.CoronaRadius);
            _sunShader.SetUniform("uSeed", data.Seed);

            int tex = _textureCache.GetTexture(ProceduralTextureKind.Sun, data.Seed, SunTextureSize);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, tex);
            _sunShader.SetUniform("uTexture", 0);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            GL.BindVertexArray(0);
        }

        private void ExtractBrightPass()
        {
            _brightFbo.Bind();
            GL.Clear(ClearBufferMask.ColorBufferBit);
            _brightShader.Use();
            _brightShader.SetUniform("uScene", 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _sceneFbo.ColorTexture);
            _quad.Draw();
        }

        private void BlurBloom()
        {
            bool horizontal = true;
            int passes = 4;
            for (int i = 0; i < passes; i++)
            {
                SceneFbo target = horizontal ? _blurPing : _blurPong;
                target.Bind();
                _blurShader.Use();
                _blurShader.SetUniform("uTexture", 0);
                _blurShader.SetUniform("uHorizontal", horizontal ? 1 : 0);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, i == 0 ? _brightFbo.ColorTexture : (horizontal ? _blurPong.ColorTexture : _blurPing.ColorTexture));
                _quad.Draw();
                horizontal = !horizontal;
            }
        }

        private void UploadOccluders(List<Vector4> occluders)
        {
            int count = Math.Min(occluders.Count, MaxOccluders);
            if (count == 0)
                return;

            float[] data = new float[count * 4];
            for (int i = 0; i < count; i++)
            {
                Vector4 o = occluders[i];
                int idx = i * 4;
                data[idx] = o.X;
                data[idx + 1] = o.Y;
                data[idx + 2] = o.Z;
                data[idx + 3] = o.W;
            }

            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _occluderBuffer);
            GL.BufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero, data.Length * sizeof(float), data);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);
        }

        private void BuildStars(StarSystem system, FrameData target, float minX, float minY, float maxX, float maxY, double simTime)
        {
            int seed = system.Seed;
            for (int i = 0; i < 220; i++)
            {
                double wx = (HashNoise.Hash01(seed, i, 11) * 2.0 - 1.0) * 120.0;
                double wy = (HashNoise.Hash01(seed, i, 31) * 2.0 - 1.0) * 120.0;
                if (wx < minX || wx > maxX || wy < minY || wy > maxY)
                    continue;

                float size = 0.25f + (float)HashNoise.Hash01(seed, i, 61) * 0.65f;
                Vector4 color = new Vector4(0.9f, 0.9f, 1f, 1f);
                float depth = 0.1f + (float)HashNoise.Hash01(seed, i, 91) * 0.2f;
                float twinkle = (float)HashNoise.Hash01(seed, i, 71);
                target.Sprites.Add(new SpriteInstance(new Vector2((float)wx, (float)wy), size, color, new Vector2(depth, twinkle)));
            }
        }

        private void BuildDebris(StarSystem system, FrameData target, float minX, float minY, float maxX, float maxY, double simTime)
        {
            int seed = system.Seed ^ 0xBEEF;
            for (int i = 0; i < 180; i++)
            {
                double wx = (HashNoise.Hash01(seed, i, 11) * 2.0 - 1.0) * 32.0;
                double wy = (HashNoise.Hash01(seed, i, 31) * 2.0 - 1.0) * 32.0;
                if (wx < minX || wx > maxX || wy < minY || wy > maxY)
                    continue;

                float size = 0.2f + (float)HashNoise.Hash01(seed, i, 71) * 0.5f;
                Vector4 color = new Vector4(0.6f, 0.6f, 0.7f, 0.8f);
                float drift = (float)(simTime * (HashNoise.Hash01(seed, i, 91) - 0.5) * 0.25);
                target.Sprites.Add(new SpriteInstance(new Vector2((float)(wx + drift), (float)wy), size, color, new Vector2(0.4f, 0f)));
            }
        }

        private void BuildNebula(StarSystem system, FrameData target, float minX, float minY, float maxX, float maxY, double simTime)
        {
            if (system.Nebulae.Count == 0)
                return;

            foreach (NebulaCloud nebula in system.Nebulae)
            {
                if (nebula.WX < minX || nebula.WX > maxX || nebula.WY < minY || nebula.WY > maxY)
                    continue;

                float size = (float)nebula.RadiusWorld;
                Vector4 color = new Vector4(0.2f, 0.3f, 0.45f, (float)(0.15 + nebula.Density01 * 0.35));
                float seed = nebula.NoiseSeed;
                target.Sprites.Add(new SpriteInstance(new Vector2((float)nebula.WX, (float)nebula.WY), size, color, new Vector2(0.15f, seed)));
            }
        }

        private void BuildPlanets(StarSystem system, FrameData target, double simTime, bool showRings)
        {
            Vector3 lightDir = new Vector3(0.2f, 0.1f, 0.9f);
            if (system.HasStar)
            {
                target.Sun = new SunInstance(new Vector2(0f, 0f), (float)system.SunRadiusWorld, (float)system.CoronaRadiusWorld, system.Seed);
            }

            for (int i = 0; i < system.Planets.Count; i++)
            {
                Planet planet = system.Planets[i];
                float radius = (float)planet.RadiusWorld;
                float depth = 0.5f + (float)planet.WZ * 0.01f;
                float spin = (float)PlanetDrawer.SpinTurns(simTime, planet.SpinSpeed);
                int seed = system.Seed ^ planet.Name.GetHashCode();

                target.Planets.Add(new PlanetInstance(new Vector2((float)planet.WX, (float)planet.WY), radius, planet.AxisTilt, spin, lightDir, depth, seed, (int)planet.Texture));
                target.Occluders.Add(new Vector4((float)planet.WX, (float)planet.WY, radius, depth));

                if (showRings && planet.HasRings)
                {
                    float inner = radius * 1.2f;
                    float outer = radius * 2.0f;
                    float pattern = spin * 2.0f;
                    target.Rings.Add(new RingInstance(new Vector2((float)planet.WX, (float)planet.WY), inner, outer, (float)planet.AxisTilt, pattern, depth + 0.001f, seed));
                }

                for (int m = 0; m < planet.Moons.Count; m++)
                {
                    Moon moon = planet.Moons[m];
                    float mRadius = (float)moon.RadiusWorld;
                    float mDepth = 0.5f + (float)moon.WZ * 0.01f;
                    float mSpin = (float)ComputeMoonSpin(simTime, moon, seed ^ m);

                    target.Planets.Add(new PlanetInstance(new Vector2((float)moon.WX, (float)moon.WY), mRadius, 0f, mSpin, lightDir, mDepth, seed ^ moon.Name.GetHashCode(), (int)moon.Texture));
                    target.Occluders.Add(new Vector4((float)moon.WX, (float)moon.WY, mRadius, mDepth));
                }
            }
        }

        private void BuildShips(StarSystem system, FrameData target, float minX, float minY, float maxX, float maxY, double simTime)
        {
            for (int i = 0; i < system.Ships.Count; i++)
            {
                Ship ship = system.Ships[i];
                if (ship.WX < minX || ship.WX > maxX || ship.WY < minY || ship.WY > maxY)
                    continue;

                Vector4 color = new Vector4(0.9f, 0.9f, 0.95f, 1f);
                float size = 0.35f;
                float heading = (float)Math.Atan2(ship.VY, ship.VX);
                target.Sprites.Add(new SpriteInstance(new Vector2((float)ship.WX, (float)ship.WY), size, color, new Vector2(0.7f, heading)));
            }
        }

        private void BuildStations(StarSystem system, FrameData target, float minX, float minY, float maxX, float maxY)
        {
            for (int i = 0; i < system.Stations.Count; i++)
            {
                Station station = system.Stations[i];
                if (station.WX < minX || station.WX > maxX || station.WY < minY || station.WY > maxY)
                    continue;

                Vector4 color = new Vector4(0.4f, 0.6f, 0.9f, 1f);
                float size = 0.45f;
                target.Sprites.Add(new SpriteInstance(new Vector2((float)station.WX, (float)station.WY), size, color, new Vector2(0.6f, 0f)));
            }
        }

        private void BuildAsteroids(StarSystem system, FrameData target, float minX, float minY, float maxX, float maxY)
        {
            for (int i = 0; i < system.Asteroids.Count; i++)
            {
                Asteroid asteroid = system.Asteroids[i];
                if (asteroid.WX < minX || asteroid.WX > maxX || asteroid.WY < minY || asteroid.WY > maxY)
                    continue;

                Vector4 color = new Vector4(0.65f, 0.6f, 0.55f, 1f);
                float size = (float)asteroid.RadiusWorld;
                target.Sprites.Add(new SpriteInstance(new Vector2((float)asteroid.WX, (float)asteroid.WY), size, color, new Vector2(0.5f, 0f)));
            }
        }

        private void BuildOrbits(StarSystem system, FrameData target)
        {
            for (int i = 0; i < system.Planets.Count; i++)
            {
                Planet planet = system.Planets[i];
                if (planet.A <= 0.0)
                    continue;

                int steps = 120;
                for (int s = 0; s < steps; s++)
                {
                    double m = (s / (double)steps) * Math.PI * 2.0;
                    ComputeOrbitPoint(system.Seed, planet, m, out double wx, out double wy);
                    target.Orbits.Add(new OrbitInstance(new Vector2((float)wx, (float)wy), new Vector3(0.3f, 0.35f, 0.45f)));
                }
            }
        }

        private void BuildTrails(StarSystem system, FrameData target, double simTime)
        {
            for (int i = 0; i < system.Ships.Count; i++)
            {
                Ship ship = system.Ships[i];
                int count = 0;
                ship.Trail.ForEachNewest((pt, age, total) =>
                {
                    float t = total > 1 ? age / (float)(total - 1) : 0f;
                    float alpha = 1f - t;
                    target.Trails.Add(new TrailInstance(new Vector2((float)pt.X, (float)pt.Y), new Vector2(alpha, t)));
                    count++;
                });
            }
        }

        private static void ComputeOrbitPoint(int systemSeed, Planet planet, double meanAnomaly, out double wx, out double wy)
        {
            OrbitMath.Kepler2D(planet.A, MathUtil.Clamp(planet.E, 0.0, 0.95), 0.0, meanAnomaly, out double x, out double y);

            int pSeed = systemSeed ^ planet.Name.GetHashCode();
            double plane = (HashNoise.Hash01(pSeed, 101, 202) * 2.0 - 1.0) * 1.05;

            double c = Math.Cos(plane);
            double s = Math.Sin(plane);
            double rx = x * c - y * s;
            double ry = x * s + y * c;

            double inc = 0.55 + 0.45 * HashNoise.Hash01(pSeed, 303, 404);
            ry *= inc;

            double co = Math.Cos(planet.Omega);
            double so = Math.Sin(planet.Omega);
            wx = rx * co - ry * so;
            wy = rx * so + ry * co;
        }

        private static double ComputeMoonSpin(double simTime, Moon moon, int seed)
        {
            if (Math.Abs(moon.SpinSpeed) > 1e-6)
                return PlanetDrawer.SpinTurns(simTime, moon.SpinSpeed);

            double period = Math.Max(0.001, moon.LocalPeriod);
            if (period <= 0.0001)
            {
                double fallback = 0.6 + 1.4 * HashNoise.Hash01(seed, 73, 91);
                return PlanetDrawer.SpinTurns(simTime, fallback);
            }

            double ang = moon.LocalPhase + (simTime * (Math.PI * 2.0) / period);
            return ang / (Math.PI * 2.0);
        }

        private static bool SupportsPersistentMapping()
        {
            string? version = GL.GetString(StringName.Version);
            if (version == null)
                return false;
            return version.StartsWith("4", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class FrameData
        {
            public readonly List<SpriteInstance> Sprites = new List<SpriteInstance>(2048);
            public readonly List<OrbitInstance> Orbits = new List<OrbitInstance>(2048);
            public readonly List<TrailInstance> Trails = new List<TrailInstance>(4096);
            public readonly List<PlanetInstance> Planets = new List<PlanetInstance>(64);
            public readonly List<RingInstance> Rings = new List<RingInstance>(32);
            public readonly List<Vector4> Occluders = new List<Vector4>(64);
            public SunInstance? Sun;

            public void Clear()
            {
                Sprites.Clear();
                Orbits.Clear();
                Trails.Clear();
                Planets.Clear();
                Rings.Clear();
                Occluders.Clear();
                Sun = null;
            }
        }

        private struct FramePrepRequest
        {
            public StarSystem System;
            public double SimTime;
            public bool ShowOrbits;
            public bool ShowBelts;
            public bool ShowRings;
            public bool ShowDebris;
            public bool ShowStarfield;
            public bool ShowNebula;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct SpriteInstance
        {
            public const int Stride = sizeof(float) * 9;
            public readonly Vector2 Position;
            public readonly float Size;
            public readonly Vector4 Color;
            public readonly Vector2 Extras;

            public SpriteInstance(Vector2 position, float size, Vector4 color, Vector2 extras)
            {
                Position = position;
                Size = size;
                Color = color;
                Extras = extras;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct OrbitInstance
        {
            public const int Stride = sizeof(float) * 5;
            public readonly Vector2 Position;
            public readonly Vector3 Color;

            public OrbitInstance(Vector2 position, Vector3 color)
            {
                Position = position;
                Color = color;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct TrailInstance
        {
            public const int Stride = sizeof(float) * 4;
            public readonly Vector2 Position;
            public readonly Vector2 Data;

            public TrailInstance(Vector2 position, Vector2 data)
            {
                Position = position;
                Data = data;
            }
        }

        private readonly struct PlanetInstance
        {
            public readonly Vector2 Position;
            public readonly float Radius;
            public readonly float AxisTilt;
            public readonly float Spin;
            public readonly Vector3 LightDir;
            public readonly float Depth;
            public readonly int Seed;
            public readonly int TextureType;

            public PlanetInstance(Vector2 position, float radius, double axisTilt, float spin, Vector3 lightDir, float depth, int seed, int textureType)
            {
                Position = position;
                Radius = radius;
                AxisTilt = (float)axisTilt;
                Spin = spin;
                LightDir = lightDir;
                Depth = depth;
                Seed = seed;
                TextureType = textureType;
            }
        }

        private readonly struct RingInstance
        {
            public readonly Vector2 Position;
            public readonly float InnerRadius;
            public readonly float OuterRadius;
            public readonly float AxisTilt;
            public readonly float Pattern;
            public readonly float Depth;
            public readonly int Seed;

            public RingInstance(Vector2 position, float innerRadius, float outerRadius, float axisTilt, float pattern, float depth, int seed)
            {
                Position = position;
                InnerRadius = innerRadius;
                OuterRadius = outerRadius;
                AxisTilt = axisTilt;
                Pattern = pattern;
                Depth = depth;
                Seed = seed;
            }
        }

        private readonly struct SunInstance
        {
            public readonly Vector2 Position;
            public readonly float Radius;
            public readonly float CoronaRadius;
            public readonly int Seed;

            public SunInstance(Vector2 position, float radius, float coronaRadius, int seed)
            {
                Position = position;
                Radius = radius;
                CoronaRadius = coronaRadius;
                Seed = seed;
            }
        }

        private const string SpriteVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 iPos;
layout(location = 2) in float iSize;
layout(location = 3) in vec4 iColor;
layout(location = 4) in vec2 iExtras;

uniform mat4 uViewProj;

out vec2 vUv;
out vec4 vColor;
out vec2 vExtras;

void main()
{
    vUv = aPos;
    vColor = iColor;
    vExtras = iExtras;
    vec2 world = iPos + (aPos * 2.0 - 1.0) * iSize;
    gl_Position = uViewProj * vec4(world, 0.0, 1.0);
}
";

        private const string SpriteFragmentShader = @"
#version 330 core
in vec2 vUv;
in vec4 vColor;
in vec2 vExtras;
out vec4 FragColor;
uniform float uTime;

void main()
{
    vec2 uv = vUv * 2.0 - 1.0;
    float r2 = dot(uv, uv);
    if (r2 > 1.0)
        discard;

    float twinkle = 0.5 + 0.5 * sin(uTime * 4.0 + vExtras.y * 12.0);
    float alpha = vColor.a;
    vec3 color = vColor.rgb * mix(0.7, 1.1, twinkle);
    FragColor = vec4(color, alpha);
}
";

        private const string OrbitVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 5) in vec2 iPos;
layout(location = 6) in vec3 iColor;

uniform mat4 uViewProj;

out vec3 vColor;

void main()
{
    vColor = iColor;
    gl_Position = uViewProj * vec4(iPos, 0.0, 1.0);
    gl_PointSize = 1.5;
}
";

        private const string OrbitFragmentShader = @"
#version 330 core
in vec3 vColor;
out vec4 FragColor;
void main()
{
    FragColor = vec4(vColor, 0.6);
}
";

        private const string TrailVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 7) in vec2 iPos;
layout(location = 8) in vec2 iData;

uniform mat4 uViewProj;

out float vAlpha;

void main()
{
    vec2 world = iPos + (aPos * 2.0 - 1.0) * 0.08;
    gl_Position = uViewProj * vec4(world, 0.0, 1.0);
    vAlpha = iData.x;
}
";

        private const string TrailFragmentShader = @"
#version 330 core
in float vAlpha;
out vec4 FragColor;
void main()
{
    FragColor = vec4(0.6, 0.8, 1.0, vAlpha * 0.6);
}
";

        private const string PlanetVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;

uniform mat4 uViewProj;
uniform vec2 uCenter;
uniform float uRadius;

out vec2 vUv;

void main()
{
    vUv = aPos;
    vec2 world = uCenter + (aPos * 2.0 - 1.0) * uRadius;
    gl_Position = uViewProj * vec4(world, 0.0, 1.0);
}
";

        private const string PlanetFragmentShader = @"
#version 330 core
layout(std430, binding = 0) buffer Occluders
{
    vec4 occluders[];
};

in vec2 vUv;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform float uSpin;
uniform float uAxisTilt;
uniform vec3 uLightDir;
uniform float uDepth;
uniform float uSeed;
uniform int uTextureType;
uniform int uOccluderCount;
uniform vec2 uCenter;
uniform float uRadius;

const float PI = 3.14159265359;

void main()
{
    vec2 p = vUv * 2.0 - 1.0;
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

    vec3 baseColor = texture(uTexture, uv).rgb;
    float ndotlRaw = dot(n, normalize(uLightDir));
    float ndotl = max(ndotlRaw, 0.0);
    float limb = 0.78 + 0.22 * n.z;
    float light = clamp(ndotl * limb, 0.0, 1.0);
    vec3 litColor = baseColor * light;

    for (int i = 0; i < uOccluderCount; i++)
    {
        vec4 occ = occluders[i];
        vec2 delta = (vUv * 2.0 - 1.0) * uRadius + uCenter - occ.xy;
        if (occ.w < uDepth && dot(delta, delta) < occ.z * occ.z)
            discard;
    }

    FragColor = vec4(litColor, 1.0);
}
";

        private const string RingVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;

uniform mat4 uViewProj;
uniform vec2 uCenter;
uniform float uOuter;

out vec2 vUv;

void main()
{
    vUv = aPos;
    vec2 world = uCenter + (aPos * 2.0 - 1.0) * uOuter;
    gl_Position = uViewProj * vec4(world, 0.0, 1.0);
}
";

        private const string RingFragmentShader = @"
#version 330 core
layout(std430, binding = 0) buffer Occluders
{
    vec4 occluders[];
};

in vec2 vUv;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform float uInner;
uniform float uOuter;
uniform float uAxisTilt;
uniform float uPattern;
uniform float uDepth;
uniform float uSeed;
uniform int uOccluderCount;

void main()
{
    vec2 p = vUv * 2.0 - 1.0;
    float r = length(p) * uOuter;
    if (r < uInner || r > uOuter)
        discard;

    float angle = atan(p.y, p.x) + uPattern;
    vec2 uv = vec2(angle * 0.159 + uSeed * 0.01, r / uOuter);
    float density = texture(uTexture, uv).r;
    float alpha = smoothstep(0.0, 1.0, density);

    FragColor = vec4(vec3(0.7, 0.7, 0.8) * density, alpha * 0.8);
}
";

        private const string SunVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;

uniform mat4 uViewProj;
uniform vec2 uCenter;
uniform float uRadius;

out vec2 vUv;

void main()
{
    vUv = aPos;
    vec2 world = uCenter + (aPos * 2.0 - 1.0) * uRadius;
    gl_Position = uViewProj * vec4(world, 0.0, 1.0);
}
";

        private const string SunFragmentShader = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform float uRadius;
uniform float uCorona;
uniform float uTime;
uniform float uSeed;

void main()
{
    vec2 p = vUv * 2.0 - 1.0;
    float r = length(p);
    if (r > 1.0)
        discard;

    vec3 base = texture(uTexture, vUv).rgb;
    float flare = smoothstep(1.0, 0.7, r);
    float corona = smoothstep(1.0, 0.8, r) * (0.6 + 0.4 * sin(uTime * 3.0 + uSeed));
    vec3 color = base + vec3(0.9, 0.5, 0.2) * corona;
    FragColor = vec4(color, 1.0);
}
";

        private const string FullscreenVertexShader = @"
#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vUv;
void main()
{
    vUv = aPos;
    vec2 ndc = aPos * 2.0 - 1.0;
    gl_Position = vec4(ndc.x, ndc.y, 0.0, 1.0);
}
";

        private const string BrightFragmentShader = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;
uniform sampler2D uScene;
void main()
{
    vec3 color = texture(uScene, vUv).rgb;
    float luminance = dot(color, vec3(0.2126, 0.7152, 0.0722));
    float bloom = smoothstep(0.7, 1.0, luminance);
    FragColor = vec4(color * bloom, 1.0);
}
";

        private const string BlurFragmentShader = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform int uHorizontal;
void main()
{
    vec2 texel = 1.0 / vec2(textureSize(uTexture, 0));
    vec3 result = texture(uTexture, vUv).rgb * 0.227027;
    for (int i = 1; i < 5; i++)
    {
        vec2 offset = uHorizontal == 1 ? vec2(texel.x * i, 0.0) : vec2(0.0, texel.y * i);
        result += texture(uTexture, vUv + offset).rgb * 0.194594;
        result += texture(uTexture, vUv - offset).rgb * 0.194594;
    }
    FragColor = vec4(result, 1.0);
}
";

        private const string CompositeFragmentShader = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;

uniform sampler2D uScene;
uniform sampler2D uBloom;
uniform sampler2D uOverlay;
uniform float uVignette;

void main()
{
    vec3 scene = texture(uScene, vUv).rgb;
    vec3 bloom = texture(uBloom, vUv).rgb;
    vec4 overlay = texture(uOverlay, vUv);

    vec2 uv = vUv - 0.5;
    float vig = smoothstep(0.9, 0.1, dot(uv, uv)) * uVignette;

    vec3 color = scene + bloom * 0.8;
    color = mix(color, color * (1.0 - vig), 0.5);
    color = mix(color, overlay.rgb, overlay.a);

    FragColor = vec4(color, 1.0);
}
";
    }
}
