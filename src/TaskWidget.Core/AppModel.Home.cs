namespace TaskWidget.Core;

public sealed partial class AppModel
{
    readonly List<ConnectedMonitor> connected = [];

    // Empty until the user drags the Widget onto a monitor.
    public string HomeMonitorId => settingsFile.HomeMonitor ?? "";

    // The monitor the Widget and Dock stand on: the home monitor, or the primary while it is unplugged.
    public string PlacementMonitorId { get; private set; } = "";

    public void NoteMonitors(IReadOnlyList<ConnectedMonitor> monitors)
    {
        connected.Clear();
        foreach (var monitor in monitors)
        {
            if (string.IsNullOrEmpty(monitor.DeviceId))
                continue;
            connected.Add(monitor);
        }

        if (connected.Count == 0)
            return;

        var next = StandingOn(connected, HomeMonitorId);
        if (next == PlacementMonitorId)
            return;

        PlacementMonitorId = next;
        OnPropertyChanged(nameof(PlacementMonitorId));
    }

    // A drop on the monitor the windows already stand on snaps back. Any other monitor becomes home.
    public void DropOnMonitor(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId) || connected.Count == 0)
            return;

        var known = false;
        foreach (var monitor in connected)
        {
            if (monitor.DeviceId == deviceId)
            {
                known = true;
                break;
            }
        }

        if (!known || deviceId == PlacementMonitorId)
            return;

        settingsFile.HomeMonitor = deviceId;
        PlacementMonitorId = deviceId;
        OnPropertyChanged(nameof(HomeMonitorId));
        OnPropertyChanged(nameof(PlacementMonitorId));
        MarkDirty();
    }

    static string StandingOn(List<ConnectedMonitor> monitors, string home)
    {
        if (home.Length > 0)
        {
            foreach (var monitor in monitors)
            {
                if (monitor.DeviceId == home)
                    return monitor.DeviceId;
            }
        }

        foreach (var monitor in monitors)
        {
            if (monitor.Primary)
                return monitor.DeviceId;
        }

        return monitors[0].DeviceId;
    }
}
