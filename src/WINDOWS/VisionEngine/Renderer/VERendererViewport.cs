namespace NOVORA.VisionEngine.Renderer;

public static class VERendererViewport
{
    public static VERendererRect CalculateVE(
        int frameWidth,
        int frameHeight,
        int outputWidth,
        int outputHeight,
        int rotationDegrees)
    {
        int width = frameWidth;
        int height = frameHeight;

        if (VERendererRotation.SwapsDimensionsVE(rotationDegrees))
            (width, height) = (height, width);

        return VERendererScaling.FitVE(width, height, outputWidth, outputHeight);
    }
}
