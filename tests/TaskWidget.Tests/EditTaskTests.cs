using System.Text.Json;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class EditTaskTests
{
    [Fact]
    public void Pressing_E_edits_the_selected_title_and_the_new_title_is_saved()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        world.Model.SelectDown();
        var task = world.Model.Tasks[0];

        world.Model.EditSelectedTitle();
        Assert.True(task.IsEditing);

        world.Model.EditTitle(task, "email Sam");

        Assert.False(task.IsEditing);
        Assert.Equal("email Sam", task.Title);

        world.Clock.Advance(TimeSpan.FromMilliseconds(300));
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.Folder, "tasks.json")));
        var stored = Assert.Single(saved.RootElement.GetProperty("tasks").EnumerateArray());
        Assert.Equal("email Sam", stored.GetProperty("title").GetString());
    }

    [Fact]
    public void Escape_leaves_the_title_as_it_was()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        var task = world.Model.Tasks[0];

        world.Model.BeginTitleEdit(task);
        world.Model.CancelTitleEdit(task);

        Assert.False(task.IsEditing);
        Assert.Equal("email Sarah", task.Title);
        world.Model.Undo();
        Assert.Equal("email Sarah", task.Title);
    }

    [Fact]
    public void Completing_a_task_closes_its_details()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        var task = world.Model.Tasks[0];
        world.Model.EditDetails(task, "the invoice");
        world.Model.ToggleDetails(task);
        Assert.True(task.IsExpanded);

        world.Model.Tick(task);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1500));

        Assert.True(task.IsDone);
        Assert.False(task.IsExpanded);
    }

    [Fact]
    public void A_title_edit_can_be_undone_and_redone()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        var task = world.Model.Tasks[0];

        world.Model.BeginTitleEdit(task);
        world.Model.EditTitle(task, "  email Sam  ");

        Assert.Equal("email Sam", task.Title);
        Assert.False(world.Model.UndoVisible);

        world.Model.Undo();
        Assert.Equal("email Sarah", task.Title);

        world.Model.Redo();
        Assert.Equal("email Sam", task.Title);
    }

    [Fact]
    public void Editing_a_title_puts_the_task_back_by_the_insertion_rule()
    {
        var folder = Directory.CreateTempSubdirectory("tw-edit-title").FullName;
        File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "manual": true,
              "tasks": [
                { "title": "water plants", "details": "", "priority": "low", "effort": "long", "created": "2026-01-03T00:00:00Z" },
                { "title": "pay rent", "details": "", "priority": "high", "effort": "quick", "created": "2026-01-01T00:00:00Z" },
                { "title": "email Sarah", "details": "", "priority": "medium", "effort": "short", "created": "2026-01-02T00:00:00Z" }
              ]
            }
            """);

        var model = new AppModel(folder, new ManualClock());
        try
        {
            var plants = model.Tasks[0];
            model.EditTitle(plants, "water the plants");

            Assert.Equal(
                ["pay rent", "email Sarah", "water the plants"],
                model.Tasks.Select(task => task.Title));
            Assert.True(model.HasManualPositions);

            model.Undo();
            Assert.Equal(
                ["water plants", "pay rent", "email Sarah"],
                model.Tasks.Select(task => task.Title));

            model.Redo();
            Assert.Equal(
                ["pay rent", "email Sarah", "water the plants"],
                model.Tasks.Select(task => task.Title));
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Enter_or_a_click_opens_one_rows_details_at_a_time()
    {
        var folder = Directory.CreateTempSubdirectory("tw-details").FullName;
        File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "tasks": [
                { "title": "email Sarah", "details": "the invoice", "priority": "medium", "effort": "short", "created": "2026-01-02T00:00:00Z" },
                { "title": "pay rent", "details": "before Friday", "priority": "high", "effort": "quick", "created": "2026-01-01T00:00:00Z" }
              ]
            }
            """);

        var model = new AppModel(folder, new ManualClock());
        try
        {
            var email = model.Tasks[0];
            var rent = model.Tasks[1];
            Assert.True(email.HasDetails);
            Assert.True(rent.HasDetails);

            model.SelectDown();
            model.ExpandSelected();
            Assert.True(email.IsExpanded);
            Assert.False(rent.IsExpanded);

            model.SelectDown();
            model.ExpandSelected();
            Assert.False(email.IsExpanded);
            Assert.True(rent.IsExpanded);

            model.ExpandSelected();
            Assert.False(rent.IsExpanded);

            model.ToggleDetails(email);
            Assert.True(email.IsExpanded);
            Assert.False(rent.IsExpanded);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_row_with_empty_details_has_no_chevron_and_does_not_open()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("buy milk");
        world.Model.CommitCapture();
        var task = world.Model.Tasks[0];

        Assert.False(task.HasDetails);
        world.Model.SelectDown();
        world.Model.ExpandSelected();
        world.Model.ToggleDetails(task);

        Assert.False(task.IsExpanded);
    }

    [Fact]
    public void Editing_details_saves_them_and_re_places_the_task()
    {
        var folder = Directory.CreateTempSubdirectory("tw-edit-details").FullName;
        File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "manual": true,
              "tasks": [
                { "title": "water plants", "details": "the fern", "priority": "low", "effort": "long", "created": "2026-01-03T00:00:00Z" },
                { "title": "pay rent", "details": "", "priority": "high", "effort": "quick", "created": "2026-01-01T00:00:00Z" },
                { "title": "email Sarah", "details": "", "priority": "medium", "effort": "short", "created": "2026-01-02T00:00:00Z" }
              ]
            }
            """);

        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            var plants = model.Tasks[0];
            model.ToggleDetails(plants);
            model.EditDetails(plants, "  the fern by the window  ");

            Assert.Equal("the fern by the window", plants.Details);
            Assert.True(plants.HasDetails);
            Assert.True(plants.IsExpanded);
            Assert.Equal(
                ["pay rent", "email Sarah", "water plants"],
                model.Tasks.Select(task => task.Title));

            clock.Advance(TimeSpan.FromMilliseconds(300));
            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "tasks.json"))))
            {
                var stored = saved.RootElement.GetProperty("tasks").EnumerateArray().Single(row => row.GetProperty("title").GetString() == "water plants");
                Assert.Equal("the fern by the window", stored.GetProperty("details").GetString());
            }

            model.Undo();
            Assert.Equal("the fern", plants.Details);
            Assert.Equal(
                ["water plants", "pay rent", "email Sarah"],
                model.Tasks.Select(task => task.Title));

            model.Redo();
            Assert.Equal("the fern by the window", plants.Details);
            Assert.Equal(
                ["pay rent", "email Sarah", "water plants"],
                model.Tasks.Select(task => task.Title));

            model.EditDetails(plants, "   ");
            Assert.Equal("", plants.Details);
            Assert.False(plants.HasDetails);
            Assert.False(plants.IsExpanded);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Clicking_the_priority_badge_cycles_it_and_the_users_choice_wins()
    {
        using var world = new WidgetWorld();
        world.Model.UpdateDraft("email Sarah");
        world.Model.CommitCapture();
        var task = world.Model.Tasks[0];
        Assert.Equal(Priority.Medium, task.Priority);
        Assert.False(task.UserSetPriority);

        world.Model.CyclePriority(task);
        Assert.Equal(Priority.Low, task.Priority);
        Assert.True(task.UserSetPriority);
        Assert.False(task.UserSetEffort);

        world.Model.CyclePriority(task);
        Assert.Equal(Priority.High, task.Priority);

        world.Model.CyclePriority(task);
        Assert.Equal(Priority.Medium, task.Priority);
        Assert.True(task.UserSetPriority);

        world.Model.Undo();
        Assert.Equal(Priority.High, task.Priority);
        world.Model.Undo();
        Assert.Equal(Priority.Low, task.Priority);
        world.Model.Undo();
        Assert.Equal(Priority.Medium, task.Priority);
        Assert.False(task.UserSetPriority);

        world.Model.Redo();
        Assert.Equal(Priority.Low, task.Priority);
        Assert.True(task.UserSetPriority);
    }

    [Fact]
    public void Raising_priority_moves_the_task_by_the_insertion_rule_and_the_choice_survives_a_restart()
    {
        var folder = Directory.CreateTempSubdirectory("tw-raise").FullName;
        File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "manual": true,
              "tasks": [
                { "title": "email Sarah", "details": "", "priority": "medium", "effort": "short", "created": "2026-01-02T00:00:00Z" },
                { "title": "pay rent", "details": "", "priority": "high", "effort": "quick", "created": "2026-01-01T00:00:00Z" },
                { "title": "water plants", "details": "", "priority": "low", "effort": "long", "created": "2026-01-03T00:00:00Z" }
              ]
            }
            """);

        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            var plants = model.Tasks[2];
            model.CyclePriority(plants);

            Assert.Equal(Priority.High, plants.Priority);
            Assert.Equal(Effort.Long, plants.Effort);
            Assert.True(plants.UserSetPriority);
            Assert.False(plants.UserSetEffort);
            Assert.Equal(2, model.OpenHighCount);
            Assert.Equal(0, model.OpenLowCount);
            Assert.Equal(
                ["water plants", "email Sarah", "pay rent"],
                model.Tasks.Select(task => task.Title));

            model.Undo();
            Assert.Equal(Priority.Low, plants.Priority);
            Assert.False(plants.UserSetPriority);
            Assert.Equal(
                ["email Sarah", "pay rent", "water plants"],
                model.Tasks.Select(task => task.Title));

            model.Redo();
            clock.Advance(TimeSpan.FromMilliseconds(300));
        }
        finally
        {
            model.Dispose();
        }

        try
        {
            var again = new AppModel(folder, new ManualClock());
            Assert.Equal(
                ["water plants", "email Sarah", "pay rent"],
                again.Tasks.Select(task => task.Title));
            Assert.Equal(Priority.High, again.Tasks[0].Priority);
            Assert.Equal(Effort.Long, again.Tasks[0].Effort);
            Assert.True(again.Tasks[0].UserSetPriority);
            Assert.False(again.Tasks[0].UserSetEffort);

            using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "tasks.json")));
            var stored = saved.RootElement.GetProperty("tasks").EnumerateArray().First();
            Assert.Equal("water plants", stored.GetProperty("title").GetString());
            Assert.Equal("high", stored.GetProperty("priority").GetString());
            Assert.True(stored.GetProperty("userSetPriority").GetBoolean());
            Assert.False(stored.GetProperty("userSetEffort").GetBoolean());
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Clicking_the_effort_chip_cycles_it_and_re_places_the_task()
    {
        var folder = Directory.CreateTempSubdirectory("tw-effort").FullName;
        File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "manual": true,
              "tasks": [
                { "title": "email Sarah", "details": "", "priority": "medium", "effort": "short", "created": "2026-01-02T00:00:00Z" },
                { "title": "buy milk", "details": "", "priority": "medium", "effort": "quick", "created": "2026-01-01T00:00:00Z" },
                { "title": "water plants", "details": "", "priority": "low", "effort": "long", "created": "2026-01-03T00:00:00Z" }
              ]
            }
            """);

        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            var email = model.Tasks[0];
            Assert.Equal(Effort.Short, email.Effort);
            Assert.False(email.UserSetEffort);

            model.CycleEffort(email);
            Assert.Equal(Effort.Long, email.Effort);
            Assert.True(email.UserSetEffort);
            Assert.False(email.UserSetPriority);
            Assert.Equal(
                ["buy milk", "email Sarah", "water plants"],
                model.Tasks.Select(task => task.Title));

            model.CycleEffort(email);
            Assert.Equal(Effort.Quick, email.Effort);
            model.CycleEffort(email);
            Assert.Equal(Effort.Short, email.Effort);
            Assert.True(email.UserSetEffort);

            model.Undo();
            model.Undo();
            model.Undo();
            Assert.Equal(Effort.Short, email.Effort);
            Assert.False(email.UserSetEffort);
            Assert.Equal(
                ["email Sarah", "buy milk", "water plants"],
                model.Tasks.Select(task => task.Title));

            model.Redo();
            clock.Advance(TimeSpan.FromMilliseconds(300));
        }
        finally
        {
            model.Dispose();
        }

        try
        {
            var again = new AppModel(folder, new ManualClock());
            Assert.Equal(Effort.Long, again.Tasks[1].Effort);
            Assert.Equal("email Sarah", again.Tasks[1].Title);
            Assert.True(again.Tasks[1].UserSetEffort);
            Assert.False(again.Tasks[1].UserSetPriority);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
