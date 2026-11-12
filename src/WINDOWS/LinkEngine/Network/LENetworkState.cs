namespace NOVORA.LinkEngine.Network;

public enum LENetworkState
{
    Stopped = 0,
    Starting = 1,
    RelayReady = 2,
    ReverseReady = 3,
    Online = 4,
    Degraded = 5,
    Failed = 6,
    Stopping = 7
}