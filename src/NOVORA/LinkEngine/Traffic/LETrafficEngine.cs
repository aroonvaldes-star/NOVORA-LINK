namespace NOVORA.LinkEngine.Traffic;

/// <summary>
/// Managed control-plane surface for LinkEngine TrafficEngine.
///
/// Actual scheduling and backpressure live in RelayCore.
/// </summary>
public sealed class LETrafficEngine
{
    public const int MaxConcurrentSessionsLE =
        5;

    public string NativePolicyOwnerLE =>
        "NOVORA.LinkEngine.Relay/relay::traffic_engine";

    public bool NativeSchedulingEnabledLE =>
        true;

    public bool CongestionTriggersRecoveryLE =>
        false;
}