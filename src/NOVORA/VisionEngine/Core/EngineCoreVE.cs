using NOVORA.Services;
using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Device;
using NOVORA.VisionEngine.Gamepad;
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
/// control y presentación SDL3/Direct3D11 sobre un HostRendererVE suministrado
/// por la interfaz de NOVORA.
/// </summary>
public sealed class EngineCoreVE : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleGate =
        new(1, 1);

    private readonly object _stateGate =
        new();

    private readonly RuntimeCoreVE _runtime;

    private StatusCoreVE _status =
        StatusCoreVE.CreateInitialVE();

    private SessionCoreVE? _session;
    private bool _disposed;

    public EngineCoreVE()
        : this(new NovoraPaths())
    {
    }

    public EngineCoreVE(NovoraPaths paths)
        : this(
            paths,
            new AdbService(
                paths ?? throw new ArgumentNullException(nameof(paths))))
    {
    }

    public EngineCoreVE(
        NovoraPaths paths,
        AdbService adb)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(adb);

        _runtime = new RuntimeCoreVE(paths, adb);
        _runtime.StatusChangedVE += Runtime_StatusChangedVE;
    }

    public event EventHandler<StatusCoreVE>? StatusChangedVE;

    public StatusCoreVE StatusVE
    {
        get
        {
            lock (_stateGate)
            {
                return _status;
            }
        }
    }

    public SessionCoreVE? SessionVE
    {
        get
        {
            lock (_stateGate)
            {
                return _session;
            }
        }
    }

    public RuntimeCoreVE RuntimeVE => _runtime;

    public bool IsInitializedVE => StatusVE.IsInitialized;

    public bool IsRunningVE => StatusVE.IsRunning;


    public void AttachRendererHostVE(HostRendererVE host)
    {
        ThrowIfDisposedVE();
        _runtime.RendererVE.AttachHostVE(host);
    }

    public void DetachRendererHostVE()
    {
        ThrowIfDisposedVE();
        _runtime.RendererVE.DetachHostVE();
    }

    public async Task<ResultCoreVE> InitializeAsync(
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
                return ResultCoreVE.Ok(
                    "VisionEngine ya estaba inicializado.");
            }

            PublishStatusVE(
                _status with
                {
                    State = StatesCoreVE.Initializing,
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
                        State = StatesCoreVE.Ready,
                        IsInitialized = true,
                        IsRunning = false,
                        RendererEnabled = false,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        Message = "VisionEngine Block D listo; renderer espera su host visual.",
                        LastError = null
                    }));

            return ResultCoreVE.Ok(
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

            return ResultCoreVE.Fail(
                "No fue posible inicializar VisionEngine.",
                ex);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<ResultCoreVE> StartAsync(
        string deviceSerial,
        CancellationToken cancellationToken = default)
        => await StartAsync(
                deviceSerial,
                OptionsServerVE.CreateDefaultVE(),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<ResultCoreVE> StartAsync(
        string deviceSerial,
        OptionsServerVE options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();

        if (string.IsNullOrWhiteSpace(deviceSerial))
        {
            return ResultCoreVE.Fail(
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
                return ResultCoreVE.Fail(
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
                    return ResultCoreVE.Ok(
                        "VisionEngine ya está activo para este dispositivo.");
                }

                return ResultCoreVE.Fail(
                    "VisionEngine ya controla otro dispositivo.");
            }

            SessionCoreVE session =
                SessionCoreVE.CreateVE(serial);

            lock (_stateGate)
            {
                _session = session;
            }

            PublishStatusVE(
                _status with
                {
                    State = StatesCoreVE.Starting,
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
                    State = StatesCoreVE.Running,
                    StartedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = "Pipeline VisionEngine Block D activo.",
                    LastError = null
                });

            PublishStatusVE(
                BuildRuntimeStatusVE(
                    _status with
                    {
                        State = StatesCoreVE.Running,
                        IsRunning = true,
                        RendererEnabled = false,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        Message = "VisionEngine Block D: video, renderer, audio, control, gamepad y exchange activos.",
                        LastError = null
                    }));

            return ResultCoreVE.Ok(
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

            return ResultCoreVE.Fail(
                "No fue posible iniciar VisionEngine.",
                ex);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<ResultCoreVE> StopAsync(
        CancellationToken cancellationToken = default)
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
                return ResultCoreVE.Ok(
                    "VisionEngine ya estaba detenido.");
            }

            PublishStatusVE(
                _status with
                {
                    State = StatesCoreVE.Stopping,
                    IsRunning = false,
                    RendererEnabled = false,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = "Deteniendo VisionEngine.",
                    LastError = null
                });

            await _runtime.StopAsync(cancellationToken)
                .ConfigureAwait(false);

            SessionCoreVE? session = SessionVE;

            if (session is not null)
            {
                UpdateSessionVE(
                    session with
                    {
                        State = StatesCoreVE.Stopped,
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
                        State = StatesCoreVE.Ready,
                        IsRunning = false,
                        DeviceSerial = null,
                        SessionId = null,
                        RendererEnabled = false,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        Message = "VisionEngine Block D listo.",
                        LastError = null
                    }));

            return ResultCoreVE.Ok(
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

            return ResultCoreVE.Fail(
                "No fue posible detener VisionEngine.",
                ex);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private StatusCoreVE BuildRuntimeStatusVE(
        StatusCoreVE source)
    {
        StatusVideoVE video = _runtime.VideoVE.StatusVE;
        StatusAudioVE audio = _runtime.AudioVE.StatusVE;
        StatusControlVE control = _runtime.ControlVE.StatusVE;
        StatusGamepadVE gamepad = _runtime.GamepadVE.StatusVE;
        StatusRendererVE renderer = _runtime.RendererVE.StatusVE;

        return source with
        {
            DeviceReady = _runtime.DeviceVE.StatusVE.State == StatesDeviceVE.Ready,
            ServerRunning = _runtime.ServerVE.StatusVE.State == StatesServerVE.Running,
            TransportConnected = _runtime.TransportVE.StateVE == StatesTransportVE.Connected,
            VideoStreaming = video.State == StatesVideoVE.Streaming,
            VideoPacketsReceived = video.Stats.PacketsReceived,
            VideoFramesDecoded = video.Stats.FramesDecoded,
            VideoDecodeErrors = video.Stats.DecodeErrors,
            AudioStreaming = audio.State == StatesAudioVE.Streaming,
            AudioPacketsReceived = audio.Stats.PacketsReceived,
            AudioFramesDecoded = audio.Stats.FramesDecoded,
            AudioDecodeErrors = audio.Stats.DecodeErrors,
            AudioPlaybackEnabled = audio.PlaybackEnabled,
            ControlReady = control.State == StatesControlVE.Ready,
            ControlMessagesSent = control.Stats.MessagesSent,
            ControlMessagesReceived = control.Stats.MessagesReceived,
            ConnectedGamepads = gamepad.ConnectedGamepads,
            GamepadReportsSent = gamepad.ReportsSent,
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

        StatusCoreVE updated = BuildRuntimeStatusVE(StatusVE);
        StatusVideoVE video = _runtime.VideoVE.StatusVE;
        StatusAudioVE audio = _runtime.AudioVE.StatusVE;
        StatusControlVE control = _runtime.ControlVE.StatusVE;
        StatusGamepadVE gamepad = _runtime.GamepadVE.StatusVE;
        StatusRendererVE renderer = _runtime.RendererVE.StatusVE;

        if (video.State == StatesVideoVE.Failed)
        {
            updated = updated with
            {
                State = StatesCoreVE.Failed,
                Message = "El pipeline de video VisionEngine falló.",
                LastError = video.LastError
            };
        }
        else if (renderer.State == StatesRendererVE.Failed)
        {
            updated = updated with
            {
                Message = "Renderer VisionEngine falló; transporte y decode permanecen observables.",
                LastError = renderer.LastError
            };
        }
        else if (audio.State == StatesAudioVE.Failed)
        {
            updated = updated with
            {
                State = StatesCoreVE.Failed,
                Message = "El pipeline de audio VisionEngine falló.",
                LastError = audio.LastError
            };
        }
        else if (control.State == StatesControlVE.Failed)
        {
            updated = updated with
            {
                State = StatesCoreVE.Failed,
                Message = "El canal de control VisionEngine falló.",
                LastError = control.LastError
            };
        }
        else if (gamepad.State == StatesGamepadVE.Failed)
        {
            updated = updated with
            {
                Message = "Gamepad VisionEngine falló sin detener video/audio.",
                LastError = gamepad.LastError
            };
        }

        PublishStatusVE(updated);
    }

    private void UpdateSessionVE(
        SessionCoreVE session)
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
        SessionCoreVE? session = SessionVE;

        if (session is not null)
        {
            UpdateSessionVE(
                session with
                {
                    State = StatesCoreVE.Failed,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = message,
                    LastError = exception.Message
                });
        }

        PublishStatusVE(
            BuildRuntimeStatusVE(
                _status with
                {
                    State = StatesCoreVE.Failed,
                    IsRunning = false,
                    RendererEnabled = false,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = message,
                    LastError = exception.Message
                }));
    }

    private void PublishStatusVE(
        StatusCoreVE status)
    {
        EventHandler<StatusCoreVE>? handler;

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
                    State = StatesCoreVE.Disposed,
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
