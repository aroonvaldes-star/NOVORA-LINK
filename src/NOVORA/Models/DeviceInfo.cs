namespace NOVORA.Models;

public sealed class DeviceInfo
{
    public string Serial { get; init; } = string.Empty;
    public string Model { get; init; } = "Dispositivo no detectado";
    public string AndroidVersion { get; init; } = string.Empty;
    public string Build { get; init; } = string.Empty;
    public bool Connected { get; init; }
    public string CustomName { get; init; } = string.Empty;
    public string ConnectionType { get; init; } = "USB";
    public string DisplayLabel => $"{(string.IsNullOrWhiteSpace(CustomName) ? Model : CustomName)} • {ConnectionType}";
    public DisplayModeInfo? BestDisplayMode { get; set; }
    public IReadOnlyList<DisplayModeInfo> SupportedDisplayModes { get; set; } = Array.Empty<DisplayModeInfo>();
}
