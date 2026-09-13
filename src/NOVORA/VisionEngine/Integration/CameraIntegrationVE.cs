namespace NOVORA.VisionEngine.Integration;

/// <summary>
/// Estado de integración de cámara.
///
/// El backend upstream puede ofrecer camera source, pero el contrato
/// OptionsServerVE actual de NOVORA todavía no expone video_source.
///
/// Por eso NO se anuncia como implementado.
/// </summary>
public sealed class CameraIntegrationVE
{
    public bool AndroidCameraSourceConfiguredVE =>
        false;

    public bool WindowsVirtualCameraVE =>
        false;
}