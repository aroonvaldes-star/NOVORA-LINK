namespace NOVORA.ExInEngine;

public sealed record ExInDevice(
    uint InstanceId,
    ushort UhidId,
    string Name,
    ushort VendorId,
    ushort ProductId,
    DateTimeOffset ConnectedAtUtc,
    ExInControllerIdentity? Identity = null);
