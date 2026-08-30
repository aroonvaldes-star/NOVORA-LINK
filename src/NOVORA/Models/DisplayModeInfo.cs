namespace NOVORA.Models;

public sealed record DisplayModeInfo(int Width, int Height, double RefreshRateHz)
{
    public int Pixels => checked(Width * Height);
    public override string ToString() => $"{Width}x{Height} @ {RefreshRateHz:0.#} Hz";
}
