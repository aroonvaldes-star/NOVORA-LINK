using NOVORA.Services;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Transport;
using System.Net.Sockets;

namespace NOVORA.VisionEngine.Audio;

public sealed class ManagerAudioVE : IAsyncDisposable
{
    private readonly NovoraPaths _paths;

    private readonly object _gateVE =
        new();

    private StatusAudioVE _statusVE =
        StatusAudioVE.CreateInitialVE();

    private CancellationTokenSource? _ctsVE;
    private Task? _taskVE;

    private DecoderAudioVE? _decoderVE;
    private PlayerAudioVE? _playerVE;

    private long _packetsVE;
    private long _bytesVE;
    private long _framesVE;
    private long _pcmBytesVE;
    private long _decodeErrorsVE;
    private long _playbackErrorsVE;

    private long _lastStatusPublishMsVE;

    private string _selectedOutputVE =
        OutputAudioVE.DefaultValueVE;

    private int _privacyProtectedVE;
    private bool _disposedVE;

    public ManagerAudioVE(
        NovoraPaths paths)
    {
        _paths =
            paths ??
            throw new ArgumentNullException(
                nameof(paths));
    }

    public event EventHandler<StatusAudioVE>?
        StatusChangedVE;

    public event EventHandler<FrameAudioVE>?
        FrameDecodedVE;

    public StatusAudioVE StatusVE
    {
        get
        {
            lock (_gateVE)
            {
                return _statusVE;
            }
        }
    }

    /// <summary>
    /// Salida solicitada por NOVORA.
    ///
    /// Si AudioVE ya está reproduciendo, cambiar esta propiedad
    /// mueve inmediatamente el stream SDL al nuevo endpoint.
    /// </summary>
    public string SelectedOutputVE
    {
        get
        {
            lock (_gateVE)
            {
                return _selectedOutputVE;
            }
        }

        set
        {
            ChangeOutputVE(
                value);
        }
    }

    public bool IsPrivacyProtectedVE =>
        Volatile.Read(ref _privacyProtectedVE) != 0;

    public void SetPrivacyProtectedVE(bool protectedVE)
    {
        int next = protectedVE ? 1 : 0;
        int previous = Interlocked.Exchange(ref _privacyProtectedVE, next);
        if (previous == next || !protectedVE)
            return;

        PlayerAudioVE? player;
        lock (_gateVE) player = _playerVE;

        if (player is not null)
        {
            try { player.ClearQueuedAudioVE(); }
            catch { }
        }
    }

    public string ActiveOutputVE
    {
        get
        {
            PlayerAudioVE? player;

            lock (_gateVE)
            {
                player =
                    _playerVE;
            }

            return player?.BoundDeviceNameVE ??
                   _selectedOutputVE;
        }
    }

    public void ChangeOutputVE(
        string? selectedOutput)
    {
        ThrowIfDisposedVE();

        string normalized =
            string.IsNullOrWhiteSpace(
                selectedOutput)
                ? OutputAudioVE.DefaultValueVE
                : selectedOutput.Trim();

        PlayerAudioVE? player;

        lock (_gateVE)
        {
            _selectedOutputVE =
                normalized;

            player =
                _playerVE;
        }

        /*
         * Si todavía no hay player, sólo guardamos la selección.
         * StartAsync() la utilizará al abrir AudioVE.
         */
        if (player is null)
        {
            return;
        }

        /*
         * La desactivación completa requiere reiniciar la sesión,
         * porque también afecta el AudioEnabled del servidor Android.
         */
        if (
            string.Equals(
                normalized,
                OutputAudioVE.DisabledValueVE,
                StringComparison.OrdinalIgnoreCase)
        )
        {
            PublishVE(
                StatusVE.State,
                StatusVE.Codec,
                null,
                true,
                "Salida AudioVE marcada como desactivada; se aplicará al reiniciar VisionEngine.");

            return;
        }

        try
        {
            player.SwitchOutputVE(
                normalized);

            PublishVE(
                StatusVE.State,
                StatusVE.Codec,
                null,
                true,
                $"AudioVE → {player.BoundDeviceNameVE}.");
        }
        catch (Exception ex)
        {
            Interlocked.Increment(
                ref _playbackErrorsVE);

            PublishVE(
                StatusVE.State,
                StatusVE.Codec,
                ex.Message,
                true,
                $"No se pudo cambiar la salida AudioVE: {ex.Message}");

            throw;
        }
    }

