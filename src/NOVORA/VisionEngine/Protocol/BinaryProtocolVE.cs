using System.Buffers.Binary;

namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// Lecturas big-endian usadas por el wire protocol de scrcpy 4.1.
/// </summary>
public static class BinaryProtocolVE
{
    public static uint ReadUInt32VE(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new ArgumentException(
                "Se requieren al menos 4 bytes.",
                nameof(data));
        }

        return BinaryPrimitives.ReadUInt32BigEndian(data);
    }

    public static ulong ReadUInt64VE(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(ulong))
        {
            throw new ArgumentException(
                "Se requieren al menos 8 bytes.",
                nameof(data));
        }

        return BinaryPrimitives.ReadUInt64BigEndian(data);
    }
}
