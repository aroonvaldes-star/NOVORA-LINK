namespace NOVORA.VisionEngine.Device;

/// <summary>
/// Estado del dispositivo dentro del ciclo de vida headless de VisionEngine.
/// </summary>
public enum VEDeviceStates
{
    Disconnected = 0,
    Checking = 1,
    Ready = 2,
    Failed = 3
}
