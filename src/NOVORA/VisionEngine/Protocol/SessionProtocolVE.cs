namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// Metadatos de una sesión de video. scrcpy 4.1 puede volver a enviar
/// este header cuando cambia el tamaño del stream.
/// </summary>
public sealed record SessionProtocolVE(
    int Width,
    int Height,
    bool ClientResized)
{
    public void ValidateVE()
    {
        if (Width <= 0 || Height <= 0)
        {
            throw new InvalidDataException(
                $"Tamaño de sesión VisionEngine inválido: {Width}x{Height}.");
        }
    }
}
