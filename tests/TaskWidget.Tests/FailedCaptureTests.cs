using System.Net;
using System.Text.Json;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class FailedCaptureTests
{
    [Fact]
    public async Task A_server_error_is_retried_once_then_stays_as_couldnt_reach_chatgpt()
    {
        using var world = new InterpretWorld();
        world.Http.StatusCode = HttpStatusCode.InternalServerError;

        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();

        var pending = Assert.Single(world.Model.Tasks);
        Assert.True(pending.IsPending);
        Assert.Equal("Interpreting…", pending.PendingText);
        Assert.False(world.Model.Attention);

        world.Release();
        Assert.True(pending.IsPending);
        Assert.Single(world.Http.Calls);

        world.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, world.Http.Calls.Count);
        Assert.True(pending.IsPending);

        world.Release();
        await committing;

        var failed = Assert.Single(world.Model.Tasks);
        Assert.Same(pending, failed);
        Assert.False(failed.IsPending);
        Assert.True(failed.IsFailed);
        Assert.True(failed.ShowFailed);
        Assert.Equal("buy milk", failed.Title);
        Assert.Equal("Couldn't reach ChatGPT", failed.Reason);
        Assert.Equal("No reply within 30 seconds, after one retry.", failed.ReasonTip);
        Assert.True(failed.RetryFirst);
        Assert.False(failed.MakeTaskFirst);
        Assert.True(world.Model.Attention);
        Assert.Equal(0, world.Model.OpenTaskCount);
        Assert.Equal(2, world.Http.Calls.Count);
    }

    [Fact]
    public async Task A_capture_times_out_after_thirty_seconds_and_one_retry()
    {
        using var world = new InterpretWorld();
        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();

        var pending = Assert.Single(world.Model.Tasks);
        Assert.Single(world.Http.Calls);

        world.Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.True(pending.IsPending);
        Assert.Single(world.Http.Calls);

        world.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(pending.IsPending);
        Assert.Single(world.Http.Calls);

        world.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, world.Http.Calls.Count);
        Assert.True(pending.IsPending);

        world.Clock.Advance(TimeSpan.FromSeconds(30));
        await committing;

        Assert.Same(pending, Assert.Single(world.Model.Tasks));
        Assert.True(pending.IsFailed);
        Assert.Equal("Couldn't reach ChatGPT", pending.Reason);
        Assert.Equal("No reply within 30 seconds, after one retry.", pending.ReasonTip);
        Assert.True(pending.RetryFirst);
        Assert.True(world.Model.Attention);
        Assert.Equal(2, world.Http.Calls.Count);
    }

    [Fact]
    public async Task A_network_error_is_retried_once_and_can_still_become_tasks()
    {
        using var world = new InterpretWorld();
        world.Http.NetworkFaults = 1;
        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"low","effort":"quick"}]}""");

        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();

        Assert.Single(world.Http.Calls);
        Assert.True(Assert.Single(world.Model.Tasks).IsPending);

        world.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, world.Http.Calls.Count);
        world.Release();
        await committing;

        var task = Assert.Single(world.Model.Tasks);
        Assert.Equal("Buy milk", task.Title);
        Assert.False(task.IsFailed);
        Assert.False(world.Model.Attention);
    }

    [Fact]
    public async Task A_failed_response_event_is_retried_once()
    {
        using var world = new InterpretWorld();
        world.Http.Payload = "data: {\"type\":\"response.failed\",\"response\":{\"status\":\"failed\"}}\n\n";

        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();
        world.Release();
        Assert.Single(world.Http.Calls);
        Assert.True(Assert.Single(world.Model.Tasks).IsPending);

        world.Clock.Advance(TimeSpan.FromSeconds(2));
        world.Release();
        await committing;

        var failed = Assert.Single(world.Model.Tasks);
        Assert.Equal("Couldn't reach ChatGPT", failed.Reason);
        Assert.Equal("No reply within 30 seconds, after one retry.", failed.ReasonTip);
        Assert.Equal(2, world.Http.Calls.Count);
    }

    [Fact]
    public async Task A_rate_limit_is_not_retried()
    {
        using var world = new InterpretWorld();
        world.Http.StatusCode = HttpStatusCode.TooManyRequests;
        world.Http.FailureBody = """{"error":{"code":"subscription_sharing_usage_limit_exceeded"}}""";

        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();
        Assert.True(world.Model.HasConnection);

        world.Release();
        await committing;

        var failed = Assert.Single(world.Model.Tasks);
        Assert.Equal("buy milk", failed.Title);
        Assert.Equal("Rate-limited by ChatGPT", failed.Reason);
        Assert.Equal("ChatGPT returned 429 Too Many Requests. Try again in a minute.", failed.ReasonTip);
        Assert.True(failed.RetryFirst);
        Assert.False(failed.MakeTaskFirst);
        Assert.True(world.Model.Attention);
        Assert.True(world.Model.HasConnection);
        Assert.Equal(SignInPhase.Idle, world.Model.SignInState);

        world.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Single(world.Http.Calls);
    }

    [Fact]
    public async Task Bad_output_is_retried_once_then_shows_couldnt_understand()
    {
        using var world = new InterpretWorld();
        world.Reply("this is not the task format");

        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();
        world.Release();
        Assert.True(Assert.Single(world.Model.Tasks).IsPending);
        Assert.Single(world.Http.Calls);

        world.Clock.Advance(TimeSpan.FromSeconds(2));
        world.Release();
        await committing;

        var failed = Assert.Single(world.Model.Tasks);
        Assert.Equal("Couldn't understand the reply", failed.Reason);
        Assert.Equal("The reply didn't match the Task format, after one retry.", failed.ReasonTip);
        Assert.True(failed.RetryFirst);
        Assert.True(world.Model.Attention);
        Assert.Equal(2, world.Http.Calls.Count);
    }

    [Fact]
    public async Task No_task_in_the_reply_offers_make_a_task_as_is_and_does_not_retry()
    {
        using var world = new InterpretWorld();
        world.Reply("""{"tasks":[]}""");

        world.Model.UpdateDraft("Thursday was a good day honestly");
        var committing = world.Model.CommitCapture();
        world.Release();
        await committing;

        var failed = Assert.Single(world.Model.Tasks);
        Assert.Equal("Thursday was a good day honestly", failed.Title);
        Assert.Equal("No task found in this", failed.Reason);
        Assert.Equal("ChatGPT found nothing to do in this Capture.", failed.ReasonTip);
        Assert.True(failed.MakeTaskFirst);
        Assert.False(failed.RetryFirst);
        Assert.True(failed.ShowFailed);
        Assert.True(world.Model.Attention);

        world.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Single(world.Http.Calls);
    }

    [Fact]
    public async Task A_plan_refusal_on_a_capture_stays_signed_in()
    {
        using var world = new InterpretWorld();
        world.Http.StatusCode = HttpStatusCode.Forbidden;
        world.Http.FailureBody = """{"error":{"code":"subscription_sharing_user_not_eligible"}}""";

        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();
        world.Release();
        await committing;

        var failed = Assert.Single(world.Model.Tasks);
        Assert.Equal("Your ChatGPT plan can't be used here", failed.Reason);
        Assert.Equal("ChatGPT returned 403: this plan isn't eligible. Go, Plus or Pro works.", failed.ReasonTip);
        Assert.True(failed.RetryFirst);
        Assert.True(world.Model.Attention);
        Assert.True(world.Model.HasConnection);
        Assert.Equal(SignInPhase.Idle, world.Model.SignInState);
        Assert.True(File.Exists(Path.Combine(world.Folder, AppModel.TokenFileName)));

        world.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Single(world.Http.Calls);
    }

    [Fact]
    public async Task Retry_runs_the_capture_again()
    {
        using var world = new InterpretWorld();
        var failed = await NothingToDo(world, "buy milk");

        world.Reply("""{"tasks":[{"title":"Buy milk","details":"","priority":"high","effort":"quick"}]}""");
        var retrying = world.Model.Retry(failed);
        Assert.True(failed.IsPending);
        Assert.False(failed.IsFailed);
        Assert.Equal("Interpreting…", failed.PendingText);

        world.Release();
        await retrying;

        var task = Assert.Single(world.Model.Tasks);
        Assert.Equal("Buy milk", task.Title);
        Assert.Equal(Priority.High, task.Priority);
        Assert.False(world.Model.Attention);
    }

    [Fact]
    public async Task Edit_then_enter_reruns_the_corrected_text()
    {
        using var world = new InterpretWorld();
        var failed = await NothingToDo(world, "buy milk");

        world.Model.EditCapture(failed);
        Assert.True(failed.IsEditingCapture);
        Assert.False(failed.ShowFailed);

        var ignored = world.Model.SubmitCapture(failed, "   ");
        await ignored;
        Assert.True(failed.IsEditingCapture);
        Assert.Single(world.Http.Calls);

        world.Model.CancelCaptureEdit(failed);
        Assert.False(failed.IsEditingCapture);
        Assert.True(failed.ShowFailed);
        Assert.Equal("No task found in this", failed.Reason);

        world.Model.EditCapture(failed);
        world.Reply("""{"tasks":[{"title":"Buy oat milk","details":"","priority":"medium","effort":"short"}]}""");
        var submitting = world.Model.SubmitCapture(failed, "buy oat milk\n");
        Assert.Equal("buy oat milk", failed.Title);
        Assert.True(failed.IsPending);

        var call = world.Http.Calls[^1];
        using (var doc = JsonDocument.Parse(call.Body))
        {
            var content = doc.RootElement.GetProperty("input")[0].GetProperty("content")[0];
            Assert.Equal("buy oat milk", content.GetProperty("text").GetString());
        }

        world.Release();
        await submitting;
        Assert.Equal("Buy oat milk", Assert.Single(world.Model.Tasks).Title);
        Assert.False(world.Model.Attention);
    }

    [Fact]
    public async Task Make_a_task_as_is_keeps_the_first_line_and_undoes()
    {
        using var world = new InterpretWorld(arrange =>
        {
            File.WriteAllText(Path.Combine(arrange.Folder, "tasks.json"), """
                {
                  "schemaVersion": 1,
                  "tasks": [
                    { "title": "Pay rent", "details": "", "priority": "high", "effort": "quick", "created": "2026-10-01T00:00:00Z" },
                    { "title": "Water plants", "details": "", "priority": "low", "effort": "long", "created": "2026-10-02T00:00:00Z" }
                  ]
                }
                """);
        });
        var failed = await NothingToDo(world, "call the dentist tomorrow\nbring the card");
        var capture = failed.Capture;

        world.Model.MakeTaskAsIs(failed);

        Assert.Equal(
            ["Pay rent", "call the dentist tomorrow", "Water plants"],
            world.Model.Tasks.Select(task => task.Title));
        var made = world.Model.Tasks[1];
        Assert.Equal(Priority.Medium, made.Priority);
        Assert.Equal(Effort.Short, made.Effort);
        Assert.Equal("", made.Details);
        Assert.Equal(capture, made.Capture);
        Assert.False(made.IsFailed);
        Assert.False(world.Model.Attention);
        Assert.Equal(3, world.Model.OpenTaskCount);

        world.Model.Undo();
        Assert.Equal(
            ["call the dentist tomorrow\nbring the card", "Pay rent", "Water plants"],
            world.Model.Tasks.Select(task => task.Title));
        Assert.True(world.Model.Tasks[0].IsFailed);
        Assert.Equal("No task found in this", world.Model.Tasks[0].Reason);
        Assert.True(world.Model.Tasks[0].MakeTaskFirst);
        Assert.True(world.Model.Attention);
        Assert.Equal(2, world.Model.OpenTaskCount);

        world.Model.Redo();
        Assert.Equal("call the dentist tomorrow", world.Model.Tasks[1].Title);
        Assert.Equal(capture, world.Model.Tasks[1].Capture);
        Assert.False(world.Model.Attention);
    }

    [Fact]
    public async Task Discard_removes_the_failed_row_and_undo_brings_it_back()
    {
        using var world = new InterpretWorld();
        var failed = await NothingToDo(world, "buy milk");
        Assert.True(world.Model.Attention);

        world.Model.Discard(failed);

        Assert.Empty(world.Model.Tasks);
        Assert.False(world.Model.Attention);
        Assert.True(world.Model.UndoVisible);
        Assert.Equal("Capture discarded", world.Model.UndoText);

        world.Model.Undo();
        var restored = Assert.Single(world.Model.Tasks);
        Assert.Equal("buy milk", restored.Title);
        Assert.Equal("No task found in this", restored.Reason);
        Assert.True(world.Model.Attention);
        Assert.False(world.Model.UndoVisible);

        world.Model.Discard(restored);
        world.Model.Dispose();
        var again = new AppModel(world.Folder, new ManualClock());
        try
        {
            Assert.Empty(again.Tasks);
            Assert.False(again.Attention);
        }
        finally
        {
            again.Dispose();
        }
    }

    [Fact]
    public async Task The_dot_stays_lit_until_every_failed_row_is_gone()
    {
        using var world = new InterpretWorld();
        await NothingToDo(world, "buy milk");
        await NothingToDo(world, "nice weather");

        Assert.Equal(2, world.Model.Tasks.Count(task => task.IsFailed));
        Assert.True(world.Model.Attention);

        world.Model.Discard(world.Model.Tasks[0]);
        Assert.True(world.Model.Attention);
        Assert.Single(world.Model.Tasks);

        world.Model.Discard(world.Model.Tasks[0]);
        Assert.Empty(world.Model.Tasks);
        Assert.False(world.Model.Attention);
    }

    [Fact]
    public async Task A_failed_row_is_still_there_after_the_widget_opens_again()
    {
        var world = new InterpretWorld();
        try
        {
            var failed = await NothingToDo(world, "Thursday was a good day honestly");
            var capture = failed.Capture;
            world.Model.Dispose();

            var again = new AppModel(world.Folder, new ManualClock());
            try
            {
                var row = Assert.Single(again.Tasks);
                Assert.Equal("Thursday was a good day honestly", row.Title);
                Assert.Equal(capture, row.Capture);
                Assert.True(row.IsFailed);
                Assert.Equal("No task found in this", row.Reason);
                Assert.Equal("ChatGPT found nothing to do in this Capture.", row.ReasonTip);
                Assert.True(row.MakeTaskFirst);
                Assert.True(again.Attention);
                Assert.Equal(0, again.OpenTaskCount);
            }
            finally
            {
                again.Dispose();
            }
        }
        finally
        {
            world.Http.ReleaseAll();
            world.Model.Dispose();
            Directory.Delete(world.Folder, recursive: true);
        }
    }

    [Fact]
    public async Task Closing_the_widget_cancels_an_in_flight_capture()
    {
        using var world = new InterpretWorld();
        world.Model.UpdateDraft("buy milk");
        var committing = world.Model.CommitCapture();
        Assert.Single(world.Http.Calls);

        world.Model.Dispose();

        Assert.True(world.Http.WasCancelled);
        await committing;
        Assert.True(world.Model.HasConnection);
    }

    static async Task<TaskRow> NothingToDo(InterpretWorld world, string text)
    {
        world.Reply("""{"tasks":[]}""");
        world.Model.UpdateDraft(text);
        var committing = world.Model.CommitCapture();
        world.Release();
        await committing;
        return world.Model.Tasks.Single(task => task.IsFailed && task.Title == text);
    }
}
