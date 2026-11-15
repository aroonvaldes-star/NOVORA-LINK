namespace NOVORA.VisionEngine.Renderer;

public static class VERendererRotation
{
    public static int NormalizeVE(int degrees)
    {
        int normalized = ((degrees % 360) + 360) % 360;

        return normalized switch
        {
            0 or 90 or 180 or 270 => normalized,
            _ => throw new ArgumentOutOfRangeException(
                nameof(degrees),
                "VisionEngine sólo admite rotaciones de 0, 90, 180 o 270 grados.")
        };
    }

    public static bool SwapsDimensionsVE(int degrees)
    {
        int normalized = NormalizeVE(degrees);
        return normalized is 90 or 270;
    }

    public static VERendererRect PrepareDestinationVE(
        VERendererRect logicalDestination,
        int degrees)
    {
        if (!SwapsDimensionsVE(degrees))
            return logicalDestination;

        float centerX = logicalDestination.X + logicalDestination.Width / 2f;
        float centerY = logicalDestination.Y + logicalDestination.Height / 2f;

        return new VERendererRect(
            centerX - logicalDestination.Height / 2f,
            centerY - logicalDestination.Width / 2f,
            logicalDestination.Height,
            logicalDestination.Width);
    }
}
