namespace NOVORA.VisionEngine.Recovery;

public sealed record HealthRecoveryVE(
    bool IsHealthy,
    ScopeRecoveryVE SuggestedScope,
    string Reason,
    DateTimeOffset EvaluatedAtUtc)
{
    public static HealthRecoveryVE HealthyVE(string reason = "VisionEngine saludable.")
        => new(true, ScopeRecoveryVE.None, reason, DateTimeOffset.UtcNow);

    public static HealthRecoveryVE DegradedVE(ScopeRecoveryVE scope, string reason)
        => new(false, scope, reason, DateTimeOffset.UtcNow);
}
