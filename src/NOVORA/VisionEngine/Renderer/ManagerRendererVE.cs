using NOVORA.Services;
using NOVORA.VisionEngine.Video;
using System.Windows.Threading;

namespace NOVORA.VisionEngine.Renderer;

/// <summary>
/// Cola de presentación de baja latencia. Mantiene como máximo dos frames y
/// presenta siempre el más reciente en el Dispatcher dueño del HWND.
/// </summary>
public sealed class ManagerRendererVE : IAsyncDisposable
{
    public const int CapacityVE = 2;

    private readonly NovoraPaths _pathsVE;
    private readonly QueueRendererVE<FrameVideoVE> _queueVE = new(CapacityVE);
    private readonly object _gateVE = new();

    private StatusRendererVE _statusVE = StatusRendererVE.CreateInitialVE();
    private SurfaceRendererVE? _surfaceVE;
    private DeviceRendererVE? _deviceVE;
    private FrameRendererVE? _frameRendererVE;
    private bool _startedVE;
    private bool _renderPendingVE;
    private bool _disposedVE;
    private int _rotationDegreesVE;

    private long _framesQueuedVE;
    private long _framesPresentedVE;
    private long _framesDroppedVE;
    private long _renderErrorsVE;
    private double _lastFrameAgeMsVE;
    private double _lastRenderLatencyMsVE;

    public ManagerRendererVE(NovoraPaths paths)
    {
        _pathsVE = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public event EventHandler<StatusRendererVE>? StatusChangedVE;

    public StatusRendererVE StatusVE
    {
        get
        {
            lock (_gateVE)
                return _statusVE;
        }
    }

    public int RotationDegreesVE
    {
        get => _rotationDegreesVE;
        set => _rotationDegreesVE = RotationRendererVE.NormalizeVE(value);
    }

    public void AttachHostVE(HostRendererVE host)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(host);

        SurfaceRendererVE surface = SurfaceRendererVE.FromHostVE(host);
        SurfaceRendererVE? previous;

        lock (_gateVE)
        {
            previous = _surfaceVE;
            _surfaceVE = surface;
        }

        if (previous is not null && previous.Handle != surface.Handle)
            ReleaseNativeVE(previous.Dispatcher);

        PublishStatusVE(StatusVE with
        {
            HostAttached = true,
            State = _startedVE ? StatesRendererVE.Starting : StatesRendererVE.Stopped,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Message = _startedVE
                ? "Host de video conectado; preparando Direct3D11."
                : "Host de video VisionEngine conectado.",
            LastError = null
        });

        if (_startedVE)
            ScheduleRenderVE();
    }

    public void DetachHostVE()
    {
        SurfaceRendererVE? surface;
        lock (_gateVE)
        {
            surface = _surfaceVE;
            _surfaceVE = null;
        }

        if (surface is not null)
            ReleaseNativeVE(surface.Dispatcher);

        int dropped = _queueVE.DrainVE();
        Interlocked.Add(ref _framesDroppedVE, dropped);

        PublishStatusVE(StatusVE with
        {
            State = _startedVE ? StatesRendererVE.WaitingForHost : StatesRendererVE.Stopped,
            HostAttached = false,
            RendererEnabled = false,
            Backend = null,
            TextureWidth = 0,
            TextureHeight = 0,
            PixelFormat = null,
            Metrics = SnapshotMetricsVE(),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Message = _startedVE
                ? "VisionEngine espera un HostRendererVE."
                : "Host de video VisionEngine desconectado.",
            LastError = null
        });
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        cancellationToken.ThrowIfCancellationRequested();

        _startedVE = true;
        SurfaceRendererVE? surface = GetSurfaceVE();

        if (surface is null)
        {
            PublishStatusVE(StatusVE with
            {
                State = StatesRendererVE.WaitingForHost,
                HostAttached = false,
                RendererEnabled = false,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Message = "Renderer listo; agrega HostRendererVE a tu XAML para presentar video.",
                LastError = null
            });
            return;
        }

        PublishStatusVE(StatusVE with
        {
            State = StatesRendererVE.Starting,
            HostAttached = true,
            RendererEnabled = false,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Message = "Inicializando SDL3/Direct3D11.",
            LastError = null
        });

        await surface.Dispatcher.InvokeAsync(
            () => EnsureNativeOnUiVE(surface),
            DispatcherPriority.Render,
            cancellationToken);
    }

    public void QueueFrameVE(FrameVideoVE frame)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(frame);

        if (!_startedVE)
        {
            frame.Dispose();
            Interlocked.Increment(ref _framesDroppedVE);
            return;
        }

        SurfaceRendererVE? surface = GetSurfaceVE();
        if (surface is null)
        {
            frame.Dispose();
            Interlocked.Increment(ref _framesDroppedVE);
            PublishMetricsVE();
            return;
        }

