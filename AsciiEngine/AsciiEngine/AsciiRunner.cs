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
                int ww, wh;
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

                int w = Math.Max(20, Math.Min(160, ww));
                int h = Math.Max(10, Math.Min(60, wh));

                ConsoleRenderer renderer = new ConsoleRenderer(w, h);
                InputState input = new InputState();

                EngineContext ctx = new EngineContext(term, renderer, input);

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
                        ConsoleRenderer newRenderer2 = new ConsoleRenderer(newW, newH);
                        ctx.ReplaceRenderer(newRenderer2);
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
    }
}
