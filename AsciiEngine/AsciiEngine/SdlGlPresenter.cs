using System;
using System.IO;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using SDL2;

namespace AsciiEngine
{
    public sealed class SdlGlPresenter : IFramePresenter
    {
        private const int FloatsPerBgVertex = 5;
        private const int FloatsPerGlyphVertex = 7;

        private readonly string _title;
        private readonly string? _fontPath;
        private readonly int _fontPixelHeight;
        private readonly int _padding;

        private IntPtr _window;
        private IntPtr _glContext;
        private bool _initialized;
        private bool _disposed;
        private bool _quitRequested;

        private int _bgVao;
        private int _bgVbo;
        private int _glyphVao;
        private int _glyphVbo;
        private int _bgProgram;
        private int _glyphProgram;
        private int _bgResolutionUniform;
        private int _glyphResolutionUniform;
        private int _glyphAtlasUniform;

        private float[] _bgVertices = Array.Empty<float>();
        private float[] _glyphVertices = Array.Empty<float>();

        private GlyphAtlas? _atlas;

        public SdlGlPresenter(string? fontPath = null, int fontPixelHeight = 18, int padding = 1, string title = "AsciiEngine")
        {
            _title = title;
            _fontPath = fontPath;
            _fontPixelHeight = fontPixelHeight;
            _padding = padding;
        }

