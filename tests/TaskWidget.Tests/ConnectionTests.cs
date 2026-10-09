using System.Net;
using System.Text;
using System.Text.Json;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class ConnectionTests
{
    [Fact]
    public async Task A_capture_refreshes_the_access_token_when_under_five_minutes_remain()
    {
        using var world = new ConnectionWorld(accessRemaining: TimeSpan.FromMinutes(4));
        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");

        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();

        Assert.Equal(1, world.Http.RefreshCalls);
        Assert.Equal("refresh_token", world.Http.RefreshForm["grant_type"]);
        Assert.Equal("refresh-1", world.Http.RefreshForm["refresh_token"]);
        Assert.Equal("oaiapp_test", world.Http.RefreshForm["client_id"]);
        Assert.Equal("https://auth.openai.com/api/accounts/oauth/token", world.Http.RefreshUri);
        var response = Assert.Single(world.Http.Calls);
        Assert.Equal("https://api.openai.com/v1/responses", response.Uri);
        Assert.Equal("Bearer access-2", response.Authorization);

        world.Release();
        await committing;

        Assert.Equal("Buy milk", Assert.Single(world.Model.Tasks).Title);
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.Folder, AppModel.TokenFileName)));
        Assert.Equal("access-2", saved.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal("refresh-2", saved.RootElement.GetProperty("refreshToken").GetString());
        Assert.Equal(world.Clock.UtcNow.AddHours(1), saved.RootElement.GetProperty("accessExpiresAt").GetDateTimeOffset());
        Assert.Equal(world.Clock.UtcNow.AddDays(30), saved.RootElement.GetProperty("refreshExpiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_capture_leaves_a_token_with_five_minutes_left_unchanged()
    {
        using var world = new ConnectionWorld(accessRemaining: TimeSpan.FromMinutes(5));
        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");

        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();

        Assert.Equal(0, world.Http.RefreshCalls);
        Assert.Equal("Bearer access-1", Assert.Single(world.Http.Calls).Authorization);
        world.Release();
        await committing;
    }

    [Fact]
    public async Task Two_captures_share_one_refresh()
    {
        using var world = new ConnectionWorld(accessRemaining: TimeSpan.FromMinutes(1));
        world.Http.HoldRefresh = true;
        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");

        world.Model.UpdateDraft("buy milk");
        var first = world.Model.CommitCapture();
        await world.Http.RefreshStarted;
        world.Model.UpdateDraft("call the bank");
        var second = world.Model.CommitCapture();

        Assert.Equal(1, world.Http.RefreshCalls);
        Assert.Empty(world.Http.Calls);

        world.Http.ReleaseRefresh();
        await Until(() => world.Http.Calls.Count == 2);
        Assert.Equal(1, world.Http.RefreshCalls);
        Assert.All(world.Http.Calls, call => Assert.Equal("Bearer access-2", call.Authorization));

        world.Release();
        world.Release();
        await first;
        await second;
        Assert.Equal(2, world.Model.Tasks.Count(task => !task.IsPending));
    }

    [Fact]
    public async Task A_rejected_access_token_is_refreshed_once_and_the_capture_retries()
    {
        using var world = new ConnectionWorld();
        world.Http.UnauthorizedResponses = 1;
        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");

        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();

        await Until(() => world.Http.Calls.Count == 2);
        Assert.Equal(1, world.Http.RefreshCalls);
        Assert.Equal("Bearer access-1", world.Http.Calls[0].Authorization);
        Assert.Equal("Bearer access-2", world.Http.Calls[1].Authorization);

        world.Release();
        await committing;
        Assert.Equal("Buy milk", Assert.Single(world.Model.Tasks).Title);
        Assert.False(world.Model.Tasks[0].IsPending);
    }

    [Fact]
    public async Task A_failed_refresh_signs_the_user_out_and_the_capture_waits()
    {
        using var world = new ConnectionWorld(accessRemaining: TimeSpan.FromMinutes(1));
        world.Http.RefreshStatus = 400;
        world.Model.UpdateDraft("email Sarah");
        await world.Model.CommitCapture();

        Assert.False(world.Model.HasConnection);
        Assert.False(File.Exists(Path.Combine(world.Folder, AppModel.TokenFileName)));
        Assert.False(world.Model.ShowWelcome);
        Assert.Equal(WidgetBanner.SignedOut, world.Model.Banner);
        Assert.Equal("You're signed out of ChatGPT. New Captures wait until you sign in.", world.Model.BannerMessage);
        Assert.Equal("Sign in", world.Model.BannerPrimary);
        Assert.True(world.Model.Attention);
        Assert.Empty(world.Http.Calls);

        var row = Assert.Single(world.Model.Tasks);
        Assert.True(row.IsWaiting);
        Assert.False(row.IsPending);
        Assert.Equal("email Sarah", row.Title);
        Assert.Equal("Signed out", row.Reason);
        Assert.True(row.LightsDot);

        world.Clock.Advance(TimeSpan.FromMilliseconds(300));
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.Folder, "tasks.json")));
        var capture = Assert.Single(saved.RootElement.GetProperty("captures").EnumerateArray());
        Assert.Equal("waiting", capture.GetProperty("state").GetString());
        Assert.Equal("signed-out", capture.GetProperty("cause").GetString());
        Assert.Equal("email Sarah", capture.GetProperty("text").GetString());
        Assert.Empty(saved.RootElement.GetProperty("tasks").EnumerateArray());
    }

    [Fact]
    public async Task Re_auth_after_a_failed_refresh_sends_login_hint_for_the_same_account()
    {
        using var world = new ConnectionWorld(accessRemaining: TimeSpan.FromMinutes(1));
        world.Http.RefreshStatus = 400;
        world.Model.UpdateDraft("buy milk");
        await world.Model.CommitCapture();

        await world.Model.SignIn();

        var authorize = world.Browser.Authorize ?? throw new InvalidOperationException("The browser never opened.");
        Assert.Equal(ConnectionWorld.Email, Query(authorize)["login_hint"]);
        Assert.False(Query(authorize).ContainsKey("id_token_hint"));
    }

    [Fact]
    public async Task Sign_out_revokes_the_token_deletes_the_file_and_keeps_tasks()
    {
        using var world = new ConnectionWorld();
        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");
        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();
        world.Release();
        await committing;

        await world.Model.SignOut();

        Assert.False(world.Model.HasConnection);
        Assert.False(world.Model.ShowWelcome);
        Assert.False(File.Exists(Path.Combine(world.Folder, AppModel.TokenFileName)));
        Assert.Equal("Buy milk", Assert.Single(world.Model.Tasks).Title);
        Assert.Equal(WidgetBanner.SignedOut, world.Model.Banner);
        Assert.Equal("https://auth.openai.com/api/accounts/oauth/revoke", world.Http.RevokeUri);
        var revoke = QueryForm(Assert.Single(world.Http.RevokeForms));
        Assert.Equal("refresh-1", revoke["token"]);
        Assert.Equal("refresh_token", revoke["token_type_hint"]);
        Assert.Equal("oaiapp_test", revoke["client_id"]);

        world.Model.Dispose();
        var again = new AppModel(world.Folder, new ManualClock(), world.Http, world.Browser, new PassThroughProtector());
        try
        {
            Assert.Equal("Buy milk", Assert.Single(again.Tasks).Title);
            Assert.False(again.HasConnection);
            Assert.Equal(WidgetBanner.SignedOut, again.Banner);
        }
        finally
        {
            again.Dispose();
        }
    }

    [Fact]
    public async Task A_sign_in_the_user_starts_sends_no_login_hint()
    {
        using var world = new ConnectionWorld(accessRemaining: TimeSpan.FromMinutes(1));
        world.Http.RefreshStatus = 400;
        world.Model.UpdateDraft("buy milk");
        await world.Model.CommitCapture();

        await world.Model.SignOut();
        world.Http.RefreshStatus = 200;
        await world.Model.SignIn();

        var authorize = world.Browser.Authorize ?? throw new InvalidOperationException("The browser never opened.");
        Assert.False(Query(authorize).ContainsKey("login_hint"));
    }

    [Fact]
    public void A_newer_version_outranks_the_signed_out_banner()
    {
        var folder = Directory.CreateTempSubdirectory("tw-banner-rank").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "settings.json"), """
                {
                  "schemaVersion": 1,
                  "welcomeRetired": true,
                  "checkForUpdatesAutomatically": false,
                  "updateResult": "available",
                  "availableVersion": "9.9.0"
                }
                """);
            var model = new AppModel(folder, new ManualClock(), new ConnectionHttp(), new CallbackBrowser(SignInWorld.SuccessCallback), new PassThroughProtector());
            Assert.False(model.HasConnection);
            Assert.Equal(WidgetBanner.NewerVersion, model.Banner);
            Assert.Equal("Version 9.9.0 is available", model.BannerMessage);
            Assert.Equal("Get update", model.BannerPrimary);
            Assert.True(model.Attention);
            model.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task An_offline_capture_waits_without_a_dot_and_runs_when_the_network_returns()
    {
        using var world = new ConnectionWorld();
        world.Network.Set(false);
        world.Model.UpdateDraft("buy milk");
        await world.Model.CommitCapture();

        var waiting = Assert.Single(world.Model.Tasks);
        Assert.True(waiting.IsWaiting);
        Assert.Equal("buy milk", waiting.Title);
        Assert.Equal("Waiting for connection", waiting.Reason);
        Assert.False(waiting.LightsDot);
        Assert.False(world.Model.Attention);
        Assert.Empty(world.Http.Calls);
        Assert.Equal(WidgetBanner.None, world.Model.Banner);

        world.Model.Dispose();
        world.Network.Set(false);
        var again = new AppModel(world.Folder, world.Clock, world.Http, world.Browser, new PassThroughProtector(), network: world.Network);
        try
        {
            var restored = Assert.Single(again.Tasks);
            Assert.True(restored.IsWaiting);
            Assert.Equal("Waiting for connection", restored.Reason);
            Assert.False(again.Attention);

            world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");
            world.Network.Set(true);
            await Until(() => world.Http.Calls.Count == 1);
            world.Release();
            await Until(() => again.Tasks.Any(task => task.Title == "Buy milk" && !task.IsWaiting));
            Assert.Equal("Buy milk", Assert.Single(again.Tasks).Title);
            Assert.False(again.Attention);
        }
        finally
        {
            again.Dispose();
        }
    }

    [Fact]
    public async Task A_capture_before_sign_in_waits_for_the_account_and_the_welcome_stays()
    {
        using var world = new ConnectionWorld(connected: false, welcomed: false);
        Assert.True(world.Model.ShowWelcome);
        Assert.False(world.Model.Attention);
        Assert.Equal(WidgetBanner.None, world.Model.Banner);

        world.Model.UpdateDraft("buy milk");
        await world.Model.CommitCapture();

        Assert.True(world.Model.ShowWelcome);
        var waiting = Assert.Single(world.Model.Tasks);
        Assert.Equal("Connect your ChatGPT account", waiting.Reason);
        Assert.True(waiting.LightsDot);
        Assert.True(world.Model.Attention);
        Assert.Empty(world.Http.Calls);

        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");
        await world.Model.SignIn();
        await Until(() => world.Http.Calls.Count == 1);
        world.Release();
        await Until(() => world.Model.Tasks.Any(task => task.Title == "Buy milk" && !task.IsWaiting));

        Assert.False(world.Model.ShowWelcome);
        Assert.Equal("Buy milk", Assert.Single(world.Model.Tasks).Title);
        Assert.False(world.Model.Attention);
        Assert.Equal(WidgetBanner.None, world.Model.Banner);
    }

    [Fact]
    public async Task Signed_out_captures_wait_and_all_run_after_sign_in()
    {
        using var world = new ConnectionWorld();
        await world.Model.SignOut();
        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");

        world.Model.UpdateDraft("buy milk");
        await world.Model.CommitCapture();
        world.Model.UpdateDraft("call the bank");
        await world.Model.CommitCapture();

        Assert.Equal(WidgetBanner.SignedOut, world.Model.Banner);
        Assert.Equal(
            ["call the bank", "buy milk"],
            world.Model.Tasks.Select(task => task.Title));
        Assert.All(world.Model.Tasks, task =>
        {
            Assert.True(task.IsWaiting);
            Assert.Equal("Signed out", task.Reason);
            Assert.True(task.LightsDot);
        });
        Assert.True(world.Model.Attention);
        Assert.False(world.Model.ShowWelcome);

        await world.Model.SignIn();
        await Until(() => world.Http.Calls.Count == 2);
        world.Release();
        world.Release();
        await Until(() => world.Model.Tasks.Count(task => !task.IsWaiting && !task.IsPending) == 2);

        Assert.Equal(2, world.Model.Tasks.Count);
        Assert.False(world.Model.Attention);
        Assert.Equal(WidgetBanner.None, world.Model.Banner);
        Assert.True(world.Model.HasConnection);
    }

    [Fact]
    public async Task A_network_error_while_online_is_not_a_waiting_capture()
    {
        using var world = new ConnectionWorld();
        world.Http.FailResponse = true;
        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();
        for (var i = 0; i < 5 && !committing.IsCompleted; i++)
        {
            await Task.Delay(30, TestContext.Current.CancellationToken);
            world.Clock.Advance(TimeSpan.FromSeconds(2));
        }

        await committing;
        var row = Assert.Single(world.Model.Tasks);
        Assert.False(row.IsWaiting);
        Assert.True(row.IsFailed);
        Assert.Equal("Couldn't reach ChatGPT", row.Reason);
        Assert.True(world.Model.HasConnection);
        Assert.Equal(WidgetBanner.None, world.Model.Banner);
    }

    [Fact]
    public void Settings_shows_the_account_plan_and_automatic_first()
    {
        using var world = new ConnectionWorld();
        Assert.Equal("gul@example.com", world.Model.Account);
        Assert.Equal("ChatGPT Pro", world.Model.Plan);
        Assert.Equal("G", world.Model.AccountInitial);
        Assert.Equal(["Automatic (gpt-5.6-sol)", "gpt-5.6-sol"], world.Model.ModelChoices);
        Assert.Equal(0, world.Model.ModelIndex);

        world.Model.ModelIndex = 1;
        world.Clock.Advance(TimeSpan.FromMilliseconds(300));
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.Folder, "settings.json")));
        Assert.Equal("gpt-5.6-sol", saved.RootElement.GetProperty("chosenModel").GetString());
    }

    [Fact]
    public async Task A_chosen_model_that_disappears_falls_back_to_automatic()
    {
        using var world = new ConnectionWorld(arrange: folder =>
        {
            var settings = Path.Combine(folder, "settings.json");
            var json = File.ReadAllText(settings).Replace(
                """{ "slug": "gpt-5.6-sol", "priority": 1 }""",
                """{ "slug": "gpt-custom", "priority": 2 }, { "slug": "gpt-5.6-sol", "priority": 1 }""",
                StringComparison.Ordinal);
            json = json.Replace(
                "\"modelsCachedAt\"",
                "\"chosenModel\": \"gpt-custom\", \"modelsCachedAt\"",
                StringComparison.Ordinal);
            File.WriteAllText(settings, json);
        });
        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");
        Assert.Equal("gpt-custom", world.Model.ModelChoices[world.Model.ModelIndex]);

        world.Model.UpdateDraft("buy milk");
        var first = world.Model.CommitCapture();
        Assert.Equal("gpt-custom", ModelName(Assert.Single(world.Http.Calls)));
        world.Release();
        await first;

        world.Clock.Set(world.Clock.UtcNow.AddDays(1));
        world.Http.ModelsBody = """{"models":[{"slug":"gpt-5.6-sol","priority":4},{"slug":"gpt-6-astra","priority":1}]}""";
        world.Model.UpdateDraft("call the bank");
        var second = world.Model.CommitCapture();
        Assert.Equal("gpt-5.6-sol", ModelName(world.Http.Calls[^1]));
        Assert.Equal(0, world.Model.ModelIndex);
        Assert.Equal("Automatic (gpt-5.6-sol)", world.Model.ModelChoices[0]);
        Assert.Equal(WidgetBanner.None, world.Model.Banner);
        Assert.Equal("", world.Model.SignInStatus);
        world.Release();
        await second;

        world.Clock.Advance(TimeSpan.FromMilliseconds(300));
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.Folder, "settings.json")));
        Assert.Equal("", saved.RootElement.GetProperty("chosenModel").GetString());
    }

    static string ModelName(InterpretCall call)
    {
        using var doc = JsonDocument.Parse(call.Body);
        return doc.RootElement.GetProperty("model").GetString()!;
    }

    static Dictionary<string, string> QueryForm(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var rawKey = (eq < 0 ? part : part[..eq]).Replace("+", "%20", StringComparison.Ordinal);
            var rawValue = (eq < 0 ? "" : part[(eq + 1)..]).Replace("+", "%20", StringComparison.Ordinal);
            result[Uri.UnescapeDataString(rawKey)] = Uri.UnescapeDataString(rawValue);
        }

        return result;
    }

    static Dictionary<string, string> Query(Uri uri)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var rawKey = eq < 0 ? part : part[..eq];
            var rawValue = eq < 0 ? "" : part[(eq + 1)..];
            result[Uri.UnescapeDataString(rawKey)] = Uri.UnescapeDataString(rawValue);
        }

        return result;
    }

    static async Task Until(Func<bool> ready)
    {
        var deadline = Environment.TickCount64 + 2000;
        while (!ready() && Environment.TickCount64 < deadline)
            await Task.Delay(10);
    }
}

