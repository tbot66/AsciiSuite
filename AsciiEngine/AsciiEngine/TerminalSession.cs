using System;
using System.Diagnostics;
using System.Threading;
using System.Text;

namespace AsciiEngine
{
    public sealed class TerminalSession : IDisposable
    {
        public bool ExitRequested { get; private set; }

        private readonly int _initialBufferW;
        private readonly int _initialBufferH;
        private readonly bool _initialCursorVisible;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastTime;

        private readonly Stopwatch _sleepTimer = Stopwatch.StartNew();
        private double _nextFrameTime;

        private int _lastW;
        private int _lastH;

        public TerminalSession()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.InputEncoding = Encoding.UTF8;
            }
            catch { }

            // These can throw if output is redirected; keep best-effort.
            try { _initialBufferW = Console.BufferWidth; } catch { _initialBufferW = 0; }
            try { _initialBufferH = Console.BufferHeight; } catch { _initialBufferH = 0; }
            try { _initialCursorVisible = Console.CursorVisible; } catch { _initialCursorVisible = true; }

            Ansi.EnableVirtualTerminalIfPossible();
            TryMatchBufferToWindow();

            try { Console.CursorVisible = false; } catch { }

            Ansi.Write(Ansi.EnterAlternateBuffer);
            Ansi.Write(Ansi.HideCursor);
            Ansi.Write(Ansi.ClearScreen);
            Ansi.Write(Ansi.Home);
            Ansi.Write(Ansi.Reset);

            _lastTime = _clock.Elapsed.TotalSeconds;

            try
            {
                _lastW = Console.WindowWidth;
                _lastH = Console.WindowHeight;
            }
            catch
            {
                _lastW = 80;
                _lastH = 25;
            }
        }

        // Returns: resized? + dt + new size (if resized)
        public bool BeginFrame(out double dt, out int newW, out int newH)
        {
            double now = _clock.Elapsed.TotalSeconds;
            dt = now - _lastTime;
            _lastTime = now;

            // Clamp dt to avoid huge jumps if debugger pauses
            if (dt < 0) dt = 0;
            if (dt > 0.25) dt = 0.25;

            newW = _lastW;
            newH = _lastH;

            int w, h;
            try
            {
                w = Console.WindowWidth;
                h = Console.WindowHeight;
            }
            catch
            {
                w = _lastW;
                h = _lastH;
            }

            bool resized = (w != _lastW) || (h != _lastH);
            if (resized)
            {
                _lastW = w;
                _lastH = h;

                TryMatchBufferToWindow();

                newW = Math.Max(1, w);
                newH = Math.Max(1, h);

                Diagnostics.Log($"[AsciiEngine] Resize detected: window={w}x{h}, engine={newW}x{newH}, buffer={GetBufferSizeText()}");
            }

            return resized;
        }

        public void PollInput(InputState input)
        {
            input.BeginFrame();

            try
            {
                while (Console.KeyAvailable)
                {
                    ConsoleKey key = Console.ReadKey(true).Key;

                    if (key == ConsoleKey.Escape)
                    {
                        ExitRequested = true;
                        break;
                    }

                    input.OnKey(key);
                }
            }
            catch
            {
                // If input is redirected / unavailable, just behave like "no input this frame".
            }

            input.EndFrame();
        }

        public void SleepToMaintainFps(int fps)
        {
            double frame = 1.0 / fps;

            double now = _sleepTimer.Elapsed.TotalSeconds;
            if (_nextFrameTime == 0) _nextFrameTime = now + frame;

            double remaining = _nextFrameTime - now;
            if (remaining > 0.001)
            {
                int ms = (int)(remaining * 1000);
                if (ms > 1) Thread.Sleep(ms - 1);
            }

            // Keep your existing behavior (tight finish), but avoid hammering as hard.
            while (true)
            {
                double t = _sleepTimer.Elapsed.TotalSeconds;
                if (t >= _nextFrameTime) break;
                Thread.SpinWait(64);
            }

            _nextFrameTime += frame;
        }

        private static void TryMatchBufferToWindow()
        {
            try
            {
                int w = Console.WindowWidth;
                int h = Console.WindowHeight;
                Console.SetBufferSize(w, h);
            }
            catch { }
        }

        private static string GetBufferSizeText()
        {
            try
            {
                return $"{Console.BufferWidth}x{Console.BufferHeight}";
            }
            catch
            {
                return "unknown";
            }
        }

        public void Dispose()
        {
            Ansi.Write(Ansi.ShowCursor);
            Ansi.Write(Ansi.ExitAlternateBuffer);
            Ansi.Write(Ansi.Reset);

            try { Console.CursorVisible = _initialCursorVisible; } catch { }
            try { if (_initialBufferW > 0 && _initialBufferH > 0) Console.SetBufferSize(_initialBufferW, _initialBufferH); } catch { }
        }
    }
}
