namespace NOVORA.VisionEngine.Recovery;

public sealed record VERecoveryResult(
    bool Success,
    VERecoveryScope Scope,
    int Attempt,
    TimeSpan Duration,
    string Message,
    Exception? Exception = null)
{
    public static VERecoveryResult OkVE(VERecoveryScope scope, int attempt, TimeSpan duration, string message)
        => new(true, scope, attempt, duration, message, null);

    public static VERecoveryResult FailVE(VERecoveryScope scope, int attempt, TimeSpan duration, string message, Exception? exception = null)
        => new(false, scope, attempt, duration, message, exception);
}
