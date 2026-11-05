using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace NOVORA.Linux.Control;

internal sealed class LinuxLinkEngine : IAsyncDisposable
{
    private const int ControlPort = 27183;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(10);
    private readonly AdbBridge _adb;
    private readonly string _serial;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private TcpClient? _activeClient;
    private Process? _relay;
    private AdbBridge.UsbReverseLease? _reverse;
    private Task _acceptTask = Task.CompletedTask;
    private string? _token;
    private string _state = "Stopped";
    private string _message = "LinkEngine detenido.";
    private bool _connected;
    private bool _stopping;

    public event EventHandler? StateChanged;
    public bool IsStarted => Volatile.Read(ref _state) is not "Stopped";
    public bool IsConnected => Volatile.Read(ref _connected);
    public string State => Volatile.Read(ref _state);
    public string Message => Volatile.Read(ref _message);

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
                return _token ?? throw new InvalidOperationException("La sesión LinkEngine no tiene token.");
            }

            if (_stop.IsCancellationRequested)
            {
                _stop.Dispose();
                _stop = new CancellationTokenSource();
            }
            _stopping = false;
            _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            AdbBridge.UsbReverseLease? reverse = null;
            Process? relay = null;
            TcpListener? listener = null;
            try
            {
                reverse = await _adb.PrepareUsbReversePortsAsync(_serial, [ControlPort, 27184], cancellationToken);
                relay = StartRelay(_token);
                relay.Exited += (_, _) =>
                {
                    if (!_stopping)
                    {
                        _connected = false;
                        SetState("Error", "El proceso nativo de LinkEngine terminó inesperadamente.");
                    }
                };
                await WaitForRelayReadyAsync(relay, cancellationToken);
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
                SetState("Waiting", "LinkEngine preparado; esperando HELLO de Android por USB.");
                _acceptTask = AcceptLoopAsync(_stop.Token);
                return _token;
            }
            catch
            {
                listener?.Stop();
                if (relay is not null)
                    await StopProcessAsync(relay);
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
            Process? relay = _relay;
            AdbBridge.UsbReverseLease? reverse = _reverse;
            _listener = null;
            _relay = null;
            _reverse = null;
            _activeClient = null;
            _token = null;
            _connected = false;
            if (relay is not null)
                await StopProcessAsync(relay);
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

    private static Process StartRelay(string token)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Tools", "LinkEngine", "novora-linkengine-relay");
        if (!File.Exists(path))
            throw new FileNotFoundException("No se encontró el relay nativo Linux de LinkEngine.", path);

        var start = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.Environment["NOVORA_RELAY_TOKEN"] = token;
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException("No se pudo iniciar el relay nativo de LinkEngine.");
        return process;
    }

    private static async Task WaitForRelayReadyAsync(Process relay, CancellationToken cancellationToken)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostics = new StringBuilder();
        object gate = new();
        relay.OutputDataReceived += (_, e) =>
        {
            if (e.Data?.StartsWith("LE_READY ", StringComparison.Ordinal) == true)
                ready.TrySetResult();
        };
        relay.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;
            lock (gate)
            {
                if (diagnostics.Length < 512)
                    diagnostics.Append(e.Data.AsSpan(0, Math.Min(e.Data.Length, 512 - diagnostics.Length)));
            }
        };
        relay.BeginOutputReadLine();
        relay.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        Task exited = relay.WaitForExitAsync(timeout.Token);
        Task completed = await Task.WhenAny(ready.Task, exited);
        if (completed == exited)
        {
            await exited;
            lock (gate)
                throw new InvalidOperationException($"El relay de LinkEngine terminó al iniciar (código {relay.ExitCode}). {diagnostics}");
        }
        await ready.Task.WaitAsync(timeout.Token);
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
            string hello = await ReadFrameAsync(stream, handshake.Token);
            if (!TryValidateHello(hello, _token!, out _))
                return;

            await WriteFrameAsync(stream, "NOVORA-LINK|2|ACK", handshake.Token);
            _connected = true;
            SetState("Connected", "Canal CONTROL de LinkEngine conectado por USB.");
            long sequence = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                heartbeat.CancelAfter(HeartbeatTimeout);
                string payload = await ReadFrameAsync(stream, heartbeat.Token);
                if (!TryCreateHeartbeatAck(payload, ref sequence, out string ack))
                    throw new InvalidDataException("Heartbeat de LinkEngine no válido.");
                await WriteFrameAsync(stream, ack, heartbeat.Token);
            }
        }
    }

    internal static bool TryValidateHello(string payload, string token, out string clientId)
    {
        clientId = "";
        string[] fields = payload.Split('|');
        if (fields.Length != 5 || fields[0] != "NOVORA-LINK" || fields[1] != "2" ||
            fields[2] != "HELLO" || string.IsNullOrWhiteSpace(fields[3]) ||
            fields[3].Length > 128 || !IsToken(fields[4]) || !IsToken(token))
            return false;
        byte[] received = Encoding.ASCII.GetBytes(fields[4].ToLowerInvariant());
        byte[] expected = Encoding.ASCII.GetBytes(token.ToLowerInvariant());
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

    private static async Task<string> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 1 or > 1024)
            throw new InvalidDataException("Frame CONTROL de LinkEngine fuera de límite.");
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return new UTF8Encoding(false, true).GetString(payload);
    }

    private static async Task WriteFrameAsync(Stream stream, string payload, CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(payload);
        byte[] frame = new byte[sizeof(int) + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, body.Length);
        body.CopyTo(frame, sizeof(int));
        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static bool IsToken(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException) { }
        finally
        {
            process.Dispose();
        }
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
