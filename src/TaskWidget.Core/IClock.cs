namespace TaskWidget.Core;

public interface IClock
{
    DateTimeOffset Now { get; }

    void Schedule(TimeSpan delay, Action callback);
    void Cancel();
}
