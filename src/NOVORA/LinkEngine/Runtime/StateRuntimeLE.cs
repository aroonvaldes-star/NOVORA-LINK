namespace NOVORA.LinkEngine.Runtime;

/// <summary>
/// Estado general del runtime persistente de LinkEngine.
/// </summary>
public enum StateRuntimeLE
{
    Stopped,
    Starting,
    ConnectingDevice,
    OpeningTransport,
    WaitingForAndroid,
    StartingRecoveryMonitor,
    Running,
    Degraded,
    Stopping,
    Failed
}
