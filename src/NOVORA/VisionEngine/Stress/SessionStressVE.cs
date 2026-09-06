namespace NOVORA.VisionEngine.Stress;

public sealed class SessionStressVE
{
    private readonly DateTimeOffset _startedAtUtcVE = DateTimeOffset.UtcNow;
    private int _samplesVE;

    public void AddSampleVE() => Interlocked.Increment(ref _samplesVE);
    public int SamplesVE => Volatile.Read(ref _samplesVE);
    public TimeSpan DurationVE => DateTimeOffset.UtcNow - _startedAtUtcVE;
}
