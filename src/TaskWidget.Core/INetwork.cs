using System.Net.NetworkInformation;

namespace TaskWidget.Core;

public interface INetwork
{
    bool Online { get; }
    event Action? Changed;
}

public sealed class SystemNetwork : INetwork
{
    public SystemNetwork() =>
        NetworkChange.NetworkAvailabilityChanged += (_, _) => Changed?.Invoke();

    public bool Online => NetworkInterface.GetIsNetworkAvailable();

    public event Action? Changed;
}
