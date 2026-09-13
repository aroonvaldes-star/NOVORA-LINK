namespace NOVORA.VisionEngine.Gamepad;

public readonly record struct EventGamepadVE(
    TypeEventGamepadVE Type,
    uint InstanceId,
    byte Axis,
    short AxisValue,
    byte Button,
    bool Pressed);