sealed class ConnectionWorld : IDisposable
{
    public ConnectionWorld(
        TimeSpan? accessRemaining = null,
        Action<ConnectionHttp>? configure = null,
        bool connected = true,
        bool welcomed = true,
        Action<string>? arrange = null)
    {
        Folder = Directory.CreateTempSubdirectory("tw-connection").FullName;
        var expires = Clock.UtcNow.Add(accessRemaining ?? TimeSpan.FromHours(1));
        File.WriteAllText(Path.Combine(Folder, "settings.json"), $$"""
            {
              "schemaVersion": 1,
              "welcomeRetired": {{(welcomed ? "true" : "false")}},
              "checkForUpdatesAutomatically": false,
              "issuedClientId": "oaiapp_test",
              "extAgentHostId": "urn:uuid:00000000-0000-0000-0000-000000000001",
              "modelsCachedAt": "{{Clock.UtcNow:O}}",
              "models": [ { "slug": "gpt-5.6-sol", "priority": 1 } ]
            }
            """);
        if (connected)
        {
            File.WriteAllText(Path.Combine(Folder, AppModel.TokenFileName), $$"""
                {
                  "clientId": "oaiapp_test",
                  "extAgentHostId": "urn:uuid:00000000-0000-0000-0000-000000000001",
                  "accessToken": "access-1",
                  "refreshToken": "refresh-1",
                  "idToken": "{{IdToken}}",
                  "accessExpiresAt": "{{expires:O}}",
                  "refreshExpiresAt": "{{Clock.UtcNow.AddDays(30):O}}"
            }
            """);
        }

        arrange?.Invoke(Folder);
        Http = new ConnectionHttp();
        configure?.Invoke(Http);
        Browser = new CallbackBrowser(SignInWorld.SuccessCallback);
        Network = new NetworkSwitch();
        Model = new AppModel(Folder, Clock, Http, Browser, new PassThroughProtector(), network: Network);
    }

