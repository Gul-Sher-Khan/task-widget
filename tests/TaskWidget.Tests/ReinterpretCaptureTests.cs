using System.Net;
using System.Text.Json;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class ReinterpretCaptureTests
{
    [Fact]
    public async Task Re_interpret_replaces_only_not_done_tasks_and_one_undo_restores_them()
    {
        using var world = new InterpretWorld();
        await Capture(world, "email Sarah the invoice and buy milk", """
            {"tasks":[
              {"title":"Email Sarah the invoice","details":"","priority":"medium","effort":"short"},
              {"title":"Buy milk","details":"","priority":"high","effort":"quick"}
            ]}
            """);
        var email = world.Model.Tasks.Single(task => task.Title == "Email Sarah the invoice");
        var milk = world.Model.Tasks.Single(task => task.Title == "Buy milk");
        world.Model.Tick(milk);
        world.Clock.Advance(AppModel.StrikeHold);

        await Capture(world, "call the bank", """
            {"tasks":[{"title":"Call the bank","details":"","priority":"high","effort":"quick"}]}
            """);
        var bank = world.Model.Tasks.Single(task => task.Title == "Call the bank");

        world.Model.Reinterpret(email);
        var editing = world.Model.Tasks[0];
        Assert.True(editing.IsEditingCapture);
        Assert.Equal("email Sarah the invoice and buy milk", editing.Title);
        Assert.False(email.IsDimmed);
        Assert.False(bank.IsDimmed);
        Assert.False(milk.IsDimmed);

        world.Reply("""
            {"tasks":[
              {"title":"Email Sarah the revised invoice","details":"","priority":"medium","effort":"short"},
              {"title":"File the receipt","details":"","priority":"low","effort":"long"}
            ]}
            """);
        var running = world.Model.SubmitCapture(editing, "email Sarah the revised invoice");

        Assert.Equal("Re-interpreting…", editing.PendingText);
        Assert.True(editing.IsPending);
        Assert.Equal("email Sarah the revised invoice", editing.Title);
        Assert.True(email.IsDimmed);
        Assert.False(bank.IsDimmed);
        Assert.False(milk.IsDimmed);
        Assert.True(world.Model.Tasks.IndexOf(editing) < world.Model.Tasks.IndexOf(email));
        Assert.Contains(email, world.Model.Tasks);
        Assert.Contains(bank, world.Model.Tasks);
        Assert.Equal("Buy milk", Assert.Single(world.Model.DoneTasks).Title);

        var call = world.Http.Calls[^1];
        using (var doc = JsonDocument.Parse(call.Body))
        {
            var root = doc.RootElement;
            Assert.True(root.GetProperty("stream").GetBoolean());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.Equal("low", root.GetProperty("reasoning").GetProperty("effort").GetString());
            Assert.True(root.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
            var message = Assert.Single(root.GetProperty("input").EnumerateArray());
            Assert.Equal("user", message.GetProperty("role").GetString());
            var content = Assert.Single(message.GetProperty("content").EnumerateArray());
            Assert.Equal("input_text", content.GetProperty("type").GetString());
            Assert.Equal("email Sarah the revised invoice", content.GetProperty("text").GetString());
        }

        Assert.DoesNotContain("buy milk", call.Body, StringComparison.Ordinal);

        world.Release();
        await running;

        Assert.Equal(
            ["Call the bank", "Email Sarah the revised invoice", "File the receipt"],
            world.Model.Tasks.Select(task => task.Title));
        Assert.Same(bank, world.Model.Tasks[0]);
        Assert.Equal(email.Capture, world.Model.Tasks[1].Capture);
        Assert.Equal(email.Capture, world.Model.Tasks[2].Capture);
        Assert.NotEqual(bank.Capture, world.Model.Tasks[1].Capture);
        Assert.Same(milk, Assert.Single(world.Model.DoneTasks));
        Assert.All(world.Model.Tasks, task => Assert.False(task.IsDimmed));
        Assert.False(world.Model.Attention);

        world.Clock.Advance(AppModel.SaveDelay);
        using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.Folder, "tasks.json"))))
        {
            var capture = saved.RootElement.GetProperty("captures").EnumerateArray()
                .Single(item => item.GetProperty("id").GetString() == email.Capture);
            Assert.Equal("email Sarah the invoice and buy milk", capture.GetProperty("text").GetString());
            Assert.Equal("email Sarah the revised invoice", capture.GetProperty("interpreted").GetString());
        }

        world.Model.Undo();

        Assert.Equal(["Call the bank", "Email Sarah the invoice"], world.Model.Tasks.Select(task => task.Title));
        Assert.Same(bank, world.Model.Tasks[0]);
        Assert.Same(email, world.Model.Tasks[1]);
        Assert.Same(milk, Assert.Single(world.Model.DoneTasks));
        Assert.False(email.IsDimmed);
        Assert.False(world.Model.Attention);
    }

    [Fact]
    public async Task A_failed_re_interpret_leaves_the_old_tasks_and_shows_the_corrected_text()
    {
        using var world = new InterpretWorld();
        await Capture(world, "email Sarah the invoice and buy milk", """
            {"tasks":[
              {"title":"Email Sarah the invoice","details":"","priority":"medium","effort":"short"},
              {"title":"Buy milk","details":"","priority":"high","effort":"quick"}
            ]}
            """);
        var email = world.Model.Tasks.Single(task => task.Title == "Email Sarah the invoice");
        var milk = world.Model.Tasks.Single(task => task.Title == "Buy milk");
        world.Model.Tick(milk);
        world.Clock.Advance(AppModel.StrikeHold);

        await Capture(world, "call the bank", """
            {"tasks":[{"title":"Call the bank","details":"","priority":"high","effort":"quick"}]}
            """);
        var bank = world.Model.Tasks.Single(task => task.Title == "Call the bank");

        world.Model.Reinterpret(email);
        var editing = world.Model.Tasks[0];
        world.Http.StatusCode = HttpStatusCode.InternalServerError;
        var running = world.Model.SubmitCapture(editing, "email Sarah the revised invoice");

        Assert.Equal("Re-interpreting…", editing.PendingText);
        Assert.True(email.IsDimmed);
        Assert.Contains(email, world.Model.Tasks);
        Assert.Contains(bank, world.Model.Tasks);

        world.Release();
        world.Clock.Advance(TimeSpan.FromSeconds(2));
        world.Release();
        await running;

        Assert.Equal(
            ["email Sarah the revised invoice", "Call the bank", "Email Sarah the invoice"],
            world.Model.Tasks.Select(task => task.Title));
        Assert.Same(bank, world.Model.Tasks[1]);
        Assert.Same(email, world.Model.Tasks[2]);
        Assert.False(email.IsDimmed);
        Assert.False(bank.IsDimmed);
        Assert.Same(milk, Assert.Single(world.Model.DoneTasks));

        var failed = world.Model.Tasks[0];
        Assert.True(failed.IsFailed);
        Assert.True(failed.ShowFailed);
        Assert.Equal("email Sarah the revised invoice", failed.Title);
        Assert.Equal("Re-interpret failed", failed.Reason);
        Assert.Equal("No reply within 30 seconds, after one retry.", failed.ReasonTip);
        Assert.True(failed.RetryFirst);
        Assert.False(failed.MakeTaskFirst);
        Assert.True(world.Model.Attention);
        Assert.Equal(2, world.Model.OpenTaskCount);
    }

    [Fact]
    public async Task Retry_runs_the_corrected_capture_again()
    {
        using var world = new InterpretWorld();
        var (email, milk, bank, failed) = await Fail(world);

        world.Http.StatusCode = HttpStatusCode.OK;
        world.Reply("""
            {"tasks":[
              {"title":"Email Sarah the revised invoice","details":"","priority":"medium","effort":"short"}
            ]}
            """);
        var retrying = world.Model.Retry(failed);

        Assert.Equal("Re-interpreting…", failed.PendingText);
        Assert.True(email.IsDimmed);
        Assert.False(bank.IsDimmed);
        Assert.Contains(email, world.Model.Tasks);
        var call = world.Http.Calls[^1];
        using (var doc = JsonDocument.Parse(call.Body))
        {
            var content = Assert.Single(
                Assert.Single(doc.RootElement.GetProperty("input").EnumerateArray()).GetProperty("content").EnumerateArray());
            Assert.Equal("email Sarah the revised invoice", content.GetProperty("text").GetString());
        }

        world.Release();
        await retrying;

        Assert.Equal(
            ["Call the bank", "Email Sarah the revised invoice"],
            world.Model.Tasks.Select(task => task.Title));
        Assert.Same(bank, world.Model.Tasks[0]);
        Assert.Equal(email.Capture, world.Model.Tasks[1].Capture);
        Assert.DoesNotContain(world.Model.Tasks, task => task.IsFailed || task.IsDimmed);
        Assert.Same(milk, Assert.Single(world.Model.DoneTasks));
        Assert.False(world.Model.Attention);
    }

    [Fact]
    public async Task Discard_removes_the_failed_row_and_keeps_the_old_tasks()
    {
        using var world = new InterpretWorld();
        var (email, milk, bank, failed) = await Fail(world);

        world.Model.Discard(failed);

        Assert.Equal(["Call the bank", "Email Sarah the invoice"], world.Model.Tasks.Select(task => task.Title));
        Assert.Same(bank, world.Model.Tasks[0]);
        Assert.Same(email, world.Model.Tasks[1]);
        Assert.Same(milk, Assert.Single(world.Model.DoneTasks));
        Assert.False(world.Model.Attention);
        Assert.False(email.IsDimmed);

        world.Clock.Advance(AppModel.SaveDelay);
        using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.Folder, "tasks.json"))))
        {
            var capture = saved.RootElement.GetProperty("captures").EnumerateArray()
                .Single(item => item.GetProperty("id").GetString() == email.Capture);
            Assert.Equal("email Sarah the invoice and buy milk", capture.GetProperty("text").GetString());
            Assert.Equal("email Sarah the invoice and buy milk", capture.GetProperty("interpreted").GetString());
            Assert.Equal("interpreted", capture.GetProperty("state").GetString());
            Assert.Equal("", capture.GetProperty("correction").GetString());
        }

        world.Model.Undo();
        Assert.Equal(
            ["email Sarah the revised invoice", "Call the bank", "Email Sarah the invoice"],
            world.Model.Tasks.Select(task => task.Title));
        Assert.Equal("Re-interpret failed", world.Model.Tasks[0].Reason);
        Assert.True(world.Model.Attention);
        Assert.Same(email, world.Model.Tasks[2]);
    }

    [Fact]
    public async Task A_failed_re_interpret_is_still_there_after_the_widget_opens_again()
    {
        var world = new InterpretWorld();
        try
        {
            var (email, _, _, failed) = await Fail(world);
            var capture = failed.Capture;
            world.Model.Dispose();

            var again = new AppModel(world.Folder, new ManualClock());
            try
            {
                Assert.Equal(
                    ["email Sarah the revised invoice", "Call the bank", "Email Sarah the invoice"],
                    again.Tasks.Select(task => task.Title));
                var row = again.Tasks[0];
                Assert.True(row.IsFailed);
                Assert.Equal("Re-interpret failed", row.Reason);
                Assert.Equal("No reply within 30 seconds, after one retry.", row.ReasonTip);
                Assert.Equal(capture, row.Capture);
                Assert.True(again.Attention);
                Assert.Equal("Buy milk", Assert.Single(again.DoneTasks).Title);
                Assert.Equal(2, again.OpenTaskCount);

                using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.Folder, "tasks.json")));
                var stored = saved.RootElement.GetProperty("captures").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == email.Capture);
                Assert.Equal("email Sarah the invoice and buy milk", stored.GetProperty("text").GetString());
                Assert.Equal("email Sarah the invoice and buy milk", stored.GetProperty("interpreted").GetString());
                Assert.Equal("email Sarah the revised invoice", stored.GetProperty("correction").GetString());
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

    static async Task<(TaskRow Email, TaskRow Milk, TaskRow Bank, TaskRow Failed)> Fail(InterpretWorld world)
    {
        await Capture(world, "email Sarah the invoice and buy milk", """
            {"tasks":[
              {"title":"Email Sarah the invoice","details":"","priority":"medium","effort":"short"},
              {"title":"Buy milk","details":"","priority":"high","effort":"quick"}
            ]}
            """);
        var email = world.Model.Tasks.Single(task => task.Title == "Email Sarah the invoice");
        var milk = world.Model.Tasks.Single(task => task.Title == "Buy milk");
        world.Model.Tick(milk);
        world.Clock.Advance(AppModel.StrikeHold);
        await Capture(world, "call the bank", """
            {"tasks":[{"title":"Call the bank","details":"","priority":"high","effort":"quick"}]}
            """);
        var bank = world.Model.Tasks.Single(task => task.Title == "Call the bank");
        world.Model.Reinterpret(email);
        var editing = world.Model.Tasks[0];
        world.Http.StatusCode = HttpStatusCode.InternalServerError;
        var running = world.Model.SubmitCapture(editing, "email Sarah the revised invoice");
        world.Release();
        world.Clock.Advance(TimeSpan.FromSeconds(2));
        world.Release();
        await running;
        return (email, milk, bank, editing);
    }

    static async Task Capture(InterpretWorld world, string text, string json)
    {
        world.Reply(json);
        world.Model.UpdateDraft(text);
        var committing = world.Model.CommitCapture();
        world.Release();
        await committing;
    }
}
