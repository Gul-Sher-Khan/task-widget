namespace TaskWidget.Core;

public sealed class TaskRow
{
    public TaskRow(string title, Priority priority, Effort effort, string details, string capture = "", bool pending = false)
    {
        Title = title;
        Priority = priority;
        Effort = effort;
        Details = details;
        Capture = capture;
        IsPending = pending;
        PendingText = pending ? "Interpreting…" : "";
    }

    public string Title { get; }
    public Priority Priority { get; }
    public Effort Effort { get; }
    public string Details { get; }
    public string Capture { get; }
    public bool IsPending { get; }
    public string PendingText { get; }
}
