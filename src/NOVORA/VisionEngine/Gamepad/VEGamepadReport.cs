using System.Buffers.Binary;

namespace NOVORA.VisionEngine.Gamepad;

/// <summary>
/// Genera el report UHID de 15 bytes usado por VisionEngine/scrcpy.
/// </summary>
public static class VEGamepadReport
{
    public const int ReportSizeVE = 15;

    public static byte[] BuildVE(VEGamepadState state)
    {
        byte[] data = new byte[ReportSizeVE];

        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(0, 2),
            RescaleAxisVE(state.LeftX));

        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(2, 2),
            RescaleAxisVE(state.LeftY));

        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(4, 2),
            RescaleAxisVE(state.RightX));

        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(6, 2),
            RescaleAxisVE(state.RightY));

        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(8, 2),
            NormalizeTriggerVE(state.LeftTrigger));

        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(10, 2),
            NormalizeTriggerVE(state.RightTrigger));

        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(12, 2),
            (ushort)((uint)state.Buttons & 0xFFFF));

        data[14] = GetDPadVE(state.Buttons);

        return data;
    }

    private static ushort RescaleAxisVE(short value)
        => unchecked((ushort)((int)value + 0x8000));

    private static ushort NormalizeTriggerVE(short value)
        => value <= 0
            ? (ushort)0
            : checked((ushort)value);

    private static byte GetDPadVE(VEGamepadButtons buttons)
    {
        bool up = buttons.HasFlag(VEGamepadButtons.DPadUp);
        bool down = buttons.HasFlag(VEGamepadButtons.DPadDown);
        bool left = buttons.HasFlag(VEGamepadButtons.DPadLeft);
        bool right = buttons.HasFlag(VEGamepadButtons.DPadRight);

        if (up)
        {
            return left
                ? (byte)8
                : right
                    ? (byte)2
                    : (byte)1;
        }

        if (down)
        {
            return left
                ? (byte)6
                : right
                    ? (byte)4
                    : (byte)5;
        }

        if (left)
        {
            return 7;
        }

        if (right)
        {
            return 3;
        }

        return 0;
    }
}
