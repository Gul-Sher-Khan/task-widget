using System.Text.Json;
using Microsoft.Win32;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void Rows_before_scrolling_changes_the_list_cap_and_is_kept()
    {
        var folder = Directory.CreateTempSubdirectory("tw-rows").FullName;
        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            Assert.Equal(8, model.RowsBeforeScrolling);
            Assert.Equal(300, model.ListMaxHeight);

            model.RowsBeforeScrolling = 12;

            Assert.Equal(12, model.RowsBeforeScrolling);
            Assert.Equal(448, model.ListMaxHeight);
            Assert.False(File.Exists(Path.Combine(folder, "settings.json")));

            clock.Advance(TimeSpan.FromMilliseconds(300));

            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "settings.json"))))
                Assert.Equal(12, saved.RootElement.GetProperty("rowsBeforeScrolling").GetInt32());

            model.Dispose();

            var again = new AppModel(folder, new ManualClock());
            Assert.Equal(12, again.RowsBeforeScrolling);
            Assert.Equal(448, again.ListMaxHeight);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Rows_before_scrolling_stays_between_four_and_fifteen()
    {
        var folder = Directory.CreateTempSubdirectory("tw-row-bounds").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.RowsBeforeScrolling = 3;
            Assert.Equal(4, model.RowsBeforeScrolling);
            Assert.Equal(152, model.ListMaxHeight);

            model.RowsBeforeScrolling = 16;
            Assert.Equal(15, model.RowsBeforeScrolling);
            Assert.Equal(559, model.ListMaxHeight);

            model.Dispose();
            File.WriteAllText(Path.Combine(folder, "settings.json"), """{"schemaVersion":1,"rowsBeforeScrolling":99}""");
            var again = new AppModel(folder, new ManualClock());
            Assert.Equal(15, again.RowsBeforeScrolling);
            Assert.Equal(559, again.ListMaxHeight);
            again.Dispose();
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_settings_file_from_before_this_choice_keeps_eight_rows()
    {
        var folder = Directory.CreateTempSubdirectory("tw-old-settings").FullName;
        File.WriteAllText(Path.Combine(folder, "settings.json"), """{"schemaVersion":1}""");
        var model = new AppModel(folder, new ManualClock());
        try
        {
            Assert.Equal(8, model.RowsBeforeScrolling);
            Assert.Equal(300, model.ListMaxHeight);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_corrupt_settings_file_opens_with_the_usual_rows()
    {
        var folder = Directory.CreateTempSubdirectory("tw-corrupt-settings").FullName;
        var settingsPath = Path.Combine(folder, "settings.json");
        File.WriteAllText(settingsPath, "{ this is not json");
        var model = new AppModel(folder, new ManualClock());
        try
        {
            Assert.Equal(8, model.RowsBeforeScrolling);
            Assert.Equal(300, model.ListMaxHeight);
            Assert.Equal("{ this is not json", File.ReadAllText(settingsPath));
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Start_with_Windows_adds_and_removes_the_Run_value()
    {
        var folder = Directory.CreateTempSubdirectory("tw-run").FullName;
        var runKey = @"Software\TaskWidgetTests\" + Guid.NewGuid().ToString("N");
        var model = new AppModel(folder, new ManualClock(), runKey);
        try
        {
            Assert.False(model.StartWithWindows);
            Assert.Null(RunValue(runKey));

            model.StartWithWindows = true;

            Assert.Equal("\"" + Environment.ProcessPath + "\"", RunValue(runKey));

            model.StartWithWindows = false;

            Assert.Null(RunValue(runKey));
            Assert.False(model.StartWithWindows);
        }
        finally
        {
            model.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(runKey, throwOnMissingSubKey: false);
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Launching_leaves_the_Run_value_as_it_was()
    {
        var folder = Directory.CreateTempSubdirectory("tw-run-keep").FullName;
        var runKey = @"Software\TaskWidgetTests\" + Guid.NewGuid().ToString("N");
        using (var key = Registry.CurrentUser.CreateSubKey(runKey))
            key.SetValue("Task Widget", @"C:\Kept\TaskWidget.exe");

        var model = new AppModel(folder, new ManualClock(), runKey);
        try
        {
            Assert.True(model.StartWithWindows);
            Assert.Equal(@"C:\Kept\TaskWidget.exe", RunValue(runKey));
            model.Dispose();
            Assert.Equal(@"C:\Kept\TaskWidget.exe", RunValue(runKey));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(runKey, throwOnMissingSubKey: false);
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void About_names_the_version_and_links_to_GitHub_and_Privacy()
    {
        var folder = Directory.CreateTempSubdirectory("tw-about").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            Assert.Equal("Task Widget 1.0.0", model.VersionLine);
            Assert.Equal("https://github.com/Gul-Sher-Khan/task-widget", model.GitHubUrl);
            Assert.Equal("https://github.com/Gul-Sher-Khan/task-widget#privacy", model.PrivacyUrl);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_gear_opens_and_closes_Settings()
    {
        var folder = Directory.CreateTempSubdirectory("tw-gear").FullName;
        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            Assert.False(model.SettingsOpen);
            Assert.True(model.ShowingTasks);

            model.ToggleSettings();
            Assert.True(model.SettingsOpen);
            Assert.False(model.ShowingTasks);

            model.ToggleSettings();
            Assert.False(model.SettingsOpen);
            Assert.True(model.ShowingTasks);

            model.ToggleSettings();
            clock.Advance(TimeSpan.FromMilliseconds(300));
            Assert.False(File.Exists(Path.Combine(folder, "settings.json")));

            model.Dispose();
            var again = new AppModel(folder, new ManualClock());
            Assert.False(again.SettingsOpen);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    static object? RunValue(string runKey) =>
        Registry.CurrentUser.OpenSubKey(runKey)?.GetValue("Task Widget");
}
