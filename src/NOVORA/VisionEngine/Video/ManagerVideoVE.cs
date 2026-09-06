using System.IO;
using NOVORA.Services;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Renderer;
using NOVORA.VisionEngine.Transport;

namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Ejecuta captura, protocolo y FFmpeg y transfiere cada AVFrame clonado a RendererVE.
/// </summary>
public sealed class ManagerVideoVE : IAsyncDisposable
{
    private readonly NovoraPaths _paths;
    private readonly object _statusGate = new();

    private StatusVideoVE _status =
        StatusVideoVE.CreateInitialVE();

    private CancellationTokenSource? _runCtsVE;
    private Task? _runTaskVE;
    private DecoderVideoVE? _decoderVE;
    private MergerVideoVE? _mergerVE;
    private DemuxerVideoVE? _demuxerVE;
    private ManagerRendererVE? _rendererVE;

    private long _packetsReceivedVE;
    private long _bytesReceivedVE;
    private long _configurationPacketsVE;
    private long _keyFramesReceivedVE;
    private long _framesDecodedVE;
    private long _decodeErrorsVE;
    private long _lastStatusPublishMsVE;

    private bool _disposed;

    public ManagerVideoVE(NovoraPaths paths)
    {
        _paths = paths
            ?? throw new ArgumentNullException(nameof(paths));
    }

    public event EventHandler<StatusVideoVE>? StatusChangedVE;

    public event EventHandler<FrameVideoVE>? FrameDecodedVE;

    public StatusVideoVE StatusVE
    {
        get
        {
            lock (_statusGate)
            {
                return _status;
            }
        }
    }

    public bool IsRunningVE
        => _runTaskVE is not null &&
           !_runTaskVE.IsCompleted;


    public void AttachRendererVE(ManagerRendererVE renderer)
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
        SessionTransportVE transport,
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
                State = StatesVideoVE.Opening,
                RendererEnabled = false,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Message = "Abriendo stream de video VisionEngine.",
                LastError = null
            });

        DemuxerVideoVE demuxer =
            new(videoStream);

        demuxer.SessionChangedVE +=
            Demuxer_SessionChangedVE;

        try
        {
            SessionProtocolVE session =
                await demuxer
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

            CodecProtocolVE codec = demuxer.CodecVE
                ?? throw new InvalidDataException(
                    "VisionEngine no recibió codec de video.");

            DecoderVideoVE decoder =
                new(_paths);

            try
            {
                decoder.InitializeVE(codec);
            }
            catch
            {
                decoder.Dispose();
                throw;
            }

            _demuxerVE = demuxer;
            _decoderVE = decoder;

            _mergerVE = codec.RequiresConfigurationMergeVE()
                ? new MergerVideoVE()
                : null;

            if (_rendererVE is not null)
                await _rendererVE.StartAsync(cancellationToken).ConfigureAwait(false);

            // El token recibido sólo gobierna el arranque. Una vez abierto el
            // stream, el ciclo de vida pertenece a VisionEngine y se cancela
            // explícitamente desde StopAsync().
            _runCtsVE =
                new CancellationTokenSource();

            PublishStatusVE(
                new StatusVideoVE(
                    State: StatesVideoVE.Streaming,
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

    public async Task StopAsync()
    {
        if (_runTaskVE is null)
        {
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

            PublishStatusVE(
                StatusVideoVE.CreateInitialVE());

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

            // Los FrameVideoVE clonados usan av_frame_free. El renderer debe
            // drenar/liberar su cola antes de descargar libavutil.
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

            _mergerVE?.ResetVE();
            _mergerVE = null;
            _demuxerVE = null;

            PublishStatusVE(
                StatusVideoVE.CreateInitialVE() with
                {
                    Stats = SnapshotStatsVE(),
                    Message =
                        "Video VisionEngine detenido; " +
                        "renderer detenido."
                });
        }
    }

    private async Task RunLoopVE(
        DemuxerVideoVE demuxer,
        DecoderVideoVE decoder,
        CodecProtocolVE codec,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PacketVideoVE? packet =
                    await demuxer
                        .ReadPacketAsync(cancellationToken)
                        .ConfigureAwait(false);

                if (packet is null)
                {
                    continue;
                }

                Interlocked.Increment(
                    ref _packetsReceivedVE);

                Interlocked.Add(
                    ref _bytesReceivedVE,
                    packet.LengthVE);

                if (packet.IsConfiguration)
                {
                    Interlocked.Increment(
                        ref _configurationPacketsVE);
                }

                if (packet.IsKeyFrame)
                {
                    Interlocked.Increment(
                        ref _keyFramesReceivedVE);
                }

                PacketVideoVE? decodePacket = packet;

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

                SessionProtocolVE session =
                    demuxer.SessionVE
                    ?? throw new InvalidDataException(
                        "VisionEngine perdió el session header del video.");

                try
                {
                    IReadOnlyList<FrameVideoVE> frames =
                        decoder.DecodeVE(
                            decodePacket,
                            session);

                    foreach (FrameVideoVE frame in frames)
                    {
                        Interlocked.Increment(
                            ref _framesDecodedVE);

                        try
                        {
                            FrameDecodedVE?.Invoke(this, frame);
                        }
                        catch
                        {
                            // Observadores de métricas no pueden romper decode.
                        }

                        if (_rendererVE is not null)
                        {
                            try
                            {
                                _rendererVE.QueueFrameVE(frame);
                            }
                            catch
                            {
                                frame.Dispose();
                                throw;
                            }
                        }
                        else
                        {
                            frame.Dispose();
                        }
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
                    State = StatesVideoVE.EndOfStream,
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
                    State = StatesVideoVE.Failed,
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
        SessionProtocolVE session)
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

        StatusVideoVE current = StatusVE;

        if (current.State != StatesVideoVE.Streaming)
        {
            return;
        }

        PublishStatusVE(
            current with
            {
                Stats = SnapshotStatsVE(),
                RendererEnabled = _rendererVE?.StatusVE.RendererEnabled == true,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
    }

    private StatsVideoVE SnapshotStatsVE()
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
        StatusRendererVE rendererStatus)
    {
        PublishStatusVE(StatusVE with
        {
            RendererEnabled = rendererStatus.RendererEnabled,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            LastError = rendererStatus.State == StatesRendererVE.Failed
                ? rendererStatus.LastError
                : StatusVE.LastError
        });
    }

    private void PublishStatusVE(
        StatusVideoVE status)
    {
        EventHandler<StatusVideoVE>? handler;

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
