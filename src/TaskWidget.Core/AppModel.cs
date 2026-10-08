using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskWidget.Core;

public sealed partial class AppModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    readonly IClock clock;
    readonly string tasksPath;
    readonly string settingsPath;
    readonly object gate = new();
    readonly Dictionary<TaskRow, DateTimeOffset> created = [];
    readonly Dictionary<TaskRow, int> spoken = [];
    string? pendingTasks;
    string? pendingSettings;
    int dirty;
    int disposed;
    bool loading;

    public AppModel(string dataFolder, IClock clock)
    {
        this.clock = clock;
        Directory.CreateDirectory(dataFolder);
        tasksPath = Path.Combine(dataFolder, "tasks.json");
        settingsPath = Path.Combine(dataFolder, "settings.json");
        Tasks.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(OpenTaskCount));
            OnPropertyChanged(nameof(OpenHighCount));
            OnPropertyChanged(nameof(OpenMediumCount));
            OnPropertyChanged(nameof(OpenLowCount));
        };
        Load();
    }

    [ObservableProperty]
    public partial string CaptureText { get; set; } = "";

    [ObservableProperty]
    public partial bool Docked { get; private set; }

    [ObservableProperty]
    public partial bool Attention { get; set; }

    public ObservableCollection<TaskRow> Tasks { get; } = [];

    public int OpenTaskCount => Tasks.Count;

    public int RowsBeforeScrolling => 8;

    public int OpenHighCount => Tasks.Count(task => task.Priority == Priority.High);

    public int OpenMediumCount => Tasks.Count(task => task.Priority == Priority.Medium);

    public int OpenLowCount => Tasks.Count(task => task.Priority == Priority.Low);

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

    public void MoveTo(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Tasks.Count || to >= Tasks.Count)
            return;

        Tasks.Move(from, to);
        HasManualPositions = true;
        MarkDirty();
    }

    // The list already moved the row (a drag). Record that, without moving it again.
    public void AcceptReorder(int from, int to)
    {
        if (from == to || to < 0)
            return;

        HasManualPositions = true;
        MarkDirty();
    }

    public void ReSort()
    {
        var sorted = Tasks.OrderBy(task => task, Comparer<TaskRow>.Create(CompareRank)).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var current = Tasks.IndexOf(sorted[i]);
            if (current != i)
                Tasks.Move(current, i);
        }

        HasManualPositions = false;
        MarkDirty();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        clock.Cancel();
        WriteIfDirty();
    }

    partial void OnCaptureTextChanged(string value) => MarkDirty();

    partial void OnDockedChanged(bool value) => MarkDirty();

    void MarkDirty()
    {
        if (loading || Volatile.Read(ref disposed) != 0)
            return;

        var tasks = JsonSerializer.Serialize(SnapshotTasks(), WidgetJsonContext.Default.TaskFile);
        var settings = JsonSerializer.Serialize(
            new SettingsFile { SchemaVersion = 1, Docked = Docked },
            WidgetJsonContext.Default.SettingsFile);
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
        Manual = HasManualPositions,
        Tasks = Tasks.Select(task => new TaskRecord
        {
            Title = task.Title,
            Details = task.Details,
            Priority = PriorityName(task.Priority),
            Effort = EffortName(task.Effort),
            Created = CreatedOf(task),
            Spoken = SpokenOf(task),
        }).ToList(),
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
            var row = new TaskRow(
                record.Title,
                ParsePriority(record.Priority),
                ParseEffort(record.Effort),
                record.Details);
            created[row] = record.Created;
            spoken[row] = record.Spoken;
            Tasks.Add(row);
        }

        HasManualPositions = file.Manual;
        CaptureText = file.Draft;
    }

    // New, edited and un-completed Tasks all land here.
    void Place(TaskRow task, DateTimeOffset at, int spokenIndex)
    {
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

        if (file is null)
            return;

        Docked = file.Docked;
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

    DateTimeOffset CreatedOf(TaskRow task) => created.TryGetValue(task, out var at) ? at : default;

    int SpokenOf(TaskRow task) => spoken.TryGetValue(task, out var index) ? index : 0;

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
