namespace AsciiEngine
{
    public interface IAsciiApp
    {
        // Called once after renderer is created (and after resize recreation).
        void Init(EngineContext ctx);

        // Called each frame with dt and input.
        void Update(EngineContext ctx);

        // Called each frame to draw into the renderer.
        void Draw(EngineContext ctx);
    }
}
