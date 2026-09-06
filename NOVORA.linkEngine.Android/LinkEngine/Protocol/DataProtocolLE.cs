using System;
using System.IO;

namespace NOVORA.LinkEngine.Android.LinkEngine;

public static class DataProtocolLE
{
    public static int ReadClientIdLE(
        Stream input)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        byte[] data =
            new byte[4];

        ReadExactlyLE(
            input,
            data,
            0,
            data.Length);

        return
            (data[0] << 24) |
            (data[1] << 16) |
            (data[2] << 8) |
            data[3];
    }

    public static int ReadIpv4PacketLE(
        Stream input,
        byte[] destination)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        ArgumentNullException.ThrowIfNull(
            destination);

        if (destination.Length <
            PacketNetworkLE.MinimumIpv4HeaderLengthLE)
        {
            throw new ArgumentException(
                "Buffer IPv4 insuficiente.",
                nameof(destination));
        }

        /*
         * El relay entrega un stream IPv4 crudo.
         *
         * Primero leemos los 20 bytes mínimos de IPv4.
         * Después usamos Total Length para saber exactamente
         * cuántos bytes pertenecen al paquete actual.
         */
        ReadExactlyLE(
            input,
            destination,
            0,
            PacketNetworkLE.MinimumIpv4HeaderLengthLE);

        int version =
            (destination[0] >> 4) &
            0x0F;

        if (version != 4)
        {
            throw new InvalidDataException(
                $"DATA recibió IP version {version}; LE-006 solo acepta IPv4.");
        }

        int headerLength =
            (destination[0] &
             0x0F) *
            4;

        if (headerLength <
            PacketNetworkLE.MinimumIpv4HeaderLengthLE)
        {
            throw new InvalidDataException(
                $"IPv4 IHL inválido: {headerLength}.");
        }

        int totalLength =
            PacketNetworkLE.GetTotalLengthLE(
                destination);

        if (totalLength <
                PacketNetworkLE.MinimumIpv4HeaderLengthLE ||
            totalLength >
                destination.Length)
        {
            throw new InvalidDataException(
                $"IPv4 TotalLength inválido: {totalLength}.");
        }

        int remaining =
            totalLength -
            PacketNetworkLE.MinimumIpv4HeaderLengthLE;

        if (remaining > 0)
        {
            ReadExactlyLE(
                input,
                destination,
                PacketNetworkLE.MinimumIpv4HeaderLengthLE,
                remaining);
        }

        return totalLength;
    }

    public static void WriteIpv4PacketLE(
        Stream output,
        byte[] packet,
        int length)
    {
        ArgumentNullException.ThrowIfNull(
            output);

        ArgumentNullException.ThrowIfNull(
            packet);

        if (!PacketNetworkLE.IsIpv4LE(
                packet,
                length))
        {
            throw new InvalidDataException(
                "Paquete IPv4 DATA inválido.");
        }

        /*
         * NetworkStream no necesita un framing adicional aquí:
         * el relay upstream reconstruye los paquetes mediante
         * el Total Length del header IPv4.
         */
        output.Write(
            packet,
            0,
            length);
    }

    private static void ReadExactlyLE(
        Stream input,
        byte[] buffer,
        int offset,
        int count)
    {
        int received =
            0;

        while (received < count)
        {
            int read =
                input.Read(
                    buffer,
                    offset + received,
                    count - received);

            if (read <= 0)
            {
                throw new EndOfStreamException(
                    "El canal DATA terminó durante la lectura.");
            }

            received +=
                read;
        }
    }
}