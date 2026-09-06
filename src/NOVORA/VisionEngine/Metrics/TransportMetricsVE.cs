using NOVORA.VisionEngine.Transport;

namespace NOVORA.VisionEngine.Metrics;

public sealed record TransportMetricsVE(
    StatesTransportVE State,
    ModeTransportVE? Mode,
    bool VideoConnected,
    bool AudioConnected,
    bool ControlConnected,
    TimeSpan Uptime)
{
    public static TransportMetricsVE EmptyVE()
        => new(StatesTransportVE.Stopped, null, false, false, false, TimeSpan.Zero);
}
