namespace NOVORA.Launcher.Models;

public sealed class DeviceInfo
{
    public string Serial { get; init; } = string.Empty;
    public string Model { get; init; } = "Dispositivo no detectado";
    public string AndroidVersion { get; init; } = string.Empty;
    public string Build { get; init; } = string.Empty;
    public bool Connected { get; init; }
    public string DisplayLabel => $"{Model} {(Connected ? "• Conectado" : "• No disponible")} [{Serial}]";
    public DisplayModeInfo? BestDisplayMode { get; set; }
    public IReadOnlyList<DisplayModeInfo> SupportedDisplayModes { get; set; } = Array.Empty<DisplayModeInfo>();
}
