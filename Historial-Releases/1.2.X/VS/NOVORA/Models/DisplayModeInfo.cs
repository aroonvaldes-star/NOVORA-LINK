namespace NOVORA.Models;
public sealed record DisplayModeInfo(int Width, int Height, double RefreshRateHz) { public long Pixels => (long)Width * Height; }
