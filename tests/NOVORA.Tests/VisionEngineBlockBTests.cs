
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
    public void KeycodeControlVE_includes_capslock_meta_state_when_enabled()
    {
        if (!System.Windows.Forms.Control.IsKeyLocked(System.Windows.Forms.Keys.CapsLock))
        {
            return;
        }

        uint meta =
            KeycodeControlVE.GetMetaStateVE(
                System.Windows.Forms.Keys.A);

        Assert.True(
            (meta & 0x00100000u) != 0);
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

        Assert.Equal("/sdcard/NOVORA/photo 01.jpg", path);
    }

    [Theory]
    [InlineData("photo 01.jpg", "/sdcard/NOVORA/Img/photo 01.jpg")]
    [InlineData("clip.mp4", "/sdcard/NOVORA/Videos/clip.mp4")]
    [InlineData("report.pdf", "/sdcard/NOVORA/Doc/report.pdf")]
    [InlineData("build.apk", "/sdcard/NOVORA/Apps/build.apk")]
    [InlineData("archive.bin", "/sdcard/NOVORA/Files/archive.bin")]
    public void PathExchangeVE_builds_categorized_android_destination(
        string fileName,
        string expected)
    {
        string path =
            PathExchangeVE.BuildCategorizedDestinationFromNameVE(
                fileName);

        Assert.Equal(
            expected,
            path);
    }

    [Fact]
    public void OptionsServerVE_stream_stability_limits_bitrate_fps_and_audio()
    {
        var options =
            NOVORA.VisionEngine.Server.OptionsServerVE.CreateDefaultVE()
                with
                {
                    VideoBitRate = 25_000_000,
                    MaxFps = 120d,
                    AudioBitRate = 192_000
                };

        var stable =
            options.ApplyStreamStabilityVE();

        Assert.Equal(
            4_000_000,
            stable.VideoBitRate);

        Assert.Equal(
            45d,
            stable.MaxFps);

        Assert.Equal(
            64_000,
            stable.AudioBitRate);
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("folder/file.txt")]
    [InlineData("folder\\file.txt")]
    public void PathExchangeVE_keeps_sanitized_names_inside_novora(string invalidName)
    {
        string destination = PathExchangeVE.BuildAndroidDownloadPathVE(invalidName);
        Assert.StartsWith("/sdcard/NOVORA/", destination);
        string name = destination["/sdcard/NOVORA/".Length..];
        Assert.NotEmpty(name);
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("\\", name);
        Assert.NotEqual("..", name);
    }
}
