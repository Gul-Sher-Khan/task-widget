namespace TaskWidget.Core;

// A display the shell can see right now. DeviceId is the monitor's device identity, not a position.
public readonly record struct ConnectedMonitor(string DeviceId, bool Primary);
