using System;

namespace NOVORA.LinkEngine.Recovery;

public sealed record LERecoveryResult(
    bool Success,
    LERecoveryState State,
    int Attempts,
    TimeSpan Duration,
    string Message)
{
    public static LERecoveryResult OkLE(
        int attempts,
        TimeSpan duration,
        string message)
    {
        return new LERecoveryResult(
            Success:
                true,

            State:
                LERecoveryState.Healthy,

            Attempts:
                attempts,

            Duration:
                duration,

            Message:
                message);
    }

    public static LERecoveryResult FailLE(
        LERecoveryState state,
        int attempts,
        TimeSpan duration,
        string message)
    {
        return new LERecoveryResult(
            Success:
                false,

            State:
                state,

            Attempts:
                attempts,

            Duration:
                duration,

            Message:
                message);
    }
}