        public void Present(ConsoleRenderer src)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SdlGlPresenter));
            if (src == null) throw new ArgumentNullException(nameof(src));

            if (!_initialized)
                Initialize(src);

            if (_quitRequested)
                return;

            PumpEvents();

            SDL.SDL_GetWindowSize(_window, out int windowW, out int windowH);
            if (windowW <= 0 || windowH <= 0)
                return;

            GL.Viewport(0, 0, windowW, windowH);
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            RenderBackground(src, windowW, windowH);
            RenderGlyphs(src, windowW, windowH);

            SDL.SDL_GL_SwapWindow(_window);
        }

        private void Initialize(ConsoleRenderer src)
        {
            if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO) < 0)
                throw new InvalidOperationException($"SDL_Init failed: {SDL.SDL_GetError()}");

            SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_MAJOR_VERSION, 3);
            SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_MINOR_VERSION, 3);
            SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL.SDL_GLprofile.SDL_GL_CONTEXT_PROFILE_CORE);
            SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_DOUBLEBUFFER, 1);

            int initialW = Math.Max(1, src.Width * _fontPixelHeight);
            int initialH = Math.Max(1, src.Height * _fontPixelHeight);

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

            string resolvedFont = ResolveFontPath(_fontPath);
            _atlas = new GlyphAtlas(resolvedFont, _fontPixelHeight, _padding);

            SDL.SDL_SetWindowSize(_window, src.Width * _atlas.CellWidth, src.Height * _atlas.CellHeight);

            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _bgProgram = CreateProgram(BackgroundVertexShader, BackgroundFragmentShader);
            _glyphProgram = CreateProgram(GlyphVertexShader, GlyphFragmentShader);

            _bgResolutionUniform = GL.GetUniformLocation(_bgProgram, "uResolution");
            _glyphResolutionUniform = GL.GetUniformLocation(_glyphProgram, "uResolution");
            _glyphAtlasUniform = GL.GetUniformLocation(_glyphProgram, "uAtlas");

            _bgVao = GL.GenVertexArray();
            _bgVbo = GL.GenBuffer();
            GL.BindVertexArray(_bgVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _bgVbo);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, FloatsPerBgVertex * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, FloatsPerBgVertex * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            _glyphVao = GL.GenVertexArray();
            _glyphVbo = GL.GenBuffer();
            GL.BindVertexArray(_glyphVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _glyphVbo);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, FloatsPerGlyphVertex * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, FloatsPerGlyphVertex * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, FloatsPerGlyphVertex * sizeof(float), 4 * sizeof(float));
            GL.EnableVertexAttribArray(2);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            _initialized = true;
        }

        private void RenderBackground(ConsoleRenderer src, int windowW, int windowH)
        {
            int cellCount = src.Width * src.Height;
            int requiredFloats = cellCount * 6 * FloatsPerBgVertex;
            EnsureCapacity(ref _bgVertices, requiredFloats);

            float cellW = windowW / (float)src.Width;
            float cellH = windowH / (float)src.Height;

            ReadOnlySpan<Color> bg = src.Background;
            int offset = 0;

            for (int y = 0; y < src.Height; y++)
            {
                float y0 = y * cellH;
                float y1 = y0 + cellH;
                int row = y * src.Width;

                for (int x = 0; x < src.Width; x++)
                {
                    int idx = row + x;
                    float x0 = x * cellW;
                    float x1 = x0 + cellW;

                    (byte r, byte g, byte b) = ColorUtils.ToRgbBytes(bg[idx]);
                    WriteQuad(_bgVertices, ref offset, x0, y0, x1, y1, r, g, b);
                }
            }

            int vertexCount = offset / FloatsPerBgVertex;
            if (vertexCount == 0)
                return;

            GL.UseProgram(_bgProgram);
            GL.Uniform2(_bgResolutionUniform, (float)windowW, (float)windowH);
            GL.BindVertexArray(_bgVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _bgVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, offset * sizeof(float), _bgVertices, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);
        }

        private void RenderGlyphs(ConsoleRenderer src, int windowW, int windowH)
        {
            if (_atlas == null)
                return;

            int cellCount = src.Width * src.Height;
            int requiredFloats = cellCount * 6 * FloatsPerGlyphVertex;
            EnsureCapacity(ref _glyphVertices, requiredFloats);

            float cellW = windowW / (float)src.Width;
            float cellH = windowH / (float)src.Height;

            ReadOnlySpan<char> chars = src.Chars;
            ReadOnlySpan<Color> fg = src.Foreground;
            int offset = 0;

            for (int y = 0; y < src.Height; y++)
            {
                float y0 = y * cellH;
                float y1 = y0 + cellH;
                int row = y * src.Width;

                for (int x = 0; x < src.Width; x++)
                {
                    int idx = row + x;
                    float x0 = x * cellW;
                    float x1 = x0 + cellW;

                    (byte r, byte g, byte b) = ColorUtils.ToRgbBytes(fg[idx]);
                    GlyphAtlas.GlyphInfo glyph = _atlas.GetGlyph(chars[idx]);

                    WriteQuad(_glyphVertices, ref offset, x0, y0, x1, y1, glyph, r, g, b);
                }
            }

            int vertexCount = offset / FloatsPerGlyphVertex;
            if (vertexCount == 0)
                return;

            GL.UseProgram(_glyphProgram);
            GL.Uniform2(_glyphResolutionUniform, (float)windowW, (float)windowH);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _atlas.TextureId);
            GL.Uniform1(_glyphAtlasUniform, 0);

            GL.BindVertexArray(_glyphVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _glyphVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, offset * sizeof(float), _glyphVertices, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);
        }

        private static void EnsureCapacity(ref float[] data, int required)
        {
            if (data.Length < required)
                Array.Resize(ref data, required);
        }

        private static void WriteQuad(float[] data, ref int offset, float x0, float y0, float x1, float y1, byte r, byte g, byte b)
        {
            float rf = r / 255f;
            float gf = g / 255f;
            float bf = b / 255f;

            data[offset++] = x0;
            data[offset++] = y0;
            data[offset++] = rf;
            data[offset++] = gf;
            data[offset++] = bf;

            data[offset++] = x1;
            data[offset++] = y0;
            data[offset++] = rf;
            data[offset++] = gf;
            data[offset++] = bf;

            data[offset++] = x1;
            data[offset++] = y1;
            data[offset++] = rf;
            data[offset++] = gf;
            data[offset++] = bf;

            data[offset++] = x0;
            data[offset++] = y0;
            data[offset++] = rf;
            data[offset++] = gf;
            data[offset++] = bf;

            data[offset++] = x1;
            data[offset++] = y1;
            data[offset++] = rf;
            data[offset++] = gf;
            data[offset++] = bf;

            data[offset++] = x0;
            data[offset++] = y1;
            data[offset++] = rf;
            data[offset++] = gf;
            data[offset++] = bf;
        }

        private static void WriteQuad(float[] data, ref int offset, float x0, float y0, float x1, float y1, GlyphAtlas.GlyphInfo glyph, byte r, byte g, byte b)
        {
            float rf = r / 255f;
            float gf = g / 255f;
            float bf = b / 255f;

            data[offset++] = x0;
            data[offset++] = y0;
            data[offset++] = glyph.U0;
            data[offset++] = glyph.V0;
            data[offset++] = rf;
            data[offset++] = gf;
            data[offset++] = bf;

            data[offset++] = x1;
            data[offset++] = y0;
            data[offset++] = glyph.U1;
            data[offset++] = glyph.V0;
            data[offset++] = rf;
            data[offset++] = gf;
            data[offset++] = bf;

            data[offset++] = x1;
            data[offset++] = y1;
            data[offset++] = glyph.U1;
            data[offset++] = glyph.V1;
            data[offset++] = rf;
            data[offset++] = gf;
            data[offset++] = bf;

            data[offset++] = x0;
            data[offset++] = y0;
            data[offset++] = glyph.U0;
            data[offset++] = glyph.V0;
            data[offset++] = rf;
            data[offset++] = gf;
            data[offset++] = bf;

            data[offset++] = x1;
            data[offset++] = y1;
            data[offset++] = glyph.U1;
            data[offset++] = glyph.V1;
            data[offset++] = rf;
            data[offset++] = gf;
            data[offset++] = bf;

            data[offset++] = x0;
            data[offset++] = y1;
            data[offset++] = glyph.U0;
            data[offset++] = glyph.V1;
            data[offset++] = rf;
            data[offset++] = gf;
            data[offset++] = bf;
        }

        private void PumpEvents()
        {
            while (SDL.SDL_PollEvent(out SDL.SDL_Event e) == 1)
            {
                if (e.type == SDL.SDL_EventType.SDL_QUIT)
                    _quitRequested = true;
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

        private static string ResolveFontPath(string? fontPath)
        {
            if (!string.IsNullOrWhiteSpace(fontPath) && File.Exists(fontPath))
                return fontPath;

            string fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            string[] candidates =
            {
                "CascadiaMono.ttf",
                "CascadiaCode.ttf",
                "Consola.ttf",
                "Consolas.ttf"
            };

            foreach (string candidate in candidates)
            {
                string path = Path.Combine(fontsDir, candidate);
                if (File.Exists(path))
                    return path;
            }

            throw new FileNotFoundException("No monospace font found. Provide a TTF path (e.g. CascadiaMono.ttf).", fontPath ?? string.Empty);
        }

        private sealed class SdlBindingsContext : IBindingsContext
        {
            public IntPtr GetProcAddress(string procName)
                => SDL.SDL_GL_GetProcAddress(procName);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _atlas?.Dispose();
            _atlas = null;

            if (_bgProgram != 0) GL.DeleteProgram(_bgProgram);
            if (_glyphProgram != 0) GL.DeleteProgram(_glyphProgram);
            if (_bgVbo != 0) GL.DeleteBuffer(_bgVbo);
            if (_glyphVbo != 0) GL.DeleteBuffer(_glyphVbo);
            if (_bgVao != 0) GL.DeleteVertexArray(_bgVao);
            if (_glyphVao != 0) GL.DeleteVertexArray(_glyphVao);

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

        private const string BackgroundVertexShader = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec3 aColor;

uniform vec2 uResolution;

out vec3 vColor;

void main()
{
    vec2 zeroToOne = aPos / uResolution;
    vec2 clip = zeroToOne * 2.0 - 1.0;
    gl_Position = vec4(clip.x, 1.0 - clip.y, 0.0, 1.0);
    vColor = aColor;
}
";

        private const string BackgroundFragmentShader = @"#version 330 core
in vec3 vColor;
out vec4 FragColor;

void main()
{
    FragColor = vec4(vColor, 1.0);
}
";

        private const string GlyphVertexShader = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec3 aColor;

uniform vec2 uResolution;

out vec2 vUv;
out vec3 vColor;

void main()
{
    vec2 zeroToOne = aPos / uResolution;
    vec2 clip = zeroToOne * 2.0 - 1.0;
    gl_Position = vec4(clip.x, 1.0 - clip.y, 0.0, 1.0);
    vUv = aUv;
    vColor = aColor;
}
";

        private const string GlyphFragmentShader = @"#version 330 core
in vec2 vUv;
in vec3 vColor;
out vec4 FragColor;

uniform sampler2D uAtlas;

void main()
{
    float a = texture(uAtlas, vUv).r;
    FragColor = vec4(vColor * a, a);
}
";
    }
}
