namespace NOVORA.VisionEngine.Recovery;

public sealed record VERecoveryHealth(
    bool IsHealthy,
    VERecoveryScope SuggestedScope,
    string Reason,
    DateTimeOffset EvaluatedAtUtc)
{
    public static VERecoveryHealth HealthyVE(string reason = "VisionEngine saludable.")
        => new(true, VERecoveryScope.None, reason, DateTimeOffset.UtcNow);

    public static VERecoveryHealth DegradedVE(VERecoveryScope scope, string reason)
        => new(false, scope, reason, DateTimeOffset.UtcNow);
}
