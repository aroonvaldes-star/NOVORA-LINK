namespace NOVORA.VisionEngine.Transport;

/// <summary>
/// Dirección del túnel ADB usado para el canal multimedia.
/// Reverse es preferido; Forward es fallback para dispositivos/ADB que no soporten reverse.
/// </summary>
public enum VETransportMode
{
    Reverse = 0,
    Forward = 1
}
