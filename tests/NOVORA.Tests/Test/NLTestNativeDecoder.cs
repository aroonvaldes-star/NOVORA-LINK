using NOVORA.Service;
using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Video;
using Xunit;

namespace NOVORA.Tests;

[CollectionDefinition("Native decoder", DisableParallelization = true)]
public sealed class NLTestNativeDecoderCollection;

[Collection("Native decoder")]
public sealed class NLTestNativeDecoder
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Real_h264_frames_render_and_software_recovery_preserves_owned_frames(bool preferNvidia)
    {
        using var decoder = new VEVideoDecoder(new NLServiceNovoraPaths());
        decoder.InitializeVE(VEProtocolCodec.H264, preferNvidia);
        Assert.False(decoder.NvdecActiveVE); // Abrir no es producir imágenes.
        var packet = new VEVideoPacket(0, false, true,
            Convert.FromBase64String(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "NLFixturesFrame.txt"))));
        var session = new VEProtocolSession(320, 240, false);
        var owned = new List<VEVideoFrame>();
        try
        {
            for (int i = 0; i < 3; i++) owned.AddRange(decoder.DecodeVE(packet, session));
            Assert.NotEmpty(owned);
            foreach (var frame in owned)
            {
                Assert.Equal(320, frame.Width);
                Assert.Equal(240, frame.Height);
                frame.ValidateForRendererVE();
            }
            if (!preferNvidia) Assert.Equal("h264", decoder.DecoderNameVE);
            if (decoder.IsNvidiaVE)
            {
                Assert.True(decoder.NvdecActiveVE);
                decoder.FallBackToSoftwareVE(new InvalidOperationException("Fallo simulado de dispositivo"));
                Assert.False(decoder.NvdecActiveVE);
                Assert.Equal("h264", decoder.DecoderNameVE);
                Assert.Contains("simulado", decoder.FallbackReasonVE);
                // Clones pendientes siguen vivos mientras cambia AVCodecContext.
                foreach (var frame in owned) frame.ValidateForRendererVE();
                var recovered = decoder.DecodeVE(packet, session);
                try { Assert.NotEmpty(recovered); foreach (var frame in recovered) frame.ValidateForRendererVE(); }
                finally { foreach (var frame in recovered) frame.Dispose(); }
            }
        }
        finally { foreach (var frame in owned) frame.Dispose(); }
    }

    [Fact]
    public void Shared_ffmpeg_still_decodes_opus_audio_to_stereo_pcm()
    {
        using var decoder = new VEAudioDecoder(new NLServiceNovoraPaths());
        decoder.InitializeVE(VEProtocolCodec.Opus);
        byte[] bytes = Convert.FromBase64String(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "NLFixturesOpus.txt")));
        var frame = Assert.Single(decoder.DecodeVE(new VEAudioPacket(0, false, bytes)));
        Assert.Equal(48000, frame.SampleRate);
        Assert.Equal(2, frame.Channels);
        Assert.True(frame.SamplesPerChannel > 0);
        Assert.Equal(frame.SamplesPerChannel * 2 * 2, frame.Pcm16Le.Length);
        Assert.Contains(frame.Pcm16Le, value => value != 0);
    }

    [Fact]
    public void Changed_configuration_reopens_decoder_for_taller_image()
    {
        using var decoder = new VEVideoDecoder(new NLServiceNovoraPaths());
        decoder.InitializeVE(VEProtocolCodec.H264, true);
        var held = new List<VEVideoFrame>();
        try
        {
            foreach (var fixture in new[] { ("NLFixturesFrame.txt", 320, 240), ("NLFixturesRotatedFrame.txt", 240, 320) })
            {
                byte[] bytes = Convert.FromBase64String(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture.Item1)));
                decoder.ConfigureVE(bytes);
                var packet = new VEVideoPacket(0, false, true, bytes);
                var frames = new List<VEVideoFrame>();
                for (int i = 0; i < 3; i++) { var next = decoder.DecodeVE(packet, new(fixture.Item2, fixture.Item3, true)); frames.AddRange(next); held.AddRange(next); }
                Assert.NotEmpty(frames);
                foreach (var frame in frames) { Assert.Equal(fixture.Item2, frame.Width); Assert.Equal(fixture.Item3, frame.Height); frame.ValidateForRendererVE(); }
            }
            foreach (var frame in held) frame.ValidateForRendererVE();
        }
        finally { foreach (var frame in held) frame.Dispose(); }
    }

    [Fact]
    public void Configuration_is_replayed_once_after_recovery_and_reset_forgets_it()
    {
        var merger = new VEVideoMerger();
        var config = new VEVideoPacket(null, true, false, [1, 2]);
        var key = new VEVideoPacket(10, false, true, [3, 4]);
        Assert.Null(merger.MergeVE(config));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, merger.MergeVE(key)!.Data);
        merger.ReplayConfigurationVE();
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, merger.MergeVE(key)!.Data);
        Assert.Equal(key.Data, merger.MergeVE(key)!.Data);
        merger.ResetVE();
        merger.ReplayConfigurationVE();
        Assert.Equal(key.Data, merger.MergeVE(key)!.Data);
    }
}
