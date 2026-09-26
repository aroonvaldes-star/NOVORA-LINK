using NOVORA.Service;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Video;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestVisionEngineProtocolWriter
{
    [Fact]
    public async Task Video_manager_accepts_a_source_stream_without_a_transport_session()
    {
        await using MemoryStream stream = new();
        await using var manager = new VEVideoManager(new NLServiceNovoraPaths());

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            manager.StartAsync(stream));

        Assert.False(manager.IsRunningVE);
    }

    [Fact]
    public async Task Writer_output_is_consumed_by_existing_video_demuxer()
    {
        await using MemoryStream stream = new();
        var writer = new VEProtocolWriter(stream);

        await writer.WriteVideoSessionAsync(
            VEProtocolCodec.H264,
            new VEProtocolSession(1080, 2400, ClientResized: false));
        await writer.WriteVideoPacketAsync(
            new byte[] { 0x01, 0x64, 0x00, 0x1f },
            presentationTimeUs: null,
            isConfiguration: true,
            isKeyFrame: false);
        await writer.WriteVideoPacketAsync(
            new byte[] { 0x65, 0x88, 0x84 },
            presentationTimeUs: 33_333,
            isConfiguration: false,
            isKeyFrame: true);

        stream.Position = 0;
        var demuxer = new VEVideoDemuxer(stream);

        VEProtocolSession session = await demuxer.OpenAsync();
        VEVideoPacket configuration = Assert.IsType<VEVideoPacket>(
            await demuxer.ReadPacketAsync());
        VEVideoPacket keyFrame = Assert.IsType<VEVideoPacket>(
            await demuxer.ReadPacketAsync());

        Assert.Equal(VEProtocolCodec.H264, demuxer.CodecVE);
        Assert.Equal(new VEProtocolSession(1080, 2400, false), session);
        Assert.True(configuration.IsConfiguration);
        Assert.Null(configuration.PresentationTimeUs);
        Assert.Equal(new byte[] { 0x01, 0x64, 0x00, 0x1f }, configuration.Data);
        Assert.True(keyFrame.IsKeyFrame);
        Assert.Equal(33_333, keyFrame.PresentationTimeUs);
        Assert.Equal(new byte[] { 0x65, 0x88, 0x84 }, keyFrame.Data);
    }

    [Fact]
    public async Task Writer_emits_session_update_without_repeating_codec()
    {
        await using MemoryStream stream = new();
        var writer = new VEProtocolWriter(stream);
        await writer.WriteVideoSessionAsync(
            VEProtocolCodec.H264,
            new VEProtocolSession(1080, 2400, false));
        await writer.WriteSessionUpdateAsync(
            new VEProtocolSession(2400, 1080, true));
        await writer.WriteVideoPacketAsync(
            new byte[] { 0x65 },
            presentationTimeUs: 50_000,
            isConfiguration: false,
            isKeyFrame: true);

        stream.Position = 0;
        var demuxer = new VEVideoDemuxer(stream);
        VEProtocolSession? changed = null;
        demuxer.SessionChangedVE += (_, session) => changed = session;

        await demuxer.OpenAsync();
        VEVideoPacket packet = Assert.IsType<VEVideoPacket>(
            await demuxer.ReadPacketAsync());

        Assert.Equal(new VEProtocolSession(2400, 1080, true), changed);
        Assert.Equal(50_000, packet.PresentationTimeUs);
    }

    [Fact]
    public async Task Writer_rejects_invalid_video_contract_values()
    {
        await using MemoryStream stream = new();
        var writer = new VEProtocolWriter(stream);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.WriteVideoSessionAsync(
                VEProtocolCodec.Opus,
                new VEProtocolSession(1080, 2400, false)).AsTask());

        await writer.WriteVideoSessionAsync(
            VEProtocolCodec.H264,
            new VEProtocolSession(1080, 2400, false));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.WriteVideoPacketAsync(
                ReadOnlyMemory<byte>.Empty,
                presentationTimeUs: 0,
                isConfiguration: false,
                isKeyFrame: false).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.WriteVideoPacketAsync(
                new byte[] { 0x01 },
                presentationTimeUs: 0,
                isConfiguration: true,
                isKeyFrame: false).AsTask());
    }
}
