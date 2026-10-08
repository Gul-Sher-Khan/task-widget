namespace TaskWidget.Core;

public sealed class SystemClock : IClock
{
    readonly object gate = new();
    readonly List<Timer> timers = [];

public DateTimeOffset Now => DateTimeOffset.UtcNow;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var context = SynchronizationContext.Current;
        var fired = 0;
        Timer timer = null!;
        timer = new Timer(_ =>
        {
            lock (gate)
                timers.Remove(timer);
            if (Interlocked.Exchange(ref fired, 1) != 0)
                return;
            if (context is null)
                callback();
            else
                context.Post(_ => callback(), null);
        }, null, delay, Timeout.InfiniteTimeSpan);
        lock (gate)
            timers.Add(timer);
        return new Handle(this, timer, () => Interlocked.Exchange(ref fired, 1));
    }

    public void Cancel()
    {
        List<Timer> copy;
        lock (gate)
        {
            copy = [.. timers];
            timers.Clear();
        }

        foreach (var timer in copy)
            timer.Dispose();
    }

    void Cancel(Timer timer, Action markFired)
    {
        markFired();
        lock (gate)
            timers.Remove(timer);
        timer.Dispose();
    }

    sealed class Handle(SystemClock clock, Timer timer, Action markFired) : IDisposable
    {
        public void Dispose() => clock.Cancel(timer, markFired);
    }
}
