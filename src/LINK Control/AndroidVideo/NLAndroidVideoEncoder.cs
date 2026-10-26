using Android.Media;
using Android.OS;
using Android.Views;
using NOVORA.Control;
using System.Threading.Channels;

namespace NOVORA.AndroidVideo;

internal sealed class NLAndroidVideoEncoder : MediaCodec.Callback, IAsyncDisposable
{
    private readonly MediaCodec _codec;
    private readonly NLAndroidVideoPacketQueue _packets;
    private readonly Action<Exception> _failed;
    private readonly object _codecGate = new();
    private readonly int _maximumBitrate;
    private readonly NLControlKeyframeGate _keyframeGate = new();
    // Without a Handler, MediaCodec delivers callbacks on the main looper, shared with the
    // UI and the accessibility gestures. Output gets its own display-priority thread.
    private readonly HandlerThread _callbackThread = new("NOVORA-VE-Encoder", (int)Android.OS.ThreadPriority.Display);
    private readonly Handler _callbackHandler;

    private bool _stopped;

    public NLAndroidVideoEncoder(int width, int height, int bitrate, int fps, Action<Exception> failed,
        long captureGeneration = 0,
        int maximumFrameBytes = NLAndroidVideoPacketQueue.MaximumIndividualFrameBytes)
    {
        _failed = failed;
        _maximumBitrate = bitrate;
        Telemetry = new NLControlCaptureTelemetryWindow(captureGeneration);
        _packets = new NLAndroidVideoPacketQueue(
            bitrate,
            Telemetry,
            maximumIndividualFrameBytes: maximumFrameBytes);
        _callbackThread.Start();
        _callbackHandler = new Handler(_callbackThread.Looper!);
        _codec = MediaCodec.CreateEncoderByType(MediaFormat.MimetypeVideoAvc)
            ?? throw new InvalidOperationException("Android no ofrece un codificador H.264.");
        try
        {
            ConfigureFormat(width, height, bitrate, fps);
            InputSurface = _codec.CreateInputSurface()
                ?? throw new InvalidOperationException("MediaCodec no creó la superficie de entrada.");
        }
        catch
        {
            // Constructor failure has no owner available to invoke DisposeAsync.
            _packets.Complete();
            try { _codec.Release(); } catch { }
            try { _codec.Dispose(); } catch { }
            _callbackThread.QuitSafely();
            throw;
        }
    }

    private void ConfigureFormat(int width, int height, int bitrate, int fps)
    {
        using var capabilities = _codec.CodecInfo.GetCapabilitiesForType(MediaFormat.MimetypeVideoAvc);
        bool supportsCbr = capabilities?.EncoderCapabilities?.IsBitrateModeSupported(BitrateMode.Cbr) == true;
        bool supportsNoBFrames = false;
        if (OperatingSystem.IsAndroidVersionAtLeast(29) && capabilities is not null)
        {
            using var probe = CreateFormat(width, height, NLAndroidVideoEncoderConfiguration.Options(
                bitrate, fps, 29, supportsCbr: false, supportsNoBFrames: false, advanced: false));
            probe.SetInteger("max-bframes", 0);
            try { supportsNoBFrames = capabilities.IsFormatSupported(probe); }
            catch (Java.Lang.IllegalArgumentException ex)
            {
                Android.Util.Log.Warn("NOVORA-VE-ENCODER", "El codec no pudo verificar max-bframes: " + ex.Message);
            }
        }

        NLAndroidVideoEncoderConfiguration.Configure(advanced =>
            {
                using var format = CreateFormat(width, height, NLAndroidVideoEncoderConfiguration.Options(
                    bitrate, fps, (int)Build.VERSION.SdkInt, supportsCbr, supportsNoBFrames, advanced));
                _codec.SetCallback(this, _callbackHandler);
                _codec.Configure(format, null, null, MediaCodecConfigFlags.Encode);
            }, _codec.Reset,
            ex => ex is Java.Lang.IllegalArgumentException or Java.Lang.UnsupportedOperationException ||
                ex is MediaCodec.CodecException { IsRecoverable: true, IsTransient: false } codecError &&
                    (int)codecError.ErrorCode is not (1100 or 1101),
            ex => Android.Util.Log.Warn("NOVORA-VE-ENCODER",
                "El codec rechazó las opciones adicionales; reintentando con formato base: " + ex.Message));
    }

    private static MediaFormat CreateFormat(int width, int height, IReadOnlyDictionary<string, int> options)
    {
        var format = MediaFormat.CreateVideoFormat(MediaFormat.MimetypeVideoAvc, width, height)
            ?? throw new InvalidOperationException("No se pudo crear el formato H.264.");
        foreach (var option in options) format.SetInteger(option.Key, option.Value);
        return format;
    }

