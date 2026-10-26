namespace NOVORA;

/// <summary>
/// Cerrojo compartido para las llamadas a SDL3 del proceso. ExIn lee eventos en un hilo de fondo
/// mientras el renderer de VisionEngine destruye la ventana y cierra el subsistema de vídeo en la UI.
/// Ambos usan la misma instancia de SDL3.dll; sin este cerrojo, la lectura de eventos puede tocar
/// memoria que el cierre del vídeo ya liberó (corrupción de heap observada el 2026-10-17).
/// </summary>
internal static class SdlSharedGate
{
    public static readonly object Gate = new();
}
