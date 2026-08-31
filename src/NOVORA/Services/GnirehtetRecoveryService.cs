using NOVORA.Models;

namespace NOVORA.Services;

/// <summary>
/// Recuperación coordinada de Gnirehtet.
/// Nunca crea/mata procesos gnirehtet.exe directamente: toda operación pasa por GnirehtetService.
/// </summary>
public sealed class GnirehtetRecoveryService
{
    private readonly AdbService _adb;
    private readonly GnirehtetService _gnirehtet;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event Action<string>? StatusChanged;

    public GnirehtetRecoveryService(AdbService adb, GnirehtetService gnirehtet)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _gnirehtet = gnirehtet ?? throw new ArgumentNullException(nameof(gnirehtet));
    }

    public async Task<bool> RecoverAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            Report("Recovery cancelado: dispositivo no válido.");
            return false;
        }

        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            Report("Recovery omitido: ya hay una recuperación en curso.");
            return false;
        }

        try
        {
            string serial = deviceId.Trim();
            Report("Iniciando recovery de Gnirehtet...");

            if (!await _adb.IsDeviceOnlineAsync(serial, cancellationToken))
            {
                Report("Recovery detenido: ADB no responde.");
                return false;
            }

            if (await _adb.PingAsync(serial, cancellationToken: cancellationToken))
            {
                Report("Internet ya responde; recovery no necesario.");
                return true;
            }

            GnirehtetResult result;

            if (_gnirehtet.IsActive || _gnirehtet.IsRelayActive)
            {
                Report("Restableciendo túnel Gnirehtet...");
                result = await _gnirehtet.ResetTunnelAsync(serial, cancellationToken);
            }
            else
            {
                Report("La sesión Gnirehtet se perdió; reconstruyendo sesión completa...");

                var devices = await _adb.GetDevicesAsync(cancellationToken, forceRefresh: true);
                var device = devices.FirstOrDefault(d =>
                    d.Connected &&
                    string.Equals(d.Serial, serial, StringComparison.OrdinalIgnoreCase));

                if (device is null)
                {
                    Report("No se encontró el dispositivo en la lista ADB.");
                    return false;
                }

                result = await _gnirehtet.StartAsync(device, devices.Count, cancellationToken);
            }

            if (!result.Success)
            {
                Report($"Recovery falló: {result.Message}");
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken);

            bool recovered = await _adb.PingAsync(
                serial,
                cancellationToken: cancellationToken);

            Report(recovered
                ? "Recovery completado correctamente."
                : "Recovery terminó, pero Internet todavía no responde.");

            return recovered;
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
        finally
        {
            _gate.Release();
        }
    }

    private void Report(string message) => StatusChanged?.Invoke(message);
}
