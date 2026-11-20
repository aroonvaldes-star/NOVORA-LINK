using System.Diagnostics;
using System.IO;
using System.Text;
using NOVORA.Launcher.Models;

namespace NOVORA.Launcher.Services;

/// <summary>
/// Controls Gnirehtet only when the user explicitly requests it.
/// It does not poll the device and does not run automatically with NOVORA.
/// </summary>
public sealed class GnirehtetService : IDisposable
{
    private readonly NovoraPaths _paths;
    private Process? _sessionProcess;
    private Process? _relayProcess;
    private string? _activeSerial;
    private readonly StringBuilder _lastError = new();

    public GnirehtetService(NovoraPaths paths)
    {
        _paths = paths;
    }

    public bool IsActive =>
        !string.IsNullOrWhiteSpace(_activeSerial) &&
        ((_sessionProcess is { HasExited: false }) || (_relayProcess is { HasExited: false }));

    public string? ActiveSerial => _activeSerial;

    public async Task<GnirehtetResult> StartAsync(DeviceInfo device, int connectedDeviceCount, CancellationToken cancellationToken = default)
    {
        if (device is null || !device.Connected || string.IsNullOrWhiteSpace(device.Serial))
            return GnirehtetResult.Fail("No hay un dispositivo Android ADB disponible.");

        try
        {
            _paths.ValidateGnirehtetTools();

            if (IsActive && string.Equals(_activeSerial, device.Serial, StringComparison.OrdinalIgnoreCase))
                return GnirehtetResult.Ok("Gnirehtet ya está activo.");

            if (IsActive)
                await StopAsync(_activeSerial, cancellationToken);

            // Gnirehtet v2.5.1 supports custom ADB/APK paths through these variables.
            if (connectedDeviceCount <= 1)
            {
                var process = StartGnirehtet(new[] { "run", device.Serial });
                _sessionProcess = process;
                _relayProcess = null;
                _activeSerial = device.Serial;

                await Task.Delay(900, cancellationToken);
                if (process.HasExited)
                {
                    var error = ReadProcessError(process);
                    ClearSession();
                    return GnirehtetResult.Fail(string.IsNullOrWhiteSpace(error)
                        ? "Gnirehtet terminó inmediatamente."
                        : error);
                }

                return GnirehtetResult.Ok("Gnirehtet activo.");
            }

            // Multiple devices: one persistent relay + one client per selected serial.
            if (_relayProcess is null || _relayProcess.HasExited)
            {
                _relayProcess = StartGnirehtet(new[] { "relay" });
                await Task.Delay(500, cancellationToken);
                if (_relayProcess.HasExited)
                {
                    var error = ReadProcessError(_relayProcess);
                    ClearRelay();
                    return GnirehtetResult.Fail(string.IsNullOrWhiteSpace(error)
                        ? "No se pudo iniciar el relay de Gnirehtet."
                        : error);
                }
            }

            using var start = StartGnirehtet(new[] { "start", device.Serial });
            await start.WaitForExitAsync(cancellationToken);
            if (start.ExitCode != 0)
            {
                var error = ReadProcessError(start);
                return GnirehtetResult.Fail(string.IsNullOrWhiteSpace(error)
                    ? "Gnirehtet no pudo iniciar el cliente."
                    : error);
            }

            _activeSerial = device.Serial;
            return GnirehtetResult.Ok("Gnirehtet activo.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ClearSession();
            return GnirehtetResult.Fail(ex.Message);
        }
    }

    public async Task StopAsync(string? serial, CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(serial) ? _activeSerial : serial;

        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stopCts.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            if (!string.IsNullOrWhiteSpace(target) && File.Exists(_paths.Gnirehtet))
            {
                var stopSerial = target;
                using var stop = StartGnirehtet(new[] { "stop", stopSerial });
                await stop.WaitForExitAsync(stopCts.Token);
            }
        }
        catch
        {
            // Process cleanup below is still attempted.
        }

        try
        {
            if (_sessionProcess is { HasExited: false })
                _sessionProcess.Kill(entireProcessTree: true);
        }
        catch { }

        try
        {
            if (_relayProcess is { HasExited: false })
                _relayProcess.Kill(entireProcessTree: true);
        }
        catch { }

        ClearSession();
        ClearRelay();
        _activeSerial = null;
    }

    private Process StartGnirehtet(IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = _paths.Gnirehtet,
            WorkingDirectory = _paths.ToolBundleDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        info.Environment["ADB"] = _paths.Adb;
        info.Environment["GNIREHTET_APK"] = _paths.GnirehtetApk;

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        lock (_lastError)
            _lastError.Clear();

        var process = Process.Start(info)
            ?? throw new InvalidOperationException("No fue posible iniciar Gnirehtet.");

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                lock (_lastError)
                {
                    _lastError.AppendLine(e.Data);
                    if (_lastError.Length > 8192) _lastError.Remove(0, _lastError.Length - 8192);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                lock (_lastError)
                {
                    _lastError.AppendLine(e.Data);
                    if (_lastError.Length > 8192) _lastError.Remove(0, _lastError.Length - 8192);
                }
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private string ReadProcessError(Process process)
    {
        lock (_lastError)
            return _lastError.ToString().Trim();
    }

    private void ClearSession()
    {
        _sessionProcess?.Dispose();
        _sessionProcess = null;
    }

    private void ClearRelay()
    {
        _relayProcess?.Dispose();
        _relayProcess = null;
    }

    public void Dispose()
    {
        try
        {
            if (_sessionProcess is { HasExited: false }) _sessionProcess.Kill(entireProcessTree: true);
        }
        catch { }
        try
        {
            if (_relayProcess is { HasExited: false }) _relayProcess.Kill(entireProcessTree: true);
        }
        catch { }
        ClearSession();
        ClearRelay();
    }
}

public sealed record GnirehtetResult(bool Success, string Message)
{
    public static GnirehtetResult Ok(string message = "Gnirehtet activo.") => new(true, message);
    public static GnirehtetResult Fail(string message) => new(false, message);
}
