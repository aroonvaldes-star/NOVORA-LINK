using NOVORA.Models;

namespace NOVORA.Services;

public sealed class DeviceIdentityService
{
    private readonly AdbService _adb;
    private readonly SettingsService _settings;

    public DeviceIdentityService(
        AdbService adb,
        SettingsService settings)
    {
        _adb = adb ??
            throw new ArgumentNullException(nameof(adb));

        _settings = settings ??
            throw new ArgumentNullException(nameof(settings));
    }

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        return await _adb.GetDevicesAsync(
            cancellationToken,
            force);
    }

    /// <summary>
    /// Devuelve el nombre que NOVORA presenta al usuario.
    /// No expone serial ni IP y agrega USB/Wi-Fi en la misma instancia.
    /// </summary>
    public string GetDisplayName(
        DeviceInfo? device)
    {
        if (device is null)
            return "Dispositivo no detectado";

        string? customName = null;

        if (!string.IsNullOrWhiteSpace(device.Serial))
        {
            customName =
                _settings.GetDeviceName(
                    device.Serial);
        }

        var visibleName =
            !string.IsNullOrWhiteSpace(customName)
                ? customName.Trim().Replace('_', ' ')
                : device.FriendlyName;

        if (!device.Connected)
            return $"{visibleName} • No disponible";

        return $"{visibleName} • {device.ConnectionType}";
    }
}
