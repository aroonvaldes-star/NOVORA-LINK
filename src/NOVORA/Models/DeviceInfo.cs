namespace NOVORA.Models;

public sealed class DeviceInfo
{
    public string Serial { get; init; } = string.Empty;
    public string Model { get; init; } = "Dispositivo no detectado";
    public string AndroidVersion { get; init; } = string.Empty;
    public string Build { get; init; } = string.Empty;
    public bool Connected { get; init; }
    public string CustomName { get; init; } = string.Empty;

    public string FriendlyName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CustomName)) return CleanName(CustomName);
            if (!string.IsNullOrWhiteSpace(Model) && !string.Equals(Model, "Dispositivo no detectado", StringComparison.OrdinalIgnoreCase))
                return CleanName(Model);
            return Connected ? "Dispositivo Android" : "Dispositivo no detectado";
        }
    }

    public bool IsWifiConnection => !string.IsNullOrWhiteSpace(Serial) && Serial.Contains(':', StringComparison.Ordinal);
    public string ConnectionType => IsWifiConnection ? "Wi-Fi" : "USB";
    public string DisplayLabel => Connected ? $"{FriendlyName} • {ConnectionType}" : $"{FriendlyName} • No disponible";

    public DisplayModeInfo? BestDisplayMode { get; set; }
    public IReadOnlyList<DisplayModeInfo> SupportedDisplayModes { get; set; } = Array.Empty<DisplayModeInfo>();
    public DeviceCapabilities Capabilities { get; init; } = DeviceCapabilities.Unknown;

    public override string ToString() => DisplayLabel;

    private static string CleanName(string value) => value.Trim().Replace('_', ' ');
}
