namespace NOVORA.VisionEngine.Stress;

public sealed class RecoveryStressVE
{
    private int _attemptsVE;
    private int _failuresVE;

    public void ObserveAttemptVE(bool success)
    {
        Interlocked.Increment(ref _attemptsVE);
        if (!success) Interlocked.Increment(ref _failuresVE);
    }

    public int AttemptsVE => Volatile.Read(ref _attemptsVE);
    public int FailuresVE => Volatile.Read(ref _failuresVE);
}
