using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;

[assembly: SupportedOSPlatform("windows")]

namespace TaskWidget.Core;

public sealed partial class AppModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan StrikeHold = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan DoneWindow = TimeSpan.FromDays(90);
    public static readonly TimeSpan UndoStay = TimeSpan.FromMilliseconds(5000);
    public const string RunValueName = "Task Widget";
    public const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string TokenFileName = "tokens.bin";

    readonly IClock clock;
    readonly string tasksPath;
    readonly string settingsPath;
    readonly string tokenPath;
    readonly string runKeyPath;
    readonly HttpClient? httpClient;
    readonly IBrowserLauncher? browser;
    readonly IDataProtector? protector;
    readonly object gate = new();
    SettingsFile settingsFile = new() { SchemaVersion = 1 };
    readonly Dictionary<TaskRow, DateTimeOffset> created = [];
    readonly Dictionary<TaskRow, int> spoken = [];
    string? pendingTasks;
    string? pendingSettings;
    IDisposable? saveTimer;
    readonly Dictionary<TaskRow, IDisposable> strikes = [];
    readonly List<TaskRow> finished = [];
    readonly List<TaskRow> deleted = [];
    readonly Stack<(Action Undo, Action Redo)> undo = new();
    readonly Stack<(Action Undo, Action Redo)> redo = new();
    IDisposable? pillTimer;
    int pillToken;
    int dirty;
    int disposed;
    bool loading;
    int rowsBeforeScrolling = 8;
    bool startWithWindows;

    public AppModel(string dataFolder, IClock clock, string? runKeyPath = null)
        : this(dataFolder, clock, null, null, null, runKeyPath)
    {
    }

    public AppModel(
        string dataFolder,
        IClock clock,
        HttpMessageHandler? http,
        IBrowserLauncher? browser,
        IDataProtector? protector,
        string? runKeyPath = null)
    {
        this.clock = clock;
        this.browser = browser;
        this.protector = protector;
        this.runKeyPath = runKeyPath ?? DefaultRunKeyPath;
        if (http is not null)
            httpClient = new HttpClient(http, disposeHandler: false);
        Directory.CreateDirectory(dataFolder);
        tasksPath = Path.Combine(dataFolder, "tasks.json");
        settingsPath = Path.Combine(dataFolder, "settings.json");
        tokenPath = Path.Combine(dataFolder, TokenFileName);
        Tasks.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(OpenTaskCount));
            OnPropertyChanged(nameof(OpenHighCount));
            OnPropertyChanged(nameof(OpenMediumCount));
            OnPropertyChanged(nameof(OpenLowCount));
            OnPropertyChanged(nameof(ShowEmptyHotkey));
        };
        Load();
        startWithWindows = RunValueExists();
    }

    [ObservableProperty]
    public partial string CaptureText { get; set; } = "";

    [ObservableProperty]
    public partial bool Docked { get; private set; }

    [ObservableProperty]
    public partial bool Attention { get; set; }

    public ObservableCollection<TaskRow> Tasks { get; } = [];

    public ObservableCollection<TaskRow> DoneTasks { get; } = [];

    [ObservableProperty]
    public partial bool ShowingDone { get; private set; }

    public ObservableCollection<TaskRow> VisibleTasks => ShowingDone ? DoneTasks : Tasks;

    public int OpenTaskCount => Tasks.Count;

