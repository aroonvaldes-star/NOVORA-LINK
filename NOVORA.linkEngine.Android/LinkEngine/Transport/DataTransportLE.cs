using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.Net;
using Java.Net;

namespace NOVORA.LinkEngine.Android.LinkEngine;

public sealed class DataTransportLE :
    IDisposable
{
    public const string HostLE =
        "127.0.0.1";

    public const int PortLE =
        27184;

    private const int ConnectTimeoutMillisecondsLE =
        2500;

    private static readonly TimeSpan RetryDelayLE =
        TimeSpan.FromMilliseconds(500);

    private readonly object _writeGate =
        new();

    private Socket? _socket;

    private Stream? _input;

    private Stream? _output;

    private bool _disposed;

    public bool IsConnectedLE
    {
        get
        {
            try
            {
                return
                    _socket is not null &&
                    _socket.IsConnected &&
                    !_socket.IsClosed;
            }
            catch
            {
                return false;
            }
        }
    }

    public int? RelayClientIdLE
    {
        get;
        private set;
    }

    public async Task ConnectAsync(
        VpnService vpnService,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            vpnService);

        ThrowIfDisposedLE();

        DateTimeOffset deadline =
            DateTimeOffset.UtcNow +
            timeout;

        Exception? lastError =
            null;

        while (
            DateTimeOffset.UtcNow <
            deadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            try
            {
                ConnectOnceLE(
                    vpnService);

                return;
            }
            catch (Exception ex)
            {
                lastError =
                    ex;

                CloseLE();

                await Task.Delay(
                        RetryDelayLE,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        throw new TimeoutException(
            $"DATA {HostLE}:{PortLE} no conectÃ³. Ãšltimo error: {lastError?.Message}");
    }

    private void ConnectOnceLE(
        VpnService vpnService)
    {
        CloseLE();

        var socket =
            new Socket();

        try
        {
            socket.TcpNoDelay =
                true;

            socket.KeepAlive =
                true;

            /*
             * ========================================================
             * NOVORA LINKENGINE - DATA TRANSPORT BUFFER V2.1
             * ========================================================
             *
             * DATA transporta el túnel completo, pero no queremos
             * esconder congestión dentro de buffers gigantes.
             *
             * 256 KiB por dirección mantiene headroom suficiente para
             * ADB/USB mientras reduce queueing frente al antiguo 1 MiB.
             *
             * Esto NO limita el throughput a 256 KiB:
             * el socket se vacía y rellena continuamente.
             */
            socket.SendBufferSize =
                256 * 1024;

            socket.ReceiveBufferSize =
                256 * 1024;
            /*
             * LE-006 DATA SOCKET FD FIX
             *
             * Java.Net.Socket puede crear su file descriptor nativo
             * de forma perezosa.
             *
             * VpnService.Protect(Socket) necesita un descriptor vÃ¡lido.
             *
             * Bind() al puerto 0:
             *
             * - crea el socket nativo
             * - asigna un puerto local efÃ­mero
             * - NO conecta todavÃ­a al relay
             *
             * Esto permite respetar el orden correcto:
             *
             *     CREATE
             *       â†“
             *     BIND / FD
             *       â†“
             *     PROTECT
             *       â†“
             *     CONNECT
             */
            var localEndpoint =
                new InetSocketAddress(
                    0);

            socket.Bind(
                localEndpoint);

            if (!socket.IsBound)
            {
                throw new InvalidOperationException(
                    "DATA socket no pudo crear/bindear su file descriptor.");
            }

            /*
             * Protegemos el propio transporte del tÃºnel.
             *
             * AsÃ­ sus paquetes no son capturados nuevamente
             * por el VpnService y no generan un loop.
             */
            bool protectedLE =
                vpnService.Protect(
                    socket);

            if (!protectedLE)
            {
                throw new InvalidOperationException(
                    "VpnService.Protect(DATA) rechazÃ³ el socket despuÃ©s de crear su FD.");
            }

            var remoteEndpoint =
                new InetSocketAddress(
                    HostLE,
                    PortLE);

            socket.Connect(
                remoteEndpoint,
                ConnectTimeoutMillisecondsLE);

            if (!socket.IsConnected)
            {
                throw new InvalidOperationException(
                    $"DATA no quedÃ³ conectado a {HostLE}:{PortLE}.");
            }

            Stream input =
                socket.InputStream ??
                throw new InvalidOperationException(
                    "DATA InputStream no disponible.");

            Stream output =
                socket.OutputStream ??
                throw new InvalidOperationException(
                    "DATA OutputStream no disponible.");

            /*
             * El relay Rust asigna un client id de 32 bits
             * y lo envÃ­a big-endian inmediatamente despuÃ©s
             * de aceptar el tÃºnel.
             */
            int relayClientId =
                DataProtocolLE.ReadClientIdLE(
                    input);

            _socket =
                socket;

            _input =
                input;

            _output =
                output;

            RelayClientIdLE =
                relayClientId;
        }
        catch
        {
            try
            {
                socket.Close();
            }
            catch
            {
            }

            throw;
        }
    }

    public void SendPacketLE(
        byte[] packet,
        int length)
    {
        ThrowIfDisposedLE();

        ArgumentNullException.ThrowIfNull(
            packet);

        Stream output =
            _output ??
            throw new InvalidOperationException(
                "DATA output no conectado.");

        lock (_writeGate)
        {
            DataProtocolLE.WriteIpv4PacketLE(
                output,
                packet,
                length);
        }
    }

    public int ReceivePacketLE(
        byte[] destination)
    {
        ThrowIfDisposedLE();

        ArgumentNullException.ThrowIfNull(
            destination);

        Stream input =
            _input ??
            throw new InvalidOperationException(
                "DATA input no conectado.");

        return DataProtocolLE.ReadIpv4PacketLE(
            input,
            destination);
    }

    public void CloseLE()
    {
        RelayClientIdLE =
            null;

        Stream? input =
            _input;

        Stream? output =
            _output;

        Socket? socket =
            _socket;

        _input =
            null;

        _output =
            null;

        _socket =
            null;

        try
        {
            input?.Dispose();
        }
        catch
        {
        }

        try
        {
            if (!ReferenceEquals(
                    input,
                    output))
            {
                output?.Dispose();
            }
        }
        catch
        {
        }

        try
        {
            socket?.Close();
        }
        catch
        {
        }
    }

    private void ThrowIfDisposedLE()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CloseLE();

        _disposed =
            true;
    }
}
