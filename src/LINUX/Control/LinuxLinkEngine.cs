using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using NOVORA.LinkEngine.Network;

namespace NOVORA.Linux.Control;

internal sealed class LinuxLinkEngine : ILinuxLinkEngine
{
    private const int ControlPort = 27183;
    private const int DataPort = 27184;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(10);
    private readonly AdbBridge _adb;
    private readonly string _serial;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private TcpClient? _activeClient;
    private LinuxLinkRelay? _relay;
    private AdbBridge.UsbReverseLease? _reverse;
    private Task _acceptTask = Task.CompletedTask;
    private string? _token;
    private string _state = "Stopped";
    private string _message = "LinkEngine detenido.";
    private bool _connected;
    private bool _stopping;
    private string _clientId = "-";
    private long _heartbeats;
    private string? _sessionKey;
    private DateTimeOffset _startedAtUtc;

    public event EventHandler? StateChanged;
    public bool IsStarted => Volatile.Read(ref _state) is not "Stopped";
    public bool IsConnected => Volatile.Read(ref _connected);
    public string State => Volatile.Read(ref _state);
    public string Message => Volatile.Read(ref _message);
    public string Transport => "USB";
    public string? Serial => _serial;
    public string? SessionKey => IsConnected ? Volatile.Read(ref _sessionKey) : null;
    public string ClientId => Volatile.Read(ref _clientId);
    public long HeartbeatCount => Interlocked.Read(ref _heartbeats);
    public int RelayRestarts => _relay?.Restarts ?? 0;
    public LERelayTrafficSnapshot? TrafficSnapshot => _relay?.TrafficSnapshot;

    public LinuxLinkEngine(AdbBridge adb, string serial)
    {
        _adb = adb;
        _serial = serial;
    }

    public async Task<string> StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (IsStarted)
            {
                if (State == "Error")
                    throw new InvalidOperationException(Message);
                return JsonSerializer.Serialize(new UsbLinkOffer("USB",
                    _token ?? throw new InvalidOperationException("La sesión LinkEngine no tiene token.")));
            }

            if (_stop.IsCancellationRequested)
            {
                _stop.Dispose();
                _stop = new CancellationTokenSource();
            }
            _stopping = false;
            _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            AdbBridge.UsbReverseLease? reverse = null;
            LinuxLinkRelay? relay = null;
            TcpListener? listener = null;
            try
            {
                reverse = await _adb.PrepareUsbReversePortsAsync(_serial, [ControlPort, DataPort], cancellationToken);
                relay = new LinuxLinkRelay();
                await relay.StartAsync(_token, cancellationToken);
                relay.ExitedUnexpectedly += OnRelayExited;
                relay.Recovering += OnRelayRecovering;
                relay.Recovered += OnRelayRecovered;
                listener = new TcpListener(IPAddress.Loopback, ControlPort);
                listener.Start(16);
                cancellationToken.ThrowIfCancellationRequested();

                _reverse = reverse;
                _relay = relay;
                _listener = listener;
                reverse = null;
                relay = null;
                listener = null;
                cancellationToken.ThrowIfCancellationRequested();
                _startedAtUtc = DateTimeOffset.UtcNow;
                SetState("Waiting", "LinkEngine preparado; esperando HELLO de Android por USB.");
                _acceptTask = AcceptLoopAsync(_stop.Token);
                return JsonSerializer.Serialize(new UsbLinkOffer("USB", _token));
            }
            catch
            {
                listener?.Stop();
                if (relay is not null)
                {
                    DetachRelay(relay);
                    await relay.DisposeAsync();
                }
                if (reverse is not null)
                    await reverse.DisposeAsync();
                _token = null;
                SetState("Stopped", "LinkEngine no pudo iniciarse.");
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (!IsStarted && _relay is null && _reverse is null)
                return;
            _stopping = true;
            _stop.Cancel();
            _listener?.Stop();
            _activeClient?.Dispose();
            await _acceptTask;
            LinuxLinkRelay? relay = _relay;
            AdbBridge.UsbReverseLease? reverse = _reverse;
            _listener = null;
            _relay = null;
            _reverse = null;
            _activeClient = null;
            _token = null;
            _connected = false;
            _sessionKey = null;
            _clientId = "-";
            Interlocked.Exchange(ref _heartbeats, 0);
            if (relay is not null)
            {
                DetachRelay(relay);
                await relay.DisposeAsync();
            }
            if (reverse is not null)
                await reverse.DisposeAsync();
            _stop.Dispose();
            SetState("Stopped", "LinkEngine detenido.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        TcpListener? listener = _listener;
        if (listener is null)
            return;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                _activeClient?.Dispose();
                _activeClient = client;
                try
                {
                    await HandleControlClientAsync(client, cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    SetState("Waiting", "LinkEngine no recibió el heartbeat de Android a tiempo.");
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        SetState("Waiting", "Canal CONTROL de Android desconectado; LinkEngine espera reconexión.");
                }
                finally
                {
                    if (_activeClient == client)
                        _activeClient = null;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (SocketException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            _activeClient?.Dispose();
            _activeClient = null;
            _connected = false;
            if (!_stopping && IsStarted)
                SetState("Waiting", "LinkEngine esperando una nueva conexión CONTROL de Android.");
        }
    }

    private async Task HandleControlClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            handshake.CancelAfter(HandshakeTimeout);
            NetworkStream stream = client.GetStream();
            string hello = await LinuxLinkProtocol.ReadFrameAsync(stream, handshake.Token);
            if (!TryValidateHello(hello, _token!, out string clientId))
                return;

            await LinuxLinkProtocol.WriteFrameAsync(stream, "NOVORA-LINK|2|ACK", handshake.Token);
            Volatile.Write(ref _clientId, clientId);
            Interlocked.Exchange(ref _heartbeats, 0);
            Volatile.Write(ref _sessionKey, FormattableString.Invariant(
                $"USB:{_serial}:{_startedAtUtc:O}:{DateTimeOffset.UtcNow:O}"));
            _connected = true;
            SetState("Connected", "Canal CONTROL de LinkEngine conectado por USB.");
            long sequence = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                heartbeat.CancelAfter(HeartbeatTimeout);
                string payload = await LinuxLinkProtocol.ReadFrameAsync(stream, heartbeat.Token);
                if (!TryCreateHeartbeatAck(payload, ref sequence, out string ack))
                    throw new InvalidDataException("Heartbeat de LinkEngine no válido.");
                await LinuxLinkProtocol.WriteFrameAsync(stream, ack, heartbeat.Token);
                Interlocked.Increment(ref _heartbeats);
            }
        }
    }

