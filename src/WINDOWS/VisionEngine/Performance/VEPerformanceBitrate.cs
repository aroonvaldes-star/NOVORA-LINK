namespace NOVORA.VisionEngine.Performance;

public sealed class VEPerformanceBitrate
{
    public VEPerformanceBitrate(
        int minBitrate = 2_000_000,
        int maxBitrate = 15_000_000,
        double decreaseFactor = 0.70,
        double increaseFactor = 1.0)
    {
        if (minBitrate < 1) throw new ArgumentOutOfRangeException(nameof(minBitrate));
        if (maxBitrate < minBitrate) throw new ArgumentOutOfRangeException(nameof(maxBitrate));
        if (decreaseFactor <= 0 || decreaseFactor >= 1) throw new ArgumentOutOfRangeException(nameof(decreaseFactor));
        if (increaseFactor < 1) throw new ArgumentOutOfRangeException(nameof(increaseFactor));

        MinBitrateVE = minBitrate;
        MaxBitrateVE = maxBitrate;
        DecreaseFactorVE = decreaseFactor;
        IncreaseFactorVE = increaseFactor;
    }

    public int MinBitrateVE { get; }
    public int MaxBitrateVE { get; }
    public double DecreaseFactorVE { get; }
    public double IncreaseFactorVE { get; }

    public int RecommendVE(int currentBitrate, VEPerformanceCongestion congestion)
    {
        int current = Math.Clamp(currentBitrate, MinBitrateVE, MaxBitrateVE);
        double factor = congestion switch
        {
            VEPerformanceCongestion.Healthy => IncreaseFactorVE,
            VEPerformanceCongestion.Mild => 1.0,
            VEPerformanceCongestion.Moderate => 0.90,
            VEPerformanceCongestion.Severe => DecreaseFactorVE,
            VEPerformanceCongestion.Critical => Math.Min(DecreaseFactorVE, 0.60),
            _ => 1.0
        };

        return Math.Clamp((int)Math.Round(current * factor), MinBitrateVE, MaxBitrateVE);
    }
}
