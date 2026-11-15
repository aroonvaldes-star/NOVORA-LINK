namespace NOVORA.VisionEngine.Integration;

/// <summary>
/// Describe capacidades actualmente disponibles para la integración
/// de la ventana VisionEngine con Windows.
///
/// La ventana dedicada ya existe en Renderer/VERendererWindow.
/// </summary>
public sealed class VEIntegrationWindow
{
    public bool DedicatedVisionWindowVE =>
        true;

    public bool FullScreenPresentationVE =>
        true;

    public bool IndependentAndroidAppWindowsVE =>
        false;
}