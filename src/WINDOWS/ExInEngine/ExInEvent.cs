namespace NOVORA.ExInEngine;

public readonly record struct ExInEvent(
    ExInTypeEvent Type,
    uint InstanceId,
    byte Axis,
    short AxisValue,
    byte Button,
    bool Pressed,
    ExInBatteryState BatteryState = ExInBatteryState.Unknown,
    int BatteryPercent = -1,
    int Touchpad = -1,
    long Finger = -1,
    float TouchX = 0,
    float TouchY = 0,
    float Pressure = 0);
