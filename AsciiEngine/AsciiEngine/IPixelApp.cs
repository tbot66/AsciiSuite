namespace AsciiEngine
{
    public interface IPixelApp
    {
        void Init(PixelEngineContext ctx);
        void Update(PixelEngineContext ctx);
        void Draw(PixelEngineContext ctx);
    }
}
