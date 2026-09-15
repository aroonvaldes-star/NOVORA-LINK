namespace NOVORA.VisionEngine.Control;

public sealed record VEControlMessageDevice(
    VEControlTypeDevice Type,
    string? ClipboardText,
    ulong? Sequence,
    ushort? UhidId,
    byte[]? Data);
