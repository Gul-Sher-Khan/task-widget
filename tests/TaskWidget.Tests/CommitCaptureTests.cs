using System.Text.Json;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class CommitCaptureTests
{
    [Fact]
    public void Committing_a_capture_shows_the_task_and_saves_it()
    {
        var folder = Directory.CreateTempSubdirectory("tw-capture").FullName;
        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            model.UpdateDraft("email Sarah the invoice");
            model.CommitCapture();

            var row = Assert.Single(model.Tasks);
            Assert.Equal("email Sarah the invoice", row.Title);
            Assert.Equal(Priority.Medium, row.Priority);
            Assert.Equal(Effort.Short, row.Effort);
            Assert.Equal("", row.Details);
            Assert.Equal("", model.CaptureText);
            Assert.Equal(1, model.OpenTaskCount);

            var tasksPath = Path.Combine(folder, "tasks.json");
            Assert.False(File.Exists(tasksPath));

            clock.Advance(TimeSpan.FromMilliseconds(299));
            Assert.False(File.Exists(tasksPath));

            clock.Advance(TimeSpan.FromMilliseconds(1));

            using (var saved = JsonDocument.Parse(File.ReadAllText(tasksPath)))
            {
                Assert.Equal(1, saved.RootElement.GetProperty("schemaVersion").GetInt32());
                var task = Assert.Single(saved.RootElement.GetProperty("tasks").EnumerateArray());
                Assert.Equal("email Sarah the invoice", task.GetProperty("title").GetString());
                Assert.Equal("", task.GetProperty("details").GetString());
                Assert.Equal("medium", task.GetProperty("priority").GetString());
                Assert.Equal("short", task.GetProperty("effort").GetString());
            }

            Assert.True(File.Exists(tasksPath + ".bak"));

            using (var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "settings.json"))))
                Assert.Equal(1, settings.RootElement.GetProperty("schemaVersion").GetInt32());
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_saved_task_is_still_there_after_a_restart()
    {
        var folder = Directory.CreateTempSubdirectory("tw-relaunch").FullName;
        var clock = new ManualClock();
        try
        {
            var first = new AppModel(folder, clock);
            first.UpdateDraft("email Sarah the invoice");
            first.CommitCapture();
            first.Dispose();

            var again = new AppModel(folder, new ManualClock());
            var row = Assert.Single(again.Tasks);
            Assert.Equal("email Sarah the invoice", row.Title);
            Assert.Equal(Priority.Medium, row.Priority);
            Assert.Equal(Effort.Short, row.Effort);
            Assert.Equal("", row.Details);
            Assert.Equal("", again.CaptureText);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Committing_a_multiline_capture_keeps_only_the_first_line_as_the_title()
    {
        var folder = Directory.CreateTempSubdirectory("tw-lines").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.UpdateDraft("email Sarah\r\nbefore Friday");
            model.CommitCapture();

            var row = Assert.Single(model.Tasks);
            Assert.Equal("email Sarah", row.Title);
            Assert.Equal("", row.Details);
            Assert.Equal(Priority.Medium, row.Priority);
            Assert.Equal(Effort.Short, row.Effort);
            Assert.Equal("", model.CaptureText);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void An_uncommitted_capture_is_still_in_the_box_after_a_restart()
    {
        var folder = Directory.CreateTempSubdirectory("tw-draft").FullName;
        try
        {
            var clock = new ManualClock();
            var model = new AppModel(folder, clock);
            model.UpdateDraft("email Sarah\r\nbefore Friday");
            clock.Advance(TimeSpan.FromMilliseconds(300));
            model.Dispose();

            var again = new AppModel(folder, new ManualClock());
            Assert.Equal("email Sarah\r\nbefore Friday", again.CaptureText);
            Assert.Empty(again.Tasks);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Enter_on_an_empty_box_does_not_add_a_task()
    {
        var folder = Directory.CreateTempSubdirectory("tw-empty").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.UpdateDraft("   ");
            model.CommitCapture();
            Assert.Empty(model.Tasks);
            Assert.Equal("   ", model.CaptureText);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_second_capture_is_placed_after_the_earlier_task()
    {
        var folder = Directory.CreateTempSubdirectory("tw-order").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.UpdateDraft("email Sarah");
            model.CommitCapture();
            model.UpdateDraft("buy milk");
            model.CommitCapture();

            Assert.Equal(2, model.OpenTaskCount);
            Assert.Equal("email Sarah", model.Tasks[0].Title);
            Assert.Equal("buy milk", model.Tasks[1].Title);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }
}

sealed class ManualClock : IClock
{
    readonly Dictionary<long, (long Due, Action Callback)> pending = [];
    long now;
    long next;

    public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);

    public long Schedule(TimeSpan delay, Action callback)
    {
        var id = ++next;
        pending[id] = (now + (long)delay.TotalMilliseconds, callback);
        return id;
    }

    public void Cancel(long id) => pending.Remove(id);

    public void Advance(TimeSpan by)
    {
        now += (long)by.TotalMilliseconds;
        UtcNow = UtcNow.Add(by);
        var due = pending.Where(item => item.Value.Due <= now).Select(item => item.Key).ToArray();
        var callbacks = new List<Action>(due.Length);
        foreach (var id in due)
        {
            callbacks.Add(pending[id].Callback);
            pending.Remove(id);
        }

        foreach (var callback in callbacks)
            callback();
    }
}
