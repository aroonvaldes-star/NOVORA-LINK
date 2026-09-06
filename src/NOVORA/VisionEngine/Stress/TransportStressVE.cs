using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Transport;

namespace NOVORA.VisionEngine.Stress;

public sealed class TransportStressVE
{
    private int _disconnectsVE;

    public void ObserveVE(TransportMetricsVE transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (transport.State == StatesTransportVE.Failed)
            Interlocked.Increment(ref _disconnectsVE);
    }

    public int DisconnectsVE => Volatile.Read(ref _disconnectsVE);
}
