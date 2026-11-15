namespace NOVORA.VisionEngine.Control;

public readonly record struct VEControlPosition(
    int X,
    int Y,
    ushort ScreenWidth,
    ushort ScreenHeight)
{
    public void ValidateVE()
    {
        if (X < 0 || Y < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(X),
                "Las coordenadas VisionEngine no pueden ser negativas.");
        }

        if (ScreenWidth == 0 || ScreenHeight == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ScreenWidth),
                "El tamaño de pantalla VisionEngine debe ser mayor que cero.");
        }
    }
}
