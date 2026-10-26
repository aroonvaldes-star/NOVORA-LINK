namespace NOVORA.LinkEngine.Transport;

public enum LETransportState
{
    Closed = 0,
    Preparing = 1,
    ReverseConfigured = 2,
    Listening = 3,
    Ready = 4,
    Connected = 5,
    Degraded = 6,
    Failed = 7,
    Closing = 8
}