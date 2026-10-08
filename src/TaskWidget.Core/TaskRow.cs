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

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial Priority Priority { get; set; }

    [ObservableProperty]
    public partial Effort Effort { get; set; }

    [ObservableProperty]
    public partial bool UserSetPriority { get; set; }

    [ObservableProperty]
    public partial bool UserSetEffort { get; set; }

    [ObservableProperty]
    public partial string Details { get; set; }

    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    partial void OnDetailsChanged(string value) => OnPropertyChanged(nameof(HasDetails));

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public string Capture { get; }
    public bool IsPending { get; }
    public string PendingText { get; }
    public DateTimeOffset CreatedAt { get; internal set; }
    public int Spoken { get; internal set; }
    public DateTimeOffset? CompletedAt { get; internal set; }
    public DateTimeOffset? DeletedAt { get; internal set; }

    [ObservableProperty]
    public partial bool IsStriking { get; set; }

    [ObservableProperty]
    public partial bool IsDone { get; set; }
}
