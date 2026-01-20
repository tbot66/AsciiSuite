namespace AsciiEngine
{
    public interface IFramePresenter : IDisposable
    {
        void Present(ConsoleRenderer src);
    }
}
