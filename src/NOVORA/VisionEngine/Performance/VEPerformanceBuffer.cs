namespace NOVORA.VisionEngine.Performance;

public sealed record VEPerformanceBuffer(
    int Capacity,
    int Count,
    double FillRatio,
    bool IsBackpressured)
{
    public static VEPerformanceBuffer CreateVE(int capacity, int count)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        int boundedCount = Math.Min(count, capacity);
        double ratio = boundedCount / (double)capacity;
        return new(capacity, boundedCount, ratio, ratio >= 0.80);
    }
}
