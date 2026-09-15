namespace NOVORA.VisionEngine.Integration;

/// <summary>
/// Estado de integración de cámara.
///
/// El backend upstream puede ofrecer camera source, pero el contrato
/// VEServerOptions actual de NOVORA todavía no expone video_source.
///
/// Por eso NO se anuncia como implementado.
/// </summary>
public sealed class VEIntegrationCamera
{
    public bool AndroidCameraSourceConfiguredVE =>
        false;

    public bool WindowsVirtualCameraVE =>
        false;
}