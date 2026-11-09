using NOVORA.LinkEngine.Network;

namespace NOVORA.Linux.Control;

/// <summary>
/// LinkEngine runtime owned by one authorized control transport. USB prepares
/// ADB reverse and a CONTROL listener; LAN prepares the native relay and an
/// authenticated TLS gateway. StartAsync returns the JSON offer consumed by
/// LINK Control through the authorized control session.
/// </summary>
internal interface ILinuxLinkEngine
{
    event EventHandler? StateChanged;
    bool IsStarted { get; }
    bool IsConnected { get; }
    string State { get; }
    string Message { get; }
    Task<string> StartAsync(CancellationToken cancellationToken);
    Task StopAsync();

    /// <summary>"USB" or "LAN".</summary>
    string Transport => "USB";
    /// <summary>ADB serial for USB sessions; null for LAN.</summary>
    string? Serial => null;
    /// <summary>Identifies one connected session (Windows ActiveLinkSourceLE key); null while not connected.</summary>
    string? SessionKey => null;
    /// <summary>Android client id from HELLO (USB) or "-" when the transport has none.</summary>
    string ClientId => "-";
    long HeartbeatCount => 0;
    int RelayRestarts => 0;
    LERelayTrafficSnapshot? TrafficSnapshot => null;
}
