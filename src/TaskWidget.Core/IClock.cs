namespace TaskWidget.Core;

public interface IClock
{
    DateTimeOffset Now { get; }
    DateTimeOffset UtcNow { get; }
    IDisposable Schedule(TimeSpan delay, Action callback);
    void Cancel();
}
