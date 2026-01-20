using System;

namespace AsciiEngine
{
    public static class AsciiRunner
    {
        // fpsCap: set to 0 for uncapped (not recommended in console)
        public static void Run(IAsciiApp app, int fpsCap)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            // Cache this so we don't branch-check every frame.
            bool capFps = fpsCap > 0;
            if (fpsCap < 0) fpsCap = 0;

            TerminalSession term = new TerminalSession();
            try
            {
                (int w, int h) = GetInitialSize();
                ConsoleRenderer renderer = CreateRenderer(w, h);
                InputState input = new InputState();

                EngineContext ctx = new EngineContext(term, renderer, input);

                Diagnostics.Log(AsciiCapabilities.DescribePresenter("ConsoleRenderer (ANSI)", renderer.Width, renderer.Height));
                Diagnostics.Log($"[AsciiEngine] FrameBuffer: size={renderer.Width}x{renderer.Height}, bufferLen={renderer.BufferLength}.");

                app.Init(ctx);

                int newW = w;
                int newH = h;

                while (!term.ExitRequested)
                {
                    // Frame start: compute dt + resize detect
                    double dt;
                    bool resized = term.BeginFrame(out dt, out newW, out newH);
                    ctx.DeltaTime = dt;
                    ctx.Time += dt;

                    // Input: update down/pressed/released
                    term.PollInput(input);

                    // Resize handling: recreate renderer buffers and re-init app (so it can relayout)
                    if (resized)
                    {
                        ctx.ReplaceRenderer(CreateRenderer(newW, newH));
                        Diagnostics.Log($"[AsciiEngine] Resize applied: renderer={ctx.Width}x{ctx.Height}, bufferLen={ctx.Renderer.BufferLength}, resized={resized}.");
                        app.Init(ctx);
                    }

                    app.Update(ctx);
                    app.Draw(ctx);

                    ctx.Renderer.Present();

                    if (capFps)
                        term.SleepToMaintainFps(fpsCap);
                }
            }
            finally
            {
                term.Dispose();
            }
        }

        // Windowed: presenter drives output; renderer buffers resize only when caller requests.
        public static void Run(IAsciiApp app, int fpsCap, IFramePresenter presenter)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (presenter == null) throw new ArgumentNullException(nameof(presenter));

            bool capFps = fpsCap > 0;
            if (fpsCap < 0) fpsCap = 0;

            TerminalSession term = new TerminalSession();
            try
            {
                (int w, int h) = GetInitialSize();
                ConsoleRenderer renderer = CreateRenderer(w, h);
                InputState input = new InputState();

                EngineContext ctx = new EngineContext(term, renderer, input);
                Diagnostics.Log(AsciiCapabilities.DescribePresenter(presenter.GetType().Name, renderer.Width, renderer.Height));
                Diagnostics.Log($"[AsciiEngine] FrameBuffer: size={renderer.Width}x{renderer.Height}, bufferLen={renderer.BufferLength}.");
                app.Init(ctx);

                while (!term.ExitRequested)
                {
                    double dt;
                    int unusedW;
                    int unusedH;
                    term.BeginFrame(out dt, out unusedW, out unusedH);
                    ctx.DeltaTime = dt;
                    ctx.Time += dt;

                    term.PollInput(input);

                    app.Update(ctx);
                    app.Draw(ctx);

                    presenter.Present(ctx.Renderer);

                    if (capFps)
                        term.SleepToMaintainFps(fpsCap);
                }
            }
            finally
            {
                presenter.Dispose();
                term.Dispose();
            }
        }

        private static (int w, int h) GetInitialSize()
        {
            int ww;
            int wh;
            try
            {
                ww = Console.WindowWidth;
                wh = Console.WindowHeight;
            }
            catch
            {
                // Fallback for redirected output / unusual terminals.
                ww = 80;
                wh = 25;
            }

            return AsciiSizing.ClampDimensions(ww, wh);
        }

        private static ConsoleRenderer CreateRenderer(int w, int h)
            => new ConsoleRenderer(w, h);
    }
}
