// Windows declares this record inside LENetworkRelay.cs (a Windows process wrapper), so Linux
// declares the identical type for the shared LESessionMeasurements compiled from `src/NOVORA LINK`.
namespace NOVORA.LinkEngine.Network;

public sealed record LERelayTrafficSnapshot(DateTimeOffset SampledAtUtc,
    long TxBytes, long RxBytes, long TxPackets, long RxPackets);
