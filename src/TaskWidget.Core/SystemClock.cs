namespace TaskWidget.Core;

public sealed class SystemClock : IClock
{
    readonly object gate = new();
    readonly Dictionary<long, Timer> timers = [];
    long next;

    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public long Schedule(TimeSpan delay, Action callback)
    {
        var id = Interlocked.Increment(ref next);
        var timer = new Timer(_ =>
        {
            if (CancelCore(id))
                callback();
        });
        lock (gate)
            timers[id] = timer;
        timer.Change(delay, Timeout.InfiniteTimeSpan);
        return id;
    }

    public void Cancel(long id) => CancelCore(id);

    bool CancelCore(long id)
    {
        Timer? timer;
        lock (gate)
        {
            if (!timers.Remove(id, out timer))
                return false;
        }

        timer.Dispose();
        return true;
    }
}
