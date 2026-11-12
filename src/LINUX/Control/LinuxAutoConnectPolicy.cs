using System.Net;
using System.Net.NetworkInformation;

namespace NOVORA.Linux.Control;

/// <summary>A private IPv4 address where the LAN control listener can run.</summary>
internal sealed record LanCandidate(string InterfaceName, IPAddress Address, bool HasGateway, NetworkInterfaceType Type);

internal enum UsbAutoDecision
{
    /// <summary>Nothing to do: no USB phone, or the USB session is authorized.</summary>
    Idle,
    /// <summary>Prepare the USB control route for <see cref="UsbAutoPlan.Device"/>.</summary>
    Start,
    /// <summary>The phone left ADB (unplugged, unauthorized or offline): release the USB route.</summary>
    Stop,
    /// <summary>The USB route never confirmed or its session ended: release it and bootstrap again.</summary>
    Restart,
    /// <summary>Several USB phones and no previous choice: never pick one silently.</summary>
    WaitAmbiguous,
    /// <summary>A LAN session has running engines; switching would interrupt them.</summary>
    WaitLanBusy,
    /// <summary>A previous attempt failed; wait for the retry delay.</summary>
    WaitBackoff,
    /// <summary>LINK Control did not answer after the bounded attempts; wait for replug or a manual action.</summary>
    WaitGaveUp,
    /// <summary>The USB route is prepared and LINK Control is confirming it.</summary>
    WaitAuthorization,
    /// <summary>The user stopped USB manually for this phone; wait for replug or a manual action.</summary>
    WaitPaused
}

internal readonly record struct UsbAutoPlan(UsbAutoDecision Decision, AdbDevice? Device);

internal readonly record struct UsbAutoState(
    IReadOnlyList<AdbDevice> Devices,
    string? RunningSerial,
    bool Authorized,
    bool SessionEnded,
    TimeSpan PendingFor,
    bool LanAuthorized,
    bool LanBusy,
    string? PreferredSerial,
    IReadOnlySet<string> PausedSerials,
    int Attempts,
    bool BackoffElapsed);

/// <summary>
/// Pure connection policy for automatic USB/LAN routing. It has no I/O so every
/// decision can be checked without ADB, sockets or a phone.
/// </summary>
internal static class LinuxAutoConnectPolicy
{
    /// <summary>Each bootstrap opens LINK Control on the phone, so they are bounded per plug-in.</summary>
    public const int MaxUsbBootstrapAttempts = 3;
    public static readonly TimeSpan UsbAuthorizationTimeout = TimeSpan.FromSeconds(20);

    private static readonly string[] VirtualPrefixes =
    [
        "docker", "br-", "virbr", "veth", "vnet", "tun", "tap", "wg", "zt", "tailscale", "vboxnet",
        "vmnet", "lxc", "lxd", "podman", "cni", "flannel", "kube", "nordlynx", "ham", "utun", "ppp"
    ];

    /// <summary>0, 1, 2, 4, 8 seconds, then at most every 15 seconds.</summary>
    public static TimeSpan RetryDelay(int failedAttempts) => TimeSpan.FromSeconds(failedAttempts switch
    {
        <= 0 => 0,
        1 => 1,
        2 => 2,
        3 => 4,
        4 => 8,
        _ => 15
    });

    public static bool IsVirtualInterface(string name) =>
        VirtualPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Keeps the current listener while its address still exists; otherwise prefers a physical
    /// interface with a default gateway (the network the phone is most likely on).
    /// </summary>
    public static LanCandidate? ChooseLan(IReadOnlyList<LanCandidate> candidates, IPAddress? current)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (current is not null)
        {
            LanCandidate? kept = candidates.FirstOrDefault(candidate => candidate.Address.Equals(current));
            if (kept is not null)
                return kept;
        }

        return candidates
            .OrderBy(candidate => IsVirtualInterface(candidate.InterfaceName) ? 1 : 0)
            .ThenBy(candidate => candidate.HasGateway ? 0 : 1)
            .ThenBy(candidate => candidate.Type switch
            {
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => 0,
                NetworkInterfaceType.Wireless80211 => 1,
                _ => 2
            })
            .ThenBy(candidate => candidate.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Address.ToString(), StringComparer.Ordinal)
            // A virtual interface is only acceptable when it is the real route (bridged host setups).
            .FirstOrDefault(candidate => !IsVirtualInterface(candidate.InterfaceName) || candidate.HasGateway);
    }

    public static UsbAutoPlan DecideUsb(UsbAutoState state)
    {
        AdbDevice[] authorized = state.Devices.Where(device => device.State == "device").ToArray();
        // The cable is preferred. Paired wireless debugging is only used to reach LINK Control when no
        // cable is present and the phone is not already connected over LAN.
        AdbDevice[] ready = authorized.Where(device => device.IsUsb).ToArray();
        if (ready.Length == 0 && !state.LanAuthorized)
            ready = authorized.Where(device => !device.IsUsb).ToArray();
        if (state.RunningSerial is not null)
        {
            AdbDevice? running = authorized.FirstOrDefault(device => device.Serial == state.RunningSerial);
            if (running is null)
                return new(UsbAutoDecision.Stop, null);
            if (state.Authorized)
                return new(UsbAutoDecision.Idle, running);
            if (state.SessionEnded || state.PendingFor >= UsbAuthorizationTimeout)
                return new(UsbAutoDecision.Restart, running);
            return new(UsbAutoDecision.WaitAuthorization, running);
        }

        if (ready.Length == 0)
            return new(UsbAutoDecision.Idle, null);
        AdbDevice? target = ready.FirstOrDefault(device => device.Serial == state.PreferredSerial) ??
            (ready.Length == 1 ? ready[0] : null);
        if (target is null)
            return new(UsbAutoDecision.WaitAmbiguous, null);
        if (state.PausedSerials.Contains(target.Serial))
            return new(UsbAutoDecision.WaitPaused, target);
        // Android keeps one control session: bootstrapping USB would move the LAN session.
        if (state.LanAuthorized && state.LanBusy)
            return new(UsbAutoDecision.WaitLanBusy, target);
        if (state.Attempts >= MaxUsbBootstrapAttempts)
            return new(UsbAutoDecision.WaitGaveUp, target);
        if (!state.BackoffElapsed)
            return new(UsbAutoDecision.WaitBackoff, target);
        return new(UsbAutoDecision.Start, target);
    }
}
