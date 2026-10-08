namespace TaskWidget.Core;

public interface IClock
{
    void Schedule(TimeSpan delay, Action callback);
    void Cancel();
}
