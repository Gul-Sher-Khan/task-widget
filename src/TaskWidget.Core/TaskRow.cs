namespace TaskWidget.Core;

public sealed class TaskRow
{
    public TaskRow(string title, Priority priority, Effort effort, string details)
    {
        Title = title;
        Priority = priority;
        Effort = effort;
        Details = details;
    }

    public string Title { get; }
    public Priority Priority { get; }
    public Effort Effort { get; }
    public string Details { get; }
}
