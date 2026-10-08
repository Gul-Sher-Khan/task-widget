using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TaskWidget.Core;

public sealed partial class AppModel
{
    const string ResponsesEndpoint = "https://api.openai.com/v1/responses";
    static readonly string[] PreferredModels = ["gpt-5.6-sol", "gpt-reserve", "gpt-5.6-terra", "gpt-6-astra"];

    async Task Interpret(string text)
    {
        var id = Guid.NewGuid().ToString("D");
        var record = new CaptureRecord { Id = id, Text = text, State = "pending" };
        captures.Add(record);
        var pending = new TaskRow(text, Priority.Medium, Effort.Short, "", id, pending: true);
        pending.CreatedAt = clock.Now;
        Tasks.Insert(0, pending);
        CaptureText = "";
        Flush();
        await Run(record, pending);
    }

    void Hold(string text)
    {
        var id = Guid.NewGuid().ToString("D");
        var record = new CaptureRecord { Id = id, Text = text, State = "waiting", Cause = WaitingCause() };
        captures.Add(record);
        var row = new TaskRow(text, Priority.Medium, Effort.Short, "", id);
        row.CreatedAt = clock.Now;
        Tasks.Insert(0, row);
        CaptureText = "";
        Wait(record, row, record.Cause);
    }

    void ReleaseWaiting()
    {
        if (Volatile.Read(ref disposed) != 0)
            return;

        foreach (var row in Tasks.Where(task => task.IsWaiting).ToArray())
        {
            var record = captures.FirstOrDefault(capture => capture.Id == row.Capture);
            if (record is null)
                continue;
            if (!HasConnection || !Online || httpClient is null)
            {
                Wait(record, row, WaitingCause());
                continue;
            }

            _ = Run(record, row);
        }
    }

    string WaitingCause() => !HasConnection
        ? (settingsFile.WelcomeRetired ? "signed-out" : "no-connection")
        : "offline";

    async Task Run(CaptureRecord record, TaskRow row)
    {
        var at = row.CreatedAt == default ? clock.Now : row.CreatedAt;
        row.IsWaiting = false;
        row.LightsDot = false;
        row.Reason = "";
        row.IsPending = true;
        row.PendingText = "Interpreting…";
        record.State = "pending";
        record.Cause = "";
        OnPropertyChanged(nameof(Attention));
        Flush();
        BeginProcessing();
        try
        {
            List<CaptureContract.ParsedTask>? parsed;
            try
            {
                parsed = await RequestTasks(record.Text);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
            {
                return;
            }

            if (parsed is null || !Tasks.Contains(row))
            {
                if (parsed is null && Tasks.Contains(row) && !HasConnection)
                    Wait(record, row, "signed-out");
                else if (parsed is null && Tasks.Contains(row) && !Online)
                    Wait(record, row, "offline");
                return;
            }

            Tasks.Remove(row);
            record.State = "interpreted";
            record.Interpreted = record.Text;
            for (var i = 0; i < parsed.Count; i++)
            {
                var task = parsed[i];
                Place(new TaskRow(task.Title, task.Priority, task.Effort, task.Details, record.Id), at, i);
            }

            MarkDirty();
        }
        finally
        {
            EndProcessing();
        }
    }

    int capturesInFlight;

    void BeginProcessing()
    {
        if (Interlocked.Increment(ref capturesInFlight) == 1)
            IsProcessing = true;
    }

    void EndProcessing()
    {
        if (Interlocked.Decrement(ref capturesInFlight) == 0)
            IsProcessing = false;
    }

    void Wait(CaptureRecord record, TaskRow row, string cause)
    {
        record.State = "waiting";
        record.Cause = cause;
        row.IsPending = false;
        row.PendingText = "";
        row.IsWaiting = true;
        row.Reason = WaitingReason(cause);
        row.LightsDot = cause != "offline";
        OnPropertyChanged(nameof(Attention));
        OnPropertyChanged(nameof(OpenTaskCount));
        OnPropertyChanged(nameof(OpenHighCount));
        OnPropertyChanged(nameof(OpenMediumCount));
        OnPropertyChanged(nameof(OpenLowCount));
        Flush();
    }

    static string WaitingReason(string cause) => cause switch
    {
        "offline" => "Waiting for connection",
        "no-connection" => "Connect your ChatGPT account",
        _ => "Signed out",
    };

    void Flush()
    {
        MarkDirty();
        saveTimer?.Dispose();
        saveTimer = null;
        WriteIfDirty();
    }

    async Task<List<CaptureContract.ParsedTask>?> RequestTasks(string text)
    {
        await EnsureModels();
        if (settingsFile.Models.Count == 0)
            return null;

        using var response = await SendAuthorized(() => new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint)
        {
            Content = new StringContent(RequestBody(text), Encoding.UTF8, "application/json"),
        });
        if (response is null || !response.IsSuccessStatusCode)
            return null;

        var body = await response.Content.ReadAsStringAsync();
        var parsed = CaptureContract.Parse(CaptureContract.OutputText(body));
        return parsed.Ok && parsed.Tasks.Count > 0 ? parsed.Tasks : null;
    }

    string RequestBody(string text)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", ChooseModel());
            writer.WriteBoolean("stream", true);
            writer.WriteBoolean("store", false);
            writer.WriteStartObject("reasoning");
            writer.WriteString("effort", "low");
            writer.WriteEndObject();
            writer.WriteString("instructions", CaptureContract.Instructions(clock.Now));
            writer.WriteStartArray("input");
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteStartArray("content");
            writer.WriteStartObject();
            writer.WriteString("type", "input_text");
            writer.WriteString("text", text);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteStartObject("text");
            writer.WriteStartObject("format");
            writer.WriteString("type", "json_schema");
            writer.WriteString("name", "capture_tasks");
            writer.WriteBoolean("strict", true);
            writer.WritePropertyName("schema");
            writer.WriteRawValue(CaptureContract.Schema);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    async Task<HttpResponseMessage?> SendAuthorized(Func<HttpRequestMessage> create)
    {
        if (httpClient is null || !await EnsureAccess(force: false))
            return null;

        var response = await Send(create());
        if ((int)response.StatusCode != 401)
            return response;

        response.Dispose();
        if (!await EnsureAccess(force: true))
            return null;

        return await Send(create());

        async Task<HttpResponseMessage> Send(HttpRequestMessage request)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return await httpClient.SendAsync(request);
        }
    }

    async Task EnsureModels()
    {
        if (settingsFile.Models.Count > 0
            && settingsFile.ModelsCachedAt != default
            && clock.UtcNow < settingsFile.ModelsCachedAt.AddDays(1))
            return;

        using var response = await SendAuthorized(() => new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint));
        if (response is null || !response.IsSuccessStatusCode)
            return;

        var models = ReadModels(await response.Content.ReadAsStringAsync());
        if (models.Count == 0)
            return;

        settingsFile.Models = models;
        settingsFile.ModelsCachedAt = clock.UtcNow;
        NoteModels(models);
    }

    string ChooseModel()
    {
        if (settingsFile.ChosenModel.Length > 0
            && settingsFile.Models.Any(model => model.Slug == settingsFile.ChosenModel))
            return settingsFile.ChosenModel;

        return AutomaticSlug();
    }

    string AutomaticSlug()
    {
        foreach (var slug in PreferredModels)
        {
            if (settingsFile.Models.Any(model => model.Slug == slug))
                return slug;
        }

        return settingsFile.Models.Count == 0 ? "" : settingsFile.Models.MaxBy(model => model.Priority)!.Slug;
    }
}
