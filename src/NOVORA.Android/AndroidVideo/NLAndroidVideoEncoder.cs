using Android.Media;
using Android.OS;
using Android.Views;
using System.Threading.Channels;

namespace NOVORA.AndroidVideo;

internal sealed record NLAndroidVideoPacket(byte[] Payload, long? PresentationTimeUs, bool Configuration, bool KeyFrame);

internal sealed class NLAndroidVideoEncoder : MediaCodec.Callback, IAsyncDisposable
{
    private readonly MediaCodec _codec;
    private readonly Channel<NLAndroidVideoPacket> _packets;
    private readonly Action<Exception> _failed;
    private bool _stopped;

    public NLAndroidVideoEncoder(int width, int height, int bitrate, int fps, Action<Exception> failed)
    {
        _failed = failed;
        _packets = Channel.CreateBounded<NLAndroidVideoPacket>(new BoundedChannelOptions(8)
        {
            // En video interactivo es preferible descartar lo antiguo y conservar
            // el frame mas reciente. El control nunca espera a que video se vacie.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
        _codec = MediaCodec.CreateEncoderByType(MediaFormat.MimetypeVideoAvc)
            ?? throw new InvalidOperationException("Android no ofrece un codificador H.264.");
        var format = MediaFormat.CreateVideoFormat(MediaFormat.MimetypeVideoAvc, width, height)
            ?? throw new InvalidOperationException("No se pudo crear el formato H.264.");
        ConfigureFormat(format, width, height, bitrate, fps, lowLatency: true);
        InputSurface = _codec.CreateInputSurface()
            ?? throw new InvalidOperationException("MediaCodec no creó la superficie de entrada.");
    }

    private void ConfigureFormat(MediaFormat format, int width, int height, int bitrate, int fps, bool lowLatency)
    {
        // COLOR_FormatSurface from the Android MediaCodec contract.
        format.SetInteger(MediaFormat.KeyColorFormat, unchecked((int)0x7F000789));
        format.SetInteger(MediaFormat.KeyBitRate, bitrate);
        format.SetInteger(MediaFormat.KeyFrameRate, fps);
        format.SetInteger(MediaFormat.KeyIFrameInterval, 2);

        if (lowLatency)
        {
            // Optional MediaCodec keys. Some vendor codecs reject one or more;
            // the caller retries with the portable base format in that case.
            format.SetInteger("latency", 0);
            format.SetInteger("max-bframes", 0);
            format.SetInteger("priority", 0);
            format.SetInteger("operating-rate", fps);
            format.SetInteger("bitrate-mode", 2); // EncoderCapabilities.BITRATE_MODE_CBR
        }

        _codec.SetCallback(this);
        try
        {
            _codec.Configure(format, null, null, MediaCodecConfigFlags.Encode);
        }
        catch (Java.Lang.IllegalArgumentException) when (lowLatency)
        {
            _codec.Reset();
            MediaFormat fallback = MediaFormat.CreateVideoFormat(MediaFormat.MimetypeVideoAvc, width, height)
                ?? throw new InvalidOperationException("No se pudo crear el formato H.264 de fallback.");
            ConfigureFormat(fallback, width, height, bitrate, fps, lowLatency: false);
        }
    }

    public Surface InputSurface { get; }
    public ChannelReader<NLAndroidVideoPacket> Packets => _packets.Reader;
    public void Start() => _codec.Start();

    public void RequestKeyFrame()
    {
        using var parameters = new Bundle();
        parameters.PutInt("request-sync", 0);
        _codec.SetParameters(parameters);
    }

    public override void OnInputBufferAvailable(MediaCodec codec, int index) { }

    public override void OnOutputBufferAvailable(MediaCodec codec, int index, MediaCodec.BufferInfo info)
    {
        try
        {
            if (_stopped || info.Size <= 0) return;
            var buffer = codec.GetOutputBuffer(index);
            if (buffer is null) return;
            buffer.Position(info.Offset);
            buffer.Limit(info.Offset + info.Size);
            byte[] payload = new byte[info.Size];
            buffer.Get(payload);
            bool configuration = (info.Flags & MediaCodecBufferFlags.CodecConfig) != 0;
            bool keyFrame = (info.Flags & MediaCodecBufferFlags.KeyFrame) != 0;
            long? pts = configuration ? null : Math.Max(0, info.PresentationTimeUs);
            _packets.Writer.TryWrite(new NLAndroidVideoPacket(payload, pts, configuration, keyFrame));
        }
        catch (Exception ex) { _failed(ex); }
        finally { try { codec.ReleaseOutputBuffer(index, false); } catch { } }
    }

    public override void OnOutputFormatChanged(MediaCodec codec, MediaFormat format) { }
    public override void OnError(MediaCodec codec, MediaCodec.CodecException e) => _failed(e);

    public ValueTask DisposeAsync()
    {
        _stopped = true;
        _packets.Writer.TryComplete();
        try { _codec.Stop(); } catch { }
        try { InputSurface.Release(); } catch { }
        _codec.Release();
        _codec.Dispose();
        return ValueTask.CompletedTask;
    }
}
