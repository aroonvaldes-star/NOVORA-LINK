namespace NOVORA.Models;

/// <summary>
/// Resultado general de compatibilidad de una capacidad o motor
/// de NOVORA con un dispositivo Android.
/// </summary>
public enum CompatibilityLevelDevice
{
    /// <summary>
    /// Todavía no existe información suficiente para determinar
    /// compatibilidad.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Falta al menos un requisito obligatorio.
    /// </summary>
    Unsupported = 1,

    /// <summary>
    /// El dispositivo puede utilizar el motor, pero alguna capacidad
    /// opcional o avanzada no está disponible.
    /// </summary>
    Partial = 2,

    /// <summary>
    /// Todas las capacidades actualmente requeridas fueron
    /// detectadas correctamente.
    /// </summary>
    Full = 3
}
