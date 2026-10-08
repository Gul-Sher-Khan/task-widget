using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;

[assembly: SupportedOSPlatform("windows")]

namespace TaskWidget.Core;

public enum WidgetBanner
{
    None,
    NewerVersion,
    SignedOut,
    Recovered,
    StartedEmpty,
}

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
    readonly SynchronizationContext? ui;
    readonly HttpMessageHandler? http;
    readonly CancellationTokenSource checking = new();
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
    bool overFullScreen;
    int rowsBeforeScrolling = 8;
    bool startWithWindows;
    ThemeChoice theme = ThemeChoice.System;
    BackdropChoice backdrop = BackdropChoice.Mica;
    bool systemDark;
    bool backdropAvailable = true;
    Task inFlight = Task.CompletedTask;
    UpdatePhase phase;
    string newVersion = "";
    string checkedWhen = "";
    string updateResult = "";
    string installerUrl = "";
    DateTimeOffset? lastUpdateCheck;
    bool checkAutomatically = true;

    public AppModel(string dataFolder, IClock clock, string? runKeyPath = null)
        : this(dataFolder, clock, null, null, null, runKeyPath)
    {
    }

    public AppModel(
        string dataFolder,
        IClock clock,
        HttpMessageHandler? http,
        IBrowserLauncher? browser = null,
        IDataProtector? protector = null,
        string? runKeyPath = null)
    {
        ui = SynchronizationContext.Current;
        this.clock = clock;
        this.http = http;
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
        if (http is not null && checkAutomatically && CheckDue())
            inFlight = CheckOnce();
    }

    [ObservableProperty]
    public partial string CaptureText { get; set; } = "";

    [ObservableProperty]
    public partial bool Docked { get; private set; }

    bool attention;
    bool updateReady;

    public bool Attention
    {
        get => attention || updateReady;
        set
        {
            if (attention == value)
                return;
            attention = value;
            OnPropertyChanged(nameof(Attention));
        }
    }

    // Newer version outranks signed out, which outranks recovered and started empty.
    public WidgetBanner Banner => updateReady ? WidgetBanner.NewerVersion : WidgetBanner.None;

    public bool HasBanner => Banner != WidgetBanner.None;

    public string BannerMessage => Banner == WidgetBanner.NewerVersion
        ? "Version " + newVersion + " is available"
        : "";

    public string BannerPrimary => Banner == WidgetBanner.NewerVersion ? "Get update" : "";

    public bool BannerIsInfo => Banner == WidgetBanner.NewerVersion;

    public bool CanCheck => phase is UpdatePhase.Idle or UpdatePhase.Current or UpdatePhase.Failed;

    [ObservableProperty]
    public partial bool Raised { get; private set; }

    [ObservableProperty]
    public partial bool FullScreenApp { get; private set; }

    public ObservableCollection<TaskRow> Tasks { get; } = [];

    public ObservableCollection<TaskRow> DoneTasks { get; } = [];

    [ObservableProperty]
    public partial bool ShowingDone { get; private set; }

    public ObservableCollection<TaskRow> VisibleTasks => ShowingDone ? DoneTasks : Tasks;

    public bool ShowDock => Docked && !Raised && !FullScreenApp;

    public bool ShowWidget => !Docked || Raised;

    public event Action? RaiseRequested;

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

    public bool UpdChecking => phase == UpdatePhase.Checking;

    public bool UpdCurrent => phase == UpdatePhase.Current;

    public bool UpdAvailable => phase == UpdatePhase.Available;

    public bool UpdFailed => phase == UpdatePhase.Failed;

    public bool UpdDownloading => phase == UpdatePhase.Downloading;

    public bool DownloadStarted { get; private set; }

    public string? InstallerPath { get; private set; }

    public bool RestartRequested { get; private set; }

    public double Downloaded => downloaded;

    public string DownloadNote => "Task Widget restarts to finish. Your Tasks and draft are kept.";

    double downloaded;

    public string NewVersion => newVersion;

    public string CheckedWhen => checkedWhen;

    public bool AutoUpdate
    {
        get => checkAutomatically;
        set
        {
            if (checkAutomatically == value)
                return;
            checkAutomatically = value;
            OnPropertyChanged(nameof(AutoUpdate));
            MarkDirty();
        }
    }

    public string AboutStatus => phase switch
    {
        UpdatePhase.Checking => "Checking for updates…",
        UpdatePhase.Available => "Version " + newVersion + " is available",
        UpdatePhase.Downloading => "Downloading " + newVersion + "…",
        UpdatePhase.Failed => "Couldn't check for updates",
        UpdatePhase.Current => "You're up to date",
        _ => "",
    };

    public Task UpdateCheck => inFlight;

    public Task CheckForUpdates()
    {
        if (!inFlight.IsCompleted)
            return inFlight;
        return inFlight = CheckOnce();
    }

    public Task StartUpdate()
    {
        if (http is null || !updateReady || phase == UpdatePhase.Downloading || installerUrl.Length == 0)
            return Task.CompletedTask;
        return inFlight = DownloadInstaller();
    }

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

    public ThemeChoice Theme
    {
        get => theme;
        set
        {
            if (theme == value)
                return;

            bool appearedDark = AppearsDark;
            theme = value;
            OnPropertyChanged(nameof(Theme));
            OnPropertyChanged(nameof(ThemeIndex));
            if (appearedDark != AppearsDark)
                OnPropertyChanged(nameof(AppearsDark));
            MarkDirty();
        }
    }

    public int ThemeIndex
    {
        get => (int)theme;
        set
        {
            if (value is < 0 or > 2)
                return;
            Theme = (ThemeChoice)value;
        }
    }

    public BackdropChoice Backdrop
    {
        get => backdrop;
        set
        {
            if (backdrop == value)
                return;

            var effective = EffectiveBackdrop;
            backdrop = value;
            OnPropertyChanged(nameof(Backdrop));
            OnPropertyChanged(nameof(BackdropIndex));
            if (effective != EffectiveBackdrop)
                OnPropertyChanged(nameof(EffectiveBackdrop));
            MarkDirty();
        }
    }

    public int BackdropIndex
    {
        get => (int)backdrop;
        set
        {
            if (value is < 0 or > 3)
                return;
            Backdrop = (BackdropChoice)value;
        }
    }

    public bool AppearsDark => theme switch
    {
        ThemeChoice.Dark => true,
        ThemeChoice.Light => false,
        _ => systemDark,
    };

    public BackdropChoice EffectiveBackdrop => backdropAvailable ? backdrop : BackdropChoice.Solid;

    public bool ShowBackdropNote => !backdropAvailable;

    public void ReportSystemAppearance(bool dark, bool backdropAvailable)
    {
        bool appearedDark = AppearsDark;
        bool showedNote = ShowBackdropNote;
        var effective = EffectiveBackdrop;
        if (systemDark == dark && this.backdropAvailable == backdropAvailable)
            return;

        systemDark = dark;
        this.backdropAvailable = backdropAvailable;
        if (appearedDark != AppearsDark)
            OnPropertyChanged(nameof(AppearsDark));
        if (effective != EffectiveBackdrop)
            OnPropertyChanged(nameof(EffectiveBackdrop));
        if (showedNote != ShowBackdropNote)
            OnPropertyChanged(nameof(ShowBackdropNote));
    }

    [ObservableProperty]
    public partial bool HasManualPositions { get; private set; }

    public void UpdateDraft(string text) => CaptureText = text;

    public void Dock()
    {
        overFullScreen = false;
        if (!Docked)
            Docked = true;
        if (Raised)
            Raised = false;
    }

    public void Expand()
    {
        if (!Docked)
            return;
        Docked = false;
    }

    public void SetFullScreenApp(bool present) => FullScreenApp = present;

    // Brings the Widget forward. A second call asks the shell to raise again.
    // Over a full-screen app the Dock stays put and the Widget leaves on commit, Esc, or deactivate.
    public void Raise()
    {
        if (FullScreenApp)
            overFullScreen = true;
        else if (Docked)
            Expand();

        if (Raised)
        {
            RaiseRequested?.Invoke();
            return;
        }

        Raised = true;
    }

    public bool Escape()
    {
        if (!overFullScreen)
            return false;

        DismissRaised();
        return true;
    }

    public void NoteDeactivated()
    {
        if (Raised)
            DismissRaised();
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
        if (overFullScreen)
            DismissRaised();
    }

    void DismissRaised()
    {
        overFullScreen = false;
        if (Raised)
            Raised = false;
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

        checking.Cancel();
        CancelSignIn();
        clock.Cancel();
        WriteIfDirty();
        httpClient?.Dispose();
    }

    partial void OnCaptureTextChanged(string value) => MarkDirty();

    partial void OnSignInStateChanged(SignInPhase value) => RaiseSignInBindings();

    partial void OnHasConnectionChanged(bool value) => RaiseWelcome();

    partial void OnDockedChanged(bool value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(ShowDock));
        OnPropertyChanged(nameof(ShowWidget));
    }

    partial void OnRaisedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDock));
        OnPropertyChanged(nameof(ShowWidget));
    }

    partial void OnFullScreenAppChanged(bool value)
    {
        if (value && Raised)
            overFullScreen = true;
        else if (!value)
            overFullScreen = false;
        OnPropertyChanged(nameof(ShowDock));
        OnPropertyChanged(nameof(ShowWidget));
    }

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
        Theme = ThemeName(theme),
        Backdrop = BackdropName(backdrop),
        CheckForUpdatesAutomatically = checkAutomatically,
        LastUpdateCheck = lastUpdateCheck,
        AvailableVersion = newVersion,
        UpdateResult = updateResult,
        InstallerUrl = installerUrl,
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
        theme = ParseTheme(file.Theme);
        backdrop = ParseBackdrop(file.Backdrop);
        checkAutomatically = file.CheckForUpdatesAutomatically;
        lastUpdateCheck = file.LastUpdateCheck;
        installerUrl = file.InstallerUrl;
        updateResult = file.UpdateResult;
        if (updateResult == "available" && file.AvailableVersion.Length > 0)
        {
            phase = UpdatePhase.Available;
            newVersion = file.AvailableVersion;
            updateReady = true;
            checkedWhen = CheckedLine();
        }
        else if (updateResult == "current")
        {
            phase = UpdatePhase.Current;
            checkedWhen = CheckedLine();
        }
        else if (updateResult == "failed")
        {
            phase = UpdatePhase.Failed;
            checkedWhen = "No connection. Will try again later.";
        }
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

    static ThemeChoice ParseTheme(string? value) => value switch
    {
        "light" => ThemeChoice.Light,
        "dark" => ThemeChoice.Dark,
        _ => ThemeChoice.System,
    };

    static BackdropChoice ParseBackdrop(string? value) => value switch
    {
        "micaAlt" => BackdropChoice.MicaAlt,
        "acrylic" => BackdropChoice.Acrylic,
        "solid" => BackdropChoice.Solid,
        _ => BackdropChoice.Mica,
    };

    static string ThemeName(ThemeChoice value) => value switch
    {
        ThemeChoice.Light => "light",
        ThemeChoice.Dark => "dark",
        _ => "system",
    };

    static string BackdropName(BackdropChoice value) => value switch
    {
        BackdropChoice.MicaAlt => "micaAlt",
        BackdropChoice.Acrylic => "acrylic",
        BackdropChoice.Solid => "solid",
        _ => "mica",
    };

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

    async Task CheckOnce()
    {
        if (http is null || Volatile.Read(ref disposed) != 0)
            return;

        phase = UpdatePhase.Checking;
        RaiseUpdate();
        try
        {
            using var client = new HttpClient(http, disposeHandler: false);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TaskWidget");
            using var response = await client.GetAsync(
                "https://api.github.com/repos/Gul-Sher-Khan/task-widget/releases?per_page=100",
                checking.Token);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(checking.Token);
            var best = NewestPublished(body);
            if (best is null || best.Version <= RunningVersion())
            {
                phase = UpdatePhase.Current;
                newVersion = "";
                installerUrl = "";
                updateResult = "current";
                updateReady = false;
            }
            else
            {
                phase = UpdatePhase.Available;
                newVersion = best.Text;
                installerUrl = best.InstallerUrl;
                updateResult = "available";
                updateReady = true;
            }

            lastUpdateCheck = clock.Now;
            checkedWhen = "Checked just now";
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            phase = UpdatePhase.Failed;
            checkedWhen = "No connection. Will try again later.";
            updateResult = "failed";
            updateReady = false;
            lastUpdateCheck = clock.Now;
        }

        if (Volatile.Read(ref disposed) != 0)
            return;

        RaiseUpdate();
        MarkDirty();
    }

    bool CheckDue() => lastUpdateCheck is not DateTimeOffset at || clock.Now - at >= TimeSpan.FromDays(1);

    string CheckedLine()
    {
        if (lastUpdateCheck is null)
            return "";
        var at = lastUpdateCheck.Value;
        if (at.Date == clock.Now.Date)
            return "Checked today, " + at.ToString("HH:mm");
        return "Checked " + at.ToString("d MMM, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }

    async Task DownloadInstaller()
    {
        phase = UpdatePhase.Downloading;
        DownloadStarted = true;
        downloaded = 0;
        RaiseUpdate();
        try
        {
            using var client = new HttpClient(http!, disposeHandler: false);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TaskWidget");
            using var response = await client.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, checking.Token);
            response.EnsureSuccessStatusCode();
            var directory = Path.Combine(Path.GetTempPath(), "TaskWidget");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "TaskWidget-" + newVersion + "-" + ArchitectureName() + ".exe");
            var total = response.Content.Headers.ContentLength ?? 0;
            await using (var input = await response.Content.ReadAsStreamAsync(checking.Token))
            await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                var buffer = new byte[81920];
                long got = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, checking.Token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), checking.Token);
                    got += read;
                    if (total > 0)
                    {
                        downloaded = (double)got / total;
                        OnUi(() => OnPropertyChanged(nameof(Downloaded)));
                    }
                }
            }

            downloaded = 1;
            InstallerPath = path;
            WriteIfDirty();
            RestartRequested = true;
            OnUi(() =>
            {
                OnPropertyChanged(nameof(InstallerPath));
                OnPropertyChanged(nameof(RestartRequested));
                OnPropertyChanged(nameof(Downloaded));
            });
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            if (Volatile.Read(ref disposed) != 0)
                return;
            phase = UpdatePhase.Available;
            RaiseUpdate();
        }
    }

    static PublishedRelease? NewestPublished(string json)
    {
        PublishedRelease? best = null;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (Flag(release, "draft") || Flag(release, "prerelease"))
                continue;
            if (!release.TryGetProperty("tag_name", out var tagName))
                continue;
            var tag = tagName.GetString();
            if (tag is null || tag.Length < 6 || tag[0] != 'v')
                continue;
            var number = tag[1..];
            if (number.Split('.').Length != 3 || !Version.TryParse(number, out var version))
                continue;
            if (best is not null && version <= best.Version)
                continue;

            best = new PublishedRelease(version, number, AssetUrl(release, number) ?? "");
        }

        return best;
    }

    static string? AssetUrl(JsonElement release, string version)
    {
        var arch = ArchitectureName();
        if (arch is null || !release.TryGetProperty("assets", out var assets))
            return null;

        var name = "TaskWidget-" + version + "-" + arch + ".exe";
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var assetName) || assetName.GetString() != name)
                continue;
            return asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() : null;
        }

        return null;
    }

    static string? ArchitectureName() => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        _ => null,
    };

    sealed record PublishedRelease(Version Version, string Text, string InstallerUrl);

    static bool Flag(JsonElement release, string name) =>
        release.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    static Version RunningVersion()
    {
        var version = typeof(AppModel).Assembly.GetName().Version!;
        return new Version(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build);
    }

    void RaiseUpdate() => OnUi(NotifyUpdate);

    void OnUi(Action action)
    {
        if (ui is null || SynchronizationContext.Current == ui)
            action();
        else
            ui.Post(_ => action(), null);
    }

    void NotifyUpdate()
    {
        OnPropertyChanged(nameof(UpdChecking));
        OnPropertyChanged(nameof(UpdCurrent));
        OnPropertyChanged(nameof(UpdAvailable));
        OnPropertyChanged(nameof(UpdFailed));
        OnPropertyChanged(nameof(UpdDownloading));
        OnPropertyChanged(nameof(DownloadStarted));
        OnPropertyChanged(nameof(Downloaded));
        OnPropertyChanged(nameof(NewVersion));
        OnPropertyChanged(nameof(CheckedWhen));
        OnPropertyChanged(nameof(AboutStatus));
        OnPropertyChanged(nameof(Attention));
        OnPropertyChanged(nameof(Banner));
        OnPropertyChanged(nameof(HasBanner));
        OnPropertyChanged(nameof(BannerMessage));
        OnPropertyChanged(nameof(BannerPrimary));
        OnPropertyChanged(nameof(BannerIsInfo));
        OnPropertyChanged(nameof(CanCheck));
    }

    enum UpdatePhase
    {
        Idle,
        Checking,
        Current,
        Available,
        Downloading,
        Failed,
    }
}
