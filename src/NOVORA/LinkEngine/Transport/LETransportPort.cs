using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace NOVORA.LinkEngine.Transport;

/// <summary>
/// Asignador de puertos HOST para CONTROL.
///
/// IMPORTANTE:
///
/// Android siempre conecta CONTROL a:
///
///     127.0.0.1:27183
///
/// pero mediante adb reverse ese puerto DEVICE puede apuntar a
/// cualquier puerto HOST libre.
///
/// Ejemplo:
///
///     Android tcp:27183
///         ->
///     Windows tcp:27185
///
/// Esto permite:
///
/// - sobrevivir a un 27183 ocupado por otro proceso;
/// - soportar varios Android simultáneamente;
/// - mantener el protocolo Android sin cambios;
/// - reservar 27184 exclusivamente para DATA/RelayCore.
///
/// Esta clase NO hace polling.
/// La comprobación de puerto ocurre únicamente al abrir una sesión.
/// </summary>
public sealed class LETransportPort
{
    /// <summary>
    /// Primer puerto candidato de CONTROL en Windows.
    /// </summary>
    public const int DefaultStartPortLE =
        27183;

    /// <summary>
    /// Último puerto candidato de CONTROL en Windows.
    /// </summary>
    public const int DefaultEndPortLE =
        27283;

    /// <summary>
    /// DATA / RelayCore.
    ///
    /// CONTROL jamás debe utilizar este puerto como HostPort.
    /// </summary>
    public const int ReservedDataPortLE =
        27184;

    private readonly ConcurrentDictionary<int, string>
        _reservations =
            new();

    private readonly int _startPort;
    private readonly int _endPort;

    public LETransportPort(
        int startPort = DefaultStartPortLE,
        int endPort = DefaultEndPortLE)
    {
        if (startPort is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startPort));
        }

        if (endPort is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endPort));
        }

        if (endPort < startPort)
        {
            throw new ArgumentException(
                "El puerto final debe ser mayor o igual al puerto inicial.");
        }

        _startPort =
            startPort;

        _endPort =
            endPort;
    }

    /// <summary>
    /// Reserva un puerto HOST para el serial.
    ///
    /// El DevicePort NO se administra aquí.
    /// Android usa siempre tcp:27183.
    /// </summary>
    public int Reserve(
        string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            serial);

        serial =
            serial.Trim();

        /*
         * Si ya había una reserva para este dispositivo,
         * sólo la reutilizamos si el puerto sigue realmente
         * disponible en Windows.
         *
         * Normalmente OpenAsync() no llega aquí cuando existe
         * un listener activo reutilizable, por lo que este caso
         * cubre principalmente reservas huérfanas.
         */
        var existing =
            _reservations.FirstOrDefault(
                item =>
                    string.Equals(
                        item.Value,
                        serial,
                        StringComparison.OrdinalIgnoreCase));

        if (existing.Key != 0)
        {
            if (existing.Key != ReservedDataPortLE &&
                IsHostPortAvailableLE(
                    existing.Key))
            {
                return existing.Key;
            }

            _reservations.TryRemove(
                existing.Key,
                out _);
        }

        for (
            int port = _startPort;
            port <= _endPort;
            port++)
        {
            /*
             * 27184 pertenece a DATA/RelayCore.
             */
            if (port == ReservedDataPortLE)
            {
                continue;
            }

            /*
             * Ya reservado por otro dispositivo.
             */
            if (_reservations.ContainsKey(
                    port))
            {
                continue;
            }

            /*
             * Puede estar ocupado por:
             *
             * - otro NOVORA viejo;
             * - PowerShell diagnóstico;
             * - otra aplicación;
             * - cualquier listener externo.
             *
             * En ese caso saltamos al siguiente puerto.
             */
            if (!IsHostPortAvailableLE(
                    port))
            {
                continue;
            }

            if (_reservations.TryAdd(
                    port,
                    serial))
            {
                return port;
            }
        }

        throw new InvalidOperationException(
            $"LETransportPort no encontró puertos HOST libres entre {_startPort} y {_endPort}. " +
            $"El puerto {ReservedDataPortLE} está reservado para DATA.");
    }

    public void Release(
        int port)
    {
        if (port > 0)
        {
            _reservations.TryRemove(
                port,
                out _);
        }
    }

    public void ReleaseBySerial(
        string serial)
    {
        if (string.IsNullOrWhiteSpace(
            serial))
        {
            return;
        }

        foreach (
            var item in
            _reservations.ToArray())
        {
            if (string.Equals(
                    item.Value,
                    serial,
                    StringComparison.OrdinalIgnoreCase))
            {
                _reservations.TryRemove(
                    item.Key,
                    out _);
            }
        }
    }

    public void Clear()
    {
        _reservations.Clear();
    }

    /// <summary>
    /// Comprobación puntual durante START.
    ///
    /// No se usa como monitor.
    /// No se llama periódicamente.
    /// </summary>
    private static bool IsHostPortAvailableLE(
        int port)
    {
        TcpListener? probe =
            null;

        try
        {
            probe =
                new TcpListener(
                    IPAddress.Loopback,
                    port);

            /*
             * No queremos compartir un CONTROL listener con
             * otra aplicación.
             */
            probe.Server.ExclusiveAddressUse =
                true;

            probe.Start(
                1);

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                probe?.Stop();
            }
            catch
            {
            }
        }
    }
}