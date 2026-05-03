using AsciiEngine;
using SolarSystemApp.Rendering;
using Spectre.Console.Rendering;

namespace SolarSystemApp.UI
{
    internal sealed class UIManager
    {
        private readonly List<UIPanel> _panels = new List<UIPanel>();
        private readonly Stack<UIPanel> _modalStack = new Stack<UIPanel>();

        public PanelBounds ViewportRect { get; private set; }

        private double _renderTimer = 0.0;
        private const double RenderInterval = 0.10;
        private readonly Dictionary<UIPanel, string> _renderCache = new Dictionary<UIPanel, string>();

        public void AddPanel(UIPanel panel)
        {
            _panels.Add(panel);
            _renderCache[panel] = "";
        }

        public void PushModal(UIPanel panel)
        {
            panel.Visible = true;
            panel.OnShow();
            _modalStack.Push(panel);
        }

        public void PopModal()
        {
            if (_modalStack.Count > 0)
            {
                var panel = _modalStack.Pop();
                panel.Visible = false;
                panel.OnHide();
            }
        }

        public bool IsModalOpen => _modalStack.Count > 0;

        public void Layout(int screenW, int screenH)
        {
            int top = 0, bottom = 0, left = 0, right = 0;

            foreach (var p in _panels)
            {
                if (!p.Visible || p.Modal) continue;

                switch (p.Anchor)
                {
                    case PanelAnchor.Top:
                        int th = p.RequestedHeight > 0 ? p.RequestedHeight : 3;
                        p.Bounds = new PanelBounds(0, top, screenW, th);
                        top += th;
                        break;

                    case PanelAnchor.Bottom:
                        int bh = p.RequestedHeight > 0 ? p.RequestedHeight : 5;
                        p.Bounds = new PanelBounds(0, screenH - bottom - bh, screenW, bh);
                        bottom += bh;
                        break;

                    case PanelAnchor.Left:
                        int lw = p.RequestedWidth > 0 ? p.RequestedWidth : 20;
                        int lh = screenH - top - bottom;
                        p.Bounds = new PanelBounds(left, top, lw, lh);
                        left += lw;
                        break;

                    case PanelAnchor.Right:
                        int rw = p.RequestedWidth > 0 ? Math.Min(p.RequestedWidth, screenW / 3) : Math.Min(30, screenW / 3);
                        int rh = screenH - top - bottom;
                        p.Bounds = new PanelBounds(screenW - right - rw, top, rw, rh);
                        right += rw;
                        break;

                    case PanelAnchor.Center:
                        int cw = p.RequestedWidth > 0 ? p.RequestedWidth : 50;
                        int ch = p.RequestedHeight > 0 ? p.RequestedHeight : 20;
                        p.Bounds = new PanelBounds(
                            (screenW - cw) / 2,
                            (screenH - ch) / 2,
                            cw, ch);
                        break;
                }
            }

            ViewportRect = new PanelBounds(left, top, screenW - left - right, screenH - top - bottom);
        }

        public bool HandleInput(InputState input, IGameContext ctx)
        {
            if (_modalStack.Count > 0)
            {
                var modal = _modalStack.Peek();
                modal.HandleInput(input, ctx);
                return true;
            }

            foreach (var p in _panels)
            {
                if (p.ToggleKey.HasValue && input.WasPressed(p.ToggleKey.Value))
                {
                    if (p.Modal)
                    {
                        if (p.Visible)
                            PopModal();
                        else
                            PushModal(p);
                    }
                    else
                    {
                        p.Visible = !p.Visible;
                        if (p.Visible) p.OnShow(); else p.OnHide();
                    }
                    return true;
                }
            }

            foreach (var p in _panels)
            {
                if (p.Visible && !p.Modal && p.HandleInput(input, ctx))
                    return true;
            }

            return false;
        }

        public void Render(ConsoleRenderer r, IGameContext ctx, double dt)
        {
            _renderTimer += dt;
            bool rebuild = _renderTimer >= RenderInterval;
            if (rebuild) _renderTimer -= RenderInterval;

            if (rebuild)
            {
                var built = new Dictionary<UIPanel, IRenderable>();
                foreach (var p in _panels)
                {
                    if (!p.Visible) continue;
                    built[p] = p.BuildContent(ctx);
                }
                Layout(r.Width, r.Height);

                foreach (var p in _panels)
                {
                    if (!p.Visible || !built.ContainsKey(p)) continue;
                    var b = p.Bounds;
                    if (b.W <= 0 || b.H <= 0) continue;
                    _renderCache[p] = SpectreBlitter.RenderToString(built[p], b.W);
                }
            }

            foreach (var p in _panels)
            {
                if (!p.Visible) continue;

                var b = p.Bounds;
                if (b.W <= 0 || b.H <= 0) continue;

                string cached = _renderCache.TryGetValue(p, out var c) ? c : "";
                SpectreBlitter.BlitPanel(r, b.X, b.Y, b.W, b.H, cached,
                    Colors.BrightWhite, Colors.Black);
            }
        }
    }
}
