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
}