        int dropped = _queueVE.EnqueueVE(frame);
        Interlocked.Increment(ref _framesQueuedVE);
        Interlocked.Add(ref _framesDroppedVE, dropped);
        PublishMetricsVE();
        ScheduleRenderVE();
    }

    private void ScheduleRenderVE()
    {
        SurfaceRendererVE? surface = GetSurfaceVE();
        if (surface is null || !_startedVE)
            return;

        lock (_gateVE)
        {
            if (_renderPendingVE)
                return;
            _renderPendingVE = true;
        }

        _ = surface.Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(ProcessLatestOnUiVE));
    }

    private void ProcessLatestOnUiVE()
    {
        lock (_gateVE)
            _renderPendingVE = false;

        if (!_startedVE)
            return;

        SurfaceRendererVE? surface = GetSurfaceVE();
        if (surface is null)
            return;

        try
        {
            EnsureNativeOnUiVE(surface);

            if (!_queueVE.TryTakeLatestVE(out FrameVideoVE? frame, out int stale) || frame is null)
                return;

            Interlocked.Add(ref _framesDroppedVE, stale);

            using (frame)
            {
                var result = _frameRendererVE!.PresentVE(frame, _rotationDegreesVE);
                Interlocked.Increment(ref _framesPresentedVE);
                _lastRenderLatencyMsVE = result.RenderLatencyMs;
                _lastFrameAgeMsVE = Math.Max(0, (DateTimeOffset.UtcNow - frame.DecodedAtUtc).TotalMilliseconds);

                PublishStatusVE(StatusVE with
                {
                    State = StatesRendererVE.Running,
                    HostAttached = true,
                    RendererEnabled = true,
                    Backend = _deviceVE?.BackendVE,
                    TextureWidth = result.Width,
                    TextureHeight = result.Height,
                    PixelFormat = result.Format.ToString(),
                    RotationDegrees = _rotationDegreesVE,
                    Metrics = SnapshotMetricsVE(),
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = $"Video VisionEngine {result.Width}x{result.Height} activo por {_deviceVE?.BackendVE}.",
                    LastError = null
                });
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _renderErrorsVE);
            PublishStatusVE(StatusVE with
            {
                State = StatesRendererVE.Failed,
                RendererEnabled = false,
                Metrics = SnapshotMetricsVE(),
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Message = "Falló la presentación de video VisionEngine.",
                LastError = ex.Message
            });
            return;
        }

        if (_queueVE.CountVE > 0)
            ScheduleRenderVE();
    }

    private void EnsureNativeOnUiVE(SurfaceRendererVE surface)
    {
        if (_deviceVE?.IsOpenVE == true && _frameRendererVE is not null)
            return;

        _frameRendererVE?.Dispose();
        _deviceVE?.Dispose();

        DeviceRendererVE device = new(_pathsVE);
        try
        {
            device.OpenVE(surface.Handle);
            _deviceVE = device;
            _frameRendererVE = new FrameRendererVE(device);
        }
        catch
        {
            device.Dispose();
            throw;
        }

        PublishStatusVE(StatusVE with
        {
            State = StatesRendererVE.Running,
            HostAttached = true,
            RendererEnabled = true,
            Backend = _deviceVE.BackendVE,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Message = $"Renderer VisionEngine activo por {_deviceVE.BackendVE}.",
            LastError = null
        });
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposedVE)
            return;

        _startedVE = false;
        int dropped = _queueVE.DrainVE();
        Interlocked.Add(ref _framesDroppedVE, dropped);

        SurfaceRendererVE? surface = GetSurfaceVE();
        if (surface is not null)
        {
            await surface.Dispatcher.InvokeAsync(
                ReleaseNativeOnUiVE,
                DispatcherPriority.Send,
                cancellationToken);
        }
        else
        {
            ReleaseNativeOnUiVE();
        }

        PublishStatusVE(StatusRendererVE.CreateInitialVE() with
        {
            HostAttached = surface is not null,
            Metrics = SnapshotMetricsVE(),
            Message = "Renderer VisionEngine detenido."
        });
    }

    private void ReleaseNativeVE(Dispatcher dispatcher)
    {
        if (dispatcher.CheckAccess())
            ReleaseNativeOnUiVE();
        else
            dispatcher.Invoke(ReleaseNativeOnUiVE);
    }

    private void ReleaseNativeOnUiVE()
    {
        _frameRendererVE?.Dispose();
        _frameRendererVE = null;
        _deviceVE?.Dispose();
        _deviceVE = null;
    }

    private SurfaceRendererVE? GetSurfaceVE()
    {
        lock (_gateVE)
            return _surfaceVE;
    }

    private MetricsRendererVE SnapshotMetricsVE()
        => new(
            FramesQueued: Interlocked.Read(ref _framesQueuedVE),
            FramesPresented: Interlocked.Read(ref _framesPresentedVE),
            FramesDropped: Interlocked.Read(ref _framesDroppedVE),
            RenderErrors: Interlocked.Read(ref _renderErrorsVE),
            QueueDepth: _queueVE.CountVE,
            LastFrameAgeMs: _lastFrameAgeMsVE,
            LastRenderLatencyMs: _lastRenderLatencyMsVE);

    private void PublishMetricsVE()
    {
        StatusRendererVE current = StatusVE;
        PublishStatusVE(current with
        {
            Metrics = SnapshotMetricsVE(),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void PublishStatusVE(StatusRendererVE status)
    {
        EventHandler<StatusRendererVE>? handler;
        lock (_gateVE)
        {
            _statusVE = status;
            handler = StatusChangedVE;
        }
        handler?.Invoke(this, status);
    }

    private void ThrowIfDisposedVE() => ObjectDisposedException.ThrowIf(_disposedVE, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposedVE)
            return;

        await StopAsync().ConfigureAwait(false);
        _queueVE.Dispose();
        _disposedVE = true;

        PublishStatusVE(StatusVE with
        {
            State = StatesRendererVE.Disposed,
            RendererEnabled = false,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Message = "Renderer VisionEngine liberado."
        });
    }
}