    public Surface InputSurface { get; }
    public ChannelReader<NLAndroidVideoPacket> Packets => _packets.Reader;
    public long DroppedPackets => _packets.DroppedPackets;
    public NLControlCaptureTelemetryWindow Telemetry { get; }

    public NLControlCaptureTelemetry? TakeTelemetry()
    {
        lock (_codecGate)
        {
            if (_stopped) return null;
            _packets.RecordOldestAge();
            return Telemetry.Take();
        }
    }

    public void Start() { lock (_codecGate) { if (!_stopped) _codec.Start(); } }

    public bool TrySetTargetBitrate(int bitrate)
    {
        lock (_codecGate)
        {
            if (_stopped || bitrate is < 250_000 or > 100_000_000 || bitrate > _maximumBitrate) return false;
            try
            {
                using var parameters = new Bundle();
                parameters.PutInt("video-bitrate", bitrate);
                _codec.SetParameters(parameters);
                if (_packets.SetTargetBitrate(bitrate)) RequestKeyFrame();
                return true;
            }
            catch (Exception ex)
            {
                Android.Util.Log.Warn("NOVORA-VE-ENCODER", "Bitrate vivo rechazado: " + ex.Message);
                return false;
            }
        }
    }

    public void RequestKeyFrame()
    {
        lock (_codecGate)
        {
            if (_stopped || !_keyframeGate.TryRequest()) return;
            try
            {
                using var parameters = new Bundle();
                parameters.PutInt("request-sync", 0);
                _codec.SetParameters(parameters);
            }
            catch (Exception ex)
            {
                Android.Util.Log.Warn("NOVORA-VE-ENCODER", "Cuadro clave rechazado: " + ex.Message);
            }
        }
    }

    public void PacketDequeued(NLAndroidVideoPacket packet)
    {
        lock (_codecGate)
        {
            // Reader.TryRead already releases the bytes atomically with producer pruning.
            Telemetry.RecordQueueAge(TimeProvider.System.GetElapsedTime(packet.EnqueuedAtTicks));
        }
    }

    private void HandleQueueResult(NLAndroidVideoQueueResult result)
    {
        if (result.EnteredRecovery)
            Android.Util.Log.Warn("NOVORA-VE-ENCODER",
                "Cola H.264 saturada o antigua; esperando IDR para recuperar el video.");
        if (result.RequestKeyFrame) RequestKeyFrame();
    }

    public override void OnInputBufferAvailable(MediaCodec codec, int index) { }

    public override void OnOutputBufferAvailable(MediaCodec codec, int index, MediaCodec.BufferInfo info)
    {
        lock (_codecGate)
        {
            try
            {
                if (_stopped || info.Size <= 0) return;
                bool configuration = (info.Flags & MediaCodecBufferFlags.CodecConfig) != 0;
                if (info.Size > (configuration ? NLAndroidVideoPacketQueue.MaximumConfigurationBytes :
                    NLAndroidVideoPacketQueue.MaximumIndividualFrameBytes))
                {
                    HandleQueueResult(_packets.RejectOversized());
                    return;
                }
                var buffer = codec.GetOutputBuffer(index);
                if (buffer is null) return;
                buffer.Position(info.Offset);
                buffer.Limit(info.Offset + info.Size);
                byte[] payload = new byte[info.Size];
                buffer.Get(payload);
                bool keyFrame = (info.Flags & MediaCodecBufferFlags.KeyFrame) != 0;
                if (keyFrame) _keyframeGate.ObserveKeyFrame();
                long? pts = configuration ? null : Math.Max(0, info.PresentationTimeUs);

                HandleQueueResult(_packets.Enqueue(new NLAndroidVideoPacket(payload, pts, configuration, keyFrame)));
            }
            catch (Exception ex) { _failed(ex); }
            finally { try { codec.ReleaseOutputBuffer(index, false); } catch { } }
        }
    }

    public override void OnOutputFormatChanged(MediaCodec codec, MediaFormat format)
    {
        // The encoder may ignore KEY_LATENCY; only its output format confirms acceptance.
        Android.Util.Log.Info("NOVORA-VE-ENCODER", format.ContainsKey("latency")
            ? "Latencia declarada por el encoder (cuadros): " + format.GetInteger("latency")
            : "El encoder no confirmó la opción latency.");
    }
    public override void OnError(MediaCodec codec, MediaCodec.CodecException e) => _failed(e);

    public ValueTask DisposeAsync()
    {
        lock (_codecGate)
        {
            if (_stopped) return ValueTask.CompletedTask;
            _stopped = true;
            _packets.Complete();
        }

        try { _codec.Stop(); } catch { }
        try { InputSurface.Release(); } catch { }
        _codec.Release();
        _codec.Dispose();
        _callbackThread.QuitSafely();
        return ValueTask.CompletedTask;
    }
}
