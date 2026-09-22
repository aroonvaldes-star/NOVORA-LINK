using System.Security.Cryptography;
using System.Text;

namespace NOVORA.ExInEngine;

public sealed record ExInControllerIdentity(
    string ProfileKey,
    ExInControllerFamily Family,
    ushort VendorId,
    ushort ProductId,
    Guid Guid,
    string Name,
    string? Serial,
    string? Path)
{
    private const ushort MicrosoftVendorIdVE = 0x045E;
    private const ushort SonyVendorIdVE = 0x054C;

    private static readonly HashSet<ushort> DualShock4ProductIdsVE =
    [
        0x05C4,
        0x09CC
    ];

    private static readonly HashSet<ushort> XboxProductIdsVE =
    [
        0x028E,
        0x028F,
        0x02D1,
        0x02DD,
        0x02E0,
        0x02EA,
        0x02FD,
        0x0B00,
        0x0B05,
        0x0B12,
        0x0B13,
        0x0B20,
        0x0B21,
        0x0B22,
        0x0B23
    ];

    public static ExInControllerIdentity CreateVE(
        uint instanceId,
        ushort vendorId,
        ushort productId,
        Guid guid,
        string name,
        string? serial,
        string? path)
    {
        string normalizedName = string.IsNullOrWhiteSpace(name) ? "Gamepad" : name.Trim();
        string? normalizedSerial = NormalizeVE(serial);
        string? normalizedPath = NormalizeVE(path);
        ExInControllerFamily family = ClassifyFamilyVE(vendorId, productId);
        string discriminator = normalizedSerial is not null
            ? $"serial:{normalizedSerial}"
            : normalizedPath is not null
                ? $"path:{normalizedPath}"
                : $"instance:{instanceId:X8}";

        string material = $"{vendorId:X4}:{productId:X4}:{discriminator}";
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..24];
        string profileKey = $"exin-{vendorId:X4}-{productId:X4}-{digest}".ToLowerInvariant();

        return new(
            profileKey,
            family,
            vendorId,
            productId,
            guid,
            normalizedName,
            normalizedSerial,
            normalizedPath);
    }

    private static ExInControllerFamily ClassifyFamilyVE(ushort vendorId, ushort productId)
    {
        if (vendorId == SonyVendorIdVE && DualShock4ProductIdsVE.Contains(productId))
            return ExInControllerFamily.DualShock4;

        if (vendorId == MicrosoftVendorIdVE && XboxProductIdsVE.Contains(productId))
            return ExInControllerFamily.Xbox;

        return ExInControllerFamily.Generic;
    }

    private static string? NormalizeVE(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
