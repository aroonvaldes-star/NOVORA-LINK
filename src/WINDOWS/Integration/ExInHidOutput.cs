using NOVORA.Contracts.Input;
using NOVORA.Service;
using System.Diagnostics;
using System.Text;

namespace NOVORA.Integration;

/// <summary>Owns Android system HID sessions for ExIn virtual controllers.</summary>
internal sealed class ExInHidOutput : INLInputOutput, INLInputUiOutput, IAsyncDisposable
{
    private readonly NLServiceADB _adb;
    private readonly NLServiceNovoraPaths _paths;
    private readonly string _serial;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<ushort, Process> _devices = new();
    private volatile bool _disposed;
    private volatile bool _failed;

    internal event EventHandler? Failed;

    internal ExInHidOutput(NLServiceADB adb, NLServiceNovoraPaths paths, string serial)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _serial = string.IsNullOrWhiteSpace(serial) ? throw new ArgumentException("Serial ADB vacío.", nameof(serial)) : serial.Trim();
    }

    public bool IsReady => !_disposed && !_failed;

    public async Task CreateAsync(NLInputDevice device, byte[] descriptor, CancellationToken cancellationToken)
    {
        string command = ExInHidProtocol.Register(device.DeviceId, device.Name,
            device.VendorId, device.ProductId, descriptor);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireReady();
            if (_devices.ContainsKey(device.DeviceId))
                throw new InvalidOperationException("El dispositivo HID ya existe.");
            await _adb.StartServerAsync(cancellationToken).ConfigureAwait(false);
            var start = new ProcessStartInfo(_paths.Adb)
            {
                WorkingDirectory = _paths.ToolsDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Encoding.UTF8
            };
            foreach (string argument in new[] { "-s", _serial, "shell", "hid", "-" })
                start.ArgumentList.Add(argument);
            Process process = Process.Start(start)
                ?? throw new InvalidOperationException("ADB no pudo iniciar Android HID.");
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => _ = HandleExitAsync(device.DeviceId, process);
                _ = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                await process.StandardInput.WriteLineAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                // Android's HID command registers in the kernel before inputflinger sees the device.
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                if (process.HasExited)
                    throw new InvalidOperationException("Android cerró el comando HID antes de registrar el mando.");
                _devices.Add(device.DeviceId, process);
            }
            catch
            {
                CloseProcess(process);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task HandleExitAsync(ushort deviceId, Process process)
    {
        bool failed = false;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_disposed && _devices.TryGetValue(deviceId, out Process? current) &&
                ReferenceEquals(current, process))
            {
                _failed = true;
                failed = true;
            }
        }
        finally { _gate.Release(); }
        if (failed) Failed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SendAsync(ushort deviceId, byte[] report, CancellationToken cancellationToken)
    {
        string command = ExInHidProtocol.Report(deviceId, report);
        bool notifyFailed = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireReady();
            if (!_devices.TryGetValue(deviceId, out Process? process) || process.HasExited)
            {
                throw new IOException("El mando virtual de Android se desconectó.");
            }
            // The deadline timer only exists when the pipe write does not complete at once;
            // a linked CancellationTokenSource per report cost a timer for every report.
            Task write = WriteLineAsync(process.StandardInput, command, cancellationToken);
            try
            {
                if (!write.IsCompleted)
                    await write.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                else
                    await write.ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                // A partly written JSON line cannot safely be retried in the same shell.
                throw new IOException("La salida HID no respondió en dos segundos.", ex);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            notifyFailed = !_disposed && !_failed;
            _failed = true;
            throw;
        }
        finally
        {
            _gate.Release();
            if (notifyFailed) Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static async Task WriteLineAsync(StreamWriter input, string command, CancellationToken cancellationToken)
    {
        await input.WriteLineAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
        await input.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DestroyAsync(ushort deviceId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_devices.Remove(deviceId, out Process? process)) return;
            await StopProcessAsync(process).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task SendUiActionAsync(NLInputUiAction action, CancellationToken cancellationToken)
    {
        string? keycode = action switch
        {
            NLInputUiAction.Up => "19",
            NLInputUiAction.Down => "20",
            NLInputUiAction.Left => "21",
            NLInputUiAction.Right => "22",
            NLInputUiAction.Select => "23",
            NLInputUiAction.Back => "4",
            _ => null
        };
        if (keycode is null) return;
        RequireReady();
        await _adb.ExecuteRawAsync(new[] { "-s", _serial, "shell", "input", "keyevent", keycode },
            cancellationToken).ConfigureAwait(false);
    }

    private void RequireReady()
    {
        if (!IsReady) throw new InvalidOperationException("La salida HID de Android no está disponible.");
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (TimeoutException) { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }

    private static void CloseProcess(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        process.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Process process in _devices.Values)
                await StopProcessAsync(process).ConfigureAwait(false);
            _devices.Clear();
        }
        finally { _gate.Release(); }
    }
}
