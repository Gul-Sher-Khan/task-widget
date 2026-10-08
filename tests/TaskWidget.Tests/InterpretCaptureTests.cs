using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class InterpretCaptureTests
{
    [Fact]
    public async Task A_signed_in_capture_shows_interpreting_then_two_ranked_tasks()
    {
        using var world = new InterpretWorld();
        world.Reply("""
            {"tasks":[
              {"title":"Email Sarah the invoice","details":"","priority":"medium","effort":"short"},
              {"title":"Buy milk","details":"","priority":"high","effort":"quick"}
            ]}
            """);

        world.Model.UpdateDraft("email Sarah the invoice and buy milk");
        var committing = world.Model.CommitCapture();

        var pending = Assert.Single(world.Model.Tasks);
        Assert.True(pending.IsPending);
        Assert.Equal("Interpreting…", pending.PendingText);
        Assert.Equal("email Sarah the invoice and buy milk", pending.Title);
        Assert.Equal("", world.Model.CaptureText);

        world.Release();
        await committing;

        Assert.Equal(["Buy milk", "Email Sarah the invoice"], world.Model.Tasks.Select(task => task.Title));
        Assert.All(world.Model.Tasks, task => Assert.False(task.IsPending));
        Assert.Equal(Priority.High, world.Model.Tasks[0].Priority);
        Assert.Equal(Effort.Quick, world.Model.Tasks[0].Effort);
        Assert.Equal("", world.Model.Tasks[0].Details);
        Assert.Equal(Priority.Medium, world.Model.Tasks[1].Priority);
        Assert.Equal(Effort.Short, world.Model.Tasks[1].Effort);
        Assert.Equal("", world.Model.Tasks[1].Details);
        Assert.False(string.IsNullOrEmpty(world.Model.Tasks[0].Capture));
        Assert.Equal(world.Model.Tasks[0].Capture, world.Model.Tasks[1].Capture);
    }

    [Fact]
    public async Task Two_captures_in_flight_resolve_on_their_own_rows()
    {
        using var world = new InterpretWorld();
        world.Model.UpdateDraft("email Sarah the invoice and buy milk");
        var first = world.Model.CommitCapture();
        world.Clock.Advance(TimeSpan.FromMilliseconds(1));
        world.Model.UpdateDraft("call the bank");
        var second = world.Model.CommitCapture();

        Assert.Equal(
            ["call the bank", "email Sarah the invoice and buy milk"],
            world.Model.Tasks.Select(task => task.Title));
        Assert.All(world.Model.Tasks, task =>
        {
            Assert.True(task.IsPending);
            Assert.Equal("Interpreting…", task.PendingText);
        });

        world.Finish("call the bank", """
            {"tasks":[{"title":"Call the bank","details":"","priority":"high","effort":"quick"}]}
            """);
        await second;

        Assert.Equal("email Sarah the invoice and buy milk", world.Model.Tasks[0].Title);
        Assert.True(world.Model.Tasks[0].IsPending);
        Assert.Equal("Call the bank", Assert.Single(world.Model.Tasks, task => !task.IsPending).Title);

        world.Finish("email Sarah the invoice and buy milk", """
            {"tasks":[
              {"title":"Email Sarah the invoice","details":"","priority":"medium","effort":"short"},
              {"title":"Buy milk","details":"","priority":"high","effort":"quick"}
            ]}
            """);
        await first;

        Assert.DoesNotContain(world.Model.Tasks, task => task.IsPending);
        Assert.Equal(
            ["Buy milk", "Call the bank", "Email Sarah the invoice"],
            world.Model.Tasks.Select(task => task.Title));
        Assert.Equal(world.Model.Tasks[0].Capture, world.Model.Tasks[2].Capture);
        Assert.NotEqual(world.Model.Tasks[0].Capture, world.Model.Tasks[1].Capture);
    }

    [Fact]
    public async Task Automatic_picks_the_first_preferred_model_that_is_listed()
    {
        using var world = new InterpretWorld(arrange =>
        {
            arrange.Clock.Set(new DateTimeOffset(2026, 10, 7, 15, 0, 0, TimeSpan.Zero));
            File.WriteAllText(Path.Combine(arrange.Folder, "settings.json"), """
                {
                  "schemaVersion": 1,
                  "modelsCachedAt": "2026-10-07T15:00:00+00:00",
                  "models": [
                    { "slug": "gpt-6-astra", "priority": 1 },
                    { "slug": "gpt-other", "priority": 100 },
                    { "slug": "gpt-5.6-terra", "priority": 3 }
                  ]
                }
                """);
        });
        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");

        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();

        Assert.Equal(0, world.Http.ModelsCalls);
        Assert.Equal("gpt-5.6-terra", ModelName(Assert.Single(world.Http.Calls)));
        world.Release();
        await committing;
    }

    [Fact]
    public async Task Automatic_uses_the_highest_priority_when_nothing_preferred_is_listed()
    {
        using var world = new InterpretWorld(arrange =>
        {
            arrange.Clock.Set(new DateTimeOffset(2026, 10, 7, 15, 0, 0, TimeSpan.Zero));
            File.WriteAllText(Path.Combine(arrange.Folder, "settings.json"), """
                {
                  "schemaVersion": 1,
                  "modelsCachedAt": "2026-10-07T15:00:00+00:00",
                  "models": [
                    { "slug": "gpt-custom", "priority": 2 },
                    { "slug": "gpt-other", "priority": 9 },
                    { "slug": "gpt-spare", "priority": 9 }
                  ]
                }
                """);
        });
        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");

        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();

        Assert.Equal("gpt-other", ModelName(Assert.Single(world.Http.Calls)));
        world.Release();
        await committing;
    }

    [Fact]
    public async Task Sign_in_caches_the_model_list_and_a_capture_refreshes_it_after_a_day()
    {
        using var signedIn = new SignInWorld(configure: http => http.ModelsBody = """
            {"models":[{"slug":"gpt-5.6-sol","priority":4},{"slug":"gpt-6-astra","priority":1}]}
            """);
        var cachedAt = signedIn.Clock.UtcNow;
        await signedIn.Model.SignIn();
        signedIn.Model.Dispose();

        using (var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(signedIn.Folder, "settings.json"))))
        {
            Assert.Equal(cachedAt, settings.RootElement.GetProperty("modelsCachedAt").GetDateTimeOffset());
            Assert.Equal("gpt-5.6-sol", settings.RootElement.GetProperty("models")[0].GetProperty("slug").GetString());
            Assert.Equal(4, settings.RootElement.GetProperty("models")[0].GetProperty("priority").GetInt32());
        }

        File.Copy(
            Path.Combine(signedIn.Folder, "settings.json"),
            Path.Combine(signedIn.Folder, "settings-copy.json"),
            overwrite: true);
        File.Copy(Path.Combine(signedIn.Folder, AppModel.TokenFileName), Path.Combine(signedIn.Folder, "tokens-copy.bin"), overwrite: true);

        var folder = Directory.CreateTempSubdirectory("tw-models").FullName;
        try
        {
            File.Copy(Path.Combine(signedIn.Folder, "settings-copy.json"), Path.Combine(folder, "settings.json"));
            File.Copy(Path.Combine(signedIn.Folder, "tokens-copy.bin"), Path.Combine(folder, AppModel.TokenFileName));
            var clock = new ManualClock();
            clock.Set(cachedAt.AddHours(23));
            var http = new InterpretHttp();
            var model = new AppModel(folder, clock, http, browser: null, new PassThroughProtector());
            try
            {
                http.Payload = Sse("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");
                model.UpdateDraft("buy milk");
                var first = model.CommitCapture();
                Assert.Equal(0, http.ModelsCalls);
                Assert.Equal("gpt-5.6-sol", ModelName(Assert.Single(http.Calls)));
                http.Release();
                await first;

                clock.Set(cachedAt.AddDays(1));
                http.ModelsBody = """{"models":[{"slug":"gpt-custom","priority":2},{"slug":"gpt-best","priority":8}]}""";
                model.UpdateDraft("call the bank");
                var second = model.CommitCapture();
                Assert.Equal(1, http.ModelsCalls);
                Assert.Equal("gpt-best", ModelName(http.Calls[^1]));
                http.Release();
                await second;

                model.UpdateDraft("water plants");
                var third = model.CommitCapture();
                Assert.Equal(1, http.ModelsCalls);
                Assert.Equal("gpt-best", ModelName(http.Calls[^1]));
                http.Release();
                await third;
            }
            finally
            {
                http.ReleaseAll();
                model.Dispose();
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    static string ModelName(InterpretCall call)
    {
        using var doc = JsonDocument.Parse(call.Body);
        return doc.RootElement.GetProperty("model").GetString()!;
    }

    static string Sse(string json)
    {
        var mid = Math.Max(1, json.Length / 2);
        return "data: " + Delta(json[..mid]) + "\n\n" + "data: " + Delta(json[mid..]) + "\n\n";
    }

    static string Delta(string text) =>
        "{\"type\":\"response.output_text.delta\",\"delta\":" + JsonSerializer.Serialize(text) + "}";

    [Fact]
    public async Task The_dock_stays_busy_while_any_capture_is_in_flight()
    {
        using var world = new InterpretWorld();
        Assert.False(world.Model.IsProcessing);

        world.Model.UpdateDraft("buy milk");
        var first = world.Model.CommitCapture();
        Assert.True(world.Model.IsProcessing);

        world.Model.UpdateDraft("call the bank");
        var second = world.Model.CommitCapture();
        Assert.True(world.Model.IsProcessing);

        world.Finish("buy milk", """{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");
        await first;
        Assert.True(world.Model.IsProcessing);

        world.Finish("call the bank", """{"tasks":[{"title":"Call the bank","details":"","priority":"high","effort":"quick"}]}""");
        await second;
        Assert.False(world.Model.IsProcessing);
    }

    [Fact]
    public async Task The_capture_is_saved_before_chatgpt_sees_the_request()
    {
        using var world = new InterpretWorld();
        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");
        world.Http.OnSend = () =>
        {
            using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.Folder, "tasks.json")));
            var capture = Assert.Single(saved.RootElement.GetProperty("captures").EnumerateArray());
            Assert.Equal("buy milk", capture.GetProperty("text").GetString());
            Assert.Equal("pending", capture.GetProperty("state").GetString());
            Assert.Empty(saved.RootElement.GetProperty("tasks").EnumerateArray());
        };

        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();
        world.Release();
        await committing;

        Assert.Equal("Buy milk", Assert.Single(world.Model.Tasks).Title);
        Assert.Equal(world.Model.Tasks[0].Capture, CaptureId(world.Folder));
    }

    static string CaptureId(string folder)
    {
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "tasks.json")));
        return saved.RootElement.GetProperty("captures")[0].GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task The_request_sends_only_the_capture_in_the_v4_shape()
    {
        using var world = new InterpretWorld();
        world.Clock.Set(new DateTimeOffset(2026, 10, 7, 15, 0, 0, TimeSpan.Zero));
        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"medium","effort":"short"}]}""");

        world.Model.UpdateDraft("email Sarah the invoice and buy milk");
        var committing = world.Model.CommitCapture();

        var call = Assert.Single(world.Http.Calls);
        Assert.Equal("POST", call.Method);
        Assert.Equal("https://api.openai.com/v1/responses", call.Uri);
        Assert.Equal("Bearer access-1", call.Authorization);

        using var doc = JsonDocument.Parse(call.Body);
        var root = doc.RootElement;
        Assert.Equal(
            ["input", "instructions", "model", "reasoning", "store", "stream", "text"],
            root.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal("low", root.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal(1, root.GetProperty("reasoning").GetPropertyCount());
        Assert.Equal(V4Instructions, root.GetProperty("instructions").GetString());

        var message = Assert.Single(root.GetProperty("input").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        var content = Assert.Single(message.GetProperty("content").EnumerateArray());
        Assert.Equal("input_text", content.GetProperty("type").GetString());
        Assert.Equal("email Sarah the invoice and buy milk", content.GetProperty("text").GetString());
        Assert.Equal(2, content.GetPropertyCount());

        var format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.Equal("capture_tasks", format.GetProperty("name").GetString());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(V4Schema), JsonNode.Parse(format.GetProperty("schema").GetRawText())));

        world.Release();
        await committing;
    }

    const string V4Instructions = """
        You turn one dictated note (a "Capture") into tasks for the user's personal to-do list.

        The Capture was dictated, so it may contain filler, false starts, self-corrections and transcription slips. When the user corrects themselves ("Tuesday, no, Thursday"), keep only the correction. Ignore filler.

        Splitting
        - Make one task per action the user would tick off on its own.
        - Never break an action into steps or sub-tasks, even if the user lists the steps. Put steps they said into details instead.
        - One action with several objects stays one task ("Buy milk, eggs and bread").
        - Thinking, deciding or checking something counts as an action ("Decide whether to move to Postgres").
        - Drop chatter, feelings, and facts that ask for no action. If nothing in the Capture asks for action, return an empty tasks list.
        - When the Capture names a meeting or appointment and also something to do for it, make a task only for that action and put the meeting in its details. A meeting mentioned on its own is a task.
        - Keep the tasks in the order they were said.

        title
        - One line, at most 60 characters, starting with a verb in the imperative ("Email Sarah the invoice").
        - Use the Capture's own words wherever you can. Add nothing that the Capture doesn't say.
        - Keep the user's point of view: "I" and "my" as they said them, never "you". Write it in the language of the Capture. Sentence case, no full stop at the end.

        details
        - Only context stated in the Capture that the title leaves out: who, deadlines, where, why, amounts, steps the user listed.
        - Never invent steps, advice or anything else the user didn't say. If there is nothing to add, use "".
        - Turn relative dates into a short weekday and date, using today's date: "before Friday" becomes "Before Fri 9 Oct". No year unless it isn't this year. Times as the user said them ("3pm", "before 9"). If a relative date could mean two dates ("next Saturday", "this weekend"), keep the user's words instead, and don't treat it as within 3 days.
        - Write details as one short phrase or sentence fragment, with no full stop at the end.

        priority
        - high: only when the Capture itself gives a reason: a deadline within 3 days of today, someone waiting on or blocked by the user, or a stated bad consequence (money, health, legal, something breaking).
        - medium: it matters, but nothing breaks this week. A deadline more than 3 days away is medium. This is the default: an ordinary errand, email or chore with no reason given is medium.
        - low: nice to do; nothing happens if it slips ("someday", "at some point", "if I get time").
        - Words the user says about urgency ("urgent", "no rush", "whenever") win over your own judgement.

        effort (how long the action itself takes)
        - quick: 15 minutes or less (a call, a short message, a purchase on the way).
        - short: up to an hour.
        - long: more than an hour (writing, building, refactoring, studying).

        Today is Wednesday 7 Oct 2026.
        """;

    const string V4Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["tasks"],
          "properties": {
            "tasks": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["title", "details", "priority", "effort"],
                "properties": {
                  "title": { "type": "string" },
                  "details": { "type": "string" },
                  "priority": { "type": "string", "enum": ["high", "medium", "low"] },
                  "effort": { "type": "string", "enum": ["quick", "short", "long"] }
                }
              }
            }
          }
        }
        """;
}

sealed class InterpretWorld : IDisposable
{
    public InterpretWorld(Action<InterpretWorld>? arrange = null)
    {
        Folder = Directory.CreateTempSubdirectory("tw-interpret").FullName;
        File.WriteAllBytes(
            Path.Combine(Folder, AppModel.TokenFileName),
            Encoding.UTF8.GetBytes("""
                {
                  "clientId": "oaiapp_test",
                  "extAgentHostId": "urn:uuid:00000000-0000-0000-0000-000000000001",
                  "accessToken": "access-1",
                  "refreshToken": "refresh-1",
                  "idToken": "id-1",
                  "accessExpiresAt": "2026-10-08T00:00:00+00:00",
                  "refreshExpiresAt": "2026-11-06T00:00:00+00:00"
                }
                """));
        Http = new InterpretHttp();
        arrange?.Invoke(this);
        Model = new AppModel(Folder, Clock, Http, browser: null, new PassThroughProtector());
    }

    public string Folder { get; }
    public ManualClock Clock { get; } = new();
    public InterpretHttp Http { get; }
    public AppModel Model { get; }

    public void Reply(string json) => Http.Payload = Sse(json);

    public void Release() => Http.Release();

    public void Finish(string captureText, string json) => Http.Finish(captureText, Sse(json));

    public void Dispose()
    {
        Http.ReleaseAll();
        Model.Dispose();
        Directory.Delete(Folder, recursive: true);
    }

    static string Sse(string json)
    {
        var mid = Math.Max(1, json.Length / 2);
        return "data: " + Delta(json[..mid]) + "\n\n" + "data: " + Delta(json[mid..]) + "\n\n";
    }

    static string Delta(string text) =>
        "{\"type\":\"response.output_text.delta\",\"delta\":" + JsonSerializer.Serialize(text) + "}";
}

sealed record InterpretCall(string Method, string Uri, string Body, string? Authorization);

sealed class InterpretHttp : HttpMessageHandler
{
    readonly List<Held> held = [];

    public string Payload { get; set; } = "";
    public string ModelsBody { get; set; } = """{"models":[{"slug":"gpt-5.6-sol","priority":1}]}""";
    public int ModelsCalls { get; private set; }
    public List<string> Bodies { get; } = [];
    public List<InterpretCall> Calls { get; } = [];
    public Action? OnSend { get; set; }

    public void Release() => Finish(_ => true, Payload);

    public void Finish(string captureText, string payload) =>
        Finish(body => body.Contains(captureText, StringComparison.Ordinal), payload);

    public void ReleaseAll()
    {
        foreach (var item in held.ToArray())
            item.Done.TrySetResult(Payload);
        held.Clear();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/oauth/token", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"access-1","refresh_token":"refresh-1","id_token":"id-1","expires_in":3600}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        }

        if (path.Contains("/v1/models", StringComparison.Ordinal))
        {
            ModelsCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ModelsBody, Encoding.UTF8, "application/json"),
            };
        }

        if (!path.Contains("/v1/responses", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        }

        OnSend?.Invoke();
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Bodies.Add(body);
        Calls.Add(new InterpretCall(
            request.Method.Method,
            request.RequestUri?.GetLeftPart(UriPartial.Path) ?? "",
            body,
            request.Headers.Authorization?.ToString()));
        var done = new TaskCompletionSource<string>();
        held.Add(new Held(body, done));
        var payload = await done.Task;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/event-stream"),
        };
    }

    void Finish(Func<string, bool> match, string payload)
    {
        var item = held.First(heldItem => match(heldItem.Body));
        held.Remove(item);
        item.Done.TrySetResult(payload);
    }

    sealed record Held(string Body, TaskCompletionSource<string> Done);
}
