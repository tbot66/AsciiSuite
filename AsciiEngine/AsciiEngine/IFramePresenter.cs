namespace AsciiEngine
{
    public interface IFramePresenter
    {
        void Present(ConsoleRenderer src);
        void Dispose();
    }
}
