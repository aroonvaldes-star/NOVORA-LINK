using System;

namespace NOVORA.LinkEngine.Android.LinkEngine;

public static class PacketNetworkLE
{
    public const int MinimumIpv4HeaderLengthLE =
        20;

    public const int MaximumIpv4PacketLengthLE =
        65535;

    public static bool IsIpv4LE(
        byte[] packet,
        int length)
    {
        ArgumentNullException.ThrowIfNull(
            packet);

        if (length <
                MinimumIpv4HeaderLengthLE ||
            length >
                packet.Length)
        {
            return false;
        }

        int version =
            (packet[0] >> 4) &
            0x0F;

        if (version != 4)
        {
            return false;
        }

        int totalLength =
            GetTotalLengthLE(
                packet);

        return
            totalLength >=
                MinimumIpv4HeaderLengthLE &&
            totalLength <=
                length;
    }

    public static int GetTotalLengthLE(
        byte[] packet)
    {
        ArgumentNullException.ThrowIfNull(
            packet);

        if (packet.Length < 4)
        {
            return 0;
        }

        return
            (packet[2] << 8) |
            packet[3];
    }
}