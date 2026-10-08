using System.Text.Json;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class HomeMonitorTests
{
    const string Main = @"\\?\DISPLAY#GSM5B0#4&abc&0&UID0#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    const string Side = @"\\?\DISPLAY#DEL40A0#5&def&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    const string Extra = @"\\?\DISPLAY#HWP0001#6&ghi&0&UID9#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    [Fact]
    public void Dragging_to_another_monitor_saves_that_monitor_and_restores_it()
    {
        var folder = Directory.CreateTempSubdirectory("tw-home").FullName;
        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            model.NoteMonitors([new ConnectedMonitor(Side, false), new ConnectedMonitor(Main, true)]);
            Assert.Equal(Main, model.PlacementMonitorId);
            Assert.Equal("", model.HomeMonitorId);

            model.DropOnMonitor(Side);
            Assert.Equal(Side, model.HomeMonitorId);
            Assert.Equal(Side, model.PlacementMonitorId);

            clock.Advance(TimeSpan.FromMilliseconds(300));
            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "settings.json"))))
            {
                Assert.Equal(Side, saved.RootElement.GetProperty("homeMonitor").GetString());
                foreach (var property in saved.RootElement.EnumerateObject())
                {
                    Assert.False(property.Name is "x" or "y" or "left" or "top" or "width" or "height" or "position");
                }
            }

            model.Dispose();
            var again = new AppModel(folder, new ManualClock());
            Assert.Equal(Side, again.HomeMonitorId);
            again.NoteMonitors([new ConnectedMonitor(Main, true), new ConnectedMonitor(Side, false)]);
            Assert.Equal(Side, again.PlacementMonitorId);
            again.Dispose();
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Dropping_the_widget_on_its_monitor_snaps_back()
    {
        var folder = Directory.CreateTempSubdirectory("tw-snap").FullName;
        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            model.NoteMonitors([new ConnectedMonitor(Main, true), new ConnectedMonitor(Side, false)]);

            model.DropOnMonitor(Main);
            clock.Advance(TimeSpan.FromMilliseconds(300));
            Assert.Equal("", model.HomeMonitorId);
            Assert.Equal(Main, model.PlacementMonitorId);
            Assert.False(File.Exists(Path.Combine(folder, "settings.json")));

            model.DropOnMonitor(Side);
            Assert.Equal(Side, model.HomeMonitorId);

            model.DropOnMonitor(Side);
            Assert.Equal(Side, model.HomeMonitorId);
            Assert.Equal(Side, model.PlacementMonitorId);

            model.NoteMonitors([new ConnectedMonitor(Main, true), new ConnectedMonitor(Extra, false)]);
            Assert.Equal(Main, model.PlacementMonitorId);
            model.DropOnMonitor(Main);
            Assert.Equal(Side, model.HomeMonitorId);
            Assert.Equal(Main, model.PlacementMonitorId);

            model.DropOnMonitor(Extra);
            Assert.Equal(Extra, model.HomeMonitorId);
            Assert.Equal(Extra, model.PlacementMonitorId);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_disconnected_home_monitor_stands_in_on_the_primary_then_returns()
    {
        var folder = Directory.CreateTempSubdirectory("tw-unplug").FullName;
        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            model.NoteMonitors([new ConnectedMonitor(Main, true), new ConnectedMonitor(Side, false)]);
            model.DropOnMonitor(Side);
            clock.Advance(TimeSpan.FromMilliseconds(300));

            model.NoteMonitors([new ConnectedMonitor(Main, true)]);
            Assert.Equal(Side, model.HomeMonitorId);
            Assert.Equal(Main, model.PlacementMonitorId);

            model.Raise();
            model.Dock();
            model.Expand();
            Assert.Equal(Side, model.HomeMonitorId);
            Assert.Equal(Main, model.PlacementMonitorId);

            clock.Advance(TimeSpan.FromMilliseconds(300));
            using (var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "settings.json"))))
                Assert.Equal(Side, settings.RootElement.GetProperty("homeMonitor").GetString());

            model.NoteMonitors([new ConnectedMonitor(Main, true), new ConnectedMonitor(Side, false)]);
            Assert.Equal(Side, model.HomeMonitorId);
            Assert.Equal(Side, model.PlacementMonitorId);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }
}
