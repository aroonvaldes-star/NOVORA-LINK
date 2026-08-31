using NOVORA.Models;

namespace NOVORA.Services;

/// <summary>Recuperación coordinada; nunca crea procesos Gnirehtet directamente.</summary>
public sealed class GnirehtetRecoveryService
{
    private readonly AdbService _adb;
    private readonly GnirehtetService _gnirehtet;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GnirehtetRecoveryService(AdbService adb, GnirehtetService gnirehtet)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _gnirehtet = gnirehtet ?? throw new ArgumentNullException(nameof(gnirehtet));
    }

    public async Task<bool> RecoverAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;
        if (!await _gate.WaitAsync(0, cancellationToken)) return false;
        try
        {
            var serial = deviceId.Trim();
            if (!await _adb.IsDeviceOnlineAsync(serial, cancellationToken)) return false;
            if (await _adb.PingAsync(serial, cancellationToken: cancellationToken)) return true;

            GnirehtetResult result;
            if ((_gnirehtet.IsActive || _gnirehtet.IsRelayActive) &&
                (string.IsNullOrWhiteSpace(_gnirehtet.ActiveSerial) || string.Equals(_gnirehtet.ActiveSerial, serial, StringComparison.OrdinalIgnoreCase)))
            {
                result = await _gnirehtet.ResetTunnelAsync(serial, cancellationToken);
            }
            else
            {
                var devices = await _adb.GetDevicesAsync(cancellationToken, forceRefresh: true);
                var device = devices.FirstOrDefault(item => item.Connected && string.Equals(item.Serial, serial, StringComparison.OrdinalIgnoreCase));
                if (device is null) return false;
                result = await _gnirehtet.StartAsync(device, devices.Count, cancellationToken);
            }

            if (!result.Success) return false;
            await Task.Delay(1200, cancellationToken);
            return await _adb.PingAsync(serial, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
        finally { _gate.Release(); }
    }
}
