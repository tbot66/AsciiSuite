namespace AsciiEngine
{
    public interface IFramePresenter<in T> : IDisposable
    {
        void Present(T src);
    }

    public interface IFramePresenter : IDisposable
    {
        void Present(ConsoleRenderer src);
    }
}
