namespace NOVORA.VisionEngine.Events;

public sealed class EventCoreVE
{
    private long _sequenceVE;

    public DispatcherEventVE DispatcherVE { get; } = new();

    public void PublishVE(TypeEventVE type)
    {
        long sequence = Interlocked.Increment(ref _sequenceVE);
        DispatcherVE.PublishVE(new MessageEventVE(type, sequence, DateTimeOffset.UtcNow));
    }
}
