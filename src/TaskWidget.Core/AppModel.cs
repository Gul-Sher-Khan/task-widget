using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskWidget.Core;

public sealed partial class AppModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan StrikeHold = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan DoneWindow = TimeSpan.FromDays(90);
    public static readonly TimeSpan UndoStay = TimeSpan.FromMilliseconds(5000);

    readonly IClock clock;
    readonly string tasksPath;
    readonly string settingsPath;
    readonly object gate = new();
    string? pendingTasks;
    string? pendingSettings;
    IDisposable? saveTimer;
    readonly Dictionary<TaskRow, IDisposable> strikes = [];
    readonly List<TaskRow> finished = [];
    readonly List<TaskRow> deleted = [];
    bool manual;
    readonly Stack<(Action Undo, Action Redo)> undo = new();
    readonly Stack<(Action Undo, Action Redo)> redo = new();
    IDisposable? pillTimer;
    int pillToken;
    int dirty;
    int disposed;

    public AppModel(string dataFolder, IClock clock)
    {
        this.clock = clock;
        Directory.CreateDirectory(dataFolder);
        tasksPath = Path.Combine(dataFolder, "tasks.json");
        settingsPath = Path.Combine(dataFolder, "settings.json");
        Tasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(OpenTaskCount));
        Load();
    }

    [ObservableProperty]
    public partial string CaptureText { get; set; } = "";

    public ObservableCollection<TaskRow> Tasks { get; } = [];

    public ObservableCollection<TaskRow> DoneTasks { get; } = [];

    [ObservableProperty]
    public partial bool ShowingDone { get; private set; }

    public ObservableCollection<TaskRow> VisibleTasks => ShowingDone ? DoneTasks : Tasks;

    public int OpenTaskCount => Tasks.Count;

    public bool HasManualPositions => manual;

    public void Move(TaskRow task, int index)
    {
        var from = Tasks.IndexOf(task);
        if (from < 0 || index < 0 || index >= Tasks.Count || index == from)
            return;

        var wasManual = manual;
        Tasks.Move(from, index);
        SetManual(true);
        MarkDirty();
        Push(
            "Move",
            () =>
            {
                Place(task, from);
                SetManual(wasManual);
                MarkDirty();
            },
            () =>
            {
                Place(task, index);
                SetManual(true);
                MarkDirty();
            },
            pill: false);
    }

    public void Resort()
    {
        var before = Tasks.ToList();
        var wasManual = manual;
        var sorted = before.OrderBy(task => task.Priority).ThenBy(task => task.Effort).ThenBy(task => task.CreatedAt).ToList();
        Apply(sorted);
        SetManual(false);
        MarkDirty();
        Push(
            "Re-sort",
            () =>
            {
                Apply(before);
                SetManual(wasManual);
                MarkDirty();
            },
            () =>
            {
                Apply(sorted);
                SetManual(false);
                MarkDirty();
            },
            pill: false);
    }

    void Place(TaskRow task, int index)
    {
        var current = Tasks.IndexOf(task);
        if (current < 0 || current == index)
            return;
        Tasks.Move(current, index);
    }

    void Apply(List<TaskRow> order)
    {
        for (var i = 0; i < order.Count; i++)
        {
            var current = Tasks.IndexOf(order[i]);
            if (current >= 0 && current != i)
                Tasks.Move(current, i);
        }
    }

    void SetManual(bool value)
    {
        if (manual == value)
            return;
        manual = value;
        OnPropertyChanged(nameof(HasManualPositions));
    }

    public void ToggleDoneView()
    {
        ShowDoneWindow();
        ShowingDone = !ShowingDone;
        SelectedIndex = -1;
    }

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    public void SelectDown() => MoveSelection(1);

    public void SelectUp() => MoveSelection(-1);

    public void TickSelected()
    {
        if (Selected() is TaskRow task)
            Tick(task);
    }

    public void DeleteSelected()
    {
        if (Selected() is not TaskRow task)
            return;

        Delete(task);
        if (Current.Count == 0)
            SelectedIndex = -1;
        else if (SelectedIndex >= Current.Count)
            SelectedIndex = Current.Count - 1;
    }

    void MoveSelection(int delta)
    {
        var count = Current.Count;
        if (count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        SelectedIndex = SelectedIndex < 0
            ? delta < 0 ? count - 1 : 0
            : Math.Clamp(SelectedIndex + delta, 0, count - 1);
    }

    TaskRow? Selected() =>
        SelectedIndex >= 0 && SelectedIndex < Current.Count ? Current[SelectedIndex] : null;

    ObservableCollection<TaskRow> Current => ShowingDone ? DoneTasks : Tasks;

    partial void OnShowingDoneChanged(bool value) => OnPropertyChanged(nameof(VisibleTasks));

    [ObservableProperty]
    public partial bool UndoVisible { get; private set; }

    [ObservableProperty]
    public partial string UndoText { get; private set; } = "";

    public void Undo()
    {
        if (undo.Count == 0)
            return;

        var step = undo.Pop();
        step.Undo();
        redo.Push(step);
        UndoVisible = false;
    }

    public void Redo()
    {
        if (redo.Count == 0)
            return;

        var step = redo.Pop();
        step.Redo();
        undo.Push(step);
    }

    public void Delete(TaskRow task)
    {
        CancelStrike(task);
        var openIndex = Tasks.IndexOf(task);
        var wasDone = finished.Contains(task);
        var completedAt = task.CompletedAt;
        if (openIndex < 0 && !wasDone)
            return;

        if (openIndex >= 0)
            Tasks.RemoveAt(openIndex);
        else
            finished.Remove(task);

        var deletedAt = clock.UtcNow;
        task.DeletedAt = deletedAt;
        deleted.Add(task);
        ShowDoneWindow();
        MarkDirty();
        Push(
            "Task deleted",
            () =>
            {
                deleted.Remove(task);
                task.DeletedAt = null;
                if (wasDone)
                    RestoreDone(task, completedAt ?? deletedAt);
                else
                    RestoreOpen(task, Math.Max(openIndex, 0));
            },
            () =>
            {
                Tasks.Remove(task);
                finished.Remove(task);
                task.IsDone = wasDone;
                task.CompletedAt = wasDone ? completedAt : null;
                task.DeletedAt = deletedAt;
                if (!deleted.Contains(task))
                    deleted.Add(task);
                ShowDoneWindow();
                MarkDirty();
            },
            pill: true);
    }

    public void UpdateDraft(string text) => CaptureText = text;

    public void CommitCapture()
    {
        var text = CaptureText.Replace("\r\n", "\n").Trim();
        if (text.Length == 0)
            return;

        var title = text.Split('\n')[0].Trim();
        if (title.Length == 0)
            return;

        var task = new TaskRow(title, Priority.Medium, Effort.Short, "");
        task.CreatedAt = clock.UtcNow;
        Tasks.Add(task);
        CaptureText = "";
    }

    public void Tick(TaskRow task)
    {
        if (task.IsStriking)
        {
            CancelStrike(task);
            return;
        }

        if (task.IsDone)
        {
            Uncomplete(task);
            return;
        }

        if (!Tasks.Contains(task))
            return;

        task.IsStriking = true;
        strikes[task] = clock.Schedule(StrikeHold, () => FinishStrike(task));
    }

    void Uncomplete(TaskRow task)
    {
        if (!finished.Remove(task))
            return;

        var completedAt = task.CompletedAt ?? clock.UtcNow;
        task.IsDone = false;
        task.CompletedAt = null;
        Tasks.Insert(InsertIndex(task), task);
        ShowDoneWindow();
        MarkDirty();
        Push(
            "Task returned",
            () => RestoreDone(task, completedAt),
            () =>
            {
                finished.Remove(task);
                task.IsDone = false;
                task.CompletedAt = null;
                if (!Tasks.Contains(task))
                    Tasks.Insert(InsertIndex(task), task);
                ShowDoneWindow();
                MarkDirty();
            },
            pill: false);
    }

    int InsertIndex(TaskRow task)
    {
        for (var i = 0; i < Tasks.Count; i++)
        {
            if (RanksAbove(task, Tasks[i]))
                return i;
        }

        return Tasks.Count;
    }

    static bool RanksAbove(TaskRow task, TaskRow other)
    {
        if (task.Priority != other.Priority)
            return task.Priority < other.Priority;
        if (task.Effort != other.Effort)
            return task.Effort < other.Effort;
        return task.CreatedAt < other.CreatedAt;
    }

    void CancelStrike(TaskRow task)
    {
        if (strikes.Remove(task, out var timer))
            timer.Dispose();
        task.IsStriking = false;
    }

    void FinishStrike(TaskRow task)
    {
        if (!task.IsStriking || !Tasks.Contains(task))
            return;

        var index = Tasks.IndexOf(task);
        strikes.Remove(task);
        var completedAt = clock.UtcNow;
        Tasks.Remove(task);
        task.IsStriking = false;
        task.IsDone = true;
        task.CompletedAt = completedAt;
        finished.Insert(0, task);
        ShowDoneWindow();
        MarkDirty();
        Push("Task done", () => RestoreOpen(task, index), () => RestoreDone(task, completedAt), pill: true);
    }

    void RestoreOpen(TaskRow task, int index)
    {
        finished.Remove(task);
        deleted.Remove(task);
        task.IsDone = false;
        task.CompletedAt = null;
        task.DeletedAt = null;
        if (!Tasks.Contains(task))
            Tasks.Insert(Math.Min(index, Tasks.Count), task);
        ShowDoneWindow();
        MarkDirty();
    }

    void RestoreDone(TaskRow task, DateTimeOffset completedAt)
    {
        Tasks.Remove(task);
        deleted.Remove(task);
        task.IsStriking = false;
        task.IsDone = true;
        task.DeletedAt = null;
        task.CompletedAt = completedAt;
        finished.Remove(task);
        finished.Insert(0, task);
        ShowDoneWindow();
        MarkDirty();
    }

    void Push(string what, Action undoAction, Action redoAction, bool pill)
    {
        undo.Push((undoAction, redoAction));
        redo.Clear();
        if (!pill)
            return;

        UndoText = what;
        UndoVisible = true;
        var token = ++pillToken;
        pillTimer?.Dispose();
        pillTimer = clock.Schedule(UndoStay, () =>
        {
            if (token == pillToken)
                UndoVisible = false;
        });
    }

    void ShowDoneWindow()
    {
        var cutoff = clock.UtcNow - DoneWindow;
        var visible = finished
            .Where(task => task.CompletedAt is DateTimeOffset completed && completed >= cutoff)
            .OrderByDescending(task => task.CompletedAt)
            .ToList();
        DoneTasks.Clear();
        foreach (var task in visible)
            DoneTasks.Add(task);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        clock.Cancel();
        WriteIfDirty();
    }

    partial void OnCaptureTextChanged(string value) => MarkDirty();

    void MarkDirty()
    {
        if (Volatile.Read(ref disposed) != 0)
            return;

        var tasks = JsonSerializer.Serialize(SnapshotTasks(), WidgetJsonContext.Default.TaskFile);
        var settings = JsonSerializer.Serialize(new SettingsFile { SchemaVersion = 1 }, WidgetJsonContext.Default.SettingsFile);
        lock (gate)
        {
            pendingTasks = tasks;
            pendingSettings = settings;
            dirty = 1;
        }

        saveTimer?.Dispose();
        saveTimer = clock.Schedule(SaveDelay, WriteIfDirty);
    }

    TaskFile SnapshotTasks() => new()
    {
        SchemaVersion = 1,
        Draft = CaptureText,
        ManualPositions = manual,
        Tasks = Tasks.Concat(finished).Concat(deleted).Select(Record).ToList(),
    };

    TaskRecord Record(TaskRow task) => new()
    {
        Title = task.Title,
        Details = task.Details,
        Priority = PriorityName(task.Priority),
        Effort = EffortName(task.Effort),
        CreatedAt = task.CreatedAt,
        CompletedAt = task.CompletedAt,
        DeletedAt = task.DeletedAt,
    };

    static string PriorityName(Priority priority) => priority switch
    {
        Priority.High => "high",
        Priority.Medium => "medium",
        Priority.Low => "low",
        _ => throw new ArgumentOutOfRangeException(nameof(priority)),
    };

    void Load()
    {
        if (!File.Exists(tasksPath))
            return;

        var file = JsonSerializer.Deserialize(File.ReadAllText(tasksPath), WidgetJsonContext.Default.TaskFile);
        if (file is null)
            return;

        foreach (var record in file.Tasks ?? [])
        {
            var task = new TaskRow(
                record.Title,
                ParsePriority(record.Priority),
                ParseEffort(record.Effort),
                record.Details);
            task.CreatedAt = record.CreatedAt ?? clock.UtcNow;
            if (record.DeletedAt is DateTimeOffset deletedAt)
            {
                task.DeletedAt = deletedAt;
                deleted.Add(task);
            }
            else if (record.CompletedAt is DateTimeOffset completed)
            {
                task.IsDone = true;
                task.CompletedAt = completed;
                finished.Add(task);
            }
            else
            {
                Tasks.Add(task);
            }
        }

        ShowDoneWindow();
        SetManual(file.ManualPositions);
        CaptureText = file.Draft;
    }

    static Priority ParsePriority(string value) => value switch
    {
        "high" => Priority.High,
        "medium" => Priority.Medium,
        "low" => Priority.Low,
        _ => throw new InvalidDataException($"Unknown priority \"{value}\"."),
    };

    static Effort ParseEffort(string value) => value switch
    {
        "quick" => Effort.Quick,
        "short" => Effort.Short,
        "long" => Effort.Long,
        _ => throw new InvalidDataException($"Unknown effort \"{value}\"."),
    };

    static string EffortName(Effort effort) => effort switch
    {
        Effort.Quick => "quick",
        Effort.Short => "short",
        Effort.Long => "long",
        _ => throw new ArgumentOutOfRangeException(nameof(effort)),
    };

    void WriteIfDirty()
    {
        string? tasks;
        string? settings;
        lock (gate)
        {
            if (dirty == 0)
                return;
            tasks = pendingTasks;
            settings = pendingSettings;
            dirty = 0;
        }

        if (tasks is not null)
            SwapIn(tasksPath, tasks);
        if (settings is not null)
            SwapIn(settingsPath, settings);
    }

    static void SwapIn(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        if (!File.Exists(path))
            File.WriteAllText(path, "");
        File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
    }
}
