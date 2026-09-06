namespace NOVORA.LinkEngine.Android.LinkEngine;

public enum NetworkStateLE
{
    Stopped = 0,
    RequestingPermission = 1,
    StartingControl = 2,
    WaitingControl = 3,
    EstablishingVpn = 4,
    ConnectingData = 5,
    Online = 6,
    Degraded = 7,
    Failed = 8,
    Stopping = 9
}