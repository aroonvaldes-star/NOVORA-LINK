using NOVORA.Service;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Server;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace NOVORA.VisionEngine.Transport;

/// <summary>
/// Administra el túnel ADB y los canales de VisionEngine.
/// Replica el orden y la estrategia de scrcpy 4.1:
/// adb reverse primero, adb forward como fallback y sockets video/audio/control.
/// </summary>
public sealed class VETransportManager
{
    private static readonly TimeSpan DefaultConnectTimeoutVE =
        TimeSpan.FromSeconds(10);

    private readonly NLServiceADB _adb;
    private readonly object _statusGate = new();

    private VETransportStates _state =
        VETransportStates.Stopped;

    public VETransportManager(NLServiceADB adb)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    }

    public event EventHandler<VETransportStates>? StateChangedVE;

    public VETransportStates StateVE
    {
        get
        {
            lock (_statusGate)
            {
                return _state;
            }
        }
    }

    public async Task<VETransportTunnel> PrepareAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        string normalizedSerial = serial.Trim();
        int scid = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        string socketName =
            VEProtocolConstants.ScrcpySocketPrefixVE +
            scid.ToString("x8");

        PublishStateVE(VETransportStates.Preparing);

        TcpListener reverseListener =
            new(IPAddress.Loopback, 0);

        // Block B puede aceptar hasta video + audio + control.
        reverseListener.Start(3);

        int reversePort =
            ((IPEndPoint)reverseListener.LocalEndpoint).Port;

        try
        {
            await _adb.ExecuteRawAsync(
                    new[]
                    {
                        "-s",
                        normalizedSerial,
                        "reverse",
                        $"localabstract:{socketName}",
                        $"tcp:{reversePort}"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            PublishStateVE(VETransportStates.Listening);

            return new VETransportTunnel(
                serial: normalizedSerial,
                scid: scid,
                socketName: socketName,
                localPort: reversePort,
                mode: VETransportMode.Reverse,
                reverseListener: reverseListener);
        }
        catch (OperationCanceledException)
        {
            reverseListener.Stop();
            throw;
        }
        catch
        {
            reverseListener.Stop();

            await TryRemoveReverseAsync(
                    normalizedSerial,
                    socketName)
                .ConfigureAwait(false);
        }

        Exception? lastForwardError = null;

        for (int attempt = 0; attempt < 16; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int forwardPort = ReserveFreePortVE();

            try
            {
                await _adb.ExecuteRawAsync(
                        new[]
                        {
                            "-s",
                            normalizedSerial,
                            "forward",
                            $"tcp:{forwardPort}",
                            $"localabstract:{socketName}"
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                PublishStateVE(VETransportStates.Listening);

                return new VETransportTunnel(
                    serial: normalizedSerial,
                    scid: scid,
                    socketName: socketName,
                    localPort: forwardPort,
                    mode: VETransportMode.Forward,
                    reverseListener: null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastForwardError = ex;
            }
        }

        PublishStateVE(VETransportStates.Failed);

        throw new InvalidOperationException(
            "ADB reverse falló y no fue posible reservar un túnel adb forward para VisionEngine.",
            lastForwardError);
    }

    public Task<VETransportSession> ConnectVideoAsync(
        VETransportTunnel tunnel,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => ConnectAsync(
            tunnel,
            videoEnabled: true,
            audioEnabled: false,
            controlEnabled: false,
            timeout: timeout,
            cancellationToken: cancellationToken);

    public Task<VETransportSession> ConnectAsync(
        VETransportTunnel tunnel,
        VEServerOptions options,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ConnectAsync(
            tunnel,
            videoEnabled: options.VideoEnabled,
            audioEnabled: options.AudioEnabled,
            controlEnabled: options.ControlEnabled,
            timeout: timeout,
            cancellationToken: cancellationToken);
    }

    public async Task<VETransportSession> ConnectAsync(
        VETransportTunnel tunnel,
        bool videoEnabled,
        bool audioEnabled,
        bool controlEnabled,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tunnel);

        if (!videoEnabled && !audioEnabled && !controlEnabled)
        {
            throw new ArgumentException(
                "VisionEngine necesita al menos un canal habilitado.");
        }

        PublishStateVE(VETransportStates.Connecting);

        TimeSpan effectiveTimeout =
            timeout ?? DefaultConnectTimeoutVE;

        using CancellationTokenSource timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutCts.CancelAfter(effectiveTimeout);

        TcpClient? videoClient = null;
        TcpClient? audioClient = null;
        TcpClient? controlClient = null;

        try
        {
            bool firstSocket = true;

            if (videoEnabled)
            {
                videoClient = await AcquireSocketAsync(
                        tunnel,
                        firstSocket,
                        timeoutCts.Token)
                    .ConfigureAwait(false);
                firstSocket = false;
            }

            if (audioEnabled)
            {
                audioClient = await AcquireSocketAsync(
                        tunnel,
                        firstSocket,
                        timeoutCts.Token)
                    .ConfigureAwait(false);
                firstSocket = false;
            }

            if (controlEnabled)
            {
                controlClient = await AcquireSocketAsync(
                        tunnel,
                        firstSocket,
                        timeoutCts.Token)
                    .ConfigureAwait(false);
                controlClient.NoDelay = true;
                firstSocket = false;
            }

            TcpClient firstClient =
                videoClient ?? audioClient ?? controlClient
                ?? throw new InvalidOperationException(
                    "VisionEngine no obtuvo ningún socket del servidor Android.");

            string deviceName = await ReadDeviceNameAsync(
                    firstClient.GetStream(),
                    timeoutCts.Token)
                .ConfigureAwait(false);

            // Ya se aceptaron todos los sockets reverse necesarios. El listener
            // no debe seguir recibiendo conexiones accidentales.
            if (tunnel.Mode == VETransportMode.Reverse)
            {
                try
                {
                    tunnel.ReverseListenerVE?.Stop();
                }
                catch
                {
                }
            }

            PublishStateVE(VETransportStates.Connected);

            return new VETransportSession(
                tunnel,
                videoClient,
                audioClient,
                controlClient,
                deviceName);
        }
        catch
        {
            DisposeClientVE(controlClient);
            DisposeClientVE(audioClient);
            DisposeClientVE(videoClient);

            PublishStateVE(VETransportStates.Failed);
            throw;
        }
    }

    private static async Task<TcpClient> AcquireSocketAsync(
        VETransportTunnel tunnel,
        bool firstSocket,
        CancellationToken cancellationToken)
    {
        TcpClient client = tunnel.Mode switch
        {
            VETransportMode.Reverse =>
                await AcceptReverseAsync(
                        tunnel,
                        cancellationToken)
                    .ConfigureAwait(false),

            VETransportMode.Forward =>
                await ConnectForwardAsync(
                        tunnel,
                        firstSocket,
                        cancellationToken)
                    .ConfigureAwait(false),

            _ => throw new InvalidOperationException(
                $"Modo de transporte desconocido: {tunnel.Mode}.")
        };

        client.NoDelay = true;
        return client;
    }

    public async Task RemoveAsync(
        VETransportTunnel? tunnel,
        CancellationToken cancellationToken = default)
    {
        if (tunnel is null)
        {
            PublishStateVE(VETransportStates.Stopped);
            return;
        }

        try
        {
            if (tunnel.Mode == VETransportMode.Reverse)
            {
                await _adb.ExecuteRawAsync(
                        new[]
                        {
                            "-s",
                            tunnel.Serial,
                            "reverse",
                            "--remove",
                            $"localabstract:{tunnel.SocketName}"
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _adb.ExecuteRawAsync(
                        new[]
                        {
                            "-s",
                            tunnel.Serial,
                            "forward",
                            "--remove",
                            $"tcp:{tunnel.LocalPort}"
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // ADB puede desaparecer durante desconexión. El cierre local sigue.
        }
        finally
        {
            await tunnel.DisposeAsync()
                .ConfigureAwait(false);

            PublishStateVE(VETransportStates.Stopped);
        }
    }

    private static async Task<TcpClient> AcceptReverseAsync(
        VETransportTunnel tunnel,
        CancellationToken cancellationToken)
    {
        TcpListener listener = tunnel.ReverseListenerVE
            ?? throw new InvalidOperationException(
                "El túnel reverse no contiene un listener local.");

        return await listener.AcceptTcpClientAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<TcpClient> ConnectForwardAsync(
        VETransportTunnel tunnel,
        bool firstSocket,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client = new();

            try
            {
                await client.ConnectAsync(
                        IPAddress.Loopback,
                        tunnel.LocalPort,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (firstSocket)
                {
                    // En forward, scrcpy 4.1 envía un dummy byte sólo por el
                    // primer socket para confirmar que el servidor está listo.
                    byte[] dummy = new byte[1];
                    await client.GetStream().ReadExactlyAsync(
                            dummy.AsMemory(),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                return client;
            }
            catch (OperationCanceledException)
            {
                client.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                client.Dispose();

                await Task.Delay(
                        TimeSpan.FromMilliseconds(100),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        throw new IOException(
            "No fue posible conectar al socket forward de VisionEngine.",
            lastError);
    }

    private static async Task<string> ReadDeviceNameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] buffer =
            new byte[VEProtocolConstants.DeviceNameFieldLengthVE];

        await stream.ReadExactlyAsync(
                buffer.AsMemory(),
                cancellationToken)
            .ConfigureAwait(false);

        int length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
        {
            length = buffer.Length;
        }

        string name = Encoding.UTF8
            .GetString(buffer, 0, length)
            .Trim();

        return string.IsNullOrWhiteSpace(name)
            ? "Android"
            : name;
    }

    private async Task TryRemoveReverseAsync(
        string serial,
        string socketName)
    {
        try
        {
            await _adb.ExecuteRawAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "reverse",
                        "--remove",
                        $"localabstract:{socketName}"
                    })
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static int ReserveFreePortVE()
    {
        TcpListener listener =
            new(IPAddress.Loopback, 0);

        listener.Start(1);

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void DisposeClientVE(TcpClient? client)
    {
        try
        {
            client?.Dispose();
        }
        catch
        {
        }
    }

    private void PublishStateVE(VETransportStates state)
    {
        EventHandler<VETransportStates>? handler;

        lock (_statusGate)
        {
            _state = state;
            handler = StateChangedVE;
        }

        handler?.Invoke(this, state);
    }
}
