namespace NOVORA.VisionEngine.Integration;

/// <summary>
/// Describe capacidades actualmente disponibles para la integración
/// de la ventana VisionEngine con Windows.
///
/// La ventana dedicada ya existe en Renderer/WindowRendererVE.
/// </summary>
public sealed class WindowIntegrationVE
{
    public bool DedicatedVisionWindowVE =>
        true;

    public bool FullScreenPresentationVE =>
        true;

    public bool IndependentAndroidAppWindowsVE =>
        false;
}