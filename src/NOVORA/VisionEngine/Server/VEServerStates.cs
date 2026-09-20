namespace NOVORA.VisionEngine.Server;

/// <summary>
/// Estado del servidor Android utilizado por VisionEngine.
/// </summary>
public enum VEServerStates
{
    Stopped = 0,
    Deploying = 1,
    Starting = 2,
    Running = 3,
    Failed = 4
}
