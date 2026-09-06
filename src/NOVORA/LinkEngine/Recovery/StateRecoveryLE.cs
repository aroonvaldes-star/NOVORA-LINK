namespace NOVORA.LinkEngine.Recovery;

public enum StateRecoveryLE
{
    Idle = 0,

    Monitoring = 1,

    Degraded = 2,

    Reconnecting = 3,

    RebuildingReverse = 4,

    WaitingForClient = 5,

    Recovering = 6,

    Healthy = 7,

    Failed = 8,

    Stopped = 9
}
