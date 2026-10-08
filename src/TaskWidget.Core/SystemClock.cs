namespace TaskWidget.Core;

public sealed class SystemClock : IClock
{
    Timer? timer;

    public void Schedule(TimeSpan delay, Action callback)
    {
        timer?.Dispose();
        timer = new Timer(_ => callback(), null, delay, Timeout.InfiniteTimeSpan);
    }

    public void Cancel()
    {
        timer?.Dispose();
        timer = null;
    }
}
