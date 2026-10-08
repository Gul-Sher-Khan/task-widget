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

    public Priority Priority { get; }
    public Effort Effort { get; }
    public string Details { get; }
    public string Capture { get; }

    [ObservableProperty]
    public partial bool IsPending { get; private set; }

    [ObservableProperty]
    public partial string PendingText { get; private set; }

    [ObservableProperty]
    public partial bool IsFailed { get; private set; }

    [ObservableProperty]
    public partial bool IsEditingCapture { get; private set; }

    [ObservableProperty]
    public partial string Reason { get; private set; } = "";

    [ObservableProperty]
    public partial string ReasonTip { get; private set; } = "";

    [ObservableProperty]
    public partial bool MakeTaskFirst { get; private set; }

    public bool RetryFirst => !MakeTaskFirst;

    public bool ShowFailed => IsFailed && !IsEditingCapture;

    public bool IsCaptureRow => IsFailed || IsEditingCapture;

    public bool IsTaskRow => !IsCaptureRow;

    public bool NotTask => IsPending || IsCaptureRow;

    public DateTimeOffset CreatedAt { get; internal set; }
    public int Spoken { get; internal set; }
    public DateTimeOffset? CompletedAt { get; internal set; }
    public DateTimeOffset? DeletedAt { get; internal set; }

    [ObservableProperty]
    public partial bool IsStriking { get; set; }

    [ObservableProperty]
    public partial bool IsDone { get; set; }

    public void BeginInterpret()
    {
        IsEditingCapture = false;
        IsFailed = false;
        MakeTaskFirst = false;
        Reason = "";
        ReasonTip = "";
        PendingText = "Interpreting…";
        IsPending = true;
        RaiseKind();
    }

    public void Fail(string reason, string tip, bool makeTaskFirst)
    {
        IsPending = false;
        IsEditingCapture = false;
        Reason = reason;
        ReasonTip = tip;
        MakeTaskFirst = makeTaskFirst;
        IsFailed = true;
        RaiseKind();
    }

    public void BeginEdit()
    {
        if (!IsFailed)
            return;
        IsEditingCapture = true;
        RaiseKind();
    }

    public void CancelEdit()
    {
        if (!IsEditingCapture)
            return;
        IsEditingCapture = false;
        RaiseKind();
    }

    void RaiseKind()
    {
        OnPropertyChanged(nameof(RetryFirst));
        OnPropertyChanged(nameof(ShowFailed));
        OnPropertyChanged(nameof(IsCaptureRow));
        OnPropertyChanged(nameof(IsTaskRow));
        OnPropertyChanged(nameof(NotTask));
    }
}
