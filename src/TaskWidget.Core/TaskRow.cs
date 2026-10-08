using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskWidget.Core;

public sealed partial class TaskRow : ObservableObject
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
    public DateTimeOffset CreatedAt { get; internal set; }
    public int Spoken { get; internal set; }
    public DateTimeOffset? CompletedAt { get; internal set; }
    public DateTimeOffset? DeletedAt { get; internal set; }

    [ObservableProperty]
    public partial bool IsStriking { get; set; }

    [ObservableProperty]
    public partial bool IsDone { get; set; }
}
