using AsciiEngine;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI
{
    internal enum PanelAnchor
    {
        Top,
        Bottom,
        Left,
        Right,
        Center
    }

    internal struct PanelBounds
    {
        public int X, Y, W, H;

        public PanelBounds(int x, int y, int w, int h)
        {
            X = x; Y = y; W = w; H = h;
        }
    }

    internal abstract class UIPanel
    {
        public PanelBounds Bounds { get; set; }
        public bool Visible { get; set; } = true;
        public bool Modal { get; set; } = false;
        public PanelAnchor Anchor { get; set; }
        public ConsoleKey? ToggleKey { get; set; }

        public abstract IRenderable BuildContent(IGameContext ctx);
        public virtual bool HandleInput(InputState input, IGameContext ctx) => false;
        public virtual void OnShow() { }
        public virtual void OnHide() { }

        public int RequestedWidth { get; set; } = 0;
        public int RequestedHeight { get; set; } = 0;
    }
}
