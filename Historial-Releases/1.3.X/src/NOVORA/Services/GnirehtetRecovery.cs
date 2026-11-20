using System.Diagnostics;
using System.IO;

namespace NOVORA.Services;

public sealed class GnirehtetRecoveryService
{
    private readonly string _adbPath;
    private readonly string _gnirehtetPath;

    public event Action<string>? StatusChanged;

    public GnirehtetRecoveryService(
        string adbPath,
        string gnirehtetPath)
    {
        _adbPath = adbPath;
        _gnirehtetPath = gnirehtetPath;
    }

    public async Task<bool> RecoverAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            Report("Recovery cancelado: dispositivo no válido.");
            return false;
        }

        try
        {
            Report("Iniciando recovery de Gnirehtet...");

            Report("Comprobando conexión ADB...");

            var adbOk = await CheckAdbAsync(
                deviceId,
                cancellationToken);

            if (!adbOk)
            {
                Report("ADB no responde.");
                return false;
            }

            Report("ADB conectado.");

            await Task.Delay(
                TimeSpan.FromMilliseconds(500),
                cancellationToken);

            Report("Comprobando conexión de red...");

            var networkOk = await CheckNetworkAsync(
                deviceId,
                cancellationToken);

            if (networkOk)
            {
                Report("La conexión de red está activa.");
                return true;
            }

            Report("La conexión de red no responde.");

            Report("Deteniendo proceso local de Gnirehtet...");

            StopLocalGnirehtet();

            await Task.Delay(
                TimeSpan.FromSeconds(1),
                cancellationToken);

            Report("Reiniciando Gnirehtet...");

            var started = StartLocalGnirehtet(
                deviceId);

            if (!started)
            {
                Report("No se pudo iniciar Gnirehtet.");
                return false;
            }

            await Task.Delay(
                TimeSpan.FromSeconds(2),
                cancellationToken);

            Report("Verificando conexión después del recovery...");

            var recovered = await CheckNetworkAsync(
                deviceId,
                cancellationToken);

            if (recovered)
            {
                Report("Recovery completado correctamente.");
                return true;
            }

            Report("Recovery finalizado sin conexión.");
            return false;
        }
        catch (OperationCanceledException)
        {
            Report("Recovery cancelado.");
            return false;
        }
        catch (Exception ex)
        {
            Report($"Error durante recovery: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> CheckAdbAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            _adbPath,
            $"-s \"{deviceId}\" get-state",
            cancellationToken);

        return result.ExitCode == 0 &&
               result.Output.Contains(
                   "device",
                   StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> CheckNetworkAsync(
    string deviceId,
    CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunProcessAsync(
                _adbPath,
                $"-s \"{deviceId}\" shell ping -c 1 -W 2 1.1.1.1",
                cancellationToken);

            if (result.ExitCode == 0)
                return true;

            Report("Ping sin respuesta. Se continuará con el recovery.");
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Report($"No se pudo comprobar Internet: {ex.Message}");
            return false;
        }
    }

    private void StopLocalGnirehtet()
    {
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.ProcessName.Contains(
                        "gnirehtet",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                    }
                }
                catch
                {
                    // Ignorar procesos que no puedan detenerse.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // No bloquear el recovery si el proceso ya terminó.
        }
    }

    private bool StartLocalGnirehtet(string deviceId)
    {
        if (!File.Exists(_gnirehtetPath))
        {
            Report("No se encontró Gnirehtet.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _gnirehtetPath,
                Arguments = $"run \"{deviceId}\"",
                WorkingDirectory =
                    Path.GetDirectoryName(_gnirehtetPath)
                    ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(startInfo);

            return process is not null;
        }
        catch (Exception ex)
        {
            Report($"No se pudo iniciar Gnirehtet: {ex.Message}");
            return false;
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            return new ProcessResult(
                -1,
                string.Empty,
                "No se pudo iniciar el proceso.");
        }

        // CAMBIO AQUÍ: Leer stdout y stderr en paralelo con WaitForExitAsync
        // para evitar deadlock por llenado de búfer.
        var outputTask =
            process.StandardOutput.ReadToEndAsync(
                cancellationToken);

        var errorTask =
            process.StandardError.ReadToEndAsync(
                cancellationToken);

        await Task.WhenAll(
            process.WaitForExitAsync(
                cancellationToken),
            outputTask,
            errorTask);

        var output = await outputTask;
        var error = await errorTask;

        return new ProcessResult(
            process.ExitCode,
            output,
            error);
    }

    private void Report(string message)
    {
        StatusChanged?.Invoke(message);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string Error);
}