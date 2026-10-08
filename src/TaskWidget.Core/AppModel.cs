using System.Collections.ObjectModel;
using System.Runtime.Versioning;
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
    public const string RunValueName = "Task Widget";
    public const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly IClock clock;
    readonly SynchronizationContext? ui;
    readonly HttpMessageHandler? http;
    readonly CancellationTokenSource checking = new();
    readonly string tasksPath;
    readonly string settingsPath;
    readonly string runKeyPath;
    readonly object gate = new();
    readonly Dictionary<TaskRow, DateTimeOffset> created = [];
    readonly Dictionary<TaskRow, int> spoken = [];
    string? pendingTasks;
    string? pendingSettings;
    int dirty;
    int disposed;
    bool loading;
    int rowsBeforeScrolling = 8;
    bool startWithWindows;
    Task inFlight = Task.CompletedTask;
    UpdatePhase phase;
    string newVersion = "";
    string checkedWhen = "";
    string updateResult = "";
    string installerUrl = "";
    DateTimeOffset? lastUpdateCheck;
    bool checkAutomatically = true;

    public AppModel(string dataFolder, IClock clock, string? runKeyPath = null, HttpMessageHandler? http = null)
    {
        ui = SynchronizationContext.Current;
        this.clock = clock;
        this.http = http;
        this.runKeyPath = runKeyPath ?? DefaultRunKeyPath;
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

    public ObservableCollection<TaskRow> Tasks { get; } = [];

    public int OpenTaskCount => Tasks.Count;

    public int OpenHighCount => Tasks.Count(task => task.Priority == Priority.High);

    public int OpenMediumCount => Tasks.Count(task => task.Priority == Priority.Medium);

    public int OpenLowCount => Tasks.Count(task => task.Priority == Priority.Low);

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

        checking.Cancel();
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

    SettingsFile SnapshotSettings() => new()
    {
        SchemaVersion = 1,
        Docked = Docked,
        RowsBeforeScrolling = rowsBeforeScrolling,
        CheckForUpdatesAutomatically = checkAutomatically,
        LastUpdateCheck = lastUpdateCheck,
        AvailableVersion = newVersion,
        UpdateResult = updateResult,
        InstallerUrl = installerUrl,
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
        rowsBeforeScrolling = ClampRows(file.RowsBeforeScrolling);
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
