namespace NOVORA.Models;

/// <summary>Capacidades físicas detectadas del panel Android.</summary>
public sealed record DeviceCapabilities
{
    public static DeviceCapabilities Unknown { get; } = new();

    public int NativeWidth { get; init; }
    public int NativeHeight { get; init; }
    public IReadOnlyList<double> SupportedRefreshRatesHz { get; init; } = Array.Empty<double>();

    public bool IsDetected => NativeWidth > 0 && NativeHeight > 0;
    public int MaxDimension => Math.Max(NativeWidth, NativeHeight);
    public double MaxRefreshRateHz => SupportedRefreshRatesHz.Count == 0 ? 60d : SupportedRefreshRatesHz.Max();
    public int MaxSelectableFps => Math.Max(15, (int)Math.Round(MaxRefreshRateHz, MidpointRounding.AwayFromZero));
    public string NativeResolutionLabel => IsDetected ? $"{NativeWidth}x{NativeHeight}" : "No detectada";
    public string RefreshRatesLabel => SupportedRefreshRatesHz.Count == 0
        ? "No detectadas"
        : string.Join(", ", SupportedRefreshRatesHz.OrderBy(value => value).Select(value => $"{value:0.#} Hz"));
}
