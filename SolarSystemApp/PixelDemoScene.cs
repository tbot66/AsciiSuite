using System;
using AsciiEngine;

namespace SolarSystemApp
{
    public sealed class PixelDemoScene : IPixelApp
    {
        private float _angle;

        public void Init(PixelEngineContext ctx)
        {
            ctx.Renderer.Clear(Colors.Black);
        }

        public void Update(PixelEngineContext ctx)
        {
            _angle += (float)ctx.DeltaTime;
        }

        public void Draw(PixelEngineContext ctx)
        {
            PixelRenderer renderer = ctx.Renderer;
            renderer.Clear(Color.FromRgb(12, 12, 18));

            int rectW = Math.Max(8, renderer.Width / 6);
            int rectH = Math.Max(8, renderer.Height / 6);

            int x = (int)((Math.Sin(_angle) * 0.5f + 0.5f) * (renderer.Width - rectW));
            int y = (int)((Math.Cos(_angle * 0.9f) * 0.5f + 0.5f) * (renderer.Height - rectH));

            renderer.FillRect(x, y, rectW, rectH, Color.FromRgb(92, 180, 255));
            renderer.DrawLine(0, 0, renderer.Width - 1, renderer.Height - 1, Color.FromRgb(255, 180, 80));
        }
    }
}
