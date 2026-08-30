using NOVORA.Models;

namespace NOVORA.Services;

public sealed class DeviceIdentityService
{
    private readonly AdbService _adb;
    private readonly SettingsService _settings;

    public DeviceIdentityService(AdbService adb, SettingsService settings)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var devices = await _adb.GetDevicesAsync(cancellationToken, force);
        return devices.Select(device => new DeviceInfo
        {
            Serial = device.Serial,
            Model = device.Model,
            AndroidVersion = device.AndroidVersion,
            Build = device.Build,
            Connected = device.Connected,
            CustomName = _settings.GetDeviceName(device.Serial) ?? device.CustomName,
            ConnectionType = device.Serial.Contains(':') ? "Wi-Fi" : "USB",
            BestDisplayMode = device.BestDisplayMode,
            SupportedDisplayModes = device.SupportedDisplayModes
        }).ToArray();
    }

    public string GetDisplayName(DeviceInfo? device)
    {
        if (device is null) return "Dispositivo no detectado";
        var custom = _settings.GetDeviceName(device.Serial);
        if (!string.IsNullOrWhiteSpace(custom)) return custom;
        if (!string.IsNullOrWhiteSpace(device.CustomName)) return device.CustomName;
        return string.IsNullOrWhiteSpace(device.Model) ? "Dispositivo Android" : device.Model;
    }
}
