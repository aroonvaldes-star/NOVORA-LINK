
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Exchange;
using NOVORA.ExInEngine;
using System.Buffers.Binary;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestVisionEngineBlockBTests
{
    [Fact]
    public void SerializerControlVE_serializes_keycode_using_scrcpy_41_layout()
    {
        byte[] payload = VEControlSerializer.SerializeVE(
            VEControlMessage.KeycodeVE(
                VEControlActionKey.Down,
                keycode: 4,
                repeat: 2,
                metaState: 0x1000));

        Assert.Equal(14, payload.Length);
        Assert.Equal((byte)VEControlType.InjectKeycode, payload[0]);
        Assert.Equal((byte)VEControlActionKey.Down, payload[1]);
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
            VEControlKeycode.GetMetaStateVE(
                System.Windows.Forms.Keys.A);

        Assert.True(
            (meta & 0x00100000u) != 0);
    }

    [Fact]
    public void SerializerControlVE_serializes_touch_using_32_byte_scrcpy_packet()
    {
        byte[] payload = VEControlSerializer.SerializeVE(
            VEControlMessage.TouchVE(
                VEControlActionMotion.Move,
                pointerId: ulong.MaxValue,
                x: 540,
                y: 1200,
                screenWidth: 1080,
                screenHeight: 2400,
                pressure: 1.0f,
                actionButton: 0,
                buttons: 1));

        Assert.Equal(32, payload.Length);
        Assert.Equal((byte)VEControlType.InjectTouchEvent, payload[0]);
        Assert.Equal((byte)VEControlActionMotion.Move, payload[1]);
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
        ExInState state = new(
            LeftX: 0,
            LeftY: 0,
            RightX: short.MaxValue,
            RightY: short.MinValue,
            LeftTrigger: 32767,
            RightTrigger: 0,
            Buttons: ExInButtons.South |
                     ExInButtons.Start |
                     ExInButtons.DPadUp |
                     ExInButtons.DPadRight);

        byte[] report = ExInReport.BuildVE(state);

        Assert.Equal(15, report.Length);
        Assert.Equal((ushort)0x8000, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(0, 2)));
        Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(4, 2)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(6, 2)));
        Assert.Equal((ushort)32767, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(8, 2)));
        Assert.Equal((ushort)0x0801, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(12, 2)));
        Assert.Equal((byte)2, report[14]);
    }

    [Theory]
    [InlineData(ExInButtons.South, 0x0001)]
    [InlineData(ExInButtons.East, 0x0002)]
    [InlineData(ExInButtons.West, 0x0008)]
    [InlineData(ExInButtons.North, 0x0010)]
    [InlineData(ExInButtons.LeftShoulder, 0x0040)]
    [InlineData(ExInButtons.RightShoulder, 0x0080)]
    [InlineData(ExInButtons.Back, 0x0400)]
    [InlineData(ExInButtons.Start, 0x0800)]
    [InlineData(ExInButtons.Guide, 0x1000)]
    [InlineData(ExInButtons.LeftStick, 0x2000)]
    [InlineData(ExInButtons.RightStick, 0x4000)]
    public void ReportGamepadVE_uses_xbox_android_hid_button_map(
        ExInButtons button,
        ushort expected)
    {
        ExInState state = new(0, 0, 0, 0, 0, 0, button);

        byte[] report = ExInReport.BuildVE(state);

        Assert.Equal(
            expected,
            BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(12, 2)));
    }

    [Fact]
    public void PathExchangeVE_builds_safe_android_download_destination()
    {
        string path = VEExchangePath.BuildAndroidDownloadPathVE("photo 01.jpg");

        Assert.Equal("/sdcard/NOVORA/Imagenes/photo 01.jpg", path);
    }

    [Theory]
    [InlineData("photo 01.jpg", "/sdcard/NOVORA/Imagenes/photo 01.jpg")]
    [InlineData("clip.mp4", "/sdcard/NOVORA/Videos/clip.mp4")]
    [InlineData("report.pdf", "/sdcard/NOVORA/Documentos/report.pdf")]
    [InlineData("build.apk", "/sdcard/NOVORA/Instaladores/build.apk")]
    [InlineData("archive.bin", "/sdcard/NOVORA/Otros/archive.bin")]
    public void PathExchangeVE_builds_categorized_android_destination(
        string fileName,
        string expected)
    {
        string path =
            VEExchangePath.BuildCategorizedDestinationFromNameVE(
                fileName);

        Assert.Equal(
            expected,
            path);
    }

    [Fact]
    public void OptionsServerVE_stream_stability_keeps_supported_bitrate_and_limits_audio()
    {
        var options =
            NOVORA.VisionEngine.Server.VEServerOptions.CreateDefaultVE()
                with
                {
                    VideoBitRate = 25_000_000,
                    MaxFps = 120d,
                    AudioBitRate = 192_000
                };

        var stable =
            options.ApplyStreamStabilityVE();

        Assert.Equal(
            15_000_000,
            stable.VideoBitRate);

        Assert.Equal(
            45d,
            stable.MaxFps);

        Assert.Equal(
            64_000,
            stable.AudioBitRate);
    }

    [Theory]
    [InlineData(0, 3_840, false)]
    [InlineData(15_360, 3_840, false)]
    [InlineData(34_560, 3_840, true)]
    public void Audio_queue_resynchronizes_before_latency_can_keep_growing(
        int queuedBytes,
        int incomingBytes,
        bool expected)
    {
        Assert.Equal(
            expected,
            NOVORA.VisionEngine.Audio.VEAudioPlayer.ShouldResynchronizeVE(
                queuedBytes,
                incomingBytes));
    }

    [Fact]
    public void ExIn_control_only_server_disables_video_and_audio()
    {
        var options = NOVORA.VisionEngine.Server.VEServerOptions.CreateControlOnlyVE();
        var arguments = options.BuildArgumentsVE(0x1234, tunnelForward: false);
        Assert.False(options.VideoEnabled);
        Assert.False(options.AudioEnabled);
        Assert.True(options.ControlEnabled);
        Assert.Contains("video=false", arguments);
        Assert.Contains("audio=false", arguments);
        Assert.DoesNotContain("control=false", arguments);
        Assert.DoesNotContain(arguments, value => value.StartsWith("video_bit_rate=", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, value => value.StartsWith("max_fps=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("folder/file.txt")]
    [InlineData("folder\\file.txt")]
    public void PathExchangeVE_keeps_sanitized_names_inside_novora(string invalidName)
    {
        string destination = VEExchangePath.BuildAndroidDownloadPathVE(invalidName);
        Assert.StartsWith("/sdcard/NOVORA/", destination);
        string name = destination[(destination.LastIndexOf('/' ) + 1)..];
        Assert.NotEmpty(name);
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("\\", name);
        Assert.NotEqual("..", name);
    }
}