public void Move(TaskRow task, int index)
    {
        var from = Tasks.IndexOf(task);
        if (from < 0 || index < 0 || index >= Tasks.Count || index == from)
            return;

        var wasManual = HasManualPositions;
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
        var wasManual = HasManualPositions;
        var sorted = before.OrderBy(task => task, Comparer<TaskRow>.Create(CompareRank)).ToList();
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

    void SetManual(bool value) => HasManualPositions = value;

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

    partial void OnShowingDoneChanged(bool value)
    {
        OnPropertyChanged(nameof(VisibleTasks));
        OnPropertyChanged(nameof(ShowingCapture));
    }

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

    public int OpenHighCount => Tasks.Count(task => task.Priority == Priority.High);

    public int OpenMediumCount => Tasks.Count(task => task.Priority == Priority.Medium);

    public int OpenLowCount => Tasks.Count(task => task.Priority == Priority.Low);

    public bool ShowWelcome => !settingsFile.WelcomeRetired && !HasConnection;

    public bool ShowEmptyHotkey => !ShowWelcome && Tasks.Count == 0;

    [ObservableProperty]
    public partial bool HasConnection { get; private set; }

    [ObservableProperty]
    public partial SignInPhase SignInState { get; private set; }

    [ObservableProperty]
    public partial string SignInStatus { get; private set; } = "";

    [ObservableProperty]
    public partial string SignInCause { get; private set; } = "";

    [ObservableProperty]
    public partial string Hotkey { get; private set; } = "Ctrl + Shift";

    public bool SignInIdle => SignInState == SignInPhase.Idle;

    public bool SignInWaiting => SignInState == SignInPhase.Waiting;

    public bool SignInFailed => SignInState == SignInPhase.Failed;

    public bool SignInNotEligible => SignInState == SignInPhase.NotEligible;

    public bool SignInButton => SignInState is SignInPhase.Idle or SignInPhase.NotEligible;

    public event Action? BringToFront;

    public int RowsBeforeScrolling
    {
        get => rowsBeforeScrolling;
        set
        {
            value = ClampRows(value);
            if (rowsBeforeScrolling == value)
                return;

            rowsBeforeScrolling = value;
            OnPropertyChanged(nameof(RowsBeforeScrolling));
            OnPropertyChanged(nameof(RowsBeforeScrollingValue));
            OnPropertyChanged(nameof(ListMaxHeight));
            MarkDirty();
        }
    }

    public double ListMaxHeight => rowsBeforeScrolling * 37d + 4d;

    public double RowsBeforeScrollingValue
    {
        get => rowsBeforeScrolling;
        set => RowsBeforeScrolling = (int)value;
    }

    public string VersionLine
    {
        get
        {
            var version = typeof(AppModel).Assembly.GetName().Version!;
            return "Task Widget " + version.Major + "." + version.Minor + "." + version.Build;
        }
    }

    public string GitHubUrl => "https://github.com/Gul-Sher-Khan/task-widget";

    public string PrivacyUrl => "https://github.com/Gul-Sher-Khan/task-widget#privacy";

    [ObservableProperty]
    public partial bool SettingsOpen { get; set; }

    public bool ShowingTasks => !SettingsOpen;

    public bool ShowingCapture => ShowingTasks && !ShowingDone;

    public void ToggleSettings() => SettingsOpen = !SettingsOpen;

    partial void OnSettingsOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowingTasks));
        OnPropertyChanged(nameof(ShowingCapture));
    }

    public bool StartWithWindows
    {
        get => startWithWindows;
        set
        {
            if (startWithWindows == value)
                return;

            startWithWindows = value;
            ApplyRunValue();
            OnPropertyChanged(nameof(StartWithWindows));
        }
    }

    [ObservableProperty]
    public partial bool HasManualPositions { get; private set; }

    public void UpdateDraft(string text) => CaptureText = text;

    public void Dock()
    {
        if (Docked)
            return;
        Docked = true;
    }

    public void Expand()
    {
        if (!Docked)
            return;
        Docked = false;
    }

    public void CommitCapture()
    {
        var text = CaptureText.Replace("\r\n", "\n").Trim();
        if (text.Length == 0)
            return;

        var title = text.Split('\n')[0].Trim();
        if (title.Length == 0)
            return;

Place(new TaskRow(title, Priority.Medium, Effort.Short, ""), clock.Now, spokenIndex: 0);
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

    int InsertIndex(TaskRow task) => IndexFor(task);

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

public void MoveTo(int from, int to)
    {
        if (from < 0 || from >= Tasks.Count)
            return;

        Move(Tasks[from], to);
    }

    public void AcceptReorder(int from, int to)
    {
        if (from == to || to < 0 || to >= Tasks.Count)
            return;

        var task = Tasks[to];
        var wasManual = HasManualPositions;
        HasManualPositions = true;
        MarkDirty();
        Push(
            "Move",
            () =>
            {
                Place(task, from);
                HasManualPositions = wasManual;
                MarkDirty();
            },
            () =>
            {
                Place(task, to);
                HasManualPositions = true;
                MarkDirty();
            },
            pill: false);
    }

    public void ReSort() => Resort();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        CancelSignIn();
        clock.Cancel();
        WriteIfDirty();
        httpClient?.Dispose();
    }

    partial void OnCaptureTextChanged(string value) => MarkDirty();

    partial void OnSignInStateChanged(SignInPhase value) => RaiseSignInBindings();

    partial void OnHasConnectionChanged(bool value) => RaiseWelcome();

    partial void OnDockedChanged(bool value) => MarkDirty();

    void RaiseSignInBindings()
    {
        OnPropertyChanged(nameof(SignInIdle));
        OnPropertyChanged(nameof(SignInWaiting));
        OnPropertyChanged(nameof(SignInFailed));
        OnPropertyChanged(nameof(SignInNotEligible));
        OnPropertyChanged(nameof(SignInButton));
    }

    void RaiseWelcome()
    {
        OnPropertyChanged(nameof(ShowWelcome));
        OnPropertyChanged(nameof(ShowEmptyHotkey));
    }

    void MarkDirty()
    {
        if (loading || Volatile.Read(ref disposed) != 0)
            return;

        var tasks = JsonSerializer.Serialize(SnapshotTasks(), WidgetJsonContext.Default.TaskFile);
        var settings = JsonSerializer.Serialize(SnapshotSettings(), WidgetJsonContext.Default.SettingsFile);
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
Manual = HasManualPositions,
        ManualPositions = HasManualPositions,
        Tasks = Tasks.Concat(finished).Concat(deleted).Select(Record).ToList(),
    };

    TaskRecord Record(TaskRow task) => new()
    {
        Title = task.Title,
        Details = task.Details,
        Priority = PriorityName(task.Priority),
        Effort = EffortName(task.Effort),
        Created = task.CreatedAt,
        CreatedAt = task.CreatedAt,
        Spoken = task.Spoken,
        CompletedAt = task.CompletedAt,
        DeletedAt = task.DeletedAt,
    };

    SettingsFile SnapshotSettings() => new()
    {
        SchemaVersion = 1,
        Docked = Docked,
        RowsBeforeScrolling = rowsBeforeScrolling,
        WelcomeRetired = settingsFile.WelcomeRetired,
        ExtAgentHostId = settingsFile.ExtAgentHostId,
        IssuedClientId = settingsFile.IssuedClientId,
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
        loading = true;
        try
        {
            if (File.Exists(tasksPath))
                LoadTasks();
            LoadSettings();
            LoadConnection();
        }
        finally
        {
            loading = false;
        }
    }

    void LoadTasks()
    {
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
            var at = record.Created ?? record.CreatedAt ?? clock.Now;
            task.CreatedAt = at;
            task.Spoken = record.Spoken;
            created[task] = at;
            spoken[task] = record.Spoken;
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
        HasManualPositions = file.Manual || file.ManualPositions;
        CaptureText = file.Draft;
    }

    // New, edited and un-completed Tasks all land here.
    void Place(TaskRow task, DateTimeOffset at, int spokenIndex)
    {
        task.CreatedAt = at;
        task.Spoken = spokenIndex;
        created[task] = at;
        spoken[task] = spokenIndex;
        Tasks.Insert(IndexFor(task), task);
    }

    int IndexFor(TaskRow task)
    {
        for (var i = 0; i < Tasks.Count; i++)
        {
            if (CompareRank(task, Tasks[i]) < 0)
                return i;
        }

        return Tasks.Count;
    }

    int CompareRank(TaskRow a, TaskRow b)
    {
        var byPriority = a.Priority.CompareTo(b.Priority);
        if (byPriority != 0)
            return byPriority;

        var byEffort = a.Effort.CompareTo(b.Effort);
        if (byEffort != 0)
            return byEffort;

        var byAge = CreatedOf(a).CompareTo(CreatedOf(b));
        if (byAge != 0)
            return byAge;

        return SpokenOf(a).CompareTo(SpokenOf(b));
    }

    void LoadSettings()
    {
        if (!File.Exists(settingsPath))
            return;

        SettingsFile? file;
        try
        {
            file = JsonSerializer.Deserialize(File.ReadAllText(settingsPath), WidgetJsonContext.Default.SettingsFile);
        }
        catch (JsonException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }

        if (file is null)
            return;

        settingsFile = file;
        Docked = file.Docked;
        rowsBeforeScrolling = ClampRows(file.RowsBeforeScrolling);
    }

    void LoadConnection()
    {
        if (protector is null || !File.Exists(tokenPath))
            return;

        try
        {
            var plain = protector.Unprotect(File.ReadAllBytes(tokenPath));
            var file = JsonSerializer.Deserialize(plain, WidgetJsonContext.Default.TokenFile);
            HasConnection = file is not null && file.AccessToken.Length > 0;
        }
        catch (JsonException)
        {
            HasConnection = false;
        }
        catch (IOException)
        {
            HasConnection = false;
        }
        catch (CryptographicException)
        {
            HasConnection = false;
        }
    }

    static int ClampRows(int value) => Math.Clamp(value, 4, 15);

    bool RunValueExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath);
        return key?.GetValue(RunValueName) is not null;
    }

    void ApplyRunValue()
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKeyPath);
        if (startWithWindows)
            key.SetValue(RunValueName, "\"" + Environment.ProcessPath + "\"");
        else
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
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

    DateTimeOffset CreatedOf(TaskRow task) =>
        created.TryGetValue(task, out var at) ? at : task.CreatedAt;

    int SpokenOf(TaskRow task) =>
        spoken.TryGetValue(task, out var index) ? index : task.Spoken;

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

    static void SwapIn(string path, string contents) => SwapIn(path, Encoding.UTF8.GetBytes(contents));

    static void SwapIn(string path, byte[] contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, contents);
        if (!File.Exists(path))
            File.WriteAllBytes(path, []);
        File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
    }
}
