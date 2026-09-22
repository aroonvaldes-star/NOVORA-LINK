using System.IO;
using NOVORA.Service;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Renderer;
using NOVORA.VisionEngine.Transport;

namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Ejecuta captura, protocolo y FFmpeg y transfiere cada AVFrame clonado a RendererVE.
/// </summary>
public sealed class VEVideoManager : IAsyncDisposable
{
    private readonly NLServiceNovoraPaths _paths;
    private readonly object _statusGate = new();

    private VEVideoStatus _status =
        VEVideoStatus.CreateInitialVE();

    private CancellationTokenSource? _runCtsVE;
    private Task? _runTaskVE;
    private VEVideoDecoder? _decoderVE;
    private VEVideoMerger? _mergerVE;
    private VEVideoDemuxer? _demuxerVE;
    private VERendererManager? _rendererVE;

    private long _packetsReceivedVE;
    private long _bytesReceivedVE;
    private long _configurationPacketsVE;
    private long _keyFramesReceivedVE;
    private long _framesDecodedVE;
    private long _decodeErrorsVE;
    private long _lastStatusPublishMsVE;

    private bool _disposed;

    public VEVideoManager(NLServiceNovoraPaths paths)
    {
        _paths = paths
            ?? throw new ArgumentNullException(nameof(paths));
    }

    public event EventHandler<VEVideoStatus>? StatusChangedVE;

    public event EventHandler<VEVideoFrame>? FrameDecodedVE;
    public event EventHandler<VEVideoPacket>? PacketReceivedVE;

    public VEVideoStatus StatusVE
    {
        get
        {
            lock (_statusGate)
            {
                return _status;
            }
        }
    }

    public bool PreferNvidiaVE { get; set; }
    public Func<CancellationToken, Task>? RequestKeyFrameVE { get; set; }

    public bool IsRunningVE
        => _runTaskVE is not null &&
           !_runTaskVE.IsCompleted;


    public void AttachRendererVE(VERendererManager renderer)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(renderer);

        if (ReferenceEquals(_rendererVE, renderer))
            return;

        if (_rendererVE is not null)
            _rendererVE.StatusChangedVE -= Renderer_StatusChangedVE;

        _rendererVE = renderer;
        _rendererVE.StatusChangedVE += Renderer_StatusChangedVE;

