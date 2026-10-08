using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class RankingTests
{
    [Fact]
    public void Tasks_sort_by_priority_then_effort_then_oldest()
    {
        var folder = Directory.CreateTempSubdirectory("tw-rank").FullName;
        File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "tasks": [
                { "title": "water plants", "details": "", "priority": "low", "effort": "long", "created": "2026-01-04T00:00:00Z" },
                { "title": "email Sarah", "details": "", "priority": "medium", "effort": "short", "created": "2026-01-02T00:00:00Z" },
                { "title": "book flights", "details": "", "priority": "high", "effort": "long", "created": "2026-01-01T00:00:00Z" },
                { "title": "pay rent", "details": "", "priority": "high", "effort": "quick", "created": "2026-01-03T00:00:00Z" },
                { "title": "buy milk", "details": "", "priority": "medium", "effort": "quick", "created": "2026-01-01T00:00:00Z" },
                { "title": "file taxes", "details": "", "priority": "high", "effort": "quick", "created": "2026-01-01T00:00:00Z" },
                { "title": "call mom", "details": "", "priority": "high", "effort": "short", "created": "2026-01-02T00:00:00Z" },
                { "title": "sweep garage", "details": "", "priority": "low", "effort": "quick", "created": "2026-01-01T00:00:00Z" }
              ]
            }
            """);

        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.ReSort();

            Assert.Equal(
                [
                    "file taxes",
                    "pay rent",
                    "call mom",
                    "book flights",
                    "buy milk",
                    "email Sarah",
                    "sweep garage",
                    "water plants",
                ],
                model.Tasks.Select(task => task.Title));
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Tasks_from_one_capture_keep_spoken_order_when_they_rank_equal()
    {
        var folder = Directory.CreateTempSubdirectory("tw-spoken").FullName;
        File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "tasks": [
                { "title": "buy milk", "details": "", "priority": "medium", "effort": "short", "created": "2026-01-01T00:00:00Z", "capture": "cap-1", "spoken": 1 },
                { "title": "call mom", "details": "", "priority": "medium", "effort": "short", "created": "2026-01-01T00:00:00Z", "capture": "cap-1", "spoken": 2 },
                { "title": "email Sarah", "details": "", "priority": "medium", "effort": "short", "created": "2026-01-01T00:00:00Z", "capture": "cap-1", "spoken": 0 }
              ]
            }
            """);

        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.ReSort();

            Assert.Equal(["email Sarah", "buy milk", "call mom"], model.Tasks.Select(task => task.Title));
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_new_task_slots_in_before_the_first_task_the_rule_ranks_below_it()
    {
        var folder = Directory.CreateTempSubdirectory("tw-insert").FullName;
        File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "manual": true,
              "tasks": [
                { "title": "email Sarah", "details": "", "priority": "medium", "effort": "short", "created": "2026-01-02T00:00:00Z", "spoken": 0 },
                { "title": "pay rent", "details": "", "priority": "high", "effort": "quick", "created": "2026-01-01T00:00:00Z", "spoken": 0 },
                { "title": "water plants", "details": "", "priority": "low", "effort": "long", "created": "2026-01-03T00:00:00Z", "spoken": 0 }
              ]
            }
            """);

        var clock = new ManualClock();
        clock.Set(new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero));
        var model = new AppModel(folder, clock);
        try
        {
            Assert.True(model.HasManualPositions);
            Assert.Equal(["email Sarah", "pay rent", "water plants"], model.Tasks.Select(task => task.Title));

            model.UpdateDraft("buy milk");
            model.CommitCapture();

            Assert.Equal(
                ["email Sarah", "pay rent", "buy milk", "water plants"],
                model.Tasks.Select(task => task.Title));
            Assert.Equal(Priority.Medium, model.Tasks[2].Priority);
            Assert.Equal(Effort.Short, model.Tasks[2].Effort);
            Assert.Equal("", model.Tasks[2].Details);
            Assert.Equal(Priority.High, model.Tasks[1].Priority);
            Assert.Equal(Effort.Quick, model.Tasks[1].Effort);
            Assert.True(model.HasManualPositions);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_new_task_lands_in_rule_order_when_nothing_has_been_moved()
    {
        var folder = Directory.CreateTempSubdirectory("tw-pure").FullName;
        File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "tasks": [
                { "title": "pay rent", "details": "", "priority": "high", "effort": "quick", "created": "2026-01-01T00:00:00Z" },
                { "title": "email Sarah", "details": "", "priority": "medium", "effort": "short", "created": "2026-01-02T00:00:00Z" },
                { "title": "water plants", "details": "", "priority": "low", "effort": "long", "created": "2026-01-03T00:00:00Z" }
              ]
            }
            """);

        var clock = new ManualClock();
        clock.Set(new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero));
        var model = new AppModel(folder, clock);
        try
        {
            Assert.False(model.HasManualPositions);
            model.UpdateDraft("buy milk");
            model.CommitCapture();

            Assert.Equal(
                ["pay rent", "email Sarah", "buy milk", "water plants"],
                model.Tasks.Select(task => task.Title));
            Assert.False(model.HasManualPositions);
            Assert.Equal(4, model.OpenTaskCount);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Moving_a_task_changes_only_its_position_and_keeps_that_order()
    {
        var folder = Directory.CreateTempSubdirectory("tw-move").FullName;
        File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "tasks": [
                { "title": "pay rent", "details": "", "priority": "high", "effort": "quick", "created": "2026-01-01T00:00:00Z" },
                { "title": "email Sarah", "details": "", "priority": "medium", "effort": "short", "created": "2026-01-02T00:00:00Z" },
                { "title": "water plants", "details": "", "priority": "low", "effort": "long", "created": "2026-01-03T00:00:00Z" }
              ]
            }
            """);

        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.MoveTo(2, 0);

            Assert.Equal(["water plants", "pay rent", "email Sarah"], model.Tasks.Select(task => task.Title));
            Assert.Equal(Priority.Low, model.Tasks[0].Priority);
            Assert.Equal(Effort.Long, model.Tasks[0].Effort);
            Assert.Equal(Priority.High, model.Tasks[1].Priority);
            Assert.Equal(Effort.Quick, model.Tasks[1].Effort);
            Assert.True(model.HasManualPositions);

            model.Dispose();

            using (var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "tasks.json"))))
            {
                Assert.True(saved.RootElement.GetProperty("manual").GetBoolean());
                Assert.Equal(
                    ["water plants", "pay rent", "email Sarah"],
                    saved.RootElement.GetProperty("tasks").EnumerateArray().Select(task => task.GetProperty("title").GetString()));
            }

            var again = new AppModel(folder, new ManualClock());
            Assert.Equal(["water plants", "pay rent", "email Sarah"], again.Tasks.Select(task => task.Title));
            Assert.Equal(Priority.Low, again.Tasks[0].Priority);
            Assert.Equal(Effort.Long, again.Tasks[0].Effort);
            Assert.True(again.HasManualPositions);
            again.Dispose();
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_dragged_row_keeps_its_new_place_after_a_restart()
    {
        var folder = Directory.CreateTempSubdirectory("tw-drag").FullName;
        File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "tasks": [
                { "title": "pay rent", "details": "", "priority": "high", "effort": "quick", "created": "2026-01-01T00:00:00Z" },
                { "title": "email Sarah", "details": "", "priority": "medium", "effort": "short", "created": "2026-01-02T00:00:00Z" },
                { "title": "water plants", "details": "", "priority": "low", "effort": "long", "created": "2026-01-03T00:00:00Z" }
              ]
            }
            """);

        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.Tasks.Move(2, 0);
            model.AcceptReorder(2, 0);

            Assert.Equal(["water plants", "pay rent", "email Sarah"], model.Tasks.Select(task => task.Title));
            Assert.Equal(Priority.Low, model.Tasks[0].Priority);
            Assert.Equal(Effort.Long, model.Tasks[0].Effort);
            Assert.True(model.HasManualPositions);

            model.Dispose();

            var again = new AppModel(folder, new ManualClock());
            Assert.Equal(["water plants", "pay rent", "email Sarah"], again.Tasks.Select(task => task.Title));
            Assert.Equal(Priority.Low, again.Tasks[0].Priority);
            Assert.True(again.HasManualPositions);
            again.Dispose();
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Re_sort_discards_manual_positions_and_restores_rule_order()
    {
        var folder = Directory.CreateTempSubdirectory("tw-resort").FullName;
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
            Assert.True(model.HasManualPositions);

            model.ReSort();

            Assert.Equal(["pay rent", "email Sarah", "water plants"], model.Tasks.Select(task => task.Title));
            Assert.False(model.HasManualPositions);

            model.Dispose();

            var again = new AppModel(folder, new ManualClock());
            Assert.Equal(["pay rent", "email Sarah", "water plants"], again.Tasks.Select(task => task.Title));
            Assert.False(again.HasManualPositions);
            again.Dispose();
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}