    internal static bool TryValidateHello(string payload, string token, out string clientId)
    {
        clientId = "";
        string[] fields = payload.Split('|');
        if (fields.Length != 5 || fields[0] != "NOVORA-LINK" || fields[1] != "2" ||
            fields[2] != "HELLO" || string.IsNullOrWhiteSpace(fields[3]) ||
            fields[3].Length > 128 || !LinuxLinkProtocol.IsToken(fields[4]) ||
            !LinuxLinkProtocol.IsToken(token))
            return false;
        byte[] received = System.Text.Encoding.ASCII.GetBytes(fields[4].ToLowerInvariant());
        byte[] expected = System.Text.Encoding.ASCII.GetBytes(token.ToLowerInvariant());
        if (!CryptographicOperations.FixedTimeEquals(received, expected))
            return false;
        clientId = fields[3];
        return true;
    }

    internal static bool TryCreateHeartbeatAck(string payload, ref long lastSequence, out string ack)
    {
        ack = "";
        string[] fields = payload.Split('|');
        if (fields.Length != 5 || fields[0] != "NOVORA-LINK" || fields[1] != "2" ||
            fields[2] != "HEARTBEAT" ||
            !long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out long sequence) ||
            sequence <= lastSequence ||
            !long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out long sent) || sent <= 0)
            return false;
        lastSequence = sequence;
        long serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ack = FormattableString.Invariant($"NOVORA-LINK|2|HEARTBEAT_ACK|{sequence}|{sent}|{serverTime}");
        return true;
    }

    private void OnRelayExited(object? sender, string message)
    {
        if (_stopping)
            return;
        _connected = false;
        SetState("Error", message);
    }

    private void OnRelayRecovering(object? sender, int attempt)
    {
        if (_stopping) return;
        SetState("Degraded", $"El relay de LinkEngine se detuvo; reiniciándolo (intento {attempt}).");
    }

    private void OnRelayRecovered(object? sender, EventArgs e)
    {
        if (_stopping) return;
        SetState(IsConnected ? "Connected" : "Waiting", IsConnected
            ? "Relay de LinkEngine recuperado; Android reconecta DATA por USB."
            : "Relay de LinkEngine recuperado; esperando a Android por USB.");
    }

    private void DetachRelay(LinuxLinkRelay relay)
    {
        relay.ExitedUnexpectedly -= OnRelayExited;
        relay.Recovering -= OnRelayRecovering;
        relay.Recovered -= OnRelayRecovered;
    }

    private void SetState(string state, string message)
    {
        Volatile.Write(ref _state, state);
        Volatile.Write(ref _message, message);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
