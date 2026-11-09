using System.Diagnostics;
using System.Text;

namespace NOVORA.Linux.Control;

/// <summary>
/// Follows ADB hotplug events with <c>adb track-devices -l</c> instead of polling.
/// <see cref="Devices"/> is null while ADB is unavailable, so callers can distinguish
/// "no phone" from "unknown".
/// </summary>
internal sealed class AdbDeviceTracker : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private IReadOnlyList<AdbDevice>? _devices;
    private Task _run = Task.CompletedTask;
    private bool _started;
    private Process? _current;

    public event Action? Changed;
    public string? LastError { get; private set; }

    public IReadOnlyList<AdbDevice>? Devices
    {
        get { lock (_gate) return _devices; }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }
        // SIGTERM skips window disposal; never leave an orphaned «adb track-devices» behind.
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        _run = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        int failures = 0;
        while (!_stop.IsCancellationRequested)
        {
            bool receivedAny = false;
            try
            {
                receivedAny = await TrackOnceAsync(_stop.Token).ConfigureAwait(false);
                LastError = "ADB dejó de informar dispositivos; se reintentará.";
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or
                                       System.ComponentModel.Win32Exception or FormatException)
            {
                LastError = ex is System.ComponentModel.Win32Exception
                    ? "ADB no está instalado o no está disponible en PATH."
                    : $"ADB no pudo seguir los dispositivos: {ex.Message}";
            }
            Publish(null);
            failures = receivedAny ? 1 : failures + 1;
            try
            {
                await Task.Delay(LinuxAutoConnectPolicy.RetryDelay(failures), _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<bool> TrackOnceAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = "adb",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("track-devices");
        start.ArgumentList.Add("-l");
        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("No se pudo iniciar ADB.");
        lock (_gate) _current = process;
        // Drain stderr so a chatty ADB server start cannot block the pipe.
        Task<string> errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        // Pipe reads do not always observe cancellation; ending ADB closes the pipe instead.
        using CancellationTokenRegistration stopRegistration = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });
        bool receivedAny = false;
        try
        {
            Stream output = process.StandardOutput.BaseStream;
            while (!cancellationToken.IsCancellationRequested)
            {
                string? frame = await ReadFrameAsync(output, cancellationToken).ConfigureAwait(false);
                if (frame is null)
                    break;
                receivedAny = true;
                LastError = null;
                Publish(ParseFrame(frame));
            }
        }
        finally
        {
            lock (_gate) _current = null;
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            try { await errors.ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
        }
        return receivedAny;
    }

    /// <summary>Reads one ADB smart-socket frame: four hex digits of byte length, then the payload.</summary>
    internal static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        if (!await ReadExactlyOrEndAsync(stream, header, cancellationToken).ConfigureAwait(false))
            return null;
        int length = int.Parse(Encoding.ASCII.GetString(header), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture);
        if (length is < 0 or > 65535)
            throw new FormatException("Trama ADB no válida.");
        byte[] payload = new byte[length];
        if (length > 0 && !await ReadExactlyOrEndAsync(stream, payload, cancellationToken).ConfigureAwait(false))
            return null;
        return Encoding.UTF8.GetString(payload);
    }

    internal static IReadOnlyList<AdbDevice> ParseFrame(string frame) =>
        frame.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => AdbBridge.ParseDevice(line.TrimEnd('\r')))
            .Where(device => device is not null)
            .Cast<AdbDevice>()
            .ToArray();

    private static async Task<bool> ReadExactlyOrEndAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0) return false;
                throw new FormatException("Trama ADB incompleta.");
            }
            offset += read;
        }
        return true;
    }

    private void Publish(IReadOnlyList<AdbDevice>? devices)
    {
        lock (_gate)
        {
            if (devices is null && _devices is null) return;
            if (devices is not null && _devices is not null && devices.SequenceEqual(_devices)) return;
            _devices = devices;
        }
        Changed?.Invoke();
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        Process? process;
        lock (_gate) process = _current;
        try { process?.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _stop.Cancel();
        try { await _run.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}
