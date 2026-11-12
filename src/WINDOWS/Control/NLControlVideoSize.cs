namespace NOVORA.Control;

public readonly record struct NLControlVideoSize(int Width, int Height)
{
    public static NLControlVideoSize Fit(int sourceWidth, int sourceHeight, int maxSize)
    {
        if (sourceWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (sourceHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        if (maxSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSize));

        double scale = Math.Min(1d, maxSize / (double)Math.Max(sourceWidth, sourceHeight));
        int width = Math.Max(2, ((int)(sourceWidth * scale)) & ~1);
        int height = Math.Max(2, ((int)(sourceHeight * scale)) & ~1);
        return new NLControlVideoSize(width, height);
    }
}
