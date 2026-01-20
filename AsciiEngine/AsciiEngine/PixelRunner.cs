using System;

namespace AsciiEngine
{
    public static class PixelRunner
    {
        public static void Run(IPixelApp app, int fpsCap, IFramePresenter<PixelRenderer> presenter, int width = 320, int height = 180)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (presenter == null) throw new ArgumentNullException(nameof(presenter));

            bool capFps = fpsCap > 0;
            if (fpsCap < 0) fpsCap = 0;

            TerminalSession terminal = new TerminalSession();
            try
            {
                PixelRenderer renderer = new PixelRenderer(width, height);
                InputState input = new InputState();
                PixelEngineContext ctx = new PixelEngineContext(terminal, renderer, input);

                Diagnostics.Log(AsciiCapabilities.DescribePresenter(presenter.GetType().Name, renderer.Width, renderer.Height));
                Diagnostics.Log($"[AsciiEngine] PixelBuffer: size={renderer.Width}x{renderer.Height}, bufferLen={renderer.BufferLength}.");

                app.Init(ctx);

                var sdlPresenter = presenter as SdlGlPixelPresenter;

                while (!terminal.ExitRequested)
                {
                    double dt;
                    int unusedW;
                    int unusedH;
                    terminal.BeginFrame(out dt, out unusedW, out unusedH);
                    ctx.DeltaTime = dt;
                    ctx.Time += dt;

                    if (sdlPresenter != null)
                    {
                        input.BeginFrame();
                        sdlPresenter.PollInput(ctx.Renderer, input);
                        input.EndFrame();
                    }
                    else
                    {
                        terminal.PollInput(input);
                    }

                    app.Update(ctx);
                    app.Draw(ctx);

                    presenter.Present(ctx.Renderer);

                    if (sdlPresenter?.QuitRequested == true)
                        break;

                    if (capFps)
                        terminal.SleepToMaintainFps(fpsCap);
                }

            }
            finally
            {
                presenter.Dispose();
                terminal.Dispose();
            }
        }
    }
}
