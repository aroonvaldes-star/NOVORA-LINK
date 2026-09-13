namespace NOVORA.Remote;

public sealed record OptionStringRemoteNV(
    string Value,
    string Label);

public sealed record OptionIntRemoteNV(
    int Value,
    string Label);

public sealed record MonitorRemoteNV(
    string DeviceName,
    string Label,
    string IdentityDescription);

public sealed record SettingsSnapshotRemoteNV
{
    public string DeviceNativeResolution { get; init; } =
        "No detectada";

    public string DeviceMaxFps { get; init; } =
        "No detectados";

    public string DeviceMaxRefreshRate { get; init; } =
        "No detectados";

    public string Theme { get; init; } =
        "Dark";

    public string VideoPresentationMode { get; init; } =
        "Window";

    public string Bitrate { get; init; } =
        "4M";

    public int TargetFps { get; init; } =
        45;

    public int MaxSize { get; init; } =
        1280;

    public string SelectedAudioOutput { get; init; } =
        "__default__";

    public string SelectedMonitorDeviceName { get; init; } =
        string.Empty;

    public bool VisionRunning { get; init; }

    public string Notice { get; init; } =
        string.Empty;

    public OptionStringRemoteNV[] ThemeOptions { get; init; } =
        Array.Empty<OptionStringRemoteNV>();

    public OptionStringRemoteNV[] PresentationModeOptions { get; init; } =
        Array.Empty<OptionStringRemoteNV>();

    public MonitorRemoteNV[] Monitors { get; init; } =
        Array.Empty<MonitorRemoteNV>();

    public OptionStringRemoteNV[] BitrateOptions { get; init; } =
        Array.Empty<OptionStringRemoteNV>();

    public OptionIntRemoteNV[] FpsOptions { get; init; } =
        Array.Empty<OptionIntRemoteNV>();

    public OptionIntRemoteNV[] ResolutionOptions { get; init; } =
        Array.Empty<OptionIntRemoteNV>();

    public OptionStringRemoteNV[] AudioOutputOptions { get; init; } =
        Array.Empty<OptionStringRemoteNV>();
}

public sealed record SettingsUpdateRemoteNV
{
    public string Theme { get; init; } =
        "Dark";

    public string VideoPresentationMode { get; init; } =
        "Window";

    public string Bitrate { get; init; } =
        "4M";

    public int TargetFps { get; init; } =
        45;

    public int MaxSize { get; init; } =
        1280;

    public string SelectedAudioOutput { get; init; } =
        "__default__";

    public string SelectedMonitorDeviceName { get; init; } =
        string.Empty;
}