    public async Task StartAsync(
        SessionTransportVE transport,
        bool playbackEnabled,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();

        ArgumentNullException.ThrowIfNull(
            transport);

        if (_taskVE is not null)
        {
            throw new InvalidOperationException(
                "Audio VisionEngine ya está iniciado.");
        }

        NetworkStream stream =
            transport.AudioStream ??
            throw new InvalidOperationException(
                "La sesión VisionEngine no contiene audio socket.");

        ResetVE();

        PublishVE(
            StatesAudioVE.Opening,
            null,
            null,
            playbackEnabled,
            "Abriendo audio VisionEngine.");

        DemuxerAudioVE demuxer =
            new(stream);

        CodecProtocolVE codec =
            await demuxer
                .OpenAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        if (
            codec ==
            CodecProtocolVE.Disabled
        )
        {
            PublishVE(
                StatesAudioVE.Disabled,
                codec,
                null,
                false,
                "Android deshabilitó el stream de audio VisionEngine.");

            return;
        }

        if (
            codec ==
            CodecProtocolVE.ConfigurationError
        )
        {
            throw new InvalidOperationException(
                "Android reportó error de configuración de audio VisionEngine.");
        }

        DecoderAudioVE decoder =
            new(_paths);

        decoder.InitializeVE(
            codec);

        PlayerAudioVE? player =
            null;

        if (playbackEnabled)
        {
            string output;

            lock (_gateVE)
            {
                output =
                    _selectedOutputVE;
            }

            player =
                new PlayerAudioVE(
                    _paths);

            player.OpenVE(
                output);
        }

        _decoderVE =
            decoder;

        lock (_gateVE)
        {
            _playerVE =
                player;
        }

        _ctsVE =
            new CancellationTokenSource();

        string outputMessage =
            player is null
                ? "reproducción desactivada"
                : $"salida '{player.BoundDeviceNameVE}'";

        PublishVE(
            StatesAudioVE.Streaming,
            codec,
            null,
            playbackEnabled,
            $"Audio {codec} VisionEngine activo; {outputMessage}.");

        _taskVE =
            RunVE(
                demuxer,
                decoder,
                codec,
                _ctsVE.Token);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts =
            _ctsVE;

        Task? task =
            _taskVE;

        _ctsVE =
            null;

        _taskVE =
            null;

        cts?.Cancel();

        if (task is not null)
        {
            try
            {
                await task
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        cts?.Dispose();

        PlayerAudioVE? player;

        lock (_gateVE)
        {
            player =
                _playerVE;

            _playerVE =
                null;
        }

        player?.Dispose();

        _decoderVE?.Dispose();

        _decoderVE =
            null;

        PublishVE(
            StatesAudioVE.Stopped,
            StatusVE.Codec,
            null,
            false,
            "Audio VisionEngine detenido.");
    }

    private async Task RunVE(
        DemuxerAudioVE demuxer,
        DecoderAudioVE decoder,
        CodecProtocolVE codec,
        CancellationToken cancellationToken)
    {
        try
        {
            while (
                !cancellationToken
                    .IsCancellationRequested)
            {
                PacketAudioVE packet =
                    await demuxer
                        .ReadPacketAsync(
                            cancellationToken)
                        .ConfigureAwait(false);

                Interlocked.Increment(
                    ref _packetsVE);

                Interlocked.Add(
                    ref _bytesVE,
                    packet.LengthVE);

                if (packet.IsConfiguration)
                {
                    PublishSnapshotVE(
                        codec);

                    continue;
                }

                IReadOnlyList<FrameAudioVE>
                    frames;

                try
                {
                    frames =
                        decoder.DecodeVE(
                            packet);
                }
                catch
                {
                    Interlocked.Increment(
                        ref _decodeErrorsVE);

                    throw;
                }

                foreach (
                    FrameAudioVE frame in frames)
                {
                    Interlocked.Increment(
                        ref _framesVE);

                    Interlocked.Add(
                        ref _pcmBytesVE,
                        frame.Pcm16Le.Length);

                    PlayerAudioVE? currentPlayer;

                    lock (_gateVE)
                    {
                        currentPlayer =
                            _playerVE;
                    }

                    bool expose =
                        Volatile.Read(ref _privacyProtectedVE) == 0;

                    if (
                        expose &&
                        currentPlayer is not null
                    )
                    {
                        try
                        {
                            /*
                             * El mismo objeto PlayerAudioVE continúa existiendo
                             * cuando SwitchOutputVE cambia de endpoint.
                             */
                            currentPlayer.QueueVE(
                                frame.Pcm16Le);
                        }
                        catch
                        {
                            Interlocked.Increment(
                                ref _playbackErrorsVE);

                            throw;
                        }
                    }

                    if (expose)
                    {
                        FrameDecodedVE?.Invoke(
                            this,
                            frame);
                    }
                }

                PublishSnapshotVE(
                    codec);
            }
        }
        catch (OperationCanceledException)
            when (
                cancellationToken
                    .IsCancellationRequested)
        {
        }
        catch (EndOfStreamException)
        {
            PublishVE(
                StatesAudioVE.EndOfStream,
                codec,
                null,
                HasPlayerVE(),
                "Android cerró el stream de audio VisionEngine.");
        }
        catch (Exception ex)
        {
            PublishVE(
                StatesAudioVE.Failed,
                codec,
                ex.Message,
                HasPlayerVE(),
                "Falló Audio VisionEngine.");
        }
    }

    private bool HasPlayerVE()
    {
        lock (_gateVE)
        {
            return _playerVE is not null;
        }
    }

    private StatsAudioVE SnapshotVE()
        => new(
            Interlocked.Read(
                ref _packetsVE),

            Interlocked.Read(
                ref _bytesVE),

            Interlocked.Read(
                ref _framesVE),

            Interlocked.Read(
                ref _pcmBytesVE),

            Interlocked.Read(
                ref _decodeErrorsVE),

            Interlocked.Read(
                ref _playbackErrorsVE));

    private void PublishSnapshotVE(
        CodecProtocolVE codec)
    {
        long now =
            Environment.TickCount64;

        long previous =
            Interlocked.Read(
                ref _lastStatusPublishMsVE);

        if (
            previous != 0 &&
            now - previous < 250
        )
        {
            return;
        }

        Interlocked.Exchange(
            ref _lastStatusPublishMsVE,
            now);

        PublishVE(
            StatusVE.State,
            codec,
            StatusVE.LastError,
            HasPlayerVE(),
            StatusVE.Message);
    }

    private void PublishVE(
        StatesAudioVE state,
        CodecProtocolVE? codec,
        string? error,
        bool playback,
        string message)
    {
        StatusAudioVE status =
            new(
                state,
                codec,
                SnapshotVE(),
                playback,
                DateTimeOffset.UtcNow,
                message,
                error);

        EventHandler<StatusAudioVE>?
            handler;

        lock (_gateVE)
        {
            _statusVE =
                status;

            handler =
                StatusChangedVE;
        }

        handler?.Invoke(
            this,
            status);
    }

    private void ResetVE()
    {
        _packetsVE = 0;
        _bytesVE = 0;
        _framesVE = 0;
        _pcmBytesVE = 0;
        _decodeErrorsVE = 0;
        _playbackErrorsVE = 0;

        _lastStatusPublishMsVE =
            0;
    }

    private void ThrowIfDisposedVE()
        => ObjectDisposedException.ThrowIf(
            _disposedVE,
            this);

    public async ValueTask DisposeAsync()
    {
        if (_disposedVE)
        {
            return;
        }

        await StopAsync()
            .ConfigureAwait(false);

        _disposedVE =
            true;
    }
}
