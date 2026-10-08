using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TaskWidget.Core;

public sealed partial class AppModel
{
    const string ResponsesEndpoint = "https://api.openai.com/v1/responses";
    static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    static readonly string[] PreferredModels = ["gpt-5.6-sol", "gpt-reserve", "gpt-5.6-terra", "gpt-6-astra"];
    readonly CancellationTokenSource captureCalls = new();

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
        await RunCapture(pending, record, text, at);
    }

    async Task RunCapture(TaskRow row, CaptureRecord record, string text, DateTimeOffset at)
    {
        BeginProcessing();
        try
        {
            CaptureOutcome outcome;
            try
            {
                outcome = await RequestWithRetry(text, captureCalls.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!Tasks.Contains(row))
                return;

            if (outcome.Tasks is not null)
            {
                Tasks.Remove(row);
                record.State = "interpreted";
                record.Interpreted = text;
                record.Cause = "";
                for (var i = 0; i < outcome.Tasks.Count; i++)
                {
                    var task = outcome.Tasks[i];
                    Place(new TaskRow(task.Title, task.Priority, task.Effort, task.Details, record.Id), at, i);
                }

                MarkDirty();
                NoteAttention();
                return;
            }

            if (outcome.Kind is CaptureKind.None)
                return;

            ApplyFailure(row, record, outcome.Kind);
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

    void Flush()
    {
        MarkDirty();
        saveTimer?.Dispose();
        saveTimer = null;
        WriteIfDirty();
    }

    async Task<CaptureOutcome> RequestWithRetry(string text, CancellationToken cancel)
    {
        var first = await Attempt(text, cancel);
        if (first.Tasks is not null || !first.Retry)
            return first;

        await Wait(RetryDelay, cancel);
        return await Attempt(text, cancel);
    }

    async Task<CaptureOutcome> Attempt(string text, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        var timer = clock.Schedule(AttemptTimeout, () => timeout.Cancel());
        try
        {
            await EnsureModels(timeout.Token);
            if (settingsFile.Models.Count == 0)
                return CaptureOutcome.Failed(CaptureKind.Unreachable, retry: true);

            using var request = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint)
            {
                Content = new StringContent(RequestBody(text), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient!.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return Classify(response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return CaptureOutcome.Failed(CaptureKind.Unreachable, retry: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return CaptureOutcome.Failed(CaptureKind.Unreachable, retry: true);
        }
        finally
        {
            timer.Dispose();
        }
    }

    static CaptureOutcome Classify(System.Net.HttpStatusCode status, string body)
    {
        var code = (int)status;
        if (code == 429)
            return CaptureOutcome.Failed(CaptureKind.RateLimited, retry: false);
        if (code == 403 && body.Contains("subscription_sharing_user_not_eligible", StringComparison.Ordinal))
            return CaptureOutcome.Failed(CaptureKind.Plan, retry: false);
        if (code >= 500)
            return CaptureOutcome.Failed(CaptureKind.Unreachable, retry: true);
        if (code >= 400)
            return CaptureOutcome.Failed(CaptureKind.Unreachable, retry: false);
        if (ResponseFailed(body))
            return CaptureOutcome.Failed(CaptureKind.Unreachable, retry: true);

        var parsed = CaptureContract.Parse(CaptureContract.OutputText(body));
        if (!parsed.Ok)
            return CaptureOutcome.Failed(CaptureKind.BadOutput, retry: true);
        if (parsed.Tasks.Count == 0)
            return CaptureOutcome.Failed(CaptureKind.ZeroTasks, retry: false);
        return CaptureOutcome.Parsed(parsed.Tasks);
    }

    static bool ResponseFailed(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').TrimStart();
            if (!trimmed.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = trimmed[5..].TrimStart();
            if (data == "[DONE]")
                continue;

            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "response.failed")
                    return true;
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }

    async Task Wait(TimeSpan delay, CancellationToken cancel)
    {
        var done = new TaskCompletionSource();
        var timer = clock.Schedule(delay, () => done.TrySetResult());
        await using var registration = cancel.Register(() =>
        {
            timer.Dispose();
            done.TrySetCanceled(cancel);
        });
        try
        {
            await done.Task;
        }
        finally
        {
            timer.Dispose();
        }
    }

    void ApplyFailure(TaskRow row, CaptureRecord record, CaptureKind kind)
    {
        var (reason, tip, makeTaskFirst) = FailureCopy(kind);
        row.Fail(reason, tip, makeTaskFirst);
        record.State = "failed";
        record.Cause = CauseName(kind);
        Flush();
        NoteAttention();
    }

    static (string Reason, string Tip, bool MakeTaskFirst) FailureCopy(CaptureKind kind) => kind switch
    {
        CaptureKind.RateLimited => (
            "Rate-limited by ChatGPT",
            "ChatGPT returned 429 Too Many Requests. Try again in a minute.",
            false),
        CaptureKind.BadOutput => (
            "Couldn't understand the reply",
            "The reply didn't match the Task format, after one retry.",
            false),
        CaptureKind.ZeroTasks => (
            "No task found in this",
            "ChatGPT found nothing to do in this Capture.",
            true),
        CaptureKind.Plan => (
            "Your ChatGPT plan can't be used here",
            "ChatGPT returned 403: this plan isn't eligible. Go, Plus or Pro works.",
            false),
        _ => (
            "Couldn't reach ChatGPT",
            "No reply within 30 seconds, after one retry.",
            false),
    };

    static string CauseName(CaptureKind kind) => kind switch
    {
        CaptureKind.RateLimited => "rate-limited",
        CaptureKind.BadOutput => "bad-output",
        CaptureKind.ZeroTasks => "zero-tasks",
        CaptureKind.Plan => "plan",
        _ => "unreachable",
    };

    static CaptureKind ParseCause(string cause) => cause switch
    {
        "rate-limited" => CaptureKind.RateLimited,
        "bad-output" => CaptureKind.BadOutput,
        "zero-tasks" => CaptureKind.ZeroTasks,
        "plan" => CaptureKind.Plan,
        _ => CaptureKind.Unreachable,
    };

    public Task Retry(TaskRow row) => Rerun(row, row.Title);

    public void EditCapture(TaskRow row)
    {
        if (row.IsFailed)
            row.BeginEdit();
    }

    public Task SubmitCapture(TaskRow row, string text)
    {
        text = text.Replace("\r\n", "\n").Trim();
        if (!row.IsEditingCapture || text.Length == 0)
            return Task.CompletedTask;

        row.Title = text;
        return Rerun(row, text);
    }

    public void CancelCaptureEdit(TaskRow row) => row.CancelEdit();

    public void MakeTaskAsIs(TaskRow row)
    {
        if (!row.IsFailed && !row.IsEditingCapture)
            return;

        var record = captures.FirstOrDefault(capture => capture.Id == row.Capture);
        var index = Tasks.IndexOf(row);
        if (index < 0)
            return;

        var title = row.Title.Replace("\r\n", "\n").Split('\n')[0].Trim();
        if (title.Length == 0)
            return;

        var cause = record?.Cause ?? CauseOf(row);
        Tasks.RemoveAt(index);
        row.CancelEdit();
        var task = new TaskRow(title, Priority.Medium, Effort.Short, "", row.Capture);
        var at = clock.Now;
        Place(task, at, 0);
        if (record is not null)
        {
            record.State = "as-is";
            record.Cause = "";
        }

        Flush();
        NoteAttention();
        Push(
            "Task added",
            () =>
            {
                Tasks.Remove(task);
                if (!Tasks.Contains(row))
                    Tasks.Insert(Math.Min(index, Tasks.Count), row);
                if (record is not null)
                {
                    record.State = "failed";
                    record.Cause = cause;
                }

                Flush();
                NoteAttention();
            },
            () =>
            {
                Tasks.Remove(row);
                if (!Tasks.Contains(task))
                    Tasks.Insert(IndexFor(task), task);
                if (record is not null)
                {
                    record.State = "as-is";
                    record.Cause = "";
                }

                Flush();
                NoteAttention();
            },
            pill: false);
    }

    public void Discard(TaskRow row)
    {
        if (!row.IsFailed && !row.IsEditingCapture)
            return;

        var record = captures.FirstOrDefault(capture => capture.Id == row.Capture);
        var index = Tasks.IndexOf(row);
        if (index < 0)
            return;

        Tasks.RemoveAt(index);
        row.CancelEdit();
        var cause = record?.Cause ?? "";
        if (record is not null)
            record.State = "discarded";

        Flush();
        NoteAttention();
        Push(
            "Capture discarded",
            () =>
            {
                if (!Tasks.Contains(row))
                    Tasks.Insert(Math.Min(index, Tasks.Count), row);
                if (record is not null)
                {
                    record.State = "failed";
                    record.Cause = cause;
                }

                Flush();
                NoteAttention();
            },
            () =>
            {
                Tasks.Remove(row);
                if (record is not null)
                    record.State = "discarded";
                Flush();
                NoteAttention();
            },
            pill: true);
    }

    Task Rerun(TaskRow row, string text)
    {
        if (httpClient is null)
            return Task.CompletedTask;

        var record = captures.FirstOrDefault(capture => capture.Id == row.Capture);
        if (record is null || (!row.IsFailed && !row.IsEditingCapture))
            return Task.CompletedTask;

        record.Text = text;
        row.Title = text;
        row.BeginInterpret();
        Flush();
        return RunCapture(row, record, text, clock.Now);
    }

    static string CauseOf(TaskRow row) => row.Reason switch
    {
        "Rate-limited by ChatGPT" => "rate-limited",
        "Couldn't understand the reply" => "bad-output",
        "No task found in this" => "zero-tasks",
        "Your ChatGPT plan can't be used here" => "plan",
        _ => "unreachable",
    };

    void NoteAttention() => OnPropertyChanged(nameof(Attention));

    TaskRow FailedRow(CaptureRecord capture)
    {
        var row = new TaskRow(capture.Text, Priority.Medium, Effort.Short, "", capture.Id);
        var (reason, tip, makeTaskFirst) = FailureCopy(ParseCause(capture.Cause));
        row.Fail(reason, tip, makeTaskFirst);
        return row;
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

    async Task EnsureModels(CancellationToken cancel)
    {
        if (settingsFile.Models.Count > 0
            && settingsFile.ModelsCachedAt != default
            && clock.UtcNow < settingsFile.ModelsCachedAt.AddDays(1))
            return;

        using var request = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient!.SendAsync(request, cancel);
        if (!response.IsSuccessStatusCode)
            return;

        var models = ReadModels(await response.Content.ReadAsStringAsync(cancel));
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

    enum CaptureKind
    {
        None,
        Unreachable,
        RateLimited,
        BadOutput,
        ZeroTasks,
        Plan,
    }

    readonly record struct CaptureOutcome(List<CaptureContract.ParsedTask>? Tasks, CaptureKind Kind, bool Retry)
    {
        public static CaptureOutcome Parsed(List<CaptureContract.ParsedTask> tasks) => new(tasks, CaptureKind.None, false);

        public static CaptureOutcome Failed(CaptureKind kind, bool retry) => new(null, kind, retry);
    }
}
