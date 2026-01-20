namespace AsciiEngine
{
    public sealed class EngineContext
    {
        public TerminalSession Terminal { get; private set; }
        public ConsoleRenderer Renderer { get; private set; }
        public InputState Input { get; private set; }

        public double DeltaTime { get; internal set; }
        public double Time { get; internal set; }

        public int Width { get { return Renderer.Width; } }
        public int Height { get { return Renderer.Height; } }

        internal EngineContext(TerminalSession term, ConsoleRenderer renderer, InputState input)
        {
            Terminal = term;
            Renderer = renderer;
            Input = input;
        }

        internal void ReplaceRenderer(ConsoleRenderer newRenderer)
        {
            Renderer = newRenderer;
        }
    }
}
