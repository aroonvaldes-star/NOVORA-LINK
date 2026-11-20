namespace NOVORA.Models;
public sealed class DeviceInfo
{
    public string Serial { get; init; } = "";
    public bool Connected { get; init; }
    public IReadOnlyList<DisplayModeInfo> SupportedDisplayModes { get; init; } = Array.Empty<DisplayModeInfo>();
    public DisplayModeInfo? BestDisplayMode => SupportedDisplayModes.OrderByDescending(x => x.Pixels).ThenByDescending(x => x.RefreshRateHz).FirstOrDefault();
}
