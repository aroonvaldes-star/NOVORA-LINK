namespace NOVORA.Models;

public sealed record MonitorInfo(
    string DeviceName,
    string FriendlyName,
    int Left,
    int Top,
    int Width,
    int Height,
    double RefreshRateHz,
    bool IsPrimary,
    int WorkingLeft = 0,
    int WorkingTop = 0,
    int WorkingWidth = 0,
    int WorkingHeight = 0)
{
    public int WindowLeft => WorkingWidth > 0 ? WorkingLeft : Left;
    public int WindowTop => WorkingHeight > 0 ? WorkingTop : Top;
    public int WindowWidth => WorkingWidth > 0 ? WorkingWidth : Width;
    public int WindowHeight => WorkingHeight > 0 ? WorkingHeight : Height;

    public string DisplayLabel
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(FriendlyName)
                ? DeviceName
                : FriendlyName.Trim();

            name = name
                .Replace(" · PRINCIPAL", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(" (Principal)", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            return $"{name} — {Width}x{Height} @ {RefreshRateHz:0.#} Hz" +
                   (IsPrimary ? " (Principal)" : string.Empty);
        }
    }

    public override string ToString() => DisplayLabel;
}
