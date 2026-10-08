using System.Text.Json;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class StorageDurabilityTests
{
    [Fact]
    public void A_damaged_task_list_is_restored_from_its_backup()
    {
        var folder = Directory.CreateTempSubdirectory("tw-recover").FullName;
        var clock = new ManualClock();
        try
        {
            var bak = Path.Combine(folder, "tasks.json.bak");
            File.WriteAllText(bak, TaskFile("email Sarah"));
            File.SetLastWriteTime(bak, new DateTime(2026, 10, 8, 14, 2, 0, DateTimeKind.Local));
            File.WriteAllText(Path.Combine(folder, "tasks.json"), "{ this is not json");

            var model = new AppModel(folder, clock);
            Assert.Equal(WidgetBanner.Recovered, model.Banner);
            Assert.Equal(
                "Your task list was damaged and has been restored from a backup (saved 14:02).",
                model.BannerMessage);
            Assert.Equal("Open folder", model.BannerPrimary);
            Assert.True(model.BannerDismissible);
            Assert.Equal("email Sarah", Assert.Single(model.Tasks).Title);

            var corrupt = Assert.Single(Directory.GetFiles(folder, "tasks.corrupt-*.json"));
            Assert.Contains("this is not json", File.ReadAllText(corrupt), StringComparison.Ordinal);
            Assert.True(File.Exists(bak));
            Assert.Contains("email Sarah", File.ReadAllText(Path.Combine(folder, "tasks.json")), StringComparison.Ordinal);

            model.DismissBanner();
            Assert.Equal(WidgetBanner.None, model.Banner);
            model.Dispose();

            var again = new AppModel(folder, new ManualClock());
            Assert.Equal(WidgetBanner.None, again.Banner);
            Assert.Equal("email Sarah", Assert.Single(again.Tasks).Title);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Both_task_files_unreadable_starts_empty_and_keeps_the_old_file()
    {
        var folder = Directory.CreateTempSubdirectory("tw-empty-start").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "tasks.json"), "{ this is not json");
            File.WriteAllText(Path.Combine(folder, "tasks.json.bak"), "also not json");

            var model = new AppModel(folder, new ManualClock());
            Assert.Equal(WidgetBanner.StartedEmpty, model.Banner);
            Assert.Equal(
                "Your task list couldn't be read, so Task Widget started fresh. The old file was kept.",
                model.BannerMessage);
            Assert.Equal("Open folder", model.BannerPrimary);
            Assert.True(model.BannerDismissible);
            Assert.Empty(model.Tasks);
            Assert.Empty(model.DoneTasks);

            var corrupt = Assert.Single(Directory.GetFiles(folder, "tasks.corrupt-*.json"));
            Assert.Contains("this is not json", File.ReadAllText(corrupt), StringComparison.Ordinal);
            Assert.Equal("also not json", File.ReadAllText(Path.Combine(folder, "tasks.json.bak")));
            Assert.False(File.Exists(Path.Combine(folder, "tasks.json")));

            model.DismissBanner();
            Assert.Equal(WidgetBanner.None, model.Banner);
            model.Dispose();

            var again = new AppModel(folder, new ManualClock());
            Assert.Equal(WidgetBanner.None, again.Banner);
            Assert.Empty(again.Tasks);
            Assert.Single(Directory.GetFiles(folder, "tasks.corrupt-*.json"));
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_newer_schema_opens_read_only_and_still_accepts_a_draft()
    {
        var folder = Directory.CreateTempSubdirectory("tw-newer").FullName;
        var clock = new ManualClock();
        var path = Path.Combine(folder, "tasks.json");
        try
        {
            File.WriteAllText(path, """
            {
              "schemaVersion": 2,
              "draft": "",
              "tasks": [
                { "title": "email Sarah", "details": "the invoice", "priority": "high", "effort": "quick" }
              ]
            }
            """);
            var before = File.ReadAllBytes(path);

            var model = new AppModel(folder, clock);
            Assert.True(model.ReadOnly);
            Assert.Equal(WidgetBanner.NewerFile, model.Banner);
            Assert.Equal(
                "These tasks were saved by a newer Task Widget. Update to make changes.",
                model.BannerMessage);
            Assert.Equal("Get update", model.BannerPrimary);
            Assert.False(model.BannerDismissible);
            Assert.Equal("Saved by a newer Task Widget. Update to add Captures.", model.CaptureLockTip);
            var task = Assert.Single(model.Tasks);
            Assert.Equal("email Sarah", task.Title);
            Assert.Equal("the invoice", task.Details);
            Assert.Equal(Priority.High, task.Priority);
            Assert.Equal(Effort.Quick, task.Effort);

            model.UpdateDraft("buy milk");
            Assert.Equal("buy milk", model.CaptureText);
            model.CommitCapture();
            Assert.Equal("buy milk", model.CaptureText);
            Assert.Equal("email Sarah", Assert.Single(model.Tasks).Title);

            model.Tick(task);
            clock.Advance(TimeSpan.FromMilliseconds(1500));
            Assert.False(task.IsStriking);
            Assert.False(task.IsDone);
            Assert.Equal("email Sarah", Assert.Single(model.Tasks).Title);

            model.EditTitle(task, "email Sam");
            Assert.Equal("email Sarah", task.Title);
            Assert.False(task.IsEditing);

            clock.Advance(TimeSpan.FromMilliseconds(300));
            model.Dispose();
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False(File.Exists(Path.Combine(folder, "tasks.v2.bak")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void An_older_schema_is_migrated_after_a_version_backup()
    {
        var folder = Directory.CreateTempSubdirectory("tw-migrate").FullName;
        var path = Path.Combine(folder, "tasks.json");
        try
        {
            File.WriteAllText(path, """
            {
              "schemaVersion": 0,
              "draft": "still drafting",
              "tasks": [
                { "title": "email Sarah", "details": "", "priority": "high", "effort": "quick" }
              ]
            }
            """);

            var model = new AppModel(folder, new ManualClock());
            Assert.False(model.ReadOnly);
            Assert.Equal(WidgetBanner.None, model.Banner);
            Assert.Equal("still drafting", model.CaptureText);
            var task = Assert.Single(model.Tasks);
            Assert.Equal("email Sarah", task.Title);
            Assert.Equal(Priority.High, task.Priority);
            Assert.Equal(Effort.Quick, task.Effort);

            var versionBak = Path.Combine(folder, "tasks.v0.bak");
            using (var old = JsonDocument.Parse(File.ReadAllText(versionBak)))
            {
                Assert.Equal(0, old.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.Equal("email Sarah", old.RootElement.GetProperty("tasks")[0].GetProperty("title").GetString());
                Assert.Equal("still drafting", old.RootElement.GetProperty("draft").GetString());
            }

            using (var saved = JsonDocument.Parse(File.ReadAllText(path)))
            {
                Assert.Equal(1, saved.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.Equal("email Sarah", saved.RootElement.GetProperty("tasks")[0].GetProperty("title").GetString());
                Assert.Equal("still drafting", saved.RootElement.GetProperty("draft").GetString());
            }

            model.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Old_done_tasks_and_orphaned_captures_are_archived_and_deleted_tasks_are_purged()
    {
        var folder = Directory.CreateTempSubdirectory("tw-retain").FullName;
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var archivePath = Path.Combine(folder, "archive.jsonl");
        try
        {
            File.WriteAllText(archivePath, """{"kind":"task","title":"already archived"}""" + "\n");
            File.WriteAllText(Path.Combine(folder, "tasks.json"), """
            {
              "schemaVersion": 1,
              "draft": "",
              "tasks": [
                { "title": "fix the login test", "details": "", "priority": "medium", "effort": "short", "capture": "cap-open" },
                { "title": "email Sarah", "details": "", "priority": "low", "effort": "long", "completedAt": "2026-07-09T12:00:00Z", "capture": "cap-gone" },
                { "title": "file the receipt", "details": "", "priority": "low", "effort": "short", "completedAt": "2026-07-09T12:00:00Z", "capture": "cap-mixed" },
                { "title": "pay the bill", "details": "", "priority": "high", "effort": "quick", "completedAt": "2026-07-10T12:00:00Z", "capture": "cap-bill" },
                { "title": "send the receipt", "details": "", "priority": "medium", "effort": "short", "completedAt": "2026-10-01T08:00:00Z", "capture": "cap-mixed" },
                { "title": "book the dentist", "details": "", "priority": "medium", "effort": "short", "completedAt": "2026-10-07T15:00:00Z" },
                { "title": "buy milk", "details": "", "priority": "low", "effort": "quick", "deletedAt": "2026-09-08T12:00:00Z", "capture": "cap-purged" },
                { "title": "call mom", "details": "", "priority": "medium", "effort": "short", "deletedAt": "2026-09-09T12:00:00Z", "capture": "cap-recent-delete" }
              ],
              "captures": [
                { "id": "cap-open", "text": "fix the login", "state": "interpreted" },
                { "id": "cap-gone", "text": "email Sarah the invoice", "state": "interpreted" },
                { "id": "cap-mixed", "text": "the receipt from the trip", "state": "interpreted" },
                { "id": "cap-bill", "text": "pay the electricity bill", "state": "interpreted" },
                { "id": "cap-purged", "text": "buy the oat milk", "state": "interpreted" },
                { "id": "cap-recent-delete", "text": "call mom tonight", "state": "interpreted" },
                { "id": "cap-failed", "text": "waiting on the bank", "state": "failed" }
              ]
            }
            """);

            var model = new AppModel(folder, new ManualClock(now));
            Assert.Equal("fix the login test", Assert.Single(model.Tasks).Title);
            Assert.Equal(
                ["book the dentist", "send the receipt", "pay the bill"],
                model.DoneTasks.Select(task => task.Title).ToArray());
            Assert.DoesNotContain(model.Tasks.Concat(model.DoneTasks), task => task.Title is "email Sarah" or "file the receipt" or "buy milk" or "call mom" or "already archived");

            var lines = File.ReadAllLines(archivePath);
            Assert.Contains("already archived", lines[0], StringComparison.Ordinal);
            Assert.Contains(lines, line => line.Contains("email Sarah", StringComparison.Ordinal) && line.Contains("task", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("file the receipt", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("email Sarah the invoice", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("buy the oat milk", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains("buy milk", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains("call mom", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains("pay the bill", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains("waiting on the bank", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains("the receipt from the trip", StringComparison.Ordinal));

            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "tasks.json"))))
            {
                var titles = saved.RootElement.GetProperty("tasks").EnumerateArray()
                    .Select(task => task.GetProperty("title").GetString() ?? "")
                    .ToArray();
                Assert.Equal(["fix the login test", "pay the bill", "send the receipt", "book the dentist", "call mom"], titles);
                var callMom = saved.RootElement.GetProperty("tasks").EnumerateArray()
                    .Single(task => task.GetProperty("title").GetString() == "call mom");
                Assert.Equal("2026-09-09T12:00:00+00:00", callMom.GetProperty("deletedAt").GetString());
                var captureText = saved.RootElement.GetProperty("captures").EnumerateArray()
                    .Select(capture => capture.GetProperty("text").GetString() ?? "")
                    .ToArray();
                Assert.Equal(
                    ["fix the login", "the receipt from the trip", "pay the electricity bill", "call mom tonight", "waiting on the bank"],
                    captureText);
            }

            var archived = File.ReadAllText(archivePath);
            model.Dispose();

            var again = new AppModel(folder, new ManualClock(now));
            Assert.Equal("fix the login test", Assert.Single(again.Tasks).Title);
            Assert.Equal(
                ["book the dentist", "send the receipt", "pay the bill"],
                again.DoneTasks.Select(task => task.Title).ToArray());
            Assert.Equal(archived, File.ReadAllText(archivePath));
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void One_banner_shows_at_a_time_with_newer_version_ahead_of_signed_out_ahead_of_recovery()
    {
        var folders = new List<string>();
        try
        {
            var recovered = "Your task list was damaged and has been restored from a backup (saved 14:02).";
            var signedOut = "You're signed out of ChatGPT. New Captures wait until you sign in.";
            var newerFile = "These tasks were saved by a newer Task Widget. Update to make changes.";

            var updateAndRecovered = Open(update: true, signedOut: false, newerFile: false, damaged: true);
            Assert.Equal(WidgetBanner.NewerVersion, updateAndRecovered.Banner);
            Assert.Equal("Version 1.2.0 is available", updateAndRecovered.BannerMessage);
            Assert.Equal("Get update", updateAndRecovered.BannerPrimary);
            Assert.Equal("email Sarah", Assert.Single(updateAndRecovered.Tasks).Title);
            Assert.NotEqual(recovered, updateAndRecovered.BannerMessage);
            updateAndRecovered.Dispose();

            var signedOutAndRecovered = Open(update: false, signedOut: true, newerFile: false, damaged: true);
            Assert.Equal(WidgetBanner.SignedOut, signedOutAndRecovered.Banner);
            Assert.Equal(signedOut, signedOutAndRecovered.BannerMessage);
            Assert.Equal("Sign in", signedOutAndRecovered.BannerPrimary);
            Assert.Equal("email Sarah", Assert.Single(signedOutAndRecovered.Tasks).Title);
            Assert.NotEqual(recovered, signedOutAndRecovered.BannerMessage);
            signedOutAndRecovered.Dispose();

            var newerFileAndSignedOut = Open(update: false, signedOut: true, newerFile: true, damaged: false);
            Assert.Equal(WidgetBanner.NewerFile, newerFileAndSignedOut.Banner);
            Assert.Equal(newerFile, newerFileAndSignedOut.BannerMessage);
            Assert.True(newerFileAndSignedOut.ReadOnly);
            Assert.NotEqual(signedOut, newerFileAndSignedOut.BannerMessage);
            newerFileAndSignedOut.Dispose();

            var updateAndNewerFile = Open(update: true, signedOut: true, newerFile: true, damaged: false);
            Assert.Equal(WidgetBanner.NewerVersion, updateAndNewerFile.Banner);
            Assert.Equal("Version 1.2.0 is available", updateAndNewerFile.BannerMessage);
            Assert.True(updateAndNewerFile.ReadOnly);
            Assert.NotEqual(newerFile, updateAndNewerFile.BannerMessage);
            updateAndNewerFile.Dispose();

            var firstRun = Open(update: false, signedOut: false, newerFile: false, damaged: false);
            Assert.Equal(WidgetBanner.None, firstRun.Banner);
            firstRun.Dispose();
        }
        finally
        {
            foreach (var folder in folders)
                Directory.Delete(folder, recursive: true);
        }

        AppModel Open(bool update, bool signedOut, bool newerFile, bool damaged)
        {
            var folder = Directory.CreateTempSubdirectory("tw-banner").FullName;
            folders.Add(folder);
            return OpenIn(folder, update, signedOut, newerFile, damaged);
        }
    }

    static AppModel OpenIn(string folder, bool update, bool signedOut, bool newerFile, bool damaged)
    {
        var tasks = newerFile
            ? """
            {
              "schemaVersion": 2,
              "draft": "",
              "tasks": [
                { "title": "email Sarah", "details": "", "priority": "medium", "effort": "short" }
              ]
            }
            """
            : TaskFile("email Sarah");
        if (damaged)
        {
            var bak = Path.Combine(folder, "tasks.json.bak");
            File.WriteAllText(bak, tasks);
            File.SetLastWriteTime(bak, new DateTime(2026, 10, 8, 14, 2, 0, DateTimeKind.Local));
            File.WriteAllText(Path.Combine(folder, "tasks.json"), "{ this is not json");
        }
        else if (newerFile)
        {
            File.WriteAllText(Path.Combine(folder, "tasks.json"), tasks);
        }

        File.WriteAllText(Path.Combine(folder, "settings.json"), $$"""
        {
          "schemaVersion": 1,
          "welcomeRetired": {{(signedOut ? "true" : "false")}},
          "checkForUpdatesAutomatically": false,
          "updateResult": "{{(update ? "available" : "")}}",
          "availableVersion": "{{(update ? "1.2.0" : "")}}"
        }
        """);
        return new AppModel(folder, new ManualClock());
    }

    static string TaskFile(string title) => $$"""
        {
          "schemaVersion": 1,
          "draft": "",
          "tasks": [
            { "title": "{{title}}", "details": "", "priority": "medium", "effort": "short" }
          ]
        }
        """;
}
