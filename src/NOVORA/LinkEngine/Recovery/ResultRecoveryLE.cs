using System;

namespace NOVORA.LinkEngine.Recovery;

public sealed record ResultRecoveryLE(
    bool Success,
    StateRecoveryLE State,
    int Attempts,
    TimeSpan Duration,
    string Message)
{
    public static ResultRecoveryLE OkLE(
        int attempts,
        TimeSpan duration,
        string message)
    {
        return new ResultRecoveryLE(
            Success:
                true,

            State:
                StateRecoveryLE.Healthy,

            Attempts:
                attempts,

            Duration:
                duration,

            Message:
                message);
    }

    public static ResultRecoveryLE FailLE(
        StateRecoveryLE state,
        int attempts,
        TimeSpan duration,
        string message)
    {
        return new ResultRecoveryLE(
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