        PublishStatusVE(StatusVE with
        {
            RendererEnabled = renderer.StatusVE.RendererEnabled,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    public async Task StartAsync(
        VETransportSession transport,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(transport);

        if (_runTaskVE is not null)
        {
            throw new InvalidOperationException(
                "El pipeline de video VisionEngine ya fue iniciado.");
        }

        Stream? videoStream = transport.VideoStream;

        if (videoStream is null)
        {
            throw new InvalidOperationException(
                "La sesión VisionEngine no contiene un stream de video.");
        }

        ResetCountersVE();

        PublishStatusVE(
            _status with
            {
                State = VEVideoStates.Opening,
                DecoderName = "Sin decoder",
                NvdecActive = false,
                DecoderFallbackReason = null,
                RendererEnabled = false,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Message = "Abriendo stream de video VisionEngine.",
                LastError = null
            });

        VEVideoDemuxer demuxer =
            new(videoStream);

        demuxer.SessionChangedVE +=
            Demuxer_SessionChangedVE;

        try
        {
            VEProtocolSession session =
                await demuxer
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

            VEProtocolCodec codec = demuxer.CodecVE
                ?? throw new InvalidDataException(
                    "VisionEngine no recibió codec de video.");

            VEVideoDecoder decoder =
                new(_paths);

            try
            {
                decoder.InitializeVE(codec, PreferNvidiaVE);
            }
            catch
            {
                decoder.Dispose();
                throw;
            }

            _demuxerVE = demuxer;
            _decoderVE = decoder;

            _mergerVE = codec.RequiresConfigurationMergeVE()
                ? new VEVideoMerger()
                : null;

            if (_rendererVE is not null)
                await _rendererVE.StartAsync(cancellationToken).ConfigureAwait(false);

            // El token recibido sólo gobierna el arranque. Una vez abierto el
            // stream, el ciclo de vida pertenece a VisionEngine y se cancela
            // explícitamente desde StopAsync().
            _runCtsVE =
                new CancellationTokenSource();

            PublishStatusVE(
                new VEVideoStatus(
                    State: VEVideoStates.Streaming,
                    Codec: codec,
                    Session: session,
                    Stats: SnapshotStatsVE(),
                    RendererEnabled: _rendererVE?.StatusVE.RendererEnabled == true,
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    Message:
                        $"Video {codec} activo {session.Width}x{session.Height}; " +
                        "RendererVE enlazado al pipeline.",
                    LastError: null));

            _runTaskVE = RunLoopVE(
                demuxer,
                decoder,
                codec,
                _runCtsVE.Token);
        }
        catch
        {
            demuxer.SessionChangedVE -=
                Demuxer_SessionChangedVE;

            if (_rendererVE is not null)
            {
                try
                {
                    await _rendererVE.StopAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }

            _decoderVE?.Dispose();
            _decoderVE = null;
            _demuxerVE = null;
            _mergerVE = null;

            throw;
        }
    }

    public async Task StopAsync(
        bool preserveRendererVE = false)
    {
        if (_runTaskVE is null)
        {
            if (_rendererVE is not null &&
                !preserveRendererVE)
            {
                try
                {
                    await _rendererVE.StopAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }

            PublishStatusVE(
                VEVideoStatus.CreateInitialVE());

            return;
        }

        try
        {
            _runCtsVE?.Cancel();

            try
            {
                await _runTaskVE
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            if (_demuxerVE is not null)
            {
                _demuxerVE.SessionChangedVE -=
                    Demuxer_SessionChangedVE;
            }

            _runCtsVE?.Dispose();
            _runCtsVE = null;
            _runTaskVE = null;

            // Los VEVideoFrame clonados usan av_frame_free. El renderer debe
            // drenar/liberar su cola antes de descargar libavutil.
            if (_rendererVE is not null &&
                !preserveRendererVE)
            {
                try
                {
                    await _rendererVE.StopAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }

            _decoderVE?.Dispose();
            _decoderVE = null;

            _mergerVE?.ResetVE();
            _mergerVE = null;
            _demuxerVE = null;

            PublishStatusVE(
                VEVideoStatus.CreateInitialVE() with
                {
                    Stats = SnapshotStatsVE(),
                    Message =
                        preserveRendererVE
                            ? "Video VisionEngine en transición; renderer conservado."
                            : "Video VisionEngine detenido; renderer detenido."
                });
        }
    }

    private async Task RunLoopVE(
        VEVideoDemuxer demuxer,
        VEVideoDecoder decoder,
        VEProtocolCodec codec,
        CancellationToken cancellationToken)
    {
        bool awaitingKeyFrame = false;
        long recoveryStarted = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                VEVideoPacket? packet =
                    await demuxer
                        .ReadPacketAsync(cancellationToken)
                        .ConfigureAwait(false);

                if (packet is null)
                {
                    continue;
                }

                try { PacketReceivedVE?.Invoke(this, packet); }
                catch { /* Observers cannot interrupt video decoding. */ }

                Interlocked.Increment(
                    ref _packetsReceivedVE);

                Interlocked.Add(
                    ref _bytesReceivedVE,
                    packet.LengthVE);

                if (packet.IsConfiguration)
                {
                    decoder.ConfigureVE(packet.Data);
                    Interlocked.Increment(
                        ref _configurationPacketsVE);
                }

                if (packet.IsKeyFrame)
                {
                    Interlocked.Increment(
                        ref _keyFramesReceivedVE);
                }

                if (awaitingKeyFrame && !packet.IsConfiguration)
                {
                    if (Environment.TickCount64 - recoveryStarted > 5000)
                        throw new InvalidOperationException("Fallback software: no llegó un keyframe en 5 segundos.");
                    if (!packet.IsKeyFrame) continue;
                    awaitingKeyFrame = false;
                }

                VEVideoPacket? decodePacket = packet;

                if (_mergerVE is not null)
                {
                    decodePacket =
                        _mergerVE.MergeVE(packet);
                }
                else if (packet.IsConfiguration)
                {
                    // Igual que el decoder de scrcpy: los config packets no se
                    // envían directamente a avcodec_send_packet.
                    decodePacket = null;
                }

                if (decodePacket is null)
                {
                    PublishStreamingSnapshotVE();
                    continue;
                }

                VEProtocolSession session =
                    demuxer.SessionVE
                    ?? throw new InvalidDataException(
                        "VisionEngine perdió el session header del video.");

                try
                {
                    IReadOnlyList<VEVideoFrame> frames;
                    try { frames = decoder.DecodeVE(decodePacket, session); }
                    catch (VEVideoDecoder.VEVideoFfmpegException ex) when (decoder.IsNvidiaVE)
                    {
                        decoder.FallBackToSoftwareVE(ex);
                        _mergerVE?.ReplayConfigurationVE();
                        if (!packet.IsKeyFrame)
                        {
                            awaitingKeyFrame = true;
                            recoveryStarted = Environment.TickCount64;
                            PublishStreamingSnapshotVE();
                            if (RequestKeyFrameVE is not null)
                                await RequestKeyFrameVE(cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        frames = decoder.DecodeVE(_mergerVE?.MergeVE(packet) ?? packet, session);
                    }

                    int transferred = 0;
                    try
                    {
                        foreach (VEVideoFrame frame in frames)
                        {
                            Interlocked.Increment(ref _framesDecodedVE);
                            try { FrameDecodedVE?.Invoke(this, frame); }
                            catch { /* Los observadores no poseen ni interrumpen los frames. */ }
                            if (_rendererVE is not null) _rendererVE.QueueFrameVE(frame);
                            else frame.Dispose();
                            transferred++;
                        }
                    }
                    finally
                    {
                        // Si falla la entrega, también libera los clones restantes del lote.
                        for (int i = transferred; i < frames.Count; i++) frames[i].Dispose();
                    }
                }
                catch
                {
                    Interlocked.Increment(
                        ref _decodeErrorsVE);

                    throw;
                }

                PublishStreamingSnapshotVE();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (EndOfStreamException)
        {
            PublishStatusVE(
                _status with
                {
                    State = VEVideoStates.EndOfStream,
                    Stats = SnapshotStatsVE(),
                    RendererEnabled = false,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message =
                        "Android cerró el stream de video VisionEngine.",
                    LastError = null
                });
        }
        catch (Exception ex)
        {
            PublishStatusVE(
                _status with
                {
                    State = VEVideoStates.Failed,
                    Stats = SnapshotStatsVE(),
                    RendererEnabled = false,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message =
                        "Falló el pipeline de video VisionEngine.",
                    LastError = ex.Message
                });
        }
    }

    private void Demuxer_SessionChangedVE(
        object? sender,
        VEProtocolSession session)
    {
        PublishStatusVE(
            _status with
            {
                Session = session,
                Stats = SnapshotStatsVE(),
                RendererEnabled = _rendererVE?.StatusVE.RendererEnabled == true,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Message =
                    $"Sesión de video actualizada a " +
                    $"{session.Width}x{session.Height}."
            });
    }

    private void PublishStreamingSnapshotVE()
    {
        // Evita convertir las métricas en polling/event spam. El stream puede
        // transportar decenas de paquetes por frame; 4 snapshots/s bastan para
        // observabilidad sin competir con decode o transporte.
        long nowMs = Environment.TickCount64;

        long previousMs =
            Interlocked.Read(
                ref _lastStatusPublishMsVE);

        if (previousMs != 0 &&
            nowMs - previousMs < 250)
        {
            return;
        }

        Interlocked.Exchange(
            ref _lastStatusPublishMsVE,
            nowMs);

        VEVideoStatus current = StatusVE;

        if (current.State != VEVideoStates.Streaming)
        {
            return;
        }

        PublishStatusVE(
            current with
            {
                Stats = SnapshotStatsVE(),
                DecoderName = _decoderVE?.DecoderNameVE ?? "Sin decoder",
                NvdecActive = _decoderVE?.NvdecActiveVE == true,
                DecoderFallbackReason = _decoderVE?.FallbackReasonVE,
                RendererEnabled = _rendererVE?.StatusVE.RendererEnabled == true,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
    }

    private VEVideoStats SnapshotStatsVE()
        => new(
            PacketsReceived:
                Interlocked.Read(
                    ref _packetsReceivedVE),

            BytesReceived:
                Interlocked.Read(
                    ref _bytesReceivedVE),

            ConfigurationPackets:
                Interlocked.Read(
                    ref _configurationPacketsVE),

            KeyFramesReceived:
                Interlocked.Read(
                    ref _keyFramesReceivedVE),

            FramesDecoded:
                Interlocked.Read(
                    ref _framesDecodedVE),

            DecodeErrors:
                Interlocked.Read(
                    ref _decodeErrorsVE));

    private void ResetCountersVE()
    {
        Interlocked.Exchange(
            ref _packetsReceivedVE,
            0);

        Interlocked.Exchange(
            ref _bytesReceivedVE,
            0);

        Interlocked.Exchange(
            ref _configurationPacketsVE,
            0);

        Interlocked.Exchange(
            ref _keyFramesReceivedVE,
            0);

        Interlocked.Exchange(
            ref _framesDecodedVE,
            0);

        Interlocked.Exchange(
            ref _decodeErrorsVE,
            0);

        Interlocked.Exchange(
            ref _lastStatusPublishMsVE,
            0);
    }

    private void Renderer_StatusChangedVE(
        object? sender,
        VERendererStatus rendererStatus)
    {
        PublishStatusVE(StatusVE with
        {
            RendererEnabled = rendererStatus.RendererEnabled,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            LastError = rendererStatus.State == VERendererStates.Failed
                ? rendererStatus.LastError
                : StatusVE.LastError
        });
    }

    private void PublishStatusVE(
        VEVideoStatus status)
    {
        if (_decoderVE is not null)
            status = status with
            {
                DecoderName = _decoderVE.DecoderNameVE,
                NvdecActive = status.State == VEVideoStates.Streaming && _decoderVE.NvdecActiveVE,
                DecoderFallbackReason = _decoderVE.FallbackReasonVE
            };
        else status = status with { NvdecActive = false };
        EventHandler<VEVideoStatus>? handler;

        lock (_statusGate)
        {
            _status = status;
            handler = StatusChangedVE;
        }

        handler?.Invoke(
            this,
            status);
    }

    private void ThrowIfDisposedVE()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await StopAsync()
                .ConfigureAwait(false);
        }
        finally
        {
            if (_rendererVE is not null)
                _rendererVE.StatusChangedVE -= Renderer_StatusChangedVE;

            _disposed = true;
        }
    }
}
