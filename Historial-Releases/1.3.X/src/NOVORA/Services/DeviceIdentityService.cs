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

    public string GetDisplayName(
        DeviceInfo? device)
    {
        if (device is null)
            return "Dispositivo no detectado";

        if (!string.IsNullOrWhiteSpace(device.Serial))
        {
            var customName =
                _settings.GetDeviceName(
                    device.Serial);

            if (!string.IsNullOrWhiteSpace(customName))
                return customName;
        }

        if (!string.IsNullOrWhiteSpace(device.CustomName))
            return device.CustomName;

        if (!string.IsNullOrWhiteSpace(device.Model))
            return device.Model;

        return string.IsNullOrWhiteSpace(device.Serial)
            ? "Dispositivo no detectado"
            : device.Serial;
    }
}