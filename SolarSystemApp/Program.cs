using System;
using AsciiEngine;
using SolarSystemApp;

internal static class Program
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8; // ✅ Spectre box chars render correctly

        if (IsAsciiMode(args))
        {
            RunAscii();
            return;
        }

        if (HasFlag(args, "--pixel-tests"))
        {
            RunPixelTests();
            return;
        }

        RunPixelGame();
    }

    private static void RunPixelGame()
    {
        using IFramePresenter<PixelRenderer> presenter = new SdlGlPixelPresenter("Solar System");
        PixelRunner.Run(new SolarSystemPixelScene(), fpsCap: 60, presenter);
    }

    private static void RunPixelTests()
    {
        using IFramePresenter<PixelRenderer> presenter = new SdlGlPixelPresenter("Pixel Render Tests");
        PixelRunner.Run(new PixelRenderTests(), fpsCap: 60, presenter);
    }

    private static void RunAscii()
    {
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
            case "auto":
            default:
                if (TryRunSdlScene())
                    return;

                AsciiRunner.Run(new SolarSystemScene(), fpsCap: 30);
                return;
        }
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

    private static bool IsAsciiMode(string[] args)
    {
        if (HasFlag(args, "--ascii"))
            return true;

        string? env = Environment.GetEnvironmentVariable("ASCII_MODE");
        if (string.IsNullOrWhiteSpace(env))
            return false;

        env = env.Trim();
        return env == "1" || env.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFlag(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
