namespace NOVORA;

/// <summary>Calculates page scrolling without trapping a precision touchpad at either edge.</summary>
public static class NLScrollWheelRouter
{
    private const double PixelsPerWheelNotch = 48d;

    public static bool TryGetNextVerticalOffset(
        double currentOffset,
        double scrollableHeight,
        int wheelDelta,
        out double nextOffset)
    {
        nextOffset = Math.Clamp(
            currentOffset - wheelDelta / 120d * PixelsPerWheelNotch,
            0d,
            Math.Max(0d, scrollableHeight));
        return Math.Abs(nextOffset - currentOffset) > double.Epsilon;
    }
}
