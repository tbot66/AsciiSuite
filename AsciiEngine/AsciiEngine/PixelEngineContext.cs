namespace AsciiEngine
{
    public sealed class PixelEngineContext
    {
        public TerminalSession Terminal { get; private set; }
        public PixelRenderer Renderer { get; private set; }
        public InputState Input { get; private set; }

        public double DeltaTime { get; internal set; }
        public double Time { get; internal set; }

        public int Width => Renderer.Width;
        public int Height => Renderer.Height;

        internal PixelEngineContext(TerminalSession terminal, PixelRenderer renderer, InputState input)
        {
            Terminal = terminal;
            Renderer = renderer;
            Input = input;
        }

        internal void ReplaceRenderer(PixelRenderer renderer)
        {
            Renderer = renderer;
        }
    }
}
