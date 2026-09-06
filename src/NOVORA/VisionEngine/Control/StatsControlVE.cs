namespace NOVORA.VisionEngine.Control;

public sealed record StatsControlVE(
    long MessagesSent,
    long BytesSent,
    long MessagesReceived,
    long BytesReceived,
    long ClipboardEvents,
    long UhidOutputs,
    long Errors);
