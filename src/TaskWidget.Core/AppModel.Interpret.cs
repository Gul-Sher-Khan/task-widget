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
        var at = clock.Now;
        var record = new CaptureRecord { Id = id, Text = text, State = "pending" };
        captures.Add(record);
        var pending = new TaskRow(text, Priority.Medium, Effort.Short, "", id, pending: true);
        Tasks.Insert(0, pending);
        CaptureText = "";
        Flush();
        BeginProcessing();
        try
        {
            List<CaptureContract.ParsedTask>? parsed;
            try
            {
                parsed = await RequestTasks(text);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
            {
                return;
            }

            if (parsed is null || !Tasks.Contains(pending))
                return;

            Tasks.Remove(pending);
            record.State = "interpreted";
            record.Interpreted = text;
            for (var i = 0; i < parsed.Count; i++)
            {
                var task = parsed[i];
                Place(new TaskRow(task.Title, task.Priority, task.Effort, task.Details, id), at, i);
            }

            MarkDirty();
        }
        finally
        {
            EndProcessing();
        }
    }

    int inFlight;

    void BeginProcessing()
    {
        if (Interlocked.Increment(ref inFlight) == 1)
            IsProcessing = true;
    }

    void EndProcessing()
    {
        if (Interlocked.Decrement(ref inFlight) == 0)
            IsProcessing = false;
    }

    void Flush()
    {
        MarkDirty();
        clock.Cancel(saveTimer);
        WriteIfDirty();
    }

    async Task<List<CaptureContract.ParsedTask>?> RequestTasks(string text)
    {
        await EnsureModels();
        if (settingsFile.Models.Count == 0)
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint)
        {
            Content = new StringContent(RequestBody(text), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient!.SendAsync(request);
        if (!response.IsSuccessStatusCode)
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

    async Task EnsureModels()
    {
        if (settingsFile.Models.Count > 0
            && settingsFile.ModelsCachedAt != default
            && clock.UtcNow < settingsFile.ModelsCachedAt.AddDays(1))
            return;

        using var request = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient!.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return;

        var models = ReadModels(await response.Content.ReadAsStringAsync());
        if (models.Count == 0)
            return;

        settingsFile.Models = models;
        settingsFile.ModelsCachedAt = clock.UtcNow;
        MarkDirty();
    }

    string ChooseModel()
    {
        foreach (var slug in PreferredModels)
        {
            if (settingsFile.Models.Any(model => model.Slug == slug))
                return slug;
        }

        return settingsFile.Models.MaxBy(model => model.Priority)!.Slug;
    }
}
