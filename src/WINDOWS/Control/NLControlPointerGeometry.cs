namespace NOVORA.Control;

public readonly record struct NLControlPointerPosition(
    int X,
    int Y,
    ushort ScreenWidth,
    ushort ScreenHeight);

public static class NLControlPointerGeometry
{
    public static bool TryMap(
        int clientX,
        int clientY,
        int outputWidth,
        int outputHeight,
        int frameWidth,
        int frameHeight,
        int rotationDegrees,
        bool clampToViewport,
        int verticalEdgeActivationPixels,
        out NLControlPointerPosition position)
    {
        position = default;
        if (outputWidth <= 0 || outputHeight <= 0 ||
            frameWidth <= 0 || frameHeight <= 0 ||
            frameWidth > ushort.MaxValue || frameHeight > ushort.MaxValue)
            return false;

        int rotation = NormalizeRotation(rotationDegrees);
        int visibleWidth = rotation is 90 or 270 ? frameHeight : frameWidth;
        int visibleHeight = rotation is 90 or 270 ? frameWidth : frameHeight;
        double scale = Math.Min(
            outputWidth / (double)visibleWidth,
            outputHeight / (double)visibleHeight);
        if (scale <= 0)
            return false;

        int viewportWidth = Math.Max(1, (int)Math.Round(visibleWidth * scale));
        int viewportHeight = Math.Max(1, (int)Math.Round(visibleHeight * scale));
        int viewportX = (outputWidth - viewportWidth) / 2;
        int viewportY = (outputHeight - viewportHeight) / 2;
        int viewportRight = viewportX + viewportWidth - 1;
        int viewportBottom = viewportY + viewportHeight - 1;

        bool inside = clientX >= viewportX && clientX <= viewportRight &&
            clientY >= viewportY && clientY <= viewportBottom;
        bool activatesVerticalEdge = verticalEdgeActivationPixels > 0 &&
            clientX >= viewportX && clientX <= viewportRight &&
            clientY >= viewportY - verticalEdgeActivationPixels &&
            clientY <= viewportBottom + verticalEdgeActivationPixels;
        if (!inside && !activatesVerticalEdge && !clampToViewport)
            return false;

        double normalizedX = Math.Clamp(
            (Math.Clamp(clientX, viewportX, viewportRight) - viewportX) /
            Math.Max(1d, viewportWidth - 1d), 0d, 1d);
        double normalizedY = Math.Clamp(
            (Math.Clamp(clientY, viewportY, viewportBottom) - viewportY) /
            Math.Max(1d, viewportHeight - 1d), 0d, 1d);
        int rotatedX = ScaleCoordinate(normalizedX, visibleWidth);
        int rotatedY = ScaleCoordinate(normalizedY, visibleHeight);

        position = new NLControlPointerPosition(
            rotatedX,
            rotatedY,
            checked((ushort)visibleWidth),
            checked((ushort)visibleHeight));
        return true;
    }

    public static bool MatchesCurrentOrientation(
        ushort commandWidth,
        ushort commandHeight,
        int currentWidth,
        int currentHeight)
    {
        if (commandWidth == 0 || commandHeight == 0 ||
            currentWidth <= 0 || currentHeight <= 0)
            return false;

        return (commandWidth >= commandHeight) == (currentWidth >= currentHeight);
    }

    public static (float X, float Y) MapToCurrentOrientation(
        float x,
        float y,
        ushort commandWidth,
        ushort commandHeight,
        int currentWidth,
        int currentHeight)
    {
        if (commandWidth == 0 || commandHeight == 0 || currentWidth <= 0 || currentHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(commandWidth));

        float nx = Math.Clamp(x / Math.Max(1f, commandWidth - 1), 0f, 1f);
        float ny = Math.Clamp(y / Math.Max(1f, commandHeight - 1), 0f, 1f);
        bool sameOrientation = MatchesCurrentOrientation(commandWidth, commandHeight, currentWidth, currentHeight);
        float currentX = sameOrientation ? nx : 1f - ny;
        float currentY = sameOrientation ? ny : nx;
        return (currentX * Math.Max(0, currentWidth - 1), currentY * Math.Max(0, currentHeight - 1));
    }

    private static int ScaleCoordinate(double normalized, int length) =>
        Math.Clamp(
            (int)Math.Round(normalized * Math.Max(0, length - 1),
                MidpointRounding.AwayFromZero),
            0,
            Math.Max(0, length - 1));

    private static int NormalizeRotation(int rotationDegrees)
    {
        int normalized = ((rotationDegrees % 360) + 360) % 360;
        return normalized switch
        {
            >= 45 and < 135 => 90,
            >= 135 and < 225 => 180,
            >= 225 and < 315 => 270,
            _ => 0
        };
    }
}
