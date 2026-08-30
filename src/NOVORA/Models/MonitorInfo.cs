namespace NOVORA.Models;

public sealed record MonitorInfo(
    string DeviceName,
    string FriendlyName,
    int Left,
    int Top,
    int Width,
    int Height,
    double RefreshRateHz,
    bool IsPrimary)
{
    public string DisplayLabel => $"{FriendlyName} — {Width}x{Height} @ {RefreshRateHz:0.#} Hz" + (IsPrimary ? " (Principal)" : string.Empty);
}