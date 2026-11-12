using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Transport;

namespace NOVORA.VisionEngine.Stress;

public sealed class VEStressTransport
{
    private int _disconnectsVE;

    public void ObserveVE(VEMetricsTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (transport.State == VETransportStates.Failed)
            Interlocked.Increment(ref _disconnectsVE);
    }

    public int DisconnectsVE => Volatile.Read(ref _disconnectsVE);
}
