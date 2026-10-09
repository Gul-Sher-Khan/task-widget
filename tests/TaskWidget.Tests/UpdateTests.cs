using System.Net;
using System.Text;
using System.Text.Json;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class UpdateTests
{
    [Fact]
    public async Task A_newer_published_release_shows_in_About_at_startup()
    {
        var folder = Directory.CreateTempSubdirectory("tw-update-newer").FullName;
        var handler = new ReleaseHandler();
        handler.Hold();
        var model = new AppModel(folder, new ManualClock(), http: handler);
        try
        {
            Assert.True(model.UpdateChecking);
            Assert.Equal("Checking for updates…", model.AboutStatus);

            handler.Release(Json("""
                [
                  {
                    "tag_name": "v1.2.0",
                    "draft": false,
                    "prerelease": false,
                    "assets": []
                  }
                ]
                """));
            await model.UpdateCheck;

            var sent = Assert.Single(handler.Sent);
            Assert.Equal("GET", sent.Method);
            Assert.Equal("https://api.github.com/repos/Gul-Sher-Khan/task-widget/releases?per_page=100", sent.Url);
            Assert.Equal("TaskWidget", sent.UserAgent);
            Assert.False(sent.HasAuthorization);
            Assert.False(sent.HasContent);
            Assert.Equal("1.2.0", model.NewVersion);
            Assert.True(model.UpdateAvailable);
            Assert.False(model.UpdateChecking);
            Assert.Equal("Version 1.2.0 is available", model.AboutStatus);
            Assert.Equal("Checked just now", model.CheckedWhen);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Drafts_and_pre_releases_are_not_the_latest()
    {
        var folder = Directory.CreateTempSubdirectory("tw-update-drafts").FullName;
        var handler = new ReleaseHandler
        {
            Respond = _ => Json("""
                [
                  { "tag_name": "v2.0.0", "draft": true, "prerelease": false, "assets": [] },
                  { "tag_name": "v1.10.0-beta.1", "draft": false, "prerelease": true, "assets": [] },
                  { "tag_name": "v1.9.0", "draft": false, "prerelease": false, "assets": [] },
                  { "tag_name": "v1.10.0", "draft": false, "prerelease": false, "assets": [] }
                ]
                """),
        };
        var model = new AppModel(folder, new ManualClock(), http: handler);
        try
        {
            await model.UpdateCheck;

            Assert.Equal("1.10.0", model.NewVersion);
            Assert.Equal("Version 1.10.0 is available", model.AboutStatus);
            Assert.True(model.UpdateAvailable);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task The_current_version_and_an_older_one_are_up_to_date()
    {
        var folder = Directory.CreateTempSubdirectory("tw-update-current").FullName;
        var handler = new ReleaseHandler
        {
            Respond = _ => Json("""
                [
                  { "tag_name": "v0.9.0", "draft": false, "prerelease": false, "assets": [] },
                  { "tag_name": "v1.0.0", "draft": false, "prerelease": false, "assets": [] }
                ]
                """),
        };
        var model = new AppModel(folder, new ManualClock(), http: handler);
        try
        {
            await model.UpdateCheck;

            Assert.Equal("", model.NewVersion);
            Assert.True(model.UpdateCurrent);
            Assert.Equal("You're up to date", model.AboutStatus);
            Assert.Equal("Checked just now", model.CheckedWhen);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_failed_check_says_it_could_not_check()
    {
        var folder = Directory.CreateTempSubdirectory("tw-update-fail").FullName;
        var handler = new ReleaseHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
        };
        var model = new AppModel(folder, new ManualClock(), http: handler);
        try
        {
            await model.UpdateCheck;

            Assert.True(model.UpdFailed);
            Assert.False(model.UpdateAvailable);
            Assert.Equal("Couldn't check for updates", model.AboutStatus);
            Assert.Equal("No connection. Will try again later.", model.CheckedWhen);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_patch_above_the_running_version_is_available()
    {
        var folder = Directory.CreateTempSubdirectory("tw-update-patch").FullName;
        var handler = new ReleaseHandler
        {
            Respond = _ => Json("""
                [{ "tag_name": "v1.0.1", "draft": false, "prerelease": false, "assets": [] }]
                """),
        };
        var model = new AppModel(folder, new ManualClock(), http: handler);
        try
        {
            await model.UpdateCheck;
            Assert.Equal("1.0.1", model.NewVersion);
            Assert.Equal("Version 1.0.1 is available", model.AboutStatus);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task The_next_startup_waits_a_day_before_asking_again()
    {
        var folder = Directory.CreateTempSubdirectory("tw-update-day").FullName;
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(start);
        var first = new ReleaseHandler { Respond = _ => Json(Newer) };
        var model = new AppModel(folder, clock, http: first);
        try
        {
            await model.UpdateCheck;
            Assert.Equal("1.2.0", model.NewVersion);
            model.Dispose();

            var soonClock = new ManualClock(start);
            soonClock.Set(new DateTimeOffset(2026, 1, 1, 23, 59, 0, TimeSpan.Zero));
            var soon = new ReleaseHandler { Respond = _ => Json(Newer) };
            var again = new AppModel(folder, soonClock, http: soon);
            Assert.Empty(soon.Sent);
            Assert.Equal("1.2.0", again.NewVersion);
            Assert.Equal("Version 1.2.0 is available", again.AboutStatus);
            Assert.Equal("Checked today, 00:00", again.CheckedWhen);
            again.Dispose();

            var dueClock = new ManualClock(start);
            dueClock.Set(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
            var due = new ReleaseHandler { Respond = _ => Json(Current) };
            var later = new AppModel(folder, dueClock, http: due);
            await later.UpdateCheck;
            Assert.Single(due.Sent);
            Assert.Equal("You're up to date", later.AboutStatus);
            Assert.Equal("", later.NewVersion);
            later.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Automatic_checks_off_sends_nothing_until_Check_now()
    {
        var folder = Directory.CreateTempSubdirectory("tw-update-manual").FullName;
        File.WriteAllText(Path.Combine(folder, "settings.json"), """
            {"schemaVersion":1,"checkForUpdatesAutomatically":false}
            """);
        var handler = new ReleaseHandler { Respond = _ => Json(Newer) };
        var model = new AppModel(folder, new ManualClock(), http: handler);
        try
        {
            await model.UpdateCheck;
            Assert.Empty(handler.Sent);
            Assert.False(model.AutoUpdate);

            await model.CheckForUpdates();

            Assert.Single(handler.Sent);
            Assert.Equal("Version 1.2.0 is available", model.AboutStatus);
            model.AutoUpdate = false;
            model.Dispose();

            var laterClock = new ManualClock();
            laterClock.Set(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero));
            var later = new ReleaseHandler { Respond = _ => Json(Newer) };
            var again = new AppModel(folder, laterClock, http: later);
            await again.UpdateCheck;
            Assert.Empty(later.Sent);
            Assert.False(again.AutoUpdate);
            Assert.Equal("1.2.0", again.NewVersion);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task An_available_update_lights_the_dot_and_shows_in_about_without_a_banner()
    {
        var folder = Directory.CreateTempSubdirectory("tw-update-banner").FullName;
        var handler = new ReleaseHandler();
        handler.Hold();
        var model = new AppModel(folder, new ManualClock(), http: handler);
        try
        {
            Assert.False(model.Attention);
            Assert.Equal(WidgetBanner.None, model.Banner);

            handler.Release(Json(Newer));
            await model.UpdateCheck;

            Assert.True(model.Attention);
            Assert.True(model.UpdateAvailable);
            Assert.Equal("1.2.0", model.NewVersion);
            Assert.False(model.HasBanner);
            Assert.Equal(WidgetBanner.None, model.Banner);
            model.Dispose();

            var soonClock = new ManualClock();
            soonClock.Set(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            var quiet = new ReleaseHandler { Respond = _ => Json(Newer) };
            var again = new AppModel(folder, soonClock, http: quiet);
            Assert.Empty(quiet.Sent);
            Assert.True(again.Attention);
            Assert.True(again.UpdateAvailable);
            Assert.Equal(WidgetBanner.None, again.Banner);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Being_up_to_date_leaves_the_dot_off_and_the_banner_down()
    {
        var folder = Directory.CreateTempSubdirectory("tw-update-no-banner").FullName;
        var handler = new ReleaseHandler { Respond = _ => Json(Current) };
        var model = new AppModel(folder, new ManualClock(), http: handler);
        try
        {
            await model.UpdateCheck;
            Assert.False(model.Attention);
            Assert.Equal(WidgetBanner.None, model.Banner);
            Assert.Equal("", model.BannerMessage);
            Assert.Equal("", model.BannerPrimary);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Update_downloads_this_architectures_installer_and_keeps_tasks_and_the_draft()
    {
        var folder = Directory.CreateTempSubdirectory("tw-update-install").FullName;
        var handler = new ReleaseHandler { Respond = _ => Json(ReleaseWithInstaller) };
        var model = new AppModel(folder, new ManualClock(), http: handler);
        string? installer = null;
        try
        {
            await model.UpdateCheck;
            model.UpdateDraft("email Sarah");
            await model.CommitCapture();
            model.UpdateDraft("buy milk");

            handler.HoldDownloads();
            var updating = model.StartUpdate();

            Assert.True(model.DownloadStarted);
            Assert.True(model.UpdDownloading);
            Assert.Equal("Downloading 1.2.0…", model.AboutStatus);
            Assert.Equal("Task Widget restarts to finish. Your Tasks and draft are kept.", model.DownloadNote);
            Assert.False(model.RestartRequested);
            var download = handler.Sent[1];
            Assert.Equal("GET", download.Method);
            Assert.Equal(InstallerForThisPc, download.Url);
            Assert.Equal("TaskWidget", download.UserAgent);
            Assert.False(download.HasAuthorization);
            Assert.False(download.HasContent);

            var bytes = new byte[] { 0x4D, 0x5A, 9, 8 };
            handler.ReleaseDownload(Bytes(bytes));
            await updating;

            installer = model.InstallerPath;
            Assert.True(model.RestartRequested);
            Assert.Equal(1, model.Downloaded);
            Assert.Equal("TaskWidget-1.2.0-" + PcArch + ".exe", Path.GetFileName(installer));
            Assert.Equal(bytes, File.ReadAllBytes(installer!));

            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "tasks.json"))))
            {
                Assert.Equal("buy milk", saved.RootElement.GetProperty("draft").GetString());
                Assert.Equal("email Sarah", saved.RootElement.GetProperty("tasks")[0].GetProperty("title").GetString());
            }

            model.Dispose();
            var again = new AppModel(folder, new ManualClock());
            Assert.Equal("buy milk", again.CaptureText);
            Assert.Equal("email Sarah", Assert.Single(again.Tasks).Title);
            again.Dispose();
        }
        finally
        {
            model.Dispose();
            if (installer is not null && File.Exists(installer))
                File.Delete(installer);
            Directory.Delete(folder, recursive: true);
        }
    }

    static string PcArch =>
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64
            ? "arm64"
            : "x64";

    static string InstallerForThisPc =>
        "https://github.com/Gul-Sher-Khan/task-widget/releases/download/v1.2.0/TaskWidget-1.2.0-" + PcArch + ".exe";

    const string ReleaseWithInstaller = """
        [{
          "tag_name": "v1.2.0",
          "draft": false,
          "prerelease": false,
          "assets": [
            {
              "name": "TaskWidget-1.2.0-x64.exe",
              "browser_download_url": "https://github.com/Gul-Sher-Khan/task-widget/releases/download/v1.2.0/TaskWidget-1.2.0-x64.exe"
            },
            {
              "name": "TaskWidget-1.2.0-arm64.exe",
              "browser_download_url": "https://github.com/Gul-Sher-Khan/task-widget/releases/download/v1.2.0/TaskWidget-1.2.0-arm64.exe"
            }
          ]
        }]
        """;

    const string Newer = """
        [{ "tag_name": "v1.2.0", "draft": false, "prerelease": false, "assets": [] }]
        """;

    const string Current = """
        [{ "tag_name": "v1.0.0", "draft": false, "prerelease": false, "assets": [] }]
        """;

    static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    static HttpResponseMessage Bytes(byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentLength = body.Length;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}

sealed class ReleaseHandler : HttpMessageHandler
{
    public List<SentRequest> Sent { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }
    TaskCompletionSource<HttpResponseMessage>? gate;
    TaskCompletionSource<HttpResponseMessage>? download;

    public void Hold() => gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release(HttpResponseMessage response) => gate!.SetResult(response);

    public void HoldDownloads() => download = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ReleaseDownload(HttpResponseMessage response) => download!.SetResult(response);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sent.Add(new SentRequest(
            request.Method.Method,
            request.RequestUri!.ToString(),
            request.Headers.UserAgent.ToString(),
            request.Headers.Authorization is not null,
            request.Content is not null));
        if (download is not null && request.RequestUri!.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return download.Task;
        if (gate is not null)
            return gate.Task;
        return Task.FromResult(Respond!(request));
    }
}

sealed record SentRequest(string Method, string Url, string UserAgent, bool HasAuthorization, bool HasContent);
