namespace NOVORA.VisionEngine.Renderer;

public static class VERendererScaling
{
    public static VERendererRect FitVE(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        if (sourceWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (sourceHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        if (targetWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWidth));
        if (targetHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetHeight));

        double scale = Math.Min(
            targetWidth / (double)sourceWidth,
            targetHeight / (double)sourceHeight);

        float width = (float)(sourceWidth * scale);
        float height = (float)(sourceHeight * scale);
        float x = (targetWidth - width) / 2f;
        float y = (targetHeight - height) / 2f;

        return new VERendererRect(x, y, width, height);
    }
}
