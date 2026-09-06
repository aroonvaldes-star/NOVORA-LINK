namespace NOVORA.VisionEngine.Recovery;

public sealed record PolicyRecoveryVE(
    TimeSpan SampleInterval,
    int ConsecutiveFailuresBeforeRecovery,
    int MaxRecoveryAttempts,
    TimeSpan RecoveryCooldown,
    TimeSpan NoProgressTimeout)
{
    public static PolicyRecoveryVE CreateDefaultVE()
        => new(
            SampleInterval: TimeSpan.FromSeconds(1),
            ConsecutiveFailuresBeforeRecovery: 3,
            MaxRecoveryAttempts: 5,
            RecoveryCooldown: TimeSpan.FromSeconds(3),
            NoProgressTimeout: TimeSpan.FromSeconds(5));

    public void ValidateVE()
    {
        if (SampleInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SampleInterval));
        if (ConsecutiveFailuresBeforeRecovery < 1)
            throw new ArgumentOutOfRangeException(nameof(ConsecutiveFailuresBeforeRecovery));
        if (MaxRecoveryAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxRecoveryAttempts));
        if (RecoveryCooldown < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RecoveryCooldown));
        if (NoProgressTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(NoProgressTimeout));
    }
}
