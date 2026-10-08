using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskWidget.Core;

public sealed partial class TaskRow : ObservableObject
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

    [ObservableProperty]
    public partial bool IsPending { get; set; }

    [ObservableProperty]
    public partial string PendingText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsWaiting { get; set; }

    [ObservableProperty]
    public partial string Reason { get; set; } = "";

    [ObservableProperty]
    public partial bool LightsDot { get; set; }

    public DateTimeOffset CreatedAt { get; internal set; }
    public int Spoken { get; internal set; }
    public DateTimeOffset? CompletedAt { get; internal set; }
    public DateTimeOffset? DeletedAt { get; internal set; }

    [ObservableProperty]
    public partial bool IsStriking { get; set; }

    [ObservableProperty]
    public partial bool IsDone { get; set; }
}
