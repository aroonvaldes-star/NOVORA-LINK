namespace NOVORA.VisionEngine.Events;

public sealed class VEEventsEventCore
{
    private long _sequenceVE;

    public VEEventsDispatcherEvent DispatcherVE { get; } = new();

    public void PublishVE(VEEventsTypeEvent type)
    {
        long sequence = Interlocked.Increment(ref _sequenceVE);
        DispatcherVE.PublishVE(new VEEventsMessageEvent(type, sequence, DateTimeOffset.UtcNow));
    }
}
