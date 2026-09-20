using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Video;
using System.Buffers.Binary;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestVisionEngineBlockATests
{
    [Fact]
    public async Task ReaderProtocolVE_parses_scrcpy_41_codec_session_and_media_header()
    {
        byte[] streamData = new byte[
            VEProtocolConstants.CodecIdSizeVE +
            VEProtocolConstants.PacketHeaderSizeVE +
            VEProtocolConstants.PacketHeaderSizeVE +
            3];

        int offset = 0;

        BinaryPrimitives.WriteUInt32BigEndian(
            streamData.AsSpan(offset, 4),
            (uint)VEProtocolCodec.H264);
        offset += 4;

        streamData[offset] = 0x80;
        streamData[offset + 3] = 0x01;
        BinaryPrimitives.WriteUInt32BigEndian(
            streamData.AsSpan(offset + 4, 4),
            1080);
        BinaryPrimitives.WriteUInt32BigEndian(
            streamData.AsSpan(offset + 8, 4),
            2400);
        offset += 12;

        ulong ptsFlags =
            VEProtocolConstants.PacketFlagKeyFrameVE |
            123456UL;

        BinaryPrimitives.WriteUInt64BigEndian(
            streamData.AsSpan(offset, 8),
            ptsFlags);
        BinaryPrimitives.WriteUInt32BigEndian(
            streamData.AsSpan(offset + 8, 4),
            3);
        offset += 12;

        streamData[offset] = 0x01;
        streamData[offset + 1] = 0x02;
        streamData[offset + 2] = 0x03;

        await using MemoryStream stream = new(streamData);
        VEProtocolReader reader = new(stream);

        VEProtocolCodec codec =
            await reader.ReadCodecAsync();

        VEProtocolHeader sessionHeader =
            await reader.ReadHeaderAsync();

        VEProtocolHeader mediaHeader =
            await reader.ReadHeaderAsync();

        byte[] payload =
            await reader.ReadPayloadAsync(
                mediaHeader.PacketLength);

        Assert.Equal(VEProtocolCodec.H264, codec);
        Assert.Equal(VEProtocolKind.Session, sessionHeader.Kind);
        Assert.NotNull(sessionHeader.Session);
        Assert.Equal(1080, sessionHeader.Session!.Width);
        Assert.Equal(2400, sessionHeader.Session.Height);
        Assert.True(sessionHeader.Session.ClientResized);

        Assert.Equal(VEProtocolKind.Media, mediaHeader.Kind);
        Assert.Equal<long?>(123456L, mediaHeader.PresentationTimeUs);
        Assert.True(mediaHeader.IsKeyFrame);
        Assert.False(mediaHeader.IsConfiguration);
        Assert.Equal(3, mediaHeader.PacketLength);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, payload);
    }

    [Fact]
    public void MergerVideoVE_prepends_h26x_configuration_once()
    {
        VEVideoMerger merger = new();

        VEVideoPacket config =
            new(
                PresentationTimeUs: null,
                IsConfiguration: true,
                IsKeyFrame: false,
                Data: new byte[] { 0xAA, 0xBB });

        VEVideoPacket media =
            new(
                PresentationTimeUs: 42,
                IsConfiguration: false,
                IsKeyFrame: true,
                Data: new byte[] { 0xCC, 0xDD });

        Assert.Null(merger.MergeVE(config));

        VEVideoPacket? merged =
            merger.MergeVE(media);

        Assert.NotNull(merged);
        Assert.Equal(
            new byte[] { 0xAA, 0xBB, 0xCC, 0xDD },
            merged!.Data);
        Assert.Equal<long?>(42L, merged.PresentationTimeUs);
        Assert.True(merged.IsKeyFrame);
        Assert.False(merged.IsConfiguration);
        Assert.False(merger.HasConfigurationVE);
    }

    [Fact]
    public async Task EngineCoreVE_is_headless_before_initialization()
    {
        await using VECoreEngine engine = new();

        Assert.False(engine.StatusVE.RendererEnabled);
        Assert.False(engine.StatusVE.VideoStreaming);
        Assert.Equal(0L, engine.StatusVE.VideoFramesDecoded);
    }
}
