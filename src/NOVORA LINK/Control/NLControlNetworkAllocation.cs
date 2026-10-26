namespace NOVORA.Control;

/// <summary>Monotonic clocks stay on their host. A receiver stamps this bounded TTL on arrival.</summary>
public sealed record NLControlNetworkAllocation(long Revision, long Generation, string LinkKey,
    NLNetworkDirection Direction, long BytesPerSecond, int BurstBytes, int ValidForMilliseconds,
    NLNetworkPolicyMode Mode);
