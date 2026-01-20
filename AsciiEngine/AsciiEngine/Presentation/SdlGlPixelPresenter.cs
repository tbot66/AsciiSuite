using System;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using SDL2;

namespace AsciiEngine
{
    public sealed class SdlGlPixelPresenter : IFramePresenter<PixelRenderer>
    {
        private readonly string _title;
        private IntPtr _window;
        private IntPtr _glContext;
        private bool _initialized;
        private bool _disposed;

        private int _vao;
        private int _vbo;
        private int _program;
        private int _resolutionUniform;
        private int _textureUniform;
        private int _textureId;

        private int _textureWidth;
        private int _textureHeight;
        private int _lastDrawableW;
        private int _lastDrawableH;

        private float[] _quadVertices = new float[6 * 4];

        public bool QuitRequested { get; private set; }

        private int _mouseX;
        private int _mouseY;
        private bool _mouseLeftDown;
        private bool _mouseLeftPressed;
        private bool _mouseLeftReleased;

        public SdlGlPixelPresenter(string title = "AsciiEngine")
        {
            _title = title;
        }

        public void Present(PixelRenderer src)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SdlGlPixelPresenter));
            if (src == null) throw new ArgumentNullException(nameof(src));

            if (!_initialized)
                Initialize(src);

            if (QuitRequested)
                return;

            SDL.SDL_GL_GetDrawableSize(_window, out int windowW, out int windowH);
            if (windowW <= 0 || windowH <= 0)
                return;

            if (windowW != _lastDrawableW || windowH != _lastDrawableH)
            {
                _lastDrawableW = windowW;
                _lastDrawableH = windowH;
                Diagnostics.Log($"[AsciiEngine] Drawable size: drawable={windowW}x{windowH}, logical={src.Width}x{src.Height}.");
                UpdateQuadVertices(windowW, windowH);
            }

            EnsureTexture(src);

            GL.Viewport(0, 0, windowW, windowH);
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            GL.UseProgram(_program);
            GL.Uniform2(_resolutionUniform, (float)windowW, (float)windowH);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _textureId);
            GL.Uniform1(_textureUniform, 0);

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _quadVertices.Length * sizeof(float), _quadVertices, BufferUsageHint.DynamicDraw);

            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, src.Width, src.Height, PixelFormat.Rgba, PixelType.UnsignedByte, src.Buffer);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

            SDL.SDL_GL_SwapWindow(_window);
        }

        public void PollInput(PixelRenderer src, InputState input)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SdlGlPixelPresenter));
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (input == null) throw new ArgumentNullException(nameof(input));

            if (!_initialized)
                Initialize(src);

            PumpEvents();

            SDL.SDL_GetWindowSize(_window, out int windowW, out int windowH);
            int sx = 0;
            int sy = 0;
            if (windowW > 0 && windowH > 0)
            {
                sx = (int)Math.Round(_mouseX * (src.Width / (double)windowW));
                sy = (int)Math.Round(_mouseY * (src.Height / (double)windowH));
            }

            sx = Math.Clamp(sx, 0, Math.Max(0, src.Width - 1));
            sy = Math.Clamp(sy, 0, Math.Max(0, src.Height - 1));

            input.SetMouseState(sx, sy, _mouseLeftDown, _mouseLeftPressed, _mouseLeftReleased);

            _mouseLeftPressed = false;
            _mouseLeftReleased = false;
        }

        private void Initialize(PixelRenderer src)
        {
            if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO) < 0)
                throw new InvalidOperationException($"SDL_Init failed: {SDL.SDL_GetError()}");

            SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_MAJOR_VERSION, 3);
            SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_MINOR_VERSION, 3);
            SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL.SDL_GLprofile.SDL_GL_CONTEXT_PROFILE_CORE);
            SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_DOUBLEBUFFER, 1);

            int initialW = Math.Max(1, src.Width * 2);
            int initialH = Math.Max(1, src.Height * 2);

            _window = SDL.SDL_CreateWindow(
                _title,
                SDL.SDL_WINDOWPOS_CENTERED,
                SDL.SDL_WINDOWPOS_CENTERED,
                initialW,
                initialH,
                SDL.SDL_WindowFlags.SDL_WINDOW_OPENGL | SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE);

            if (_window == IntPtr.Zero)
                throw new InvalidOperationException($"SDL_CreateWindow failed: {SDL.SDL_GetError()}");

            _glContext = SDL.SDL_GL_CreateContext(_window);
            if (_glContext == IntPtr.Zero)
                throw new InvalidOperationException($"SDL_GL_CreateContext failed: {SDL.SDL_GetError()}");

            SDL.SDL_GL_MakeCurrent(_window, _glContext);
            SDL.SDL_GL_SetSwapInterval(1);
            GL.LoadBindings(new SdlBindingsContext());

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);

            _program = CreateProgram(VertexShaderSource, FragmentShaderSource);
            _resolutionUniform = GL.GetUniformLocation(_program, "uResolution");
            _textureUniform = GL.GetUniformLocation(_program, "uTexture");

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            _textureId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _textureId);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            SDL.SDL_GL_GetDrawableSize(_window, out int drawableW, out int drawableH);
            _lastDrawableW = drawableW;
            _lastDrawableH = drawableH;
            UpdateQuadVertices(drawableW, drawableH);

            EnsureTexture(src);

            _initialized = true;
        }

        private void EnsureTexture(PixelRenderer src)
        {
            if (src.Width == _textureWidth && src.Height == _textureHeight)
                return;

            _textureWidth = src.Width;
            _textureHeight = src.Height;

            GL.BindTexture(TextureTarget.Texture2D, _textureId);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, _textureWidth, _textureHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);

            Diagnostics.Log($"[AsciiEngine] Pixel texture: size={_textureWidth}x{_textureHeight}, bufferBytes={src.BufferLength}.");
        }

        private void UpdateQuadVertices(int windowW, int windowH)
        {
            float w = windowW;
            float h = windowH;

            int offset = 0;
            WriteVertex(_quadVertices, ref offset, 0f, 0f, 0f, 0f);
            WriteVertex(_quadVertices, ref offset, w, 0f, 1f, 0f);
            WriteVertex(_quadVertices, ref offset, w, h, 1f, 1f);

            WriteVertex(_quadVertices, ref offset, 0f, 0f, 0f, 0f);
            WriteVertex(_quadVertices, ref offset, w, h, 1f, 1f);
            WriteVertex(_quadVertices, ref offset, 0f, h, 0f, 1f);
        }

        private static void WriteVertex(float[] data, ref int offset, float x, float y, float u, float v)
        {
            data[offset++] = x;
            data[offset++] = y;
            data[offset++] = u;
            data[offset++] = v;
        }

        private void PumpEvents()
        {
            while (SDL.SDL_PollEvent(out SDL.SDL_Event e) == 1)
            {
                if (e.type == SDL.SDL_EventType.SDL_QUIT)
                    QuitRequested = true;
                else if (e.type == SDL.SDL_EventType.SDL_MOUSEMOTION)
                {
                    _mouseX = e.motion.x;
                    _mouseY = e.motion.y;
                }
                else if (e.type == SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN)
                {
                    if (e.button.button == SDL.SDL_BUTTON_LEFT)
                    {
                        _mouseLeftDown = true;
                        _mouseLeftPressed = true;
                        _mouseX = e.button.x;
                        _mouseY = e.button.y;
                    }
                }
                else if (e.type == SDL.SDL_EventType.SDL_MOUSEBUTTONUP)
                {
                    if (e.button.button == SDL.SDL_BUTTON_LEFT)
                    {
                        _mouseLeftDown = false;
                        _mouseLeftReleased = true;
                        _mouseX = e.button.x;
                        _mouseY = e.button.y;
                    }
                }
            }
        }

        private static int CreateProgram(string vertexSource, string fragmentSource)
        {
            int vertex = CompileShader(ShaderType.VertexShader, vertexSource);
            int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);

            int program = GL.CreateProgram();
            GL.AttachShader(program, vertex);
            GL.AttachShader(program, fragment);
            GL.LinkProgram(program);

            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
            if (status == 0)
            {
                string info = GL.GetProgramInfoLog(program);
                GL.DeleteProgram(program);
                GL.DeleteShader(vertex);
                GL.DeleteShader(fragment);
                throw new InvalidOperationException($"Program link failed: {info}");
            }

            GL.DetachShader(program, vertex);
            GL.DetachShader(program, fragment);
            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);
            return program;
        }

        private static int CompileShader(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                string info = GL.GetShaderInfoLog(shader);
                GL.DeleteShader(shader);
                throw new InvalidOperationException($"{type} compile failed: {info}");
            }

            return shader;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_textureId != 0) GL.DeleteTexture(_textureId);
            if (_program != 0) GL.DeleteProgram(_program);
            if (_vbo != 0) GL.DeleteBuffer(_vbo);
            if (_vao != 0) GL.DeleteVertexArray(_vao);

            if (_glContext != IntPtr.Zero)
            {
                SDL.SDL_GL_DeleteContext(_glContext);
                _glContext = IntPtr.Zero;
            }

            if (_window != IntPtr.Zero)
            {
                SDL.SDL_DestroyWindow(_window);
                _window = IntPtr.Zero;
            }

            SDL.SDL_Quit();
        }

        private const string VertexShaderSource = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUv;
uniform vec2 uResolution;
out vec2 vUv;

void main()
{
    vec2 zeroToOne = aPos / uResolution;
    vec2 zeroToTwo = zeroToOne * 2.0;
    vec2 clip = zeroToTwo - 1.0;
    gl_Position = vec4(clip.x, -clip.y, 0.0, 1.0);
    vUv = aUv;
}";

        private const string FragmentShaderSource = @"
#version 330 core
in vec2 vUv;
uniform sampler2D uTexture;
out vec4 FragColor;

void main()
{
    FragColor = texture(uTexture, vUv);
}";
    }
}
