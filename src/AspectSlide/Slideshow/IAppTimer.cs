namespace AspectSlide.Slideshow;

public interface IAppTimer : IDisposable { void Arm(TimeSpan delay, Action tick); }

public sealed class SystemAppTimer : IAppTimer
{
    private readonly System.Threading.Timer timer;
    private Action? callback;
    public SystemAppTimer() => timer = new(_ => callback?.Invoke());
    public void Arm(TimeSpan delay, Action tick)
    {
        callback = tick;
        timer.Change(delay, Timeout.InfiniteTimeSpan);
    }
    public void Dispose() => timer.Dispose();
}
