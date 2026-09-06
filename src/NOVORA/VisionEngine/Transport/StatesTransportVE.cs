namespace NOVORA.VisionEngine.Transport;

/// <summary>
/// Estado del transporte VisionEngine.
/// </summary>
public enum StatesTransportVE
{
    Stopped = 0,
    Preparing = 1,
    Listening = 2,
    Connecting = 3,
    Connected = 4,
    Failed = 5
}
