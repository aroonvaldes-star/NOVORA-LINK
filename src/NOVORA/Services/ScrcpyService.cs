using NOVORA.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace NOVORA.Services;

public sealed class ScrcpyService : IDisposable
{
    private readonly NovoraPaths _paths;
    private readonly ConcurrentDictionary<string, ScrcpySession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public ScrcpyService(NovoraPaths paths) { _paths = paths ?? throw new ArgumentNullException(nameof(paths)); }

    public bool IsRunning(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial)) return false;
        if (!_sessions.TryGetValue(serial, out var session)) return false;
        if (!session.Process.HasExited) return true;
        RemoveFinishedSession(serial); return false;
    }

    public Process? GetProcess(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial)) return null;
        if (!_sessions.TryGetValue(serial, out var session)) return null;
        if (!session.Process.HasExited) return session.Process;
        RemoveFinishedSession(serial); return null;
    }

    public OutputProfile? GetProfile(string serial) => string.IsNullOrWhiteSpace(serial) ? null : _sessions.TryGetValue(serial, out var session) ? session.Profile : null;

    public Process StartOptimized(DeviceInfo device, MonitorInfo monitor, OutputProfile profile, bool audioEnabled = true)
    {
        ThrowIfDisposed(); _paths.ValidateRequiredTools(); ValidateDevice(device); ValidateMonitor(monitor); ValidateProfile(profile);
        var serial = device.Serial.Trim();
        if (_sessions.TryGetValue(serial, out var existing)) { if (!existing.Process.HasExited) return existing.Process; RemoveFinishedSession(serial); }
        var process = StartProcess(device, monitor, profile, audioEnabled);
        var session = new ScrcpySession(process, monitor, profile, audioEnabled);
        if (!_sessions.TryAdd(serial, session))
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            process.Dispose(); throw new InvalidOperationException("No se pudo registrar la sesión Scrcpy.");
        }
        process.Exited += (_, _) => RemoveFinishedSession(serial);
        return process;
    }

    public async Task StopAsync(string serial, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial)) return;
        if (!_sessions.TryRemove(serial, out var session)) return;
        try { if (!session.Process.HasExited) { session.Process.Kill(entireProcessTree: true); await session.Process.WaitForExitAsync(cancellationToken); } }
        catch (InvalidOperationException) { }
        finally { session.Process.Dispose(); }
    }

    public async Task<Process> RestartWithProfileAsync(DeviceInfo device, MonitorInfo monitor, OutputProfile profile, bool audioEnabled = true, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed(); ValidateDevice(device); ValidateMonitor(monitor); ValidateProfile(profile);
        var serial = device.Serial.Trim(); await StopAsync(serial, cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        return StartOptimized(device, monitor, profile, audioEnabled);
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var serial in _sessions.Keys.ToArray()) { cancellationToken.ThrowIfCancellationRequested(); await StopAsync(serial, cancellationToken); }
    }

    private Process StartProcess(DeviceInfo device, MonitorInfo monitor, OutputProfile profile, bool audioEnabled)
    {
        var maxSize = profile.MaxSize > 0 ? profile.MaxSize : 1024;
        var targetFps = profile.TargetFps > 0 ? profile.TargetFps : 60;
        var bitrate = string.IsNullOrWhiteSpace(profile.Bitrate) ? "4M" : profile.Bitrate.Trim();
        var startInfo = new ProcessStartInfo { FileName = _paths.Scrcpy, WorkingDirectory = _paths.ToolsDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = false, RedirectStandardOutput = false };
        startInfo.ArgumentList.Add("--serial"); startInfo.ArgumentList.Add(device.Serial);
        startInfo.ArgumentList.Add("--video-codec"); startInfo.ArgumentList.Add("h264");
        startInfo.ArgumentList.Add("--video-bit-rate"); startInfo.ArgumentList.Add(bitrate);
        startInfo.ArgumentList.Add("--max-fps"); startInfo.ArgumentList.Add(targetFps.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--max-size"); startInfo.ArgumentList.Add(maxSize.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--stay-awake"); startInfo.ArgumentList.Add("--disable-screensaver");

        const int windowMargin = 24;
        var availableWidth = Math.Max(320, monitor.Width - (windowMargin * 2));
        var availableHeight = Math.Max(240, monitor.Height - (windowMargin * 2));
        var windowWidth = Math.Max(320, (int)(availableWidth * 0.90));
        var windowHeight = Math.Max(240, (int)(availableHeight * 0.90));
        var windowX = monitor.Left + ((monitor.Width - windowWidth) / 2);
        var windowY = monitor.Top + ((monitor.Height - windowHeight) / 2);
        startInfo.ArgumentList.Add("--window-x"); startInfo.ArgumentList.Add(windowX.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--window-y"); startInfo.ArgumentList.Add(windowY.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--window-width"); startInfo.ArgumentList.Add(windowWidth.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--window-height"); startInfo.ArgumentList.Add(windowHeight.ToString(CultureInfo.InvariantCulture));
        var model = string.IsNullOrWhiteSpace(device.Model) ? "Android" : device.Model;
        startInfo.ArgumentList.Add("--window-title"); startInfo.ArgumentList.Add($"NOVORA — {model}");
        if (!audioEnabled) startInfo.ArgumentList.Add("--no-audio");
        return Process.Start(startInfo) ?? throw new InvalidOperationException("No se pudo iniciar scrcpy.");
    }

    private void RemoveFinishedSession(string serial) { if (!_sessions.TryRemove(serial, out var session)) return; try { session.Process.Dispose(); } catch { } }
    private static void ValidateDevice(DeviceInfo device) { if (device is null || !device.Connected || string.IsNullOrWhiteSpace(device.Serial)) throw new InvalidOperationException("No hay un dispositivo Android conectado."); }
    private static void ValidateMonitor(MonitorInfo monitor) { if (monitor is null) throw new InvalidOperationException("No se pudo determinar el monitor de salida."); }
    private static void ValidateProfile(OutputProfile profile) { if (profile is null) throw new InvalidOperationException("No se pudo determinar el perfil de salida."); }
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(ScrcpyService)); }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (var pair in _sessions) { try { if (!pair.Value.Process.HasExited) pair.Value.Process.Kill(entireProcessTree: true); } catch { } try { pair.Value.Process.Dispose(); } catch { } }
        _sessions.Clear();
    }

    private sealed record ScrcpySession(Process Process, MonitorInfo Monitor, OutputProfile Profile, bool AudioEnabled);
}