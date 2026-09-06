namespace NOVORA.LinkEngine.Android.LinkEngine;

public enum StateTransportLE
{
    Stopped = 0,
    Starting = 1,
    Connecting = 2,
    Handshaking = 3,
    Connected = 4,
    Reconnecting = 5,
    Failed = 6,
    Stopping = 7
}