using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Video;
using System.Buffers.Binary;
using Xunit;

namespace NOVORA.Tests;

public sealed class VisionEngineBlockATests
{
    [Fact]
    public async Task ReaderProtocolVE_parses_scrcpy_41_codec_session_and_media_header()
    {
        byte[] streamData = new byte[
            ConstantsProtocolVE.CodecIdSizeVE +
            ConstantsProtocolVE.PacketHeaderSizeVE +
            ConstantsProtocolVE.PacketHeaderSizeVE +
            3];

        int offset = 0;

        BinaryPrimitives.WriteUInt32BigEndian(
            streamData.AsSpan(offset, 4),
            (uint)CodecProtocolVE.H264);
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
            ConstantsProtocolVE.PacketFlagKeyFrameVE |
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
        ReaderProtocolVE reader = new(stream);

        CodecProtocolVE codec =
            await reader.ReadCodecAsync();

        HeaderProtocolVE sessionHeader =
            await reader.ReadHeaderAsync();

        HeaderProtocolVE mediaHeader =
            await reader.ReadHeaderAsync();

        byte[] payload =
            await reader.ReadPayloadAsync(
                mediaHeader.PacketLength);

        Assert.Equal(CodecProtocolVE.H264, codec);
        Assert.Equal(KindProtocolVE.Session, sessionHeader.Kind);
        Assert.NotNull(sessionHeader.Session);
        Assert.Equal(1080, sessionHeader.Session!.Width);
        Assert.Equal(2400, sessionHeader.Session.Height);
        Assert.True(sessionHeader.Session.ClientResized);

        Assert.Equal(KindProtocolVE.Media, mediaHeader.Kind);
        Assert.Equal<long?>(123456L, mediaHeader.PresentationTimeUs);
        Assert.True(mediaHeader.IsKeyFrame);
        Assert.False(mediaHeader.IsConfiguration);
        Assert.Equal(3, mediaHeader.PacketLength);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, payload);
    }

    [Fact]
    public void MergerVideoVE_prepends_h26x_configuration_once()
    {
        MergerVideoVE merger = new();

        PacketVideoVE config =
            new(
                PresentationTimeUs: null,
                IsConfiguration: true,
                IsKeyFrame: false,
                Data: new byte[] { 0xAA, 0xBB });

        PacketVideoVE media =
            new(
                PresentationTimeUs: 42,
                IsConfiguration: false,
                IsKeyFrame: true,
                Data: new byte[] { 0xCC, 0xDD });

        Assert.Null(merger.MergeVE(config));

        PacketVideoVE? merged =
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
        await using EngineCoreVE engine = new();

        Assert.False(engine.StatusVE.RendererEnabled);
        Assert.False(engine.StatusVE.VideoStreaming);
        Assert.Equal(0L, engine.StatusVE.VideoFramesDecoded);
    }
}
