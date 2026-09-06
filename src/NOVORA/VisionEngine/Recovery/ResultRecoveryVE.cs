namespace NOVORA.VisionEngine.Recovery;

public sealed record ResultRecoveryVE(
    bool Success,
    ScopeRecoveryVE Scope,
    int Attempt,
    TimeSpan Duration,
    string Message,
    Exception? Exception = null)
{
    public static ResultRecoveryVE OkVE(ScopeRecoveryVE scope, int attempt, TimeSpan duration, string message)
        => new(true, scope, attempt, duration, message, null);

    public static ResultRecoveryVE FailVE(ScopeRecoveryVE scope, int attempt, TimeSpan duration, string message, Exception? exception = null)
        => new(false, scope, attempt, duration, message, exception);
}
