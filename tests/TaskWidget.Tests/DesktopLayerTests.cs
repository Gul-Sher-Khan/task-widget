using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class DesktopLayerTests
{
    [Fact]
    public void A_second_launch_asks_the_running_instance_to_raise_the_widget()
    {
        var name = Guid.NewGuid().ToString("N");
        var folder = Directory.CreateTempSubdirectory("tw-instance").FullName;
        Assert.True(InstanceGate.TryAcquire(name, out var first));
        var model = new AppModel(folder, new ManualClock());
        var seen = new ManualResetEventSlim(false);
        first.RaiseRequested += () =>
        {
            model.Raise();
            seen.Set();
        };
        first.Listen();
        try
        {
            Assert.True(first.IsFirstInstance);
            // A named mutex is re-entrant on the thread that owns it, so the second launch is another thread.
            InstanceGate? second = null;
            var acquired = true;
            var other = new Thread(() => acquired = InstanceGate.TryAcquire(name, out second));
            other.Start();
            other.Join();
            Assert.False(acquired);
            Assert.False(second!.IsFirstInstance);

            InstanceGate.SignalRaise(name);

            Assert.True(seen.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            Assert.True(model.Raised);
            Assert.True(model.ShowWidget);
            second.Dispose();
        }
        finally
        {
            first.Dispose();
            model.Dispose();
            seen.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_second_launch_while_docked_opens_the_widget()
    {
        var folder = Directory.CreateTempSubdirectory("tw-raise-dock").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.Dock();
            Assert.True(model.ShowDock);

            model.Raise();

            Assert.False(model.Docked);
            Assert.True(model.Raised);
            Assert.True(model.ShowWidget);
            Assert.False(model.ShowDock);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_crashed_instance_does_not_block_the_next_launch()
    {
        var name = Guid.NewGuid().ToString("N");
        var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            Assert.True(InstanceGate.TryAcquire(name, out _));
            ready.Set();
        });
        thread.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        thread.Join();

        Assert.True(InstanceGate.TryAcquire(name, out var next));
        Assert.True(next.IsFirstInstance);
        next.Dispose();
        ready.Dispose();
    }

    [Fact]
    public void Full_screen_hides_the_dock_until_the_app_leaves()
    {
        var folder = Directory.CreateTempSubdirectory("tw-fullscreen").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.Dock();
            Assert.True(model.ShowDock);
            Assert.False(model.ShowWidget);

            model.SetFullScreenApp(true);

            Assert.True(model.Docked);
            Assert.False(model.ShowDock);
            Assert.False(model.ShowWidget);

            model.SetFullScreenApp(false);

            Assert.True(model.ShowDock);
            Assert.True(model.Docked);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Raising_over_full_screen_keeps_the_dock_and_commit_sends_the_widget_away()
    {
        var folder = Directory.CreateTempSubdirectory("tw-raise-fs").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.Dock();
            model.SetFullScreenApp(true);
            model.UpdateDraft("buy milk");
            model.Raise();

            Assert.True(model.Docked);
            Assert.True(model.Raised);
            Assert.True(model.ShowWidget);
            Assert.False(model.ShowDock);

            model.CommitCapture();

            Assert.False(model.Raised);
            Assert.True(model.Docked);
            Assert.False(model.ShowDock);
            Assert.False(model.ShowWidget);
            Assert.Equal("buy milk", Assert.Single(model.Tasks).Title);
            Assert.Equal("", model.CaptureText);

            model.SetFullScreenApp(false);
            Assert.True(model.ShowDock);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Escape_over_full_screen_sends_the_widget_away_and_keeps_the_draft()
    {
        var folder = Directory.CreateTempSubdirectory("tw-esc-fs").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.Dock();
            model.SetFullScreenApp(true);
            model.UpdateDraft("buy milk");
            model.Raise();

            Assert.True(model.Escape());

            Assert.False(model.Raised);
            Assert.True(model.Docked);
            Assert.False(model.ShowDock);
            Assert.Empty(model.Tasks);
            Assert.Equal("buy milk", model.CaptureText);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Deactivate_over_full_screen_hides_the_dock_again()
    {
        var folder = Directory.CreateTempSubdirectory("tw-deactivate-fs").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.Dock();
            model.SetFullScreenApp(true);
            model.Raise();
            model.NoteDeactivated();

            Assert.False(model.Raised);
            Assert.True(model.Docked);
            Assert.False(model.ShowDock);
            Assert.False(model.ShowWidget);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Commit_and_escape_leave_a_raised_widget_up_when_nothing_is_full_screen()
    {
        var folder = Directory.CreateTempSubdirectory("tw-raise-desk").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.UpdateDraft("buy milk");
            model.Raise();
            Assert.False(model.Escape());
            Assert.True(model.Raised);
            Assert.Equal("buy milk", model.CaptureText);

            model.CommitCapture();

            Assert.True(model.Raised);
            Assert.True(model.ShowWidget);
            Assert.Equal("buy milk", Assert.Single(model.Tasks).Title);

            model.NoteDeactivated();
            Assert.False(model.Raised);
            Assert.True(model.ShowWidget);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Full_screen_does_not_hide_the_widget_when_it_is_already_open()
    {
        var folder = Directory.CreateTempSubdirectory("tw-fs-widget").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.SetFullScreenApp(true);

            Assert.False(model.Docked);
            Assert.True(model.ShowWidget);
            Assert.False(model.ShowDock);
            Assert.False(model.Raised);

            model.UpdateDraft("buy milk");
            model.Raise();
            model.CommitCapture();

            Assert.False(model.Raised);
            Assert.True(model.ShowWidget);
            Assert.False(model.ShowDock);
            Assert.Equal("buy milk", Assert.Single(model.Tasks).Title);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Raising_again_asks_for_another_bring_to_front()
    {
        var folder = Directory.CreateTempSubdirectory("tw-raise-again").FullName;
        var model = new AppModel(folder, new ManualClock());
        var calls = 0;
        model.RaiseRequested += () => calls++;
        try
        {
            model.Raise();
            Assert.Equal(0, calls);
            model.Raise();
            Assert.Equal(1, calls);
            Assert.True(model.Raised);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }
}
