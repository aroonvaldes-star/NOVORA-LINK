namespace NOVORA.VisionEngine.Control;

public sealed record MessageDeviceControlVE(
    TypeDeviceControlVE Type,
    string? ClipboardText,
    ulong? Sequence,
    ushort? UhidId,
    byte[]? Data);
