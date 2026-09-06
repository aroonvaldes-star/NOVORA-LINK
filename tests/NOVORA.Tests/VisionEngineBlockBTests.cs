using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Exchange;
using NOVORA.VisionEngine.Gamepad;
using System.Buffers.Binary;
using Xunit;

namespace NOVORA.Tests;

public sealed class VisionEngineBlockBTests
{
    [Fact]
    public void SerializerControlVE_serializes_keycode_using_scrcpy_41_layout()
    {
        byte[] payload = SerializerControlVE.SerializeVE(
            MessageControlVE.KeycodeVE(
                ActionKeyControlVE.Down,
                keycode: 4,
                repeat: 2,
                metaState: 0x1000));

        Assert.Equal(14, payload.Length);
        Assert.Equal((byte)TypeControlVE.InjectKeycode, payload[0]);
        Assert.Equal((byte)ActionKeyControlVE.Down, payload[1]);
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(2, 4)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(6, 4)));
        Assert.Equal(0x1000u, BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(10, 4)));
    }

    [Fact]
    public void SerializerControlVE_serializes_touch_using_32_byte_scrcpy_packet()
    {
        byte[] payload = SerializerControlVE.SerializeVE(
            MessageControlVE.TouchVE(
                ActionMotionControlVE.Move,
                pointerId: ulong.MaxValue,
                x: 540,
                y: 1200,
                screenWidth: 1080,
                screenHeight: 2400,
                pressure: 1.0f,
                actionButton: 0,
                buttons: 1));

        Assert.Equal(32, payload.Length);
        Assert.Equal((byte)TypeControlVE.InjectTouchEvent, payload[0]);
        Assert.Equal((byte)ActionMotionControlVE.Move, payload[1]);
        Assert.Equal(ulong.MaxValue, BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(2, 8)));
        Assert.Equal(540u, BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(10, 4)));
        Assert.Equal(1200u, BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(14, 4)));
        Assert.Equal((ushort)1080, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(18, 2)));
        Assert.Equal((ushort)2400, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(20, 2)));
        Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(22, 2)));
    }

    [Fact]
    public void ReportGamepadVE_matches_scrcpy_15_byte_hid_layout()
    {
        StateGamepadVE state = new(
            LeftX: 0,
            LeftY: 0,
            RightX: short.MaxValue,
            RightY: short.MinValue,
            LeftTrigger: 32767,
            RightTrigger: 0,
            Buttons: ButtonsGamepadVE.South |
                     ButtonsGamepadVE.Start |
                     ButtonsGamepadVE.DPadUp |
                     ButtonsGamepadVE.DPadRight);

        byte[] report = ReportGamepadVE.BuildVE(state);

        Assert.Equal(15, report.Length);
        Assert.Equal((ushort)0x8000, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(0, 2)));
        Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(4, 2)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(6, 2)));
        Assert.Equal((ushort)32767, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(8, 2)));
        Assert.Equal((ushort)0x0801, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(12, 2)));
        Assert.Equal((byte)2, report[14]);
    }

    [Fact]
    public void PathExchangeVE_builds_safe_android_download_destination()
    {
        string path = PathExchangeVE.BuildAndroidDownloadPathVE("photo 01.jpg");

        Assert.Equal("/sdcard/Download/photo 01.jpg", path);
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("folder/file.txt")]
    [InlineData("folder\\file.txt")]
    public void PathExchangeVE_rejects_non_file_names(string invalidName)
    {
        Assert.Throws<ArgumentException>(
            () => PathExchangeVE.BuildAndroidDownloadPathVE(invalidName));
    }
}
