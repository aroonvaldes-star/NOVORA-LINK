namespace NOVORA.VisionEngine.Events;

public sealed class MessageEventVE : EventArgs
{
    public MessageEventVE(TypeEventVE type, long sequence, DateTimeOffset timestampUtc)
    {
        Type = type;
        Sequence = sequence;
        TimestampUtc = timestampUtc;
    }

    public TypeEventVE Type { get; }
    public long Sequence { get; }
    public DateTimeOffset TimestampUtc { get; }
}
