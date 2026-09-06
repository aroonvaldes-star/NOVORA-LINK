namespace NOVORA.VisionEngine.Renderer;

public static class ViewportRendererVE
{
    public static RectRendererVE CalculateVE(
        int frameWidth,
        int frameHeight,
        int outputWidth,
        int outputHeight,
        int rotationDegrees)
    {
        int width = frameWidth;
        int height = frameHeight;

        if (RotationRendererVE.SwapsDimensionsVE(rotationDegrees))
            (width, height) = (height, width);

        return ScalingRendererVE.FitVE(width, height, outputWidth, outputHeight);
    }
}
