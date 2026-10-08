namespace TaskWidget.Core;

public interface IClock
{
    DateTimeOffset Now { get; }
    DateTimeOffset UtcNow { get; }
    long Schedule(TimeSpan delay, Action callback);
    void Cancel(long id);
}
