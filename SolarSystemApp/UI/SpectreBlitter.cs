using AsciiEngine;
using SolarSystemApp.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.IO;

namespace SolarSystemApp.UI
{
    internal static class SpectreBlitter
    {
        public static string RenderToString(IRenderable renderable, int width)
        {
            width = Math.Max(10, width);

            var sw = new StringWriter();
            var settings = new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(sw),
            };

            var console = AnsiConsole.Create(settings);
            console.Profile.Width = width;
            console.Write(renderable);
            return sw.ToString();
        }

        public static void BlitPanel(
            ConsoleRenderer r,
            int x0, int y0,
            int w, int h,
            string text,
            AsciiEngine.Color fg,
            AsciiEngine.Color bg)
        {
            if (w <= 0 || h <= 0) return;

            r.FillRect(x0, y0, w, h, ' ', bg, bg, RenderZ.UI_BG);

            if (string.IsNullOrEmpty(text)) return;

            int x = 0, y = 0;
            int cx = x0, cy = y0;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\r') continue;

                if (ch == '\n')
                {
                    y++;
                    if (y >= h) break;
                    x = 0;
                    cx = x0;
                    cy = y0 + y;
                    continue;
                }

                if (x < w)
                    r.Set(cx, cy, ch, fg, bg, z: RenderZ.UI_TEXT);

                x++;
                cx++;

                if (x >= w)
                {
                    y++;
                    if (y >= h) break;
                    x = 0;
                    cx = x0;
                    cy = y0 + y;
                }
            }
        }
    }
}