    public string Folder { get; }
    public ManualClock Clock { get; } = new();
    public ConnectionHttp Http { get; }
    public NetworkSwitch Network { get; }
    public CallbackBrowser Browser { get; }
    public AppModel Model { get; }

    public const string Email = "gul@example.com";

    public static string IdToken { get; } = Jwt("""
        {"email":"gul@example.com","https://api.openai.com/auth":{"chatgpt_plan_type":"pro"}}
        """);

    public static string Jwt(string payload)
    {
        static string Part(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return Part("""{"alg":"none","typ":"JWT"}""") + "." + Part(payload) + ".sig";
    }

    public void Reply(string json) => Http.Payload = Sse(json);

    public void Release() => Http.Release();

    public void Dispose()
    {
        Http.ReleaseAll();
        Model.Dispose();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    static string Sse(string json)
    {
        var mid = Math.Max(1, json.Length / 2);
        return "data: " + Delta(json[..mid]) + "\n\n" + "data: " + Delta(json[mid..]) + "\n\n";
    }

    static string Delta(string text) =>
        "{\"type\":\"response.output_text.delta\",\"delta\":" + JsonSerializer.Serialize(text) + "}";
}

sealed class NetworkSwitch : INetwork
{
    public bool Online { get; private set; } = true;
    public event Action? Changed;

    public void Set(bool online)
    {
        if (Online == online)
            return;
        Online = online;
        Changed?.Invoke();
    }
}

sealed class ConnectionHttp : HttpMessageHandler
{
    readonly object gate = new();
    readonly List<Held> held = [];

    public string Payload { get; set; } = "";
    public int RefreshCalls { get; private set; }
    public bool HoldRefresh { get; set; }
    readonly TaskCompletionSource refreshGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task RefreshStarted => started.Task;
    public string? RefreshUri { get; private set; }
    public Dictionary<string, string> RefreshForm { get; private set; } = [];
    public int RefreshStatus { get; set; } = 200;
    public string RefreshBody { get; set; } = """
        {"access_token":"access-2","refresh_token":"refresh-2","id_token":"id-2","expires_in":3600,"token_type":"Bearer"}
        """;
    public List<InterpretCall> Calls { get; } = [];
    public int ResponseStatus { get; set; } = 200;
    public int UnauthorizedResponses { get; set; }
    public bool FailResponse { get; set; }
    public List<string> RevokeForms { get; } = [];
    public string? RevokeUri { get; private set; }
    public string ModelsBody { get; set; } = """{"models":[{"slug":"gpt-5.6-sol","priority":1}]}""";

    public void ReleaseRefresh() => refreshGate.TrySetResult();

    public void Release()
    {
        Held item;
        lock (gate)
        {
            item = held[0];
            held.RemoveAt(0);
        }

        item.Done.TrySetResult((ResponseStatus, Payload));
    }

    public void ReleaseAll()
    {
        refreshGate.TrySetResult();
        Held[] pending;
        lock (gate)
        {
            pending = held.ToArray();
            held.Clear();
        }

        foreach (var item in pending)
            item.Done.TrySetResult((ResponseStatus, Payload));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/oauth/token", StringComparison.Ordinal))
        {
            var form = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (form.Contains("grant_type=refresh_token", StringComparison.Ordinal))
            {
                RefreshCalls++;
                RefreshUri = request.RequestUri!.GetLeftPart(UriPartial.Path);
                RefreshForm = Form(form);
                started.TrySetResult();
                if (HoldRefresh)
                    await refreshGate.Task;
                return new HttpResponseMessage((HttpStatusCode)RefreshStatus)
                {
                    Content = new StringContent(RefreshBody, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"access-1","refresh_token":"refresh-1","id_token":"id-1","expires_in":3600}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        }

        if (path.Contains("/oauth/revoke", StringComparison.Ordinal))
        {
            RevokeUri = request.RequestUri!.GetLeftPart(UriPartial.Path);
            RevokeForms.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        if (path.Contains("/v1/models", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ModelsBody, Encoding.UTF8, "application/json"),
            };
        }

        if (!path.Contains("/v1/responses", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }

        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        if (FailResponse)
            throw new HttpRequestException("The network failed.");
        var status = ResponseStatus;
        if (UnauthorizedResponses > 0)
        {
            UnauthorizedResponses--;
            status = 401;
        }

        var done = new TaskCompletionSource<(int Status, string Body)>();
        lock (gate)
        {
            Calls.Add(new InterpretCall(
                request.Method.Method,
                request.RequestUri?.GetLeftPart(UriPartial.Path) ?? "",
                body,
                request.Headers.Authorization?.ToString()));
            if (status != 401)
                held.Add(new Held(done));
        }

        if (status == 401)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var result = await done.Task;
        return new HttpResponseMessage((HttpStatusCode)result.Status)
        {
            Content = new StringContent(result.Body, Encoding.UTF8, "text/event-stream"),
        };
    }

    static Dictionary<string, string> Form(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var rawKey = (eq < 0 ? part : part[..eq]).Replace("+", "%20", StringComparison.Ordinal);
            var rawValue = (eq < 0 ? "" : part[(eq + 1)..]).Replace("+", "%20", StringComparison.Ordinal);
            result[Uri.UnescapeDataString(rawKey)] = Uri.UnescapeDataString(rawValue);
        }

        return result;
    }

    sealed record Held(TaskCompletionSource<(int Status, string Body)> Done);
}

