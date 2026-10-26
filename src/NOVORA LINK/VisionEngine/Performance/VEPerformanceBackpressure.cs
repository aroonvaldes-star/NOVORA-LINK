namespace NOVORA.VisionEngine.Performance;

public sealed class VEPerformanceBackpressure
{
    public bool ShouldAcceptVE(
        int queueDepth,
        int capacity,
        VEPerformancePriority priority)
    {
        VEPerformanceBuffer buffer = VEPerformanceBuffer.CreateVE(capacity, queueDepth);
        if (priority == VEPerformancePriority.Critical) return true;
        if (priority == VEPerformancePriority.Realtime) return buffer.FillRatio < 0.95;
        if (priority == VEPerformancePriority.Normal) return buffer.FillRatio < 0.80;
        return buffer.FillRatio < 0.60;
    }
}
