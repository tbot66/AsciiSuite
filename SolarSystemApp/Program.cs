using System;
using AsciiEngine;
using SolarSystemApp;

internal static class Program
{
    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8; // ✅ Spectre box chars render correctly
        string? rendererChoice = Environment.GetEnvironmentVariable("ASCII_RENDERER");
        string choice = string.IsNullOrWhiteSpace(rendererChoice) ? "auto" : rendererChoice.Trim().ToLowerInvariant();

        Diagnostics.Log($"[AsciiEngine] Presenter choice: {choice}.");

        switch (choice)
        {
            case "terminal":
                AsciiRunner.Run(new SolarSystemScene(), fpsCap: 30);
                return;
            case "sdl":
                using (IFramePresenter presenter = new SdlGlPresenter())
                    AsciiRunner.Run(new SolarSystemScene(), fpsCap: 30, presenter);
                return;
            case "pixel":
                RunPixelDemo();
                return;
            case "auto":
            default:
                if (TryRunPixelDemo())
                    return;

                if (TryRunSdlScene())
                    return;

                AsciiRunner.Run(new SolarSystemScene(), fpsCap: 30);
                return;
        }
    }

    private static bool TryRunPixelDemo()
    {
        try
        {
            RunPixelDemo();
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"[AsciiEngine] Pixel presenter failed, falling back. {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void RunPixelDemo()
    {
        using IFramePresenter<PixelRenderer> presenter = new SdlGlPixelPresenter();
        PixelRunner.Run(new PixelDemoScene(), fpsCap: 60, presenter);
    }

    private static bool TryRunSdlScene()
    {
        try
        {
            using IFramePresenter presenter = new SdlGlPresenter();
            AsciiRunner.Run(new SolarSystemScene(), fpsCap: 30, presenter);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"[AsciiEngine] SDL presenter failed, falling back. {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
