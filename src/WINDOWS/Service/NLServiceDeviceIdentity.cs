using NOVORA.Model;

namespace NOVORA.Service;

public sealed class NLServiceDeviceIdentity
{
    private readonly NLServiceADB _adb;
    private readonly NLServiceSettings _settings;

    public NLServiceDeviceIdentity(
        NLServiceADB adb,
        NLServiceSettings settings)
    {
        _adb = adb ??
            throw new ArgumentNullException(nameof(adb));

        _settings = settings ??
            throw new ArgumentNullException(nameof(settings));
    }

    public async Task<IReadOnlyList<NLModelDeviceInfo>> GetDevicesAsync(
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
        NLModelDeviceInfo? device)
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
