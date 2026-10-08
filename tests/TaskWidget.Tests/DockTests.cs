using System.Text.Json;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class DockTests
{
    [Fact]
    public void Collapse_docks_and_a_restart_opens_the_dock()
    {
        var folder = Directory.CreateTempSubdirectory("tw-dock").FullName;
        var clock = new ManualClock();
        try
        {
            var model = new AppModel(folder, clock);
            Assert.False(model.Docked);

            model.Dock();
            Assert.True(model.Docked);
            model.Dispose();

            using (var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "settings.json"))))
            {
                Assert.Equal(1, settings.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.True(settings.RootElement.GetProperty("docked").GetBoolean());
            }

            var again = new AppModel(folder, new ManualClock());
            Assert.True(again.Docked);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Clicking_the_dock_expands_and_a_restart_opens_the_widget()
    {
        var folder = Directory.CreateTempSubdirectory("tw-expand").FullName;
        try
        {
            var model = new AppModel(folder, new ManualClock());
            model.Dock();
            model.Expand();
            Assert.False(model.Docked);
            model.Dispose();

            using (var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "settings.json"))))
                Assert.False(settings.RootElement.GetProperty("docked").GetBoolean());

            var again = new AppModel(folder, new ManualClock());
            Assert.False(again.Docked);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Dock_counts_match_open_tasks_and_a_level_with_none_is_zero()
    {
        var folder = Directory.CreateTempSubdirectory("tw-counts").FullName;
        File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "tasks": [
                { "title": "Fix the build", "details": "", "priority": "high", "effort": "quick" },
                { "title": "Email Sarah", "details": "", "priority": "high", "effort": "short" },
                { "title": "Buy milk", "details": "", "priority": "low", "effort": "long" }
              ]
            }
            """);
        var model = new AppModel(folder, new ManualClock());
        try
        {
            Assert.Equal(2, model.OpenHighCount);
            Assert.Equal(0, model.OpenMediumCount);
            Assert.Equal(1, model.OpenLowCount);

            model.UpdateDraft("call the bank");
            model.CommitCapture();

            Assert.Equal(2, model.OpenHighCount);
            Assert.Equal(1, model.OpenMediumCount);
            Assert.Equal(1, model.OpenLowCount);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_attention_dot_stays_off_until_it_is_lit()
    {
        var folder = Directory.CreateTempSubdirectory("tw-dot").FullName;
        File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "tasks": [
                { "title": "Fix the build", "details": "", "priority": "high", "effort": "quick" }
              ]
            }
            """);
        var model = new AppModel(folder, new ManualClock());
        try
        {
            Assert.False(model.Attention);
            model.Attention = true;
            Assert.True(model.Attention);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_corrupt_settings_file_opens_the_widget()
    {
        var folder = Directory.CreateTempSubdirectory("tw-settings-corrupt").FullName;
        File.WriteAllText(Path.Combine(folder, "settings.json"), "{ not json");
        var model = new AppModel(folder, new ManualClock());
        try
        {
            Assert.False(model.Docked);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }
}
