namespace NOVORA.VisionEngine.Video;

/// <summary>Hard bound on retained encoded packets; the recorder never blocks the decoder for disk I/O.</summary>
internal sealed class VEMediaPacketBudget
{
    internal const int MaximumBytes = 32 * 1024 * 1024;
    internal const int MaximumPacketBytes = 8 * 1024 * 1024;
    private int _bytes;
    public bool TryReserve(int bytes)
    {
        if (bytes <= 0 || bytes > MaximumPacketBytes) return false;
        while (true)
        {
            int current = Volatile.Read(ref _bytes);
            if (bytes > MaximumBytes - current) return false;
            if (Interlocked.CompareExchange(ref _bytes, current + bytes, current) == current) return true;
        }
    }
    public void Release(int bytes) => Interlocked.Add(ref _bytes, -bytes);
}
