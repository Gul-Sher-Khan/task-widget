namespace TaskWidget.Core;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    IDisposable Schedule(TimeSpan delay, Action callback);
    void Cancel();
}
