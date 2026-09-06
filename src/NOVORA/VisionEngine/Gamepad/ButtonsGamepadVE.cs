namespace NOVORA.VisionEngine.Gamepad;

[Flags]
public enum ButtonsGamepadVE : uint
{
    None = 0,
    South = 0x0001,
    East = 0x0002,
    West = 0x0008,
    North = 0x0010,
    LeftShoulder = 0x0040,
    RightShoulder = 0x0080,
    Back = 0x0400,
    Start = 0x0800,
    Guide = 0x1000,
    LeftStick = 0x2000,
    RightStick = 0x4000,
    DPadUp = 0x0001_0000,
    DPadDown = 0x0002_0000,
    DPadLeft = 0x0004_0000,
    DPadRight = 0x0008_0000
}
