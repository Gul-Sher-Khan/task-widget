using System.Text.Json;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class TickDoneUndoTests
{
    [Fact]
    public void A_tick_strikes_the_task_for_the_hold_then_it_leaves_the_open_list()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        var task = Assert.Single(world.Model.Tasks);

        world.Model.Tick(task);

        Assert.True(task.IsStriking);
        Assert.Equal("email Sarah", Assert.Single(world.Model.Tasks).Title);
        Assert.Empty(world.Model.DoneTasks);
        Assert.Equal(1, world.Model.OpenTaskCount);

        world.Clock.Advance(TimeSpan.FromMilliseconds(1499));
        Assert.True(task.IsStriking);
        Assert.Single(world.Model.Tasks);
        Assert.Empty(world.Model.DoneTasks);

        world.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(task.IsStriking);
        Assert.True(task.IsDone);
        Assert.Empty(world.Model.Tasks);
        Assert.Equal(0, world.Model.OpenTaskCount);
        Assert.Equal("email Sarah", Assert.Single(world.Model.DoneTasks).Title);
    }

    [Fact]
    public void A_second_tick_inside_the_hold_cancels_the_completion()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        var task = Assert.Single(world.Model.Tasks);

        world.Model.Tick(task);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1499));
        world.Model.Tick(task);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1500));

        Assert.False(task.IsStriking);
        Assert.False(task.IsDone);
        Assert.Equal("email Sarah", Assert.Single(world.Model.Tasks).Title);
        Assert.Empty(world.Model.DoneTasks);
        Assert.Equal(1, world.Model.OpenTaskCount);
    }

    [Fact]
    public void The_done_view_lists_the_last_90_days_newest_first()
    {
        var folder = Directory.CreateTempSubdirectory("tw-done").FullName;
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        try
        {
            File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "tasks": [
                { "title": "fix the login test", "details": "", "priority": "medium", "effort": "short" },
                { "title": "email Sarah", "details": "", "priority": "low", "effort": "long", "completedAt": "2026-07-09T12:00:00Z" },
                { "title": "pay the bill", "details": "", "priority": "high", "effort": "quick", "completedAt": "2026-07-10T12:00:00Z" },
                { "title": "buy milk", "details": "the oat kind", "priority": "high", "effort": "quick", "completedAt": "2026-09-01T08:00:00Z" },
                { "title": "book the dentist", "details": "", "priority": "medium", "effort": "short", "completedAt": "2026-10-07T15:00:00Z" }
              ]
            }
            """);

            var model = new AppModel(folder, new ManualClock(now));
            Assert.Equal("fix the login test", Assert.Single(model.Tasks).Title);
            Assert.Equal(
                ["book the dentist", "buy milk", "pay the bill"],
                model.DoneTasks.Select(task => task.Title).ToArray());
            Assert.Equal("the oat kind", model.DoneTasks[1].Details);
            Assert.Equal(Priority.High, model.DoneTasks[1].Priority);
            Assert.Equal(Effort.Quick, model.DoneTasks[1].Effort);
            Assert.All(model.DoneTasks, task => Assert.True(task.IsDone));
            model.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_header_toggles_between_open_tasks_and_the_done_view()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        var done = Assert.Single(world.Model.Tasks);
        world.Model.Tick(done);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1500));
        world.Model.UpdateDraft("buy milk");
        world.Model.CommitCapture();

        Assert.False(world.Model.ShowingDone);
        Assert.Equal("buy milk", Assert.Single(world.Model.VisibleTasks).Title);

        world.Model.ToggleDoneView();
        Assert.True(world.Model.ShowingDone);
        Assert.Equal("email Sarah", Assert.Single(world.Model.VisibleTasks).Title);

        world.Model.ToggleDoneView();
        Assert.False(world.Model.ShowingDone);
        Assert.Equal("buy milk", Assert.Single(world.Model.VisibleTasks).Title);
    }

    [Fact]
    public void Un_completing_a_task_puts_it_back_by_the_insertion_rule()
    {
        var folder = Directory.CreateTempSubdirectory("tw-place").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "tasks": [
                { "title": "reply to Sana", "details": "", "priority": "high", "effort": "quick", "createdAt": "2026-10-01T12:00:00Z" },
                { "title": "write the release notes", "details": "", "priority": "medium", "effort": "short", "createdAt": "2026-10-05T12:00:00Z" },
                { "title": "fix the login test", "details": "", "priority": "high", "effort": "short", "createdAt": "2026-10-02T12:00:00Z" },
                { "title": "review the PR", "details": "the eviction policy", "priority": "medium", "effort": "short", "createdAt": "2026-10-03T12:00:00Z", "completedAt": "2026-10-07T12:00:00Z" }
              ]
            }
            """);

            var model = new AppModel(folder, new ManualClock(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero)));
            var review = Assert.Single(model.DoneTasks);
            model.Tick(review);

            Assert.False(review.IsDone);
            Assert.Empty(model.DoneTasks);
            Assert.Equal(
                ["reply to Sana", "review the PR", "write the release notes", "fix the login test"],
                model.Tasks.Select(task => task.Title).ToArray());
            Assert.Equal("the eviction policy", model.Tasks[1].Details);
            model.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_deleted_task_is_absent_from_the_list_and_the_done_view()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        world.Model.UpdateDraft("buy milk");
        world.Model.CommitCapture();
        var email = world.Model.Tasks[0];
        var milk = world.Model.Tasks[1];
        world.Model.Tick(email);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1500));

        world.Model.Delete(milk);
        world.Model.Delete(email);

        Assert.Empty(world.Model.Tasks);
        Assert.Empty(world.Model.DoneTasks);
        Assert.Equal(0, world.Model.OpenTaskCount);
        Assert.DoesNotContain(world.Model.VisibleTasks, task => task.Title is "email Sarah" or "buy milk");

        world.Model.Dispose();
        using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.Folder, "tasks.json"))))
        {
            var records = saved.RootElement.GetProperty("tasks").EnumerateArray().ToArray();
            Assert.Equal(2, records.Length);
            Assert.Contains(records, task => task.GetProperty("title").GetString() == "email Sarah");
            Assert.Contains(records, task => task.GetProperty("title").GetString() == "buy milk");
            Assert.All(records, task => Assert.False(string.IsNullOrEmpty(task.GetProperty("deletedAt").GetString())));
        }

        var again = new AppModel(world.Folder, new ManualClock());
        Assert.Empty(again.Tasks);
        Assert.Empty(again.DoneTasks);
        again.Dispose();
    }

    [Fact]
    public void The_undo_pill_shows_for_five_seconds_and_undoes_a_completion()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        world.Model.UpdateDraft("buy milk");
        world.Model.CommitCapture();
        var email = world.Model.Tasks[0];

        world.Model.Tick(email);
        Assert.False(world.Model.UndoVisible);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1500));

        Assert.True(world.Model.UndoVisible);
        Assert.Equal("Task done", world.Model.UndoText);
        Assert.Equal("buy milk", Assert.Single(world.Model.Tasks).Title);

        world.Clock.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.True(world.Model.UndoVisible);

        world.Model.Undo();
        Assert.False(world.Model.UndoVisible);
        Assert.False(email.IsDone);
        Assert.Empty(world.Model.DoneTasks);
        Assert.Equal(["email Sarah", "buy milk"], world.Model.Tasks.Select(task => task.Title).ToArray());

        world.Model.Redo();
        Assert.Equal("buy milk", Assert.Single(world.Model.Tasks).Title);
        Assert.Equal("email Sarah", Assert.Single(world.Model.DoneTasks).Title);
        Assert.True(email.IsDone);
    }

    [Fact]
    public void The_undo_pill_hides_after_five_seconds_and_undo_still_works()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        var email = Assert.Single(world.Model.Tasks);
        world.Model.Tick(email);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1500));
        world.Clock.Advance(TimeSpan.FromMilliseconds(5000));

        Assert.False(world.Model.UndoVisible);
        world.Model.Undo();
        Assert.Equal("email Sarah", Assert.Single(world.Model.Tasks).Title);
        Assert.False(email.IsDone);
        Assert.Empty(world.Model.DoneTasks);
    }

    [Fact]
    public void The_undo_pill_undoes_a_delete()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        world.Model.UpdateDraft("buy milk");
        world.Model.CommitCapture();
        var email = world.Model.Tasks[0];
        world.Model.Tick(email);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1500));
        world.Model.Delete(world.Model.Tasks[0]);

        Assert.True(world.Model.UndoVisible);
        Assert.Equal("Task deleted", world.Model.UndoText);
        Assert.Empty(world.Model.Tasks);
        Assert.Equal("email Sarah", Assert.Single(world.Model.DoneTasks).Title);

        world.Model.Undo();
        Assert.False(world.Model.UndoVisible);
        Assert.Equal("buy milk", Assert.Single(world.Model.Tasks).Title);
        Assert.Equal("email Sarah", Assert.Single(world.Model.DoneTasks).Title);

        world.Model.Redo();
        Assert.Empty(world.Model.Tasks);
        Assert.Equal("email Sarah", Assert.Single(world.Model.DoneTasks).Title);

        world.Model.Delete(email);
        world.Model.Undo();
        Assert.Equal("email Sarah", Assert.Single(world.Model.DoneTasks).Title);
        Assert.Empty(world.Model.Tasks);
    }

    [Fact]
    public void Undo_and_redo_move_and_re_sort()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        world.Clock.Advance(TimeSpan.FromMilliseconds(1));
        world.Model.UpdateDraft("buy milk");
        world.Model.CommitCapture();
        var milk = world.Model.Tasks[1];

        world.Model.Move(milk, 0);
        Assert.False(world.Model.UndoVisible);
        Assert.True(world.Model.HasManualPositions);
        Assert.Equal(["buy milk", "email Sarah"], world.Model.Tasks.Select(task => task.Title).ToArray());

        world.Model.Resort();
        Assert.False(world.Model.UndoVisible);
        Assert.False(world.Model.HasManualPositions);
        Assert.Equal(["email Sarah", "buy milk"], world.Model.Tasks.Select(task => task.Title).ToArray());

        world.Model.Undo();
        Assert.True(world.Model.HasManualPositions);
        Assert.Equal(["buy milk", "email Sarah"], world.Model.Tasks.Select(task => task.Title).ToArray());

        world.Model.Redo();
        Assert.False(world.Model.HasManualPositions);
        Assert.Equal(["email Sarah", "buy milk"], world.Model.Tasks.Select(task => task.Title).ToArray());

        world.Model.Undo();
        world.Model.Undo();
        Assert.False(world.Model.HasManualPositions);
        Assert.Equal(["email Sarah", "buy milk"], world.Model.Tasks.Select(task => task.Title).ToArray());
    }

    [Fact]
    public void Undo_returns_an_un_completed_task_to_the_done_view()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        var email = Assert.Single(world.Model.Tasks);
        world.Model.Tick(email);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1500));
        world.Model.Tick(email);

        Assert.False(email.IsDone);
        Assert.Equal("email Sarah", Assert.Single(world.Model.Tasks).Title);
        Assert.Empty(world.Model.DoneTasks);

        world.Model.Undo();
        Assert.True(email.IsDone);
        Assert.Empty(world.Model.Tasks);
        Assert.Equal("email Sarah", Assert.Single(world.Model.DoneTasks).Title);

        world.Model.Redo();
        Assert.False(email.IsDone);
        Assert.Equal("email Sarah", Assert.Single(world.Model.Tasks).Title);
        Assert.Empty(world.Model.DoneTasks);
    }

    [Fact]
    public void Undo_history_is_forgotten_when_the_widget_opens_again()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        var email = Assert.Single(world.Model.Tasks);
        world.Model.Tick(email);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1500));
        world.Model.Dispose();

        var again = new AppModel(world.Folder, new ManualClock());
        again.Undo();
        again.Redo();
        Assert.Empty(again.Tasks);
        Assert.Equal("email Sarah", Assert.Single(again.DoneTasks).Title);
        Assert.False(again.UndoVisible);
        again.Dispose();
    }

    [Fact]
    public void Up_and_down_select_space_ticks_and_delete_removes()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        world.Model.UpdateDraft("buy milk");
        world.Model.CommitCapture();

        world.Model.TickSelected();
        world.Model.DeleteSelected();
        Assert.Equal(-1, world.Model.SelectedIndex);
        Assert.Equal(2, world.Model.Tasks.Count);

        world.Model.SelectDown();
        Assert.Equal(0, world.Model.SelectedIndex);
        world.Model.SelectDown();
        Assert.Equal(1, world.Model.SelectedIndex);
        world.Model.SelectDown();
        Assert.Equal(1, world.Model.SelectedIndex);
        world.Model.SelectUp();
        Assert.Equal(0, world.Model.SelectedIndex);
        world.Model.SelectUp();
        Assert.Equal(0, world.Model.SelectedIndex);

        world.Model.TickSelected();
        Assert.True(world.Model.Tasks[0].IsStriking);
        Assert.Equal("email Sarah", world.Model.Tasks[0].Title);

        world.Model.SelectDown();
        world.Model.DeleteSelected();
        Assert.Equal("email Sarah", Assert.Single(world.Model.Tasks).Title);
        Assert.True(world.Model.Tasks[0].IsStriking);
    }

    [Fact]
    public void Space_and_delete_in_the_done_view_act_on_the_selected_done_task()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        world.Clock.Advance(TimeSpan.FromMilliseconds(1));
        world.Model.UpdateDraft("buy milk");
        world.Model.CommitCapture();
        var email = world.Model.Tasks[0];
        world.Model.Tick(email);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1500));
        world.Model.ToggleDoneView();

        Assert.Equal(-1, world.Model.SelectedIndex);
        world.Model.SelectDown();
        world.Model.DeleteSelected();
        Assert.Empty(world.Model.DoneTasks);
        Assert.Equal("buy milk", Assert.Single(world.Model.Tasks).Title);

        world.Model.Undo();
        world.Model.SelectDown();
        world.Model.TickSelected();
        Assert.False(email.IsDone);
        Assert.Equal(["email Sarah", "buy milk"], world.Model.Tasks.Select(task => task.Title).ToArray());
        Assert.Empty(world.Model.DoneTasks);
    }
}

sealed class WidgetWorld : IDisposable
{
    public WidgetWorld()
    {
        Folder = Directory.CreateTempSubdirectory("tw-tick").FullName;
        Clock = new ManualClock();
        Model = new AppModel(Folder, Clock);
    }

    public string Folder { get; }
    public ManualClock Clock { get; }
    public AppModel Model { get; }

    public void Dispose()
    {
        Model.Dispose();
        Directory.Delete(Folder, recursive: true);
    }
}
