namespace NOVORA.VisionEngine.Gamepad;

public readonly record struct VEGamepadEvent(
    VEGamepadTypeEvent Type,
    uint InstanceId,
    byte Axis,
    short AxisValue,
    byte Button,
    bool Pressed);
