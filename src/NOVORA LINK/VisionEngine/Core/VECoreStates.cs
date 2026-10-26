namespace NOVORA.VisionEngine.Core;

/// <summary>
/// Estado global del ciclo de vida de VisionEngine.
///
/// Esta fase es deliberadamente headless: Running significa que la sesión
/// lógica del motor está activa, no que exista un renderer o una imagen.
/// </summary>
public enum VECoreStates
{
    Stopped = 0,
    Initializing = 1,
    Ready = 2,
    Starting = 3,
    Running = 4,
    Stopping = 5,
    Failed = 6,
    Disposed = 7
}
