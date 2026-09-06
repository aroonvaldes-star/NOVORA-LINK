namespace NOVORA.VisionEngine.Performance;

public sealed class BackpressurePerformanceVE
{
    public bool ShouldAcceptVE(
        int queueDepth,
        int capacity,
        PriorityPerformanceVE priority)
    {
        BufferPerformanceVE buffer = BufferPerformanceVE.CreateVE(capacity, queueDepth);
        if (priority == PriorityPerformanceVE.Critical) return true;
        if (priority == PriorityPerformanceVE.Realtime) return buffer.FillRatio < 0.95;
        if (priority == PriorityPerformanceVE.Normal) return buffer.FillRatio < 0.80;
        return buffer.FillRatio < 0.60;
    }
}
