using System;

namespace NOVORA.LinkEngine.Recovery;

public sealed record LERecoveryPolicy(
    TimeSpan HealthCheckInterval,
    TimeSpan InitialReconnectDelay,
    TimeSpan MaximumReconnectDelay,
    TimeSpan RecoveryTimeout,
    int MaximumConsecutiveFailures)
{
    public static LERecoveryPolicy DefaultLE { get; } =
        new(
            HealthCheckInterval:
                TimeSpan.FromSeconds(1),

            InitialReconnectDelay:
                TimeSpan.FromSeconds(1),

            MaximumReconnectDelay:
                TimeSpan.FromSeconds(8),

            RecoveryTimeout:
                TimeSpan.FromSeconds(30),

            MaximumConsecutiveFailures:
                8);

    public TimeSpan GetReconnectDelayLE(
        int attempt)
    {
        if (attempt <= 1)
        {
            return InitialReconnectDelay;
        }

        int exponent =
            Math.Min(
                attempt - 1,
                10);

        double multiplier =
            Math.Pow(
                2,
                exponent);

        double milliseconds =
            InitialReconnectDelay
                .TotalMilliseconds *
            multiplier;

        milliseconds =
            Math.Min(
                milliseconds,
                MaximumReconnectDelay
                    .TotalMilliseconds);

        return TimeSpan.FromMilliseconds(
            milliseconds);
    }
}
