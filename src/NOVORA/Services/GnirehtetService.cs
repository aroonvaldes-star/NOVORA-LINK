using NOVORA.Models;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NOVORA.Services;

public sealed class GnirehtetService : IDisposable
{
    private readonly NovoraPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StringBuilder _lastOutput = new();
    private Process? _relayProcess;
    private string? _activeSerial;
    private bool _disposed;

    public GnirehtetService(NovoraPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    public bool IsActive => !_disposed && !string.IsNullOrWhiteSpace(_activeSerial) && _relayProcess is { HasExited: false };
    public bool IsRelayActive => !_disposed && _relayProcess is { HasExited: false };
    public string? ActiveSerial => _activeSerial;

    public async Task<GnirehtetResult> StartAsync(DeviceInfo device, int connectedDeviceCount, CancellationToken cancellationToken = default)
    {
        _ = connectedDeviceCount;
        if (_disposed) return GnirehtetResult.Fail("El servicio de Gnirehtet ya fue liberado.");
        if (device is null || !device.Connected || string.IsNullOrWhiteSpace(device.Serial))
            return GnirehtetResult.Fail("No hay un dispositivo Android conectado con serial ADB válido.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _paths.ValidateGnirehtetTools();
            var serial = device.Serial.Trim();
            if (IsActive && string.Equals(_activeSerial, serial, StringComparison.OrdinalIgnoreCase))
                return GnirehtetResult.Ok("Gnirehtet ya está activo.");
            if (IsActive) await StopCoreAsync(_activeSerial, cancellationToken);

            if (_relayProcess is null || _relayProcess.HasExited)
            {
                await TerminateRelayAsync(CancellationToken.None);
                _relayProcess = StartGnirehtet("relay");
                await Task.Delay(700, cancellationToken);
                if (_relayProcess.HasExited)
                {
                    var error = GetLastOutput();
                    await TerminateRelayAsync(CancellationToken.None);
                    return GnirehtetResult.Fail(string.IsNullOrWhiteSpace(error) ? "El relay de Gnirehtet terminó inmediatamente." : error);
                }
            }

            using (var install = StartGnirehtet("install", serial))
            {
                await install.WaitForExitAsync(cancellationToken);
                if (install.ExitCode != 0)
                {
                    var error = GetLastOutput();
                    await TerminateRelayAsync(CancellationToken.None);
                    _activeSerial = null;
                    return GnirehtetResult.Fail(string.IsNullOrWhiteSpace(error) ? "No se pudo instalar Gnirehtet en el dispositivo." : error);
                }
            }

            using (var start = StartGnirehtet("start", serial))
            {
                await start.WaitForExitAsync(cancellationToken);
                if (start.ExitCode != 0)
                {
                    var error = GetLastOutput();
                    await TerminateRelayAsync(CancellationToken.None);
                    _activeSerial = null;
                    return GnirehtetResult.Fail(string.IsNullOrWhiteSpace(error) ? "Gnirehtet no pudo iniciar el cliente VPN." : error);
                }
            }

            await Task.Delay(1200, cancellationToken);
            _activeSerial = serial;
            return GnirehtetResult.Ok("Gnirehtet activo y túnel iniciado.");
        }
        catch (OperationCanceledException)
        {
            await TerminateRelayAsync(CancellationToken.None);
            _activeSerial = null;
            throw;
        }
        catch (Exception ex)
        {
            await TerminateRelayAsync(CancellationToken.None);
            _activeSerial = null;
            return GnirehtetResult.Fail(ex.Message);
        }
        finally { _gate.Release(); }
    }

    public async Task<GnirehtetResult> ResetTunnelAsync(string? serial = null, CancellationToken cancellationToken = default)
    {
        if (_disposed) return GnirehtetResult.Fail("El servicio de Gnirehtet ya fue liberado.");
        var target = string.IsNullOrWhiteSpace(serial) ? _activeSerial : serial.Trim();
        if (string.IsNullOrWhiteSpace(target)) return GnirehtetResult.Fail("No hay un dispositivo asociado a Gnirehtet.");

        await _gate.WaitAsync(cancellationToken);
        var relayCreatedHere = false;
        try
        {
            _paths.ValidateGnirehtetTools();
            if (_relayProcess is null || _relayProcess.HasExited)
            {
                await TerminateRelayAsync(CancellationToken.None);
                _relayProcess = StartGnirehtet("relay");
                relayCreatedHere = true;
                await Task.Delay(700, cancellationToken);
                if (_relayProcess.HasExited)
                {
                    var error = GetLastOutput();
                    await TerminateRelayAsync(CancellationToken.None);
                    return GnirehtetResult.Fail(string.IsNullOrWhiteSpace(error) ? "No se pudo restaurar el relay de Gnirehtet." : error);
                }
            }

            using var tunnel = StartGnirehtet("tunnel", target);
            await tunnel.WaitForExitAsync(cancellationToken);
            if (tunnel.ExitCode != 0)
            {
                var error = GetLastOutput();
                if (relayCreatedHere && string.IsNullOrWhiteSpace(_activeSerial)) await TerminateRelayAsync(CancellationToken.None);
                return GnirehtetResult.Fail(string.IsNullOrWhiteSpace(error) ? "Gnirehtet no pudo restablecer el túnel." : error);
            }
            _activeSerial = target;
            return GnirehtetResult.Ok("Túnel Gnirehtet restablecido.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            if (relayCreatedHere && string.IsNullOrWhiteSpace(_activeSerial)) await TerminateRelayAsync(CancellationToken.None);
            return GnirehtetResult.Fail(ex.Message);
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(string? serial, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        await _gate.WaitAsync(cancellationToken);
        try { await StopCoreAsync(serial, cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task StopCoreAsync(string? serial, CancellationToken cancellationToken)
    {
        var target = string.IsNullOrWhiteSpace(serial) ? _activeSerial : serial.Trim();
        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stopCts.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            if (!string.IsNullOrWhiteSpace(target) && File.Exists(_paths.Gnirehtet))
            {
                using var stop = StartGnirehtet("stop", target);
                await stop.WaitForExitAsync(stopCts.Token);
            }
        }
        catch { }
        await TerminateRelayAsync(CancellationToken.None);
        _activeSerial = null;
    }

    private async Task TerminateRelayAsync(CancellationToken cancellationToken)
    {
        var relay = _relayProcess;
        _relayProcess = null;
        if (relay is null) return;
        using var cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cleanupCts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            if (!relay.HasExited)
            {
                relay.Kill(entireProcessTree: true);
                await relay.WaitForExitAsync(cleanupCts.Token);
            }
        }
        catch { }
        finally
        {
            try { relay.Dispose(); } catch { }
        }
    }

    private Process StartGnirehtet(params string[] arguments)
    {
        _paths.ValidateGnirehtetTools();
        var info = new ProcessStartInfo
        {
            FileName = _paths.Gnirehtet,
            WorkingDirectory = _paths.ToolsDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        info.Environment["ADB"] = _paths.Adb;
        info.Environment["GNIREHTET_APK"] = _paths.GnirehtetApk;
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        lock (_lastOutput) _lastOutput.Clear();
        var process = Process.Start(info) ?? throw new InvalidOperationException("No fue posible iniciar Gnirehtet.");
        process.OutputDataReceived += (_, e) => AppendOutput(e.Data);
        process.ErrorDataReceived += (_, e) => AppendOutput(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private void AppendOutput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_lastOutput)
        {
            _lastOutput.AppendLine(text);
            const int maxLength = 8192;
            if (_lastOutput.Length > maxLength) _lastOutput.Remove(0, _lastOutput.Length - maxLength);
        }
    }

    private string GetLastOutput() { lock (_lastOutput) return _lastOutput.ToString().Trim(); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var relay = _relayProcess;
        _relayProcess = null;
        if (relay is not null)
        {
            try { if (!relay.HasExited) relay.Kill(entireProcessTree: true); } catch { }
            try { relay.Dispose(); } catch { }
        }
        _activeSerial = null;
        _gate.Dispose();
    }
}

public sealed record GnirehtetResult(bool Success, string Message)
{
    public static GnirehtetResult Ok(string message = "Gnirehtet activo.") => new(true, message);
    public static GnirehtetResult Fail(string message) => new(false, message);
}
