using System;
using System.IO;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using SDL2;

namespace AsciiEngine
{
    public sealed class SdlGlPresenter : IFramePresenter, IInputSource
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

            SDL.SDL_GL_GetDrawableSize(_window, out int drawableW, out int drawableH);
            if (drawableW <= 0 || drawableH <= 0)
                return;

            GL.Viewport(0, 0, drawableW, drawableH);
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            GetRenderMetrics(src, drawableW, drawableH, out float originX, out float originY, out float cellW, out float cellH);

            RenderBackground(src, drawableW, drawableH, originX, originY, cellW, cellH);
            RenderGlyphs(src, drawableW, drawableH, originX, originY, cellW, cellH);

            SDL.SDL_GL_SwapWindow(_window);
        }

        public void PumpEvents(InputState input)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SdlGlPresenter));
            if (input == null) throw new ArgumentNullException(nameof(input));

            while (SDL.SDL_PollEvent(out SDL.SDL_Event e) == 1)
            {
                switch (e.type)
                {
                    case SDL.SDL_EventType.SDL_QUIT:
                        _quitRequested = true;
                        input.RequestQuit();
                        break;
                    case SDL.SDL_EventType.SDL_KEYDOWN:
                        HandleKeyDown(input, e.key);
                        break;
                    case SDL.SDL_EventType.SDL_KEYUP:
                        HandleKeyUp(input, e.key);
                        break;
                    case SDL.SDL_EventType.SDL_MOUSEMOTION:
                        input.OnMouseMove(e.motion.x, e.motion.y, e.motion.xrel, e.motion.yrel);
                        break;
                    case SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
                        if (TryMapMouseButton(e.button.button, out MouseButton downButton))
                            input.OnMouseButtonDown(downButton);
                        break;
                    case SDL.SDL_EventType.SDL_MOUSEBUTTONUP:
                        if (TryMapMouseButton(e.button.button, out MouseButton upButton))
                            input.OnMouseButtonUp(upButton);
                        break;
                }
            }
        }

        private void HandleKeyDown(InputState input, SDL.SDL_KeyboardEvent keyEvent)
        {
            UpdateModifiers(input, keyEvent.keysym.mod);
            if (TryMapKey(keyEvent.keysym.sym, out ConsoleKey key))
            {
                input.OnKeyDown(key);
                if (key == ConsoleKey.Escape)
                {
                    _quitRequested = true;
                    input.RequestQuit();
                }
            }
        }

        private static void HandleKeyUp(InputState input, SDL.SDL_KeyboardEvent keyEvent)
        {
            UpdateModifiers(input, keyEvent.keysym.mod);
            if (TryMapKey(keyEvent.keysym.sym, out ConsoleKey key))
                input.OnKeyUp(key);
        }

        private static void UpdateModifiers(InputState input, SDL.SDL_Keymod mod)
        {
            bool shift = (mod & SDL.SDL_Keymod.KMOD_SHIFT) != 0;
            bool alt = (mod & SDL.SDL_Keymod.KMOD_ALT) != 0;
            bool control = (mod & SDL.SDL_Keymod.KMOD_CTRL) != 0;
            input.SetModifiers(shift, alt, control);
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
                SDL.SDL_WindowFlags.SDL_WINDOW_OPENGL | SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL.SDL_WindowFlags.SDL_WINDOW_ALLOW_HIGHDPI);

            if (_window == IntPtr.Zero)
                throw new InvalidOperationException($"SDL_CreateWindow failed: {SDL.SDL_GetError()}");

            _glContext = SDL.SDL_GL_CreateContext(_window);
            if (_glContext == IntPtr.Zero)
                throw new InvalidOperationException($"SDL_GL_CreateContext failed: {SDL.SDL_GetError()}");

            SDL.SDL_GL_MakeCurrent(_window, _glContext);
            SDL.SDL_GL_SetSwapInterval(1);
            GL.LoadBindings(new SdlBindingsContext());

            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            string resolvedFont = ResolveFontPath(_fontPath);
            _atlas = new GlyphAtlas(resolvedFont, _fontPixelHeight, _padding);

            SDL.SDL_SetWindowSize(_window, src.Width * _atlas.CellWidth, src.Height * _atlas.CellHeight);
            SDL.SDL_GL_GetDrawableSize(_window, out int drawableW, out int drawableH);
            GL.Viewport(0, 0, drawableW, drawableH);

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

        private void RenderBackground(ConsoleRenderer src, int windowW, int windowH, float originX, float originY, float cellW, float cellH)
        {
            int cellCount = src.Width * src.Height;
            int requiredFloats = cellCount * 6 * FloatsPerBgVertex;
            EnsureCapacity(ref _bgVertices, requiredFloats);

            ReadOnlySpan<Color> bg = src.Background;
            int offset = 0;

            for (int y = 0; y < src.Height; y++)
            {
                float y0 = originY + y * cellH;
                float y1 = y0 + cellH;
                int row = y * src.Width;

                for (int x = 0; x < src.Width; x++)
                {
                    int idx = row + x;
                    float x0 = originX + x * cellW;
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

        private void RenderGlyphs(ConsoleRenderer src, int windowW, int windowH, float originX, float originY, float cellW, float cellH)
        {
            if (_atlas == null)
                return;

            int cellCount = src.Width * src.Height;
            int requiredFloats = cellCount * 6 * FloatsPerGlyphVertex;
            EnsureCapacity(ref _glyphVertices, requiredFloats);

            ReadOnlySpan<char> chars = src.Chars;
            ReadOnlySpan<Color> fg = src.Foreground;
            int offset = 0;

            for (int y = 0; y < src.Height; y++)
            {
                float y0 = originY + y * cellH;
                float y1 = y0 + cellH;
                int row = y * src.Width;

                for (int x = 0; x < src.Width; x++)
                {
                    int idx = row + x;
                    float x0 = originX + x * cellW;
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

        private void GetRenderMetrics(ConsoleRenderer src, int windowW, int windowH, out float originX, out float originY, out float cellW, out float cellH)
        {
            float baseCellW = _atlas?.CellWidth ?? _fontPixelHeight;
            float baseCellH = _atlas?.CellHeight ?? _fontPixelHeight;

            float contentW = src.Width * baseCellW;
            float contentH = src.Height * baseCellH;

            float scale = MathF.Min(windowW / contentW, windowH / contentH);
            float renderW = contentW * scale;
            float renderH = contentH * scale;

            originX = (windowW - renderW) * 0.5f;
            originY = (windowH - renderH) * 0.5f;
            cellW = renderW / src.Width;
            cellH = renderH / src.Height;
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

        private static bool TryMapMouseButton(uint sdlButton, out MouseButton button)
        {
            switch (sdlButton)
            {
                case SDL.SDL_BUTTON_LEFT:
                    button = MouseButton.Left;
                    return true;
                case SDL.SDL_BUTTON_MIDDLE:
                    button = MouseButton.Middle;
                    return true;
                case SDL.SDL_BUTTON_RIGHT:
                    button = MouseButton.Right;
                    return true;
                case SDL.SDL_BUTTON_X1:
                    button = MouseButton.X1;
                    return true;
                case SDL.SDL_BUTTON_X2:
                    button = MouseButton.X2;
                    return true;
                default:
                    button = MouseButton.Left;
                    return false;
            }
        }

        private static bool TryMapKey(SDL.SDL_Keycode keycode, out ConsoleKey key)
        {
            switch (keycode)
            {
                case SDL.SDL_Keycode.SDLK_BACKSPACE:
                    key = ConsoleKey.Backspace;
                    return true;
                case SDL.SDL_Keycode.SDLK_TAB:
                    key = ConsoleKey.Tab;
                    return true;
                case SDL.SDL_Keycode.SDLK_RETURN:
                    key = ConsoleKey.Enter;
                    return true;
                case SDL.SDL_Keycode.SDLK_ESCAPE:
                    key = ConsoleKey.Escape;
                    return true;
                case SDL.SDL_Keycode.SDLK_SPACE:
                    key = ConsoleKey.Spacebar;
                    return true;
                case SDL.SDL_Keycode.SDLK_UP:
                    key = ConsoleKey.UpArrow;
                    return true;
                case SDL.SDL_Keycode.SDLK_DOWN:
                    key = ConsoleKey.DownArrow;
                    return true;
                case SDL.SDL_Keycode.SDLK_LEFT:
                    key = ConsoleKey.LeftArrow;
                    return true;
                case SDL.SDL_Keycode.SDLK_RIGHT:
                    key = ConsoleKey.RightArrow;
                    return true;
                case SDL.SDL_Keycode.SDLK_F1:
                    key = ConsoleKey.F1;
                    return true;
                case SDL.SDL_Keycode.SDLK_F2:
                    key = ConsoleKey.F2;
                    return true;
                case SDL.SDL_Keycode.SDLK_F3:
                    key = ConsoleKey.F3;
                    return true;
                case SDL.SDL_Keycode.SDLK_F4:
                    key = ConsoleKey.F4;
                    return true;
                case SDL.SDL_Keycode.SDLK_F5:
                    key = ConsoleKey.F5;
                    return true;
                case SDL.SDL_Keycode.SDLK_F6:
                    key = ConsoleKey.F6;
                    return true;
                case SDL.SDL_Keycode.SDLK_F7:
                    key = ConsoleKey.F7;
                    return true;
                case SDL.SDL_Keycode.SDLK_F8:
                    key = ConsoleKey.F8;
                    return true;
                case SDL.SDL_Keycode.SDLK_F9:
                    key = ConsoleKey.F9;
                    return true;
                case SDL.SDL_Keycode.SDLK_F10:
                    key = ConsoleKey.F10;
                    return true;
                case SDL.SDL_Keycode.SDLK_F11:
                    key = ConsoleKey.F11;
                    return true;
                case SDL.SDL_Keycode.SDLK_F12:
                    key = ConsoleKey.F12;
                    return true;
                case SDL.SDL_Keycode.SDLK_0:
                    key = ConsoleKey.D0;
                    return true;
                case SDL.SDL_Keycode.SDLK_1:
                    key = ConsoleKey.D1;
                    return true;
                case SDL.SDL_Keycode.SDLK_2:
                    key = ConsoleKey.D2;
                    return true;
                case SDL.SDL_Keycode.SDLK_3:
                    key = ConsoleKey.D3;
                    return true;
                case SDL.SDL_Keycode.SDLK_4:
                    key = ConsoleKey.D4;
                    return true;
                case SDL.SDL_Keycode.SDLK_5:
                    key = ConsoleKey.D5;
                    return true;
                case SDL.SDL_Keycode.SDLK_6:
                    key = ConsoleKey.D6;
                    return true;
                case SDL.SDL_Keycode.SDLK_7:
                    key = ConsoleKey.D7;
                    return true;
                case SDL.SDL_Keycode.SDLK_8:
                    key = ConsoleKey.D8;
                    return true;
                case SDL.SDL_Keycode.SDLK_9:
                    key = ConsoleKey.D9;
                    return true;
                case SDL.SDL_Keycode.SDLK_a:
                    key = ConsoleKey.A;
                    return true;
                case SDL.SDL_Keycode.SDLK_b:
                    key = ConsoleKey.B;
                    return true;
                case SDL.SDL_Keycode.SDLK_c:
                    key = ConsoleKey.C;
                    return true;
                case SDL.SDL_Keycode.SDLK_d:
                    key = ConsoleKey.D;
                    return true;
                case SDL.SDL_Keycode.SDLK_e:
                    key = ConsoleKey.E;
                    return true;
                case SDL.SDL_Keycode.SDLK_f:
                    key = ConsoleKey.F;
                    return true;
                case SDL.SDL_Keycode.SDLK_g:
                    key = ConsoleKey.G;
                    return true;
                case SDL.SDL_Keycode.SDLK_h:
                    key = ConsoleKey.H;
                    return true;
                case SDL.SDL_Keycode.SDLK_i:
                    key = ConsoleKey.I;
                    return true;
                case SDL.SDL_Keycode.SDLK_j:
                    key = ConsoleKey.J;
                    return true;
                case SDL.SDL_Keycode.SDLK_k:
                    key = ConsoleKey.K;
                    return true;
                case SDL.SDL_Keycode.SDLK_l:
                    key = ConsoleKey.L;
                    return true;
                case SDL.SDL_Keycode.SDLK_m:
                    key = ConsoleKey.M;
                    return true;
                case SDL.SDL_Keycode.SDLK_n:
                    key = ConsoleKey.N;
                    return true;
                case SDL.SDL_Keycode.SDLK_o:
                    key = ConsoleKey.O;
                    return true;
                case SDL.SDL_Keycode.SDLK_p:
                    key = ConsoleKey.P;
                    return true;
                case SDL.SDL_Keycode.SDLK_q:
                    key = ConsoleKey.Q;
                    return true;
                case SDL.SDL_Keycode.SDLK_r:
                    key = ConsoleKey.R;
                    return true;
                case SDL.SDL_Keycode.SDLK_s:
                    key = ConsoleKey.S;
                    return true;
                case SDL.SDL_Keycode.SDLK_t:
                    key = ConsoleKey.T;
                    return true;
                case SDL.SDL_Keycode.SDLK_u:
                    key = ConsoleKey.U;
                    return true;
                case SDL.SDL_Keycode.SDLK_v:
                    key = ConsoleKey.V;
                    return true;
                case SDL.SDL_Keycode.SDLK_w:
                    key = ConsoleKey.W;
                    return true;
                case SDL.SDL_Keycode.SDLK_x:
                    key = ConsoleKey.X;
                    return true;
                case SDL.SDL_Keycode.SDLK_y:
                    key = ConsoleKey.Y;
                    return true;
                case SDL.SDL_Keycode.SDLK_z:
                    key = ConsoleKey.Z;
                    return true;
                case SDL.SDL_Keycode.SDLK_EQUALS:
                case SDL.SDL_Keycode.SDLK_PLUS:
                    key = ConsoleKey.OemPlus;
                    return true;
                case SDL.SDL_Keycode.SDLK_MINUS:
                    key = ConsoleKey.OemMinus;
                    return true;
                case SDL.SDL_Keycode.SDLK_KP_0:
                    key = ConsoleKey.NumPad0;
                    return true;
                case SDL.SDL_Keycode.SDLK_KP_1:
                    key = ConsoleKey.NumPad1;
                    return true;
                case SDL.SDL_Keycode.SDLK_KP_2:
                    key = ConsoleKey.NumPad2;
                    return true;
                case SDL.SDL_Keycode.SDLK_KP_3:
                    key = ConsoleKey.NumPad3;
                    return true;
                case SDL.SDL_Keycode.SDLK_KP_4:
                    key = ConsoleKey.NumPad4;
                    return true;
                case SDL.SDL_Keycode.SDLK_KP_5:
                    key = ConsoleKey.NumPad5;
                    return true;
                case SDL.SDL_Keycode.SDLK_KP_6:
                    key = ConsoleKey.NumPad6;
                    return true;
                case SDL.SDL_Keycode.SDLK_KP_7:
                    key = ConsoleKey.NumPad7;
                    return true;
                case SDL.SDL_Keycode.SDLK_KP_8:
                    key = ConsoleKey.NumPad8;
                    return true;
                case SDL.SDL_Keycode.SDLK_KP_9:
                    key = ConsoleKey.NumPad9;
                    return true;
                default:
                    key = ConsoleKey.NoName;
                    return false;
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
    vUv = vec2(aUv.x, 1.0 - aUv.y);
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
