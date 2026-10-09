using System.Net.Http;
using System.Text;
using TaskWidget.Core;

namespace TaskWidget;

// Screenshot start-up overrides, copied from the look prototype's LOOK_* list.
// Any one of them points the process at a temp folder so a screenshot run cannot
// rewrite the user's tasks. Unset, the app launches exactly as before.
static class LookLaunch
{
    public const string GateName = "TaskWidget.Look";

    static readonly string[] Names =
    [
        "LOOK_SCENE", "LOOK_THEME", "LOOK_BACKDROP", "LOOK_DOCKED", "LOOK_BUSY",
        "LOOK_SETTINGS", "LOOK_NOBAR", "LOOK_UPDATE", "LOOK_CONTRAST", "LOOK_SIGNIN",
        "LOOK_DICTATION", "LOOK_COUNT",
    ];

    public static bool Active { get; private set; }
    public static bool ContrastPreview { get; private set; }
    public static bool OpenSettings { get; private set; }
    public static bool ShowUpdate { get; private set; }
    public static bool Dictation { get; private set; }
    public static bool Busy { get; private set; }
    public static int Scene { get; private set; }
    public static int SignIn { get; private set; }
    public static string Folder { get; private set; } = "";

    public static void Prepare()
    {
        if (!Names.Any(name => Environment.GetEnvironmentVariable(name) is not null))
            return;

        Active = true;
        Scene = Env("LOOK_SCENE");
        if (Scene is < 0 or > 7)
            Scene = 0;
        SignIn = Env("LOOK_SIGNIN");
        ContrastPreview = Env("LOOK_CONTRAST") == 1;
        OpenSettings = Env("LOOK_SETTINGS") == 1;
        ShowUpdate = Env("LOOK_UPDATE") == 1;
        Dictation = Env("LOOK_DICTATION") == 1;
        Busy = Env("LOOK_BUSY") == 1;
        Folder = Path.Combine(Path.GetTempPath(), "TaskWidget-look", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        WriteSettings();
        WriteTasks();
        if (SignedIn)
            WriteToken();
        if (ContrastPreview)
            ApplyContrastPreview();
    }

    public static HttpMessageHandler CreateHandler() =>
        Active && Busy ? new HangHandler() : new HttpClientHandler();

    public static INetwork CreateNetwork() =>
        Active && Scene == 7 ? new OfflineNetwork() : new SystemNetwork();

    public static void AfterModel(AppModel model)
    {
        if (!Active)
            return;

        if (Scene == 7)
            OpenReinterpret(model);

        if (!Busy || model.ReadOnly)
            return;

        model.UpdateDraft("Email Bilal the contract, he's waiting today");
        _ = model.CommitCapture();
    }

    // Scene 7 opens the re-interpret editor and dims that Capture's Tasks.
    // A waiting row and a pending "Re-interpreting…" row cannot show together:
    // startup releases every waiting Capture while the PC is online, and a pending
    // row only stays pending while it is online.
    static void OpenReinterpret(AppModel model)
    {
        var source = model.Tasks.FirstOrDefault(task => task.Capture == "bilal" && task.IsTaskRow);
        if (source is null)
            return;

        model.Reinterpret(source);
        foreach (var task in model.Tasks)
        {
            if (task.Capture == "bilal" && task.IsTaskRow)
                task.IsDimmed = true;
        }
    }

    static bool SignedIn => Scene is not (1 or 4);

    static void WriteSettings()
    {
        var theme = Env("LOOK_THEME") switch { 1 => "light", 2 => "dark", _ => "system" };
        var backdrop = Env("LOOK_BACKDROP") switch { 1 => "micaAlt", 2 => "acrylic", 3 => "solid", _ => "mica" };
        var docked = Env("LOOK_DOCKED") == 1 ? "true" : "false";
        var retired = Scene == 1 ? "false" : "true";
        var update = ShowUpdate
            ? $"""
              "availableVersion": "0.2.0",
              "updateResult": "available",
              "lastUpdateCheck": "{DateTimeOffset.Now:o}"
            """
            : """
              "availableVersion": "",
              "updateResult": ""
            """;
        File.WriteAllText(Path.Combine(Folder, "settings.json"), $$"""
            {
              "schemaVersion": 1,
              "docked": {{docked}},
              "rowsBeforeScrolling": 8,
              "theme": "{{theme}}",
              "backdrop": "{{backdrop}}",
              "checkForUpdatesAutomatically": false,
              "welcomeRetired": {{retired}},
              {{update}}
            }
            """);
    }

    static void WriteTasks()
    {
        if (Scene is 1 or 2)
            return;

        if (Scene == 6)
        {
            File.WriteAllText(Path.Combine(Folder, "tasks.json"), "{ this is not json");
            return;
        }

        var schema = Scene == 3 ? 2 : 1;
        var count = Env("LOOK_COUNT");
        var tasks = count > 0 ? CountedTasks(count) : Scene == 7 ? CaptureStateTasks() : EverydayTasks();
        var captures = Scene == 7 ? CaptureStateCaptures() : "";
        var json = $$"""
            {
              "schemaVersion": {{schema}},
              "draft": "",
              "tasks": [
            {{tasks}}
              ],
              "captures": [
            {{captures}}
              ]
            }
            """;
        if (Scene == 5)
        {
            var bak = Path.Combine(Folder, "tasks.json.bak");
            File.WriteAllText(bak, json);
            File.SetLastWriteTime(bak, DateTime.Today.AddHours(14).AddMinutes(2));
            File.WriteAllText(Path.Combine(Folder, "tasks.json"), "{ this is not json");
            return;
        }

        File.WriteAllText(Path.Combine(Folder, "tasks.json"), json);
    }

    static void WriteToken()
    {
        var jwt = Base64Url("""{"alg":"none","typ":"JWT"}""")
            + "."
            + Base64Url("""{"email":"gul@example.com","https://api.openai.com/auth":{"chatgpt_plan_type":"pro"}}""")
            + ".x";
        var json = $$"""
            {
              "clientId": "look",
              "extAgentHostId": "look",
              "accessToken": "look-access",
              "refreshToken": "look-refresh",
              "idToken": "{{jwt}}",
              "accessExpiresAt": "2099-01-01T00:00:00Z",
              "refreshExpiresAt": "2099-01-01T00:00:00Z"
            }
            """;
        var protectedBytes = new DpapiProtector().Protect(Encoding.UTF8.GetBytes(json));
        File.WriteAllBytes(Path.Combine(Folder, AppModel.TokenFileName), protectedBytes);
    }

    static void ApplyContrastPreview()
    {
        var tokens = Microsoft.UI.Xaml.Application.Current.Resources.MergedDictionaries[1];
        var dark = (Microsoft.UI.Xaml.ResourceDictionary)tokens.ThemeDictionaries["Dark"];
        var preview = (Microsoft.UI.Xaml.ResourceDictionary)tokens["ContrastPreview"];
        foreach (var entry in preview)
            dark[entry.Key] = entry.Value;
    }

    static string CountedTasks(int count)
    {
        var priorities = new[] { "high", "medium", "low" };
        var efforts = new[] { "quick", "short", "long" };
        var rows = new string[count];
        for (var i = 0; i < count; i++)
            rows[i] = Task($"Review item {i + 1}", "", priorities[i % 3], efforts[i % 3]);
        return string.Join(",\n", rows);
    }

    static string EverydayTasks() => string.Join(",\n", new[]
    {
        Task("Reply to Sana about the Q3 budget", "She needs the revised numbers before Thursday's finance sync; the travel line is the open question.", "high", "quick"),
        Task("Send the invoice to Northwind", "", "high", "quick"),
        Task("Fix the flaky login test on CI", "Fails about 1 in 5 runs on the Windows agent since the token refresh change.", "high", "short"),
        Task("Book the dentist appointment", "", "medium", "quick"),
        Task("Renew the domain before it lapses", "Expires on the 28th.", "medium", "quick"),
        Task("Review Ali's PR on the caching layer", "Ali asked for eyes on the eviction policy specifically.", "medium", "short"),
        Task("Write release notes for 0.4", "Mention the new Dock and the Ctrl+Shift tap.", "medium", "short"),
        Task("Plan Friday's demo", "Ten minutes, for the whole team, live rather than slides.", "medium", "long"),
        Task("Clean up the Downloads folder", "", "low", "short"),
        Task("Pay the electricity bill", "", "high", "quick", completed: "2026-10-01T09:00:00Z"),
        Task("Order a new keyboard", "The Keychron with brown switches.", "low", "quick", completed: "2026-10-01T09:05:00Z"),
    });

    static string CaptureStateTasks() => string.Join(",\n", new[]
    {
        Task("Reply to Sana about the Q3 budget", "She needs the revised numbers before Thursday's finance sync; the travel line is the open question.", "high", "quick"),
        Task("Email Bilal the signed contract", "He's waiting on it today", "high", "quick", "bilal"),
        Task("Send the invoice to Northwind", "", "high", "quick"),
        Task("Fix the flaky login test on CI", "Fails about 1 in 5 runs on the Windows agent since the token refresh change.", "high", "short"),
        Task("Book the dentist appointment", "", "medium", "quick"),
        Task("Review Ali's PR on the caching layer", "Ali asked for eyes on the eviction policy specifically.", "medium", "short"),
        Task("Plan the offsite agenda", "", "medium", "long", "bilal"),
    });

    static string CaptureStateCaptures() => """
            { "id": "hamza", "text": "Ask Hamza for the staging credentials", "state": "waiting", "cause": "offline" },
            { "id": "thursday", "text": "Thursday was a good day honestly", "state": "failed", "cause": "zero-tasks" },
            { "id": "parking", "text": "Renew the parking permit before the 15th, the office one not home", "state": "failed", "cause": "unreachable" },
            { "id": "bilal", "text": "Email Bilal the signed contract, he's waiting on it today and also plan the offsite agenda", "interpreted": "Email Bilal the signed contract, he's waiting on it today and also plan the offsite agenda", "state": "interpreted" }
        """;

    static string Task(string title, string details, string priority, string effort, string capture = "", string? completed = null)
    {
        var extra = "";
        if (capture.Length > 0)
            extra += $",\n        \"capture\": \"{capture}\"";
        if (completed is not null)
            extra += $",\n        \"completedAt\": \"{completed}\"";
        return $$"""
                {
                  "title": "{{title}}",
                  "details": "{{details}}",
                  "priority": "{{priority}}",
                  "effort": "{{effort}}"{{extra}}
                }
            """;
    }

    static int Env(string name) => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : 0;

    static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    sealed partial class HangHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
            return pending.Task;
        }
    }

    sealed class OfflineNetwork : INetwork
    {
        public bool Online => false;

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }
}
