namespace NOVORA.VisionEngine.Gamepad;

public sealed record VEGamepadDevice(
    uint InstanceId,
    ushort UhidId,
    string Name,
    ushort VendorId,
    ushort ProductId,
    DateTimeOffset ConnectedAtUtc);
