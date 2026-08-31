using NOVORA.Models;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NOVORA.Services;

/// <summary>
/// Control principal de Gnirehtet.
///
/// Responsabilidades:
/// - Iniciar Gnirehtet.
/// - Mantener el relay cuando sea necesario.
/// - Asociar el túnel a un dispositivo concreto.
/// - Reiniciar únicamente el túnel mediante "tunnel".
/// - Detener Gnirehtet solo cuando NOVORA lo solicita explícitamente.
///
/// Recovery se encarga de decidir cuándo recuperar una conexión.
/// </summary>
public sealed class GnirehtetService : IDisposable
{
    private readonly NovoraPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Process? _relayProcess;

    private string? _activeSerial;

    private readonly StringBuilder _lastOutput = new();

    private bool _disposed;

    public event Action<string>? StatusChanged;

    public GnirehtetService(NovoraPaths paths)
    {
        _paths = paths ??
            throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>
    /// Indica si NOVORA tiene una sesión Gnirehtet asociada.
    /// </summary>
    public bool IsActive
    {
        get
        {
            if (_disposed)
                return false;

            if (string.IsNullOrWhiteSpace(_activeSerial))
                return false;

            bool relayAlive =
                _relayProcess is { HasExited: false };

            return relayAlive;
        }
    }

    /// <summary>
    /// Indica si existe un proceso relay activo.
    /// </summary>
    public bool IsRelayActive =>
        !_disposed &&
        _relayProcess is { HasExited: false };

    /// <summary>
    /// Serial del dispositivo asociado actualmente.
    /// </summary>
    public string? ActiveSerial =>
        _activeSerial;

    /// <summary>
    /// Inicia Gnirehtet para el dispositivo indicado.
    ///
    /// Se utiliza el flujo oficial:
    ///
    /// relay
    /// install [serial]
    /// start [serial]
    ///
    /// Esto permite utilizar el mismo comportamiento para uno o varios
    /// dispositivos y facilita la recuperación mediante "tunnel".
    /// </summary>
    public async Task<GnirehtetResult> StartAsync(
        DeviceInfo device,
        int connectedDeviceCount,
        CancellationToken cancellationToken = default)
    {
        _ = connectedDeviceCount;

        if (_disposed)
        {
            return GnirehtetResult.Fail(
                "El servicio de Gnirehtet ya fue liberado.");
        }

        if (device is null)
        {
            return GnirehtetResult.Fail(
                "No se recibió información del dispositivo.");
        }

        if (!device.Connected)
        {
            return GnirehtetResult.Fail(
                "El dispositivo Android no está conectado.");
        }

        if (string.IsNullOrWhiteSpace(device.Serial))
        {
            return GnirehtetResult.Fail(
                "El dispositivo no tiene un serial ADB válido.");
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            _paths.ValidateGnirehtetTools();

            cancellationToken.ThrowIfCancellationRequested();

            string serial = device.Serial.Trim();

            // -----------------------------------------------------
            // Ya existe una sesión para el mismo dispositivo.
            // No crear otro túnel.
            // -----------------------------------------------------

            if (IsActive &&
                string.Equals(
                    _activeSerial,
                    serial,
                    StringComparison.OrdinalIgnoreCase))
            {
                return GnirehtetResult.Ok(
                    "Gnirehtet ya está activo.");
            }

            // -----------------------------------------------------
            // Si hay otra sesión, limpiarla antes de iniciar.
            // -----------------------------------------------------

            if (IsActive)
            {
                await StopCoreAsync(
                    _activeSerial,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // -----------------------------------------------------
            // 1. Iniciar relay
            // -----------------------------------------------------

            if (_relayProcess is null ||
                _relayProcess.HasExited)
            {
                _relayProcess =
                    StartGnirehtet(
                        "relay");

                await WaitForProcessStartupAsync(
                    _relayProcess,
                    TimeSpan.FromMilliseconds(700),
                    cancellationToken);

                if (_relayProcess.HasExited)
                {
                    string error =
                        GetLastOutput();

                    ClearRelay();

                    return GnirehtetResult.Fail(
                        string.IsNullOrWhiteSpace(error)
                            ? "El relay de Gnirehtet terminó inmediatamente."
                            : error);
                }
            }

            // -----------------------------------------------------
            // 2. Instalar/actualizar APK de Gnirehtet
            // -----------------------------------------------------

            using (var installProcess =
                StartGnirehtet(
                    "install",
                    serial))
            {
                await installProcess.WaitForExitAsync(
                    cancellationToken);

                if (installProcess.ExitCode != 0)
                {
                    string error =
                        GetLastOutput();

                    // CAMBIO AQUÍ: Limpiar relay si el proceso falla.
                    ClearRelay();

                    return GnirehtetResult.Fail(
                        string.IsNullOrWhiteSpace(error)
                            ? "No se pudo instalar Gnirehtet en el dispositivo."
                            : error);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // -----------------------------------------------------
            // 3. Iniciar cliente VPN
            // -----------------------------------------------------

            using (var startProcess =
                StartGnirehtet(
                    "start",
                    serial))
            {
                await startProcess.WaitForExitAsync(
                    cancellationToken);

                if (startProcess.ExitCode != 0)
                {
                    string error =
                        GetLastOutput();

                    // CAMBIO AQUÍ: Limpiar relay si el proceso falla.
                    ClearRelay();

                    return GnirehtetResult.Fail(
                        string.IsNullOrWhiteSpace(error)
                            ? "Gnirehtet no pudo iniciar el cliente VPN."
                            : error);
                }
            }

            // -----------------------------------------------------
            // 4. Dar un pequeño margen para que Android levante
            //    la interfaz VPN.
            // -----------------------------------------------------

            await Task.Delay(
                TimeSpan.FromMilliseconds(1200),
                cancellationToken);

            _activeSerial = serial;

            Report("Gnirehtet activo y túnel iniciado.");

            return GnirehtetResult.Ok(
                "Gnirehtet activo y túnel iniciado.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ClearRelay();
            _activeSerial = null;

            return GnirehtetResult.Fail(
                ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reinicia únicamente el túnel de Gnirehtet.
    ///
    /// NO detiene la VPN completa.
    /// Es el método destinado a Recovery.
    /// </summary>
    public async Task<GnirehtetResult> ResetTunnelAsync(
        string? serial = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return GnirehtetResult.Fail(
                "El servicio de Gnirehtet ya fue liberado.");
        }

        string? target =
            string.IsNullOrWhiteSpace(serial)
                ? _activeSerial
                : serial.Trim();

        if (string.IsNullOrWhiteSpace(target))
        {
            return GnirehtetResult.Fail(
                "No hay un dispositivo asociado a Gnirehtet.");
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            _paths.ValidateGnirehtetTools();

            cancellationToken.ThrowIfCancellationRequested();

            // Si el relay murió, Recovery debe reconstruirlo.
            if (_relayProcess is null ||
                _relayProcess.HasExited)
            {
                _relayProcess =
                    StartGnirehtet(
                        "relay");

                await WaitForProcessStartupAsync(
                    _relayProcess,
                    TimeSpan.FromMilliseconds(700),
                    cancellationToken);

                if (_relayProcess.HasExited)
                {
                    string error =
                        GetLastOutput();

                    ClearRelay();

                    return GnirehtetResult.Fail(
                        string.IsNullOrWhiteSpace(error)
                            ? "No se pudo restaurar el relay de Gnirehtet."
                            : error);
                }
            }

            // -----------------------------------------------------
            // IMPORTANTE:
            //
            // "tunnel" reinicia el túnel sin matar arbitrariamente
            // la VPN ni el proceso relay.
            // -----------------------------------------------------

            using (var tunnelProcess =
                StartGnirehtet(
                    "tunnel",
                    target))
            {
                await tunnelProcess.WaitForExitAsync(
                    cancellationToken);

                if (tunnelProcess.ExitCode != 0)
                {
                    string error =
                        GetLastOutput();

                    return GnirehtetResult.Fail(
                        string.IsNullOrWhiteSpace(error)
                            ? "Gnirehtet no pudo restablecer el túnel."
                            : error);
                }
            }

            _activeSerial = target;

            Report("Túnel Gnirehtet restablecido.");

            return GnirehtetResult.Ok(
                "Túnel Gnirehtet restablecido.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return GnirehtetResult.Fail(
                ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Detiene Gnirehtet y limpia la sesión local.
    ///
    /// Este método solo debe utilizarse cuando NOVORA realmente
    /// quiere detener Gnirehtet.
    /// </summary>
    public async Task StopAsync(
        string? serial,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await StopCoreAsync(
                serial,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Lógica interna de detención.
    /// No adquiere el semáforo para poder utilizarse desde StartAsync.
    /// </summary>
    private async Task StopCoreAsync(
        string? serial,
        CancellationToken cancellationToken)
    {
        string? target =
            string.IsNullOrWhiteSpace(serial)
                ? _activeSerial
                : serial.Trim();

        using var stopCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        stopCts.CancelAfter(
            TimeSpan.FromSeconds(4));

        // ---------------------------------------------------------
        // Detener cliente Android mediante Gnirehtet.
        // ---------------------------------------------------------

        try
        {
            if (!string.IsNullOrWhiteSpace(target) &&
                File.Exists(_paths.Gnirehtet))
            {
                using (var stopProcess =
                    StartGnirehtet(
                        "stop",
                        target))
                {
                    await stopProcess.WaitForExitAsync(
                        stopCts.Token);
                }
            }
        }
        catch
        {
            // La limpieza local continúa aunque Android no responda.
        }

        // ---------------------------------------------------------
        // Limpiar relay.
        // ---------------------------------------------------------

        try
        {
            if (_relayProcess is { HasExited: false })
            {
                _relayProcess.Kill(
                    entireProcessTree: true);

                await _relayProcess.WaitForExitAsync(
                    stopCts.Token);
            }
        }
        catch
        {
        }

        ClearRelay();

        _activeSerial = null;
        Report("Gnirehtet detenido.");
    }

    /// <summary>
    /// Inicia un proceso de Gnirehtet utilizando exclusivamente
    /// las herramientas incluidas por NOVORA.
    /// </summary>
    private Process StartGnirehtet(
        params string[] arguments)
    {
        if (!File.Exists(_paths.Gnirehtet))
        {
            throw new FileNotFoundException(
                "No se encontró gnirehtet.exe.",
                _paths.Gnirehtet);
        }

        if (!File.Exists(_paths.Adb))
        {
            throw new FileNotFoundException(
                "No se encontró adb.exe.",
                _paths.Adb);
        }

        if (!File.Exists(_paths.GnirehtetApk))
        {
            throw new FileNotFoundException(
                "No se encontró gnirehtet.apk.",
                _paths.GnirehtetApk);
        }

        var info =
            new ProcessStartInfo
            {
                FileName =
                    _paths.Gnirehtet,

                WorkingDirectory =
                    _paths.ToolsDirectory,

                UseShellExecute = false,
                CreateNoWindow = true,

                RedirectStandardOutput = true,
                RedirectStandardError = true,

                StandardOutputEncoding =
                    Encoding.UTF8,

                StandardErrorEncoding =
                    Encoding.UTF8
            };

        // Gnirehtet permite utilizar rutas personalizadas
        // mediante estas variables.
        info.Environment["ADB"] =
            _paths.Adb;

        info.Environment["GNIREHTET_APK"] =
            _paths.GnirehtetApk;

        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(
                argument);
        }

        lock (_lastOutput)
        {
            _lastOutput.Clear();
        }

        Process process =
            Process.Start(info)
            ?? throw new InvalidOperationException(
                "No fue posible iniciar Gnirehtet.");

        process.OutputDataReceived +=
            (_, e) =>
            {
                AppendOutput(
                    e.Data);
            };

        process.ErrorDataReceived +=
            (_, e) =>
            {
                AppendOutput(
                    e.Data);
            };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    /// <summary>
    /// Espera un tiempo corto y comprueba si el proceso terminó
    /// inmediatamente.
    /// </summary>
    private static async Task WaitForProcessStartupAsync(
        Process process,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        await Task.Delay(
            delay,
            cancellationToken);

        if (process.HasExited)
            return;
    }

    /// <summary>
    /// Guarda los últimos mensajes de Gnirehtet.
    /// </summary>
    private void AppendOutput(
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        lock (_lastOutput)
        {
            _lastOutput.AppendLine(
                text);

            const int maxLength = 8192;

            if (_lastOutput.Length > maxLength)
            {
                _lastOutput.Remove(
                    0,
                    _lastOutput.Length - maxLength);
            }
        }
    }

    /// <summary>
    /// Obtiene la salida reciente de Gnirehtet.
    /// </summary>
    private string GetLastOutput()
    {
        lock (_lastOutput)
        {
            return _lastOutput
                .ToString()
                .Trim();
        }
    }

    private void Report(string message)
    {
        StatusChanged?.Invoke(message);
    }

    private void ClearRelay()
    {
        try
        {
            _relayProcess?.Dispose();
        }
        catch
        {
        }

        _relayProcess = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (_relayProcess is { HasExited: false })
            {
                _relayProcess.Kill(
                    entireProcessTree: true);
            }
        }
        catch
        {
        }

        ClearRelay();

        _activeSerial = null;

        _gate.Dispose();
    }
}

/// <summary>
/// Resultado estándar de las operaciones Gnirehtet.
/// </summary>
public sealed record GnirehtetResult(
    bool Success,
    string Message)
{
    public static GnirehtetResult Ok(
        string message = "Gnirehtet activo.")
        => new(true, message);

    public static GnirehtetResult Fail(
        string message)
        => new(false, message);
}