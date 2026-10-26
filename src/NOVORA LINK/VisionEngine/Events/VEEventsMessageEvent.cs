namespace NOVORA.VisionEngine.Events;

public sealed class VEEventsMessageEvent : EventArgs
{
    public VEEventsMessageEvent(VEEventsTypeEvent type, long sequence, DateTimeOffset timestampUtc)
    {
        Type = type;
        Sequence = sequence;
        TimestampUtc = timestampUtc;
    }

    public VEEventsTypeEvent Type { get; }
    public long Sequence { get; }
    public DateTimeOffset TimestampUtc { get; }
}
