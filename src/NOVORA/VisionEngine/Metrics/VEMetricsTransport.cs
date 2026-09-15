using NOVORA.VisionEngine.Transport;

namespace NOVORA.VisionEngine.Metrics;

public sealed record VEMetricsTransport(
    VETransportStates State,
    VETransportMode? Mode,
    bool VideoConnected,
    bool AudioConnected,
    bool ControlConnected,
    TimeSpan Uptime)
{
    public static VEMetricsTransport EmptyVE()
        => new(VETransportStates.Stopped, null, false, false, false, TimeSpan.Zero);
}
