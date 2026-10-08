using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;

[assembly: SupportedOSPlatform("windows")]

namespace TaskWidget.Core;

public sealed partial class AppModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);
    public const string RunValueName = "Task Widget";
    public const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly IClock clock;
    readonly string tasksPath;
    readonly string settingsPath;
    readonly string runKeyPath;
    readonly object gate = new();
    string? pendingTasks;
    string? pendingSettings;
    int dirty;
    int disposed;
    int rowsBeforeScrolling = 8;
    bool startWithWindows;

    public AppModel(string dataFolder, IClock clock, string? runKeyPath = null)
    {
        this.clock = clock;
        this.runKeyPath = runKeyPath ?? DefaultRunKeyPath;
        Directory.CreateDirectory(dataFolder);
        tasksPath = Path.Combine(dataFolder, "tasks.json");
        settingsPath = Path.Combine(dataFolder, "settings.json");
        Tasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(OpenTaskCount));
        Load();
        startWithWindows = RunValueExists();
    }

    [ObservableProperty]
    public partial string CaptureText { get; set; } = "";

    public ObservableCollection<TaskRow> Tasks { get; } = [];

    public int OpenTaskCount => Tasks.Count;

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

    public void ToggleSettings() => SettingsOpen = !SettingsOpen;

    partial void OnSettingsOpenChanged(bool value) => OnPropertyChanged(nameof(ShowingTasks));

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

    public void UpdateDraft(string text) => CaptureText = text;

    public void CommitCapture()
    {
        var text = CaptureText.Replace("\r\n", "\n").Trim();
        if (text.Length == 0)
            return;

        var title = text.Split('\n')[0].Trim();
        if (title.Length == 0)
            return;

        Tasks.Add(new TaskRow(title, Priority.Medium, Effort.Short, ""));
        CaptureText = "";
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
        var settings = JsonSerializer.Serialize(SnapshotSettings(), WidgetJsonContext.Default.SettingsFile);
        lock (gate)
        {
            pendingTasks = tasks;
            pendingSettings = settings;
            dirty = 1;
        }

        clock.Cancel();
        clock.Schedule(SaveDelay, WriteIfDirty);
    }

    TaskFile SnapshotTasks() => new()
    {
        SchemaVersion = 1,
        Draft = CaptureText,
        Tasks = Tasks.Select(task => new TaskRecord
        {
            Title = task.Title,
            Details = task.Details,
            Priority = PriorityName(task.Priority),
            Effort = EffortName(task.Effort),
        }).ToList(),
    };

    static string PriorityName(Priority priority) => priority switch
    {
        Priority.High => "high",
        Priority.Medium => "medium",
        Priority.Low => "low",
        _ => throw new ArgumentOutOfRangeException(nameof(priority)),
    };

    SettingsFile SnapshotSettings() => new()
    {
        SchemaVersion = 1,
        RowsBeforeScrolling = rowsBeforeScrolling,
    };

    void Load()
    {
        LoadSettings();
        if (!File.Exists(tasksPath))
            return;

        var file = JsonSerializer.Deserialize(File.ReadAllText(tasksPath), WidgetJsonContext.Default.TaskFile);
        if (file is null)
            return;

        foreach (var record in file.Tasks ?? [])
        {
            Tasks.Add(new TaskRow(
                record.Title,
                ParsePriority(record.Priority),
                ParseEffort(record.Effort),
                record.Details));
        }

        CaptureText = file.Draft;
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

        if (file is null)
            return;

        rowsBeforeScrolling = ClampRows(file.RowsBeforeScrolling);
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
