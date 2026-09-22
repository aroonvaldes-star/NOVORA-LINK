using NOVORA.Service;
using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Device;
using NOVORA.VisionEngine.Renderer;
using NOVORA.VisionEngine.Server;
using NOVORA.VisionEngine.Transport;
using NOVORA.VisionEngine.Video;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.VisionEngine.Core;

/// <summary>
/// Punto de entrada de VisionEngine Block D. Integra captura, decode, audio,
/// control y presentación SDL3/Direct3D11 sobre un VERendererHost suministrado
/// por la interfaz de NOVORA.
/// </summary>
public sealed class VECoreEngine : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleGate =
        new(1, 1);

    private readonly object _stateGate =
        new();

    private readonly VECoreRuntime _runtime;

    private VECoreStatus _status =
        VECoreStatus.CreateInitialVE();

    private VECoreSession? _session;
    private bool _disposed;

    public VECoreEngine()
        : this(new NLServiceNovoraPaths())
    {
    }

    public VECoreEngine(NLServiceNovoraPaths paths)
        : this(
            paths,
            new NLServiceADB(paths ?? throw new ArgumentNullException(nameof(paths))))
    {
    }

    public VECoreEngine(
        NLServiceNovoraPaths paths,
        NLServiceADB adb)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(adb);

        _runtime = new VECoreRuntime(paths, adb);
        _runtime.StatusChangedVE += Runtime_StatusChangedVE;
    }

    public event EventHandler<VECoreStatus>? StatusChangedVE;

    public VECoreStatus StatusVE
    {
        get
        {
            lock (_stateGate)
            {
                return _status;
            }
        }
    }

    public VECoreSession? SessionVE
    {
        get
        {
            lock (_stateGate)
            {
                return _session;
            }
        }
    }

    public VECoreRuntime RuntimeVE => _runtime;

    public bool IsInitializedVE => StatusVE.IsInitialized;

    public bool IsRunningVE => StatusVE.IsRunning;


    public void AttachRendererHostVE(VERendererHost host)
    {
        ThrowIfDisposedVE();
        _runtime.RendererVE.AttachHostVE(host);
    }

    public void DetachRendererHostVE()
    {
        ThrowIfDisposedVE();
        _runtime.RendererVE.DetachHostVE();
    }

    public async Task<VECoreResult> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();

        await _lifecycleGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_status.IsInitialized)
            {
                return VECoreResult.Ok(
                    "VisionEngine ya estaba inicializado.");
            }

            PublishStatusVE(
                _status with
                {
                    State = VECoreStates.Initializing,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = "Inicializando VisionEngine Block D.",
                    LastError = null
                });

            await _runtime.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

            PublishStatusVE(
                BuildRuntimeStatusVE(
                    _status with
                    {
                        State = VECoreStates.Ready,
                        IsInitialized = true,
                        IsRunning = false,
                        RendererEnabled = false,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        Message = "VisionEngine Block D listo; renderer espera su host visual.",
                        LastError = null
                    }));

            return VECoreResult.Ok(
                "VisionEngine Block D inicializado.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PublishFailureVE(
                "No fue posible inicializar VisionEngine.",
                ex);

            return VECoreResult.Fail(
                "No fue posible inicializar VisionEngine.",
                ex);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<VECoreResult> StartAsync(
        string deviceSerial,
        CancellationToken cancellationToken = default)
        => await StartAsync(
                deviceSerial,
                VEServerOptions.CreateDefaultVE(),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<VECoreResult> StartAsync(
        string deviceSerial,
        VEServerOptions options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();

        if (string.IsNullOrWhiteSpace(deviceSerial))
        {
            return VECoreResult.Fail(
                "El serial del dispositivo es obligatorio.");
        }

        ArgumentNullException.ThrowIfNull(options);

        await _lifecycleGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (!_status.IsInitialized)
            {
                return VECoreResult.Fail(
                    "VisionEngine debe inicializarse antes de iniciar una sesión.");
            }

            string serial = deviceSerial.Trim();

            if (_status.IsRunning)
            {
                if (string.Equals(
                        _status.DeviceSerial,
                        serial,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return VECoreResult.Ok(
                        "VisionEngine ya está activo para este dispositivo.");
                }

                return VECoreResult.Fail(
                    "VisionEngine ya controla otro dispositivo.");
            }

            VECoreSession session =
                VECoreSession.CreateVE(serial);

            lock (_stateGate)
            {
                _session = session;
            }

            PublishStatusVE(
                _status with
                {
                    State = VECoreStates.Starting,
                    IsRunning = false,
                    DeviceSerial = serial,
                    SessionId = session.SessionId,
                    RendererEnabled = false,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = "Iniciando pipeline multimedia y RendererVE.",
                    LastError = null
                });

            await _runtime.StartAsync(
                    serial,
                    options,
                    cancellationToken)
                .ConfigureAwait(false);

            UpdateSessionVE(
                session with
                {
                    State = VECoreStates.Running,
                    StartedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = "Pipeline VisionEngine Block D activo.",
                    LastError = null
                });

            PublishStatusVE(
                BuildRuntimeStatusVE(
                    _status with
                    {
                        State = VECoreStates.Running,
                        IsRunning = true,
                        RendererEnabled = false,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        Message = "VisionEngine Block D: video, renderer, audio, control y exchange activos.",
                        LastError = null
                    }));

            return VECoreResult.Ok(
                "VisionEngine Block D inició correctamente.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PublishFailureVE(
                "No fue posible iniciar VisionEngine.",
                ex);

            return VECoreResult.Fail(
                "No fue posible iniciar VisionEngine.",
                ex);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<VECoreResult> StopAsync(
        CancellationToken cancellationToken = default,
        bool preserveRendererVE = false)
    {
        ThrowIfDisposedVE();

        await _lifecycleGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (!_status.IsRunning &&
                _session is null)
            {
                return VECoreResult.Ok(
                    "VisionEngine ya estaba detenido.");
            }

            PublishStatusVE(
                _status with
                {
                    State = VECoreStates.Stopping,
                    IsRunning = false,
                    RendererEnabled = false,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = "Deteniendo VisionEngine.",
                    LastError = null
                });

            await _runtime.StopAsync(
                    cancellationToken,
                    preserveRendererVE)
                .ConfigureAwait(false);

            VECoreSession? session = SessionVE;

            if (session is not null)
            {
                UpdateSessionVE(
                    session with
                    {
                        State = VECoreStates.Stopped,
                        StoppedAtUtc = DateTimeOffset.UtcNow,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        Message = "Sesión VisionEngine detenida.",
                        LastError = null
                    });
            }

            lock (_stateGate)
            {
                _session = null;
            }

            PublishStatusVE(
                BuildRuntimeStatusVE(
                    _status with
                    {
                        State = VECoreStates.Ready,
                        IsRunning = false,
                        DeviceSerial = null,
                        SessionId = null,
                        RendererEnabled = false,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        Message = "VisionEngine Block D listo.",
                        LastError = null
                    }));

            return VECoreResult.Ok(
                "VisionEngine detenido correctamente.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PublishFailureVE(
                "No fue posible detener VisionEngine.",
                ex);

            return VECoreResult.Fail(
                "No fue posible detener VisionEngine.",
                ex);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private VECoreStatus BuildRuntimeStatusVE(
        VECoreStatus source)
    {
        VEVideoStatus video = _runtime.VideoVE.StatusVE;
        VEAudioStatus audio = _runtime.AudioVE.StatusVE;
        VEControlStatus control = _runtime.ControlVE.StatusVE;
        VERendererStatus renderer = _runtime.RendererVE.StatusVE;

        return source with
        {
            DeviceReady = _runtime.DeviceVE.StatusVE.State == VEDeviceStates.Ready,
            ServerRunning = _runtime.ServerVE.StatusVE.State == VEServerStates.Running,
            TransportConnected = _runtime.TransportVE.StateVE == VETransportStates.Connected,
            VideoStreaming = video.State == VEVideoStates.Streaming,
            VideoPacketsReceived = video.Stats.PacketsReceived,
            VideoFramesDecoded = video.Stats.FramesDecoded,
            VideoDecodeErrors = video.Stats.DecodeErrors,
            AudioStreaming = audio.State == VEAudioStates.Streaming,
            AudioPacketsReceived = audio.Stats.PacketsReceived,
            AudioFramesDecoded = audio.Stats.FramesDecoded,
            AudioDecodeErrors = audio.Stats.DecodeErrors,
            AudioPlaybackEnabled = audio.PlaybackEnabled,
            ControlReady = control.State == VEControlStates.Ready,
            ControlMessagesSent = control.Stats.MessagesSent,
            ControlMessagesReceived = control.Stats.MessagesReceived,
            RendererEnabled = renderer.RendererEnabled,
            RendererBackend = renderer.Backend,
            VideoFramesRendered = renderer.Metrics.FramesPresented,
            RendererFramesDropped = renderer.Metrics.FramesDropped,
            RendererErrors = renderer.Metrics.RenderErrors,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private void Runtime_StatusChangedVE(
        object? sender,
        EventArgs e)
    {
        if (_disposed) return;

        VECoreStatus updated = BuildRuntimeStatusVE(StatusVE);
        VEVideoStatus video = _runtime.VideoVE.StatusVE;
        VEAudioStatus audio = _runtime.AudioVE.StatusVE;
        VEControlStatus control = _runtime.ControlVE.StatusVE;
        VERendererStatus renderer = _runtime.RendererVE.StatusVE;

        if (video.State == VEVideoStates.Failed)
        {
            updated = updated with
            {
                State = VECoreStates.Failed,
                Message = "El pipeline de video VisionEngine falló.",
                LastError = video.LastError
            };
        }
        else if (renderer.State == VERendererStates.Failed)
        {
            updated = updated with
            {
                Message = "Renderer VisionEngine falló; transporte y decode permanecen observables.",
                LastError = renderer.LastError
            };
        }
        else if (audio.State == VEAudioStates.Failed)
        {
            updated = updated with
            {
                State = VECoreStates.Failed,
                Message = "El pipeline de audio VisionEngine falló.",
                LastError = audio.LastError
            };
        }
        else if (control.State == VEControlStates.Failed)
        {
            updated = updated with
            {
                State = VECoreStates.Failed,
                Message = "El canal de control VisionEngine falló.",
                LastError = control.LastError
            };
        }
        PublishStatusVE(updated);
    }

    private void UpdateSessionVE(
        VECoreSession session)
    {
        lock (_stateGate)
        {
            _session = session;
        }
    }

    private void PublishFailureVE(
        string message,
        Exception exception)
    {
        VECoreSession? session = SessionVE;

        if (session is not null)
        {
            UpdateSessionVE(
                session with
                {
                    State = VECoreStates.Failed,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = message,
                    LastError = exception.Message
                });
        }

        PublishStatusVE(
            BuildRuntimeStatusVE(
                _status with
                {
                    State = VECoreStates.Failed,
                    IsRunning = false,
                    RendererEnabled = false,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = message,
                    LastError = exception.Message
                }));
    }

    private void PublishStatusVE(
        VECoreStatus status)
    {
        EventHandler<VECoreStatus>? handler;

        lock (_stateGate)
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
            if (_status.IsRunning ||
                _session is not null)
            {
                await StopAsync()
                    .ConfigureAwait(false);
            }

            await _runtime.DisposeAsync()
                .ConfigureAwait(false);
        }
        finally
        {
            _runtime.StatusChangedVE -= Runtime_StatusChangedVE;
            _disposed = true;

            PublishStatusVE(
                _status with
                {
                    State = VECoreStates.Disposed,
                    IsRunning = false,
                    RendererEnabled = false,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = "VisionEngine liberado.",
                    LastError = null
                });

            _lifecycleGate.Dispose();
        }
    }
}
