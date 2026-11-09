using System.IO;
using System.Threading.Channels;
using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Video;

namespace NOVORA.LinkEngine.Egress;

/// <summary>
/// Salida en vivo de LinkEngine: toma los paquetes H264 de VE sin recodificar
/// y el PCM del teléfono (→ AAC) y los publica por RTMP(S). Los eventos de VE
/// solo copian y hacen TryWrite; toda la red ocurre en el writer de LE. Si la
/// red no mantiene el ritmo se descartan GOPs completos, nunca se bloquea VE.
/// </summary>
public sealed class LEEgressLiveSession : IAsyncDisposable
{
    private static readonly TimeSpan[] BackoffLE = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];
    private static readonly TimeSpan MaxLagLE = TimeSpan.FromSeconds(2);
    private const int QueueLimitLE = 512;

    private readonly VEVideoManager _video;
    private readonly VEAudioManager _audio;
    private readonly string _toolsDirectory;
    private readonly object _gate = new();
    private Channel<Item>? _queue;
    private CancellationTokenSource? _stop;
    private Task? _writer;
    private byte[]? _configuration, _avcConfiguration;
    private bool _protected, _dropUntilKey, _audioIncluded;
    private long _bytesSent, _droppedVideo, _lastKeyRequest = long.MinValue / 2;
    private int _reconnects, _privacyEpoch;
    private LEEgressAacEncoder? _encoder;
    private LEEgressStatus _status = new(LEEgressStates.Idle, null, 0, 0, 0, false, null);

    private enum Kind : byte { Video, Audio, Configuration }
    private sealed record Item(Kind Kind, long TimeUs, bool KeyFrame, byte[] Data, long EnqueuedTicks);

    public bool PhoneAudioAllowed { get; set; }
    public event EventHandler<LEEgressStatus>? StatusChangedLE;
    public LEEgressStatus StatusLE { get { lock (_gate) return _status with { BytesSent = Interlocked.Read(ref _bytesSent), DroppedVideo = Interlocked.Read(ref _droppedVideo) }; } }
    public bool CanStartLE { get { lock (_gate) return !_protected && _video.StatusVE.State == VEVideoStates.Streaming && _video.StatusVE.Codec == VEProtocolCodec.H264 && _avcConfiguration is not null; } }

    public LEEgressLiveSession(VEVideoManager video, VEAudioManager audio, string toolsDirectory)
    {
        _video = video; _audio = audio; _toolsDirectory = toolsDirectory;
        video.PacketReceivedVE += OnPacket;
        video.StatusChangedVE += OnVideoState;
        audio.FrameDecodedVE += OnAudio;
    }

    public void Start(LEEgressTarget target)
    {
        if (!target.IsComplete) throw new InvalidOperationException("Configura servidor y clave de transmisión en Ajustes.");
        _ = target.ServerUri;
        lock (_gate)
        {
            if (_writer is { IsCompleted: false }) throw new InvalidOperationException("La transmisión ya está activa.");
            if (!CanStartLE) throw new InvalidOperationException("La transmisión requiere video VE H.264 activo y sin protección de privacidad.");
            _audioIncluded = PhoneAudioAllowed && _audio.StatusVE.State == VEAudioStates.Streaming;
            _queue = Channel.CreateBounded<Item>(new BoundedChannelOptions(QueueLimitLE) { SingleReader = false, SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite });
            _stop = new CancellationTokenSource();
            _dropUntilKey = true;
            Interlocked.Exchange(ref _bytesSent, 0); Interlocked.Exchange(ref _droppedVideo, 0); _reconnects = 0;
            _status = new(LEEgressStates.Connecting, null, 0, 0, 0, false, null);
            var queue = _queue; var token = _stop.Token;
            _writer = Task.Run(() => RunAsync(queue, target, token));
        }
        Publish();
    }

    public async Task StopAsync()
    {
        Task? writer;
        lock (_gate) { _stop?.Cancel(); _queue?.Writer.TryComplete(); _queue = null; writer = _writer; }
        if (writer is not null) { try { await writer.ConfigureAwait(false); } catch { } }
    }

    public void SetProtected(bool value)
    {
        lock (_gate)
        {
            _protected = value;
            _dropUntilKey = true;
            if (value)
            {
                // Lo capturado antes de la pausa no debe salir: se vacía antes de publicar Paused.
                _privacyEpoch++;
                if (_queue is { } queue)
                    while (queue.Reader.TryRead(out _)) { }
            }
            if (_status.Active) _status = _status with { State = value ? LEEgressStates.Paused : LEEgressStates.Live };
        }
        if (!value) RequestKeyFrame();
        Publish();
    }

    private void OnVideoState(object? sender, VEVideoStatus state)
    {
        if (state.State == VEVideoStates.Streaming) return;
        lock (_gate) { _configuration = null; _avcConfiguration = null; _dropUntilKey = true; }
    }

    private void OnPacket(object? sender, VEVideoPacket packet)
    {
        lock (_gate)
        {
            if (packet.IsConfiguration)
            {
                if (_video.StatusVE.Codec != VEProtocolCodec.H264) return;
                _configuration = VEMediaMatroska.MergeConfiguration(_configuration, packet.PayloadVE.ToArray());
                byte[]? prepared;
                try { prepared = VEMediaMatroska.AvcConfiguration(_configuration); }
                catch (InvalidDataException) { prepared = null; }
                if (prepared is not null && (_avcConfiguration is null || !_avcConfiguration.AsSpan().SequenceEqual(prepared)))
                {
                    _avcConfiguration = prepared;
                    _queue?.Writer.TryWrite(new(Kind.Configuration, 0, true, prepared, Environment.TickCount64));
                }
                return;
            }
            if (_protected || _queue is null || packet.PresentationTimeUs is null) return;
            if (_dropUntilKey && !packet.IsKeyFrame) { Interlocked.Increment(ref _droppedVideo); return; }
            if (_queue.Writer.TryWrite(new(Kind.Video, packet.PresentationTimeUs.Value, packet.IsKeyFrame, packet.PayloadVE.ToArray(), Environment.TickCount64)))
                _dropUntilKey = false;
            else { _dropUntilKey = true; Interlocked.Increment(ref _droppedVideo); }
        }
        if (_dropUntilKey) RequestKeyFrame();
    }

    private void OnAudio(object? sender, VEAudioFrame frame)
    {
        lock (_gate)
        {
            if (_protected || !_audioIncluded || !PhoneAudioAllowed || _queue is null || frame.SourcePresentationTimeUs is null) return;
            if (frame.SampleRate != 48000 || frame.Channels != 2) return;
            _queue.Writer.TryWrite(new(Kind.Audio, frame.SourcePresentationTimeUs.Value, true, frame.Pcm16Le, Environment.TickCount64));
        }
    }

    private async Task RunAsync(Channel<Item> queue, LEEgressTarget target, CancellationToken token)
    {
        try
        {
            if (_audioIncluded)
            {
                try { _encoder = new LEEgressAacEncoder(_toolsDirectory, target.AudioBitrate); }
                catch (Exception ex) { SetState(LEEgressStates.Connecting, "Audio AAC no disponible; se transmite solo video: " + ex.Message); }
            }
            int attempt = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var client = await LEEgressRtmpClient.ConnectAsync(target, token).ConfigureAwait(false);
                    attempt = 0;
                    await PumpAsync(client, queue, target, token).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (!token.IsCancellationRequested && ex is IOException or System.Net.Sockets.SocketException or TimeoutException or InvalidDataException or System.Security.Authentication.AuthenticationException)
                {
                    lock (_gate) { _reconnects++; _dropUntilKey = true; }
                    SetState(LEEgressStates.Reconnecting, ex.Message);
                    while (queue.Reader.TryRead(out _)) { }
                    await Task.Delay(BackoffLE[Math.Min(attempt++, BackoffLE.Length - 1)], token).ConfigureAwait(false);
                    RequestKeyFrame();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { SetState(LEEgressStates.Error, ex.Message); return; }
        finally { _encoder?.Dispose(); _encoder = null; }
        SetState(LEEgressStates.Idle, null);
    }

    private async Task PumpAsync(LEEgressRtmpClient client, Channel<Item> queue, LEEgressTarget target, CancellationToken token)
    {
        byte[] avc;
        int privacyEpoch;
        lock (_gate)
        {
            avc = _avcConfiguration ?? throw new IOException("Falta la configuración H.264.");
            privacyEpoch = _privacyEpoch;
        }
        var session = _video.StatusVE.Session;
        await client.SendMetadataAsync(session?.Width ?? 0, session?.Height ?? 0, _encoder is not null, token).ConfigureAwait(false);
        await client.SendVideoAsync(0, VideoTag(true, 0, avc), token).ConfigureAwait(false);
        if (_encoder is not null) await client.SendAudioAsync(0, AudioTag(0, LEEgressAacEncoder.AudioSpecificConfigLE), token).ConfigureAwait(false);
        long? baseUs = null, audioUs = null;
        long sentBefore = 0;
        bool skipToKey = true, announced = false;
        RequestKeyFrame();
        await foreach (var item in queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
        {
            if (client.Failure is { } failure) throw new IOException("La conexión RTMP se cerró: " + failure.Message, failure);
            bool isProtected;
            int currentEpoch;
            lock (_gate) { isProtected = _protected; currentEpoch = _privacyEpoch; }
            // Un elemento leído justo antes de activar privacidad tampoco se envía.
            if (isProtected && item.Kind != Kind.Configuration) continue;
            if (currentEpoch != privacyEpoch)
            {
                // El AAC retiene PCM anterior a la pausa; se reemplaza para no emitirlo al reanudar.
                privacyEpoch = currentEpoch;
                audioUs = null;
                if (_encoder is not null)
                {
                    _encoder.Dispose();
                    _encoder = null;
                    _encoder = new LEEgressAacEncoder(_toolsDirectory, target.AudioBitrate);
                }
            }
            bool late = TimeSpan.FromMilliseconds(Environment.TickCount64 - item.EnqueuedTicks) > MaxLagLE;
            switch (item.Kind)
            {
                case Kind.Configuration:
                    await client.SendVideoAsync(Stamp(baseUs, item.TimeUs), VideoTag(true, 0, item.Data), token).ConfigureAwait(false);
                    skipToKey = true;
                    break;
                case Kind.Video:
                    if (late && !item.KeyFrame) { skipToKey = true; Interlocked.Increment(ref _droppedVideo); RequestKeyFrame(); continue; }
                    if (skipToKey && !item.KeyFrame) { Interlocked.Increment(ref _droppedVideo); continue; }
                    skipToKey = false;
                    baseUs ??= item.TimeUs;
                    if (item.TimeUs < baseUs) continue;
                    await client.SendVideoAsync(Stamp(baseUs, item.TimeUs), VideoTag(item.KeyFrame, 1, VEMediaMatroska.AvcSample(item.Data)), token).ConfigureAwait(false);
                    if (!announced) { announced = true; lock (_gate) _status = _status with { LiveSince = DateTimeOffset.Now }; SetState(_protected ? LEEgressStates.Paused : LEEgressStates.Live, null); }
                    break;
                case Kind.Audio:
                    if (_encoder is null || baseUs is null || late) continue;
                    audioUs ??= Math.Max(item.TimeUs, baseUs.Value);
                    foreach (var frame in _encoder.Encode(item.Data))
                    {
                        await client.SendAudioAsync(Stamp(baseUs, audioUs.Value), AudioTag(1, frame), token).ConfigureAwait(false);
                        audioUs += LEEgressAacEncoder.FrameSamplesLE * 1_000_000L / 48000;
                        if (!_status.AudioIncluded) { lock (_gate) _status = _status with { AudioIncluded = true }; Publish(); }
                    }
                    break;
            }
            Interlocked.Add(ref _bytesSent, client.BytesWritten - sentBefore);
            sentBefore = client.BytesWritten;
        }
    }

    private static uint Stamp(long? baseUs, long timeUs) => baseUs is null ? 0 : (uint)Math.Max(0, (timeUs - baseUs.Value) / 1000);

    private static byte[] VideoTag(bool key, byte packetType, byte[] data)
    {
        var tag = new byte[data.Length + 5];
        tag[0] = (byte)((key ? 0x10 : 0x20) | 7); // frame type + AVC
        tag[1] = packetType; // 0 = AVCDecoderConfigurationRecord, 1 = NALU (sin B-frames, CTS 0)
        data.CopyTo(tag, 5);
        return tag;
    }

    private static byte[] AudioTag(byte packetType, byte[] data)
    {
        var tag = new byte[data.Length + 2];
        tag[0] = 0xAF; // AAC, 44 kHz flag (obligatorio en FLV), 16 bit, estéreo
        tag[1] = packetType;
        data.CopyTo(tag, 2);
        return tag;
    }

    private void RequestKeyFrame()
    {
        long now = Environment.TickCount64, last = Interlocked.Read(ref _lastKeyRequest);
        if (now - last < 1000 || Interlocked.CompareExchange(ref _lastKeyRequest, now, last) != last) return;
        if (_video.RequestKeyFrameVE is not { } request) return;
        _ = Task.Run(async () =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await request(deadline.Token).ConfigureAwait(false); } catch { }
        });
    }

    private void SetState(LEEgressStates state, string? error)
    {
        lock (_gate) _status = _status with { State = state, Error = error, Reconnects = _reconnects };
        Publish();
    }

    private void Publish() { try { StatusChangedLE?.Invoke(this, StatusLE); } catch { } }

    public async ValueTask DisposeAsync()
    {
        _video.PacketReceivedVE -= OnPacket;
        _video.StatusChangedVE -= OnVideoState;
        _audio.FrameDecodedVE -= OnAudio;
        await StopAsync().ConfigureAwait(false);
    }

}
