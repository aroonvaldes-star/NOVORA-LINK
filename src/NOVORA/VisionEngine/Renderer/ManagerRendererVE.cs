using NOVORA.Services;
using NOVORA.VisionEngine.Video;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;

namespace NOVORA.VisionEngine.Renderer;

/// <summary>
/// Renderer de baja latencia de VisionEngine.
///
/// Principios:
/// - Cola pequeña.
/// - Siempre presentar el frame más reciente.
/// - Nunca bloquear el decoder esperando a la UI.
/// - No publicar telemetría por frame.
/// - Estado sólo cuando cambia.
/// - Métricas limitadas a una frecuencia baja y estable.
/// </summary>
public sealed class ManagerRendererVE : IAsyncDisposable
{
    public const int CapacityVE = 2;

    /*
     * 250 ms = 4 publicaciones de métricas por segundo.
     *
     * El renderer puede trabajar a 60/90/120 FPS sin generar
     * decenas o cientos de eventos UI por segundo.
     */
    private const long MetricsPublishIntervalMsVE = 250;

    private readonly NovoraPaths _pathsVE;
    private readonly QueueRendererVE<FrameVideoVE> _queueVE =
        new(CapacityVE);

    private readonly object _gateVE =
        new();

    private StatusRendererVE _statusVE =
        StatusRendererVE.CreateInitialVE();

    private SurfaceRendererVE? _surfaceVE;
    private DeviceRendererVE? _deviceVE;
    private FrameRendererVE? _frameRendererVE;

    private bool _startedVE;
    private bool _renderPendingVE;
    private bool _compositionHookedVE;
    private int _privacyProtectedVE;
    private bool _disposedVE;

    private int _rotationDegreesVE;

    private long _framesQueuedVE;
    private long _framesPresentedVE;
    private long _framesDroppedVE;
    private long _renderErrorsVE;

    private double _lastFrameAgeMsVE;
    private double _lastRenderLatencyMsVE;

    /*
     * TickCount64 evita crear DateTime sólo para saber
     * cuándo volver a publicar telemetría.
     */
    private long _lastMetricsPublishTickVE;

    /*
     * Cacheamos datos visuales del último frame para no reconstruir
     * StatusRendererVE en cada presentación.
     */
    private int _lastWidthVE;
    private int _lastHeightVE;
    private string? _lastPixelFormatVE;
    private string? _lastBackendVE;

    public ManagerRendererVE(
        NovoraPaths paths)
    {
        _pathsVE =
            paths ??
            throw new ArgumentNullException(
                nameof(paths));
    }

    public event EventHandler<StatusRendererVE>?
        StatusChangedVE;

    public StatusRendererVE StatusVE
    {
        get
        {
            lock (_gateVE)
            {
                return _statusVE;
            }
        }
    }

    public int RotationDegreesVE
    {
        get =>
            Volatile.Read(
                ref _rotationDegreesVE);

        set =>
            Volatile.Write(
                ref _rotationDegreesVE,
                RotationRendererVE.NormalizeVE(
                    value));
    }

    public bool IsPrivacyProtectedVE =>
        Volatile.Read(ref _privacyProtectedVE) != 0;

    public void SetPrivacyProtectedVE(bool protectedVE)
    {
        ThrowIfDisposedVE();

        int next = protectedVE ? 1 : 0;
        int previous = Interlocked.Exchange(ref _privacyProtectedVE, next);
        if (previous == next)
            return;

        if (!protectedVE)
        {
            if (_queueVE.CountVE > 0)
                ScheduleRenderVE();
            return;
        }

        int dropped = _queueVE.DrainVE();
        Interlocked.Add(ref _framesDroppedVE, dropped);

        SurfaceRendererVE? surface = GetSurfaceVE();
        if (surface is null)
            return;

        _ = surface.Dispatcher.BeginInvoke(
            DispatcherPriority.Send,
            new Action(ClearPrivacySurfaceOnUiVE));
    }

    // ============================================================
    // HOST
    // ============================================================

    public void AttachHostVE(
        HostRendererVE host)
    {
        ThrowIfDisposedVE();

        ArgumentNullException.ThrowIfNull(
            host);

        SurfaceRendererVE surface =
            SurfaceRendererVE.FromHostVE(
                host);

        SurfaceRendererVE? previous;

        lock (_gateVE)
        {
            previous =
                _surfaceVE;

            _surfaceVE =
                surface;
        }

        if (
            previous is not null &&
            previous.Handle != surface.Handle)
        {
            ReleaseNativeVE(
                previous.Dispatcher);
        }

        PublishStatusVE(
            StatusVE with
            {
                HostAttached = true,
                State =
                    _startedVE
                        ? StatesRendererVE.Starting
                        : StatesRendererVE.Stopped,
                UpdatedAtUtc =
                    DateTimeOffset.UtcNow,
                Message =
                    _startedVE
                        ? "Host de video conectado; preparando Direct3D11."
                        : "Host de video VisionEngine conectado.",
                LastError = null
            });

        if (_startedVE)
        {
            ScheduleRenderVE();
        }
    }

    public void DetachHostVE()
    {
        SurfaceRendererVE? surface;

        lock (_gateVE)
        {
            surface =
                _surfaceVE;

            _surfaceVE =
                null;
        }

        if (surface is not null)
        {
            ReleaseNativeVE(
                surface.Dispatcher);
        }

        int dropped =
            _queueVE.DrainVE();

        Interlocked.Add(
            ref _framesDroppedVE,
            dropped);

        PublishStatusVE(
            StatusVE with
            {
                State =
                    _startedVE
                        ? StatesRendererVE.WaitingForHost
                        : StatesRendererVE.Stopped,
                HostAttached = false,
                RendererEnabled = false,
                Backend = null,
                TextureWidth = 0,
                TextureHeight = 0,
                PixelFormat = null,
                Metrics =
                    SnapshotMetricsVE(),
                UpdatedAtUtc =
                    DateTimeOffset.UtcNow,
                Message =
                    _startedVE
                        ? "VisionEngine espera un HostRendererVE."
                        : "Host de video VisionEngine desconectado.",
                LastError = null
            });
    }

    // ============================================================
    // START
    // ============================================================

    public async Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();

        cancellationToken
            .ThrowIfCancellationRequested();

        _startedVE =
            true;

        _lastMetricsPublishTickVE =
            Environment.TickCount64;

        SurfaceRendererVE? surface =
            GetSurfaceVE();

        if (surface is null)
        {
            PublishStatusVE(
                StatusVE with
                {
                    State =
                        StatesRendererVE.WaitingForHost,
                    HostAttached = false,
                    RendererEnabled = false,
                    UpdatedAtUtc =
                        DateTimeOffset.UtcNow,
                    Message =
                        "Renderer listo; agrega HostRendererVE a tu XAML para presentar video.",
                    LastError = null
                });

            return;
        }

        PublishStatusVE(
            StatusVE with
            {
                State =
                    StatesRendererVE.Starting,
                HostAttached = true,
                RendererEnabled = false,
                UpdatedAtUtc =
                    DateTimeOffset.UtcNow,
                Message =
                    "Inicializando SDL3/Direct3D11.",
                LastError = null
            });

        await surface
            .Dispatcher
            .InvokeAsync(
                () =>
                    EnsureNativeOnUiVE(
                        surface),
                DispatcherPriority.Render,
                cancellationToken);
    }

    // ============================================================
    // FRAME INPUT
    // ============================================================

    public void QueueFrameVE(
        FrameVideoVE frame)
    {
        ThrowIfDisposedVE();

        ArgumentNullException.ThrowIfNull(
            frame);

        if (!_startedVE)
        {
            frame.Dispose();

            Interlocked.Increment(
                ref _framesDroppedVE);

            return;
        }

        if (IsPrivacyProtectedVE)
        {
            frame.Dispose();
            Interlocked.Increment(ref _framesDroppedVE);
            PublishMetricsIfDueVE();
            return;
        }

        SurfaceRendererVE? surface =
            GetSurfaceVE();

        if (surface is null)
        {
            frame.Dispose();

            Interlocked.Increment(
                ref _framesDroppedVE);

            /*
             * No disparamos evento por cada frame perdido.
             * La telemetría queda rate-limited.
             */
            PublishMetricsIfDueVE();

            return;
        }

        int dropped =
            _queueVE.EnqueueVE(
                frame);

        Interlocked.Increment(
            ref _framesQueuedVE);

        if (dropped > 0)
        {
            Interlocked.Add(
                ref _framesDroppedVE,
                dropped);
        }

        /*
         * No PublishStatus por frame.
         */
        PublishMetricsIfDueVE();

        ScheduleRenderVE();
    }

    // ============================================================
    // SCHEDULER
    // ============================================================

    private void ScheduleRenderVE()
    {
        SurfaceRendererVE? surface =
            GetSurfaceVE();

        if (
            surface is null ||
            !_startedVE)
        {
            return;
        }

        lock (_gateVE)
        {
            if (_renderPendingVE)
            {
                return;
            }

            _renderPendingVE =
                true;
        }

        _ =
            surface
                .Dispatcher
                .BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(
                        ProcessLatestOnUiVE));
    }

    // ============================================================
    // UI PRESENTATION
    // ============================================================

    private void ProcessLatestOnUiVE()
    {
        lock (_gateVE)
        {
            _renderPendingVE =
                false;
        }

        if (!_startedVE)
        {
            return;
        }

        SurfaceRendererVE? surface =
            GetSurfaceVE();

        if (surface is null)
        {
            return;
        }

        try
        {
            EnsureNativeOnUiVE(
                surface);

            _deviceVE?.PumpEventsVE();

            if (IsPrivacyProtectedVE)
            {
                int privacyDropped = _queueVE.DrainVE();
                Interlocked.Add(ref _framesDroppedVE, privacyDropped);
                _deviceVE?.ClearVE();
                PublishMetricsIfDueVE();
                return;
            }

            if (
                !_queueVE.TryTakeLatestVE(
                    out FrameVideoVE? frame,
                    out int stale) ||
                frame is null)
            {
                return;
            }

            if (stale > 0)
            {
                Interlocked.Add(
                    ref _framesDroppedVE,
                    stale);
            }

            using (frame)
            {
                int rotation =
                    Volatile.Read(
                        ref _rotationDegreesVE);

                var result =
                    _frameRendererVE!
                        .PresentVE(
                            frame,
                            rotation);

                Interlocked.Increment(
                    ref _framesPresentedVE);

                _lastRenderLatencyMsVE =
                    result.RenderLatencyMs;

                _lastFrameAgeMsVE =
                    Math.Max(
                        0,
                        (
                            DateTimeOffset.UtcNow -
                            frame.DecodedAtUtc
                        ).TotalMilliseconds);

                /*
                 * Guardamos atributos.
                 * No construimos un Status completo cada frame.
                 */
                _lastWidthVE =
                    result.Width;

                _lastHeightVE =
                    result.Height;

                _lastPixelFormatVE =
                    result.Format.ToString();

                _lastBackendVE =
                    _deviceVE?
                        .BackendVE;

                PublishMetricsIfDueVE();
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(
                ref _renderErrorsVE);

            PublishStatusVE(
                StatusVE with
                {
                    State =
                        StatesRendererVE.Failed,
                    RendererEnabled = false,
                    Metrics =
                        SnapshotMetricsVE(),
                    UpdatedAtUtc =
                        DateTimeOffset.UtcNow,
                    Message =
                        "Falló la presentación de video VisionEngine.",
                    LastError =
                        ex.Message
                });

            return;
        }

        /*
         * Si mientras renderizábamos llegó un frame nuevo,
         * agendamos otra presentación.
         */
        if (_queueVE.CountVE > 0)
        {
            ScheduleRenderVE();
        }
    }

    // ============================================================
    // NATIVE
    // ============================================================

    private void EnsureNativeOnUiVE(
        SurfaceRendererVE surface)
    {
        if (
            _deviceVE?.IsOpenVE == true &&
            _frameRendererVE is not null)
        {
            return;
        }

        _frameRendererVE?
            .Dispose();

        _deviceVE?
            .Dispose();

        DeviceRendererVE device =
            new(_pathsVE);

        try
        {
            device.OpenVE(
                surface.Handle);

            _deviceVE =
                device;

            _frameRendererVE =
                new FrameRendererVE(
                    device);
        }
        catch
        {
            device.Dispose();

            throw;
        }

        _lastBackendVE =
            _deviceVE.BackendVE;

        HookCompositionEventsVE();

        if (IsPrivacyProtectedVE)
            _deviceVE.ClearVE();

        PublishStatusVE(
            StatusVE with
            {
                State =
                    StatesRendererVE.Running,
                HostAttached = true,
                RendererEnabled = true,
                Backend =
                    _deviceVE.BackendVE,
                UpdatedAtUtc =
                    DateTimeOffset.UtcNow,
                Message =
                    $"Renderer VisionEngine activo por {_deviceVE.BackendVE}.",
                LastError = null
            });
    }

    private void HookCompositionEventsVE()
    {
        if (_compositionHookedVE)
            return;

        CompositionTarget.Rendering += CompositionTarget_RenderingVE;
        _compositionHookedVE = true;
    }

    private void UnhookCompositionEventsVE()
    {
        if (!_compositionHookedVE)
            return;

        CompositionTarget.Rendering -= CompositionTarget_RenderingVE;
        _compositionHookedVE = false;
    }

    private void CompositionTarget_RenderingVE(object? sender, EventArgs e)
    {
        try
        {
            _deviceVE?.PumpEventsVE();
        }
        catch
        {
            // El bombeo SDL de eventos no debe derribar la presentación.
        }
    }

    private void ClearPrivacySurfaceOnUiVE()
    {
        try
        {
            _deviceVE?.ClearVE();
        }
        catch
        {
            // Privacy no debe disparar Recovery por un clear fallido.
        }
    }

    // ============================================================
    // METRICS
    // ============================================================

    private void PublishMetricsIfDueVE()
    {
        long now =
            Environment.TickCount64;

        long previous =
            Volatile.Read(
                ref _lastMetricsPublishTickVE);

        if (
            now - previous <
            MetricsPublishIntervalMsVE)
        {
            return;
        }

        if (
            Interlocked.CompareExchange(
                ref _lastMetricsPublishTickVE,
                now,
                previous) != previous)
        {
            return;
        }

        StatusRendererVE current =
            StatusVE;

        /*
         * Una sola publicación aproximadamente cada 250 ms.
         */
        PublishStatusVE(
            current with
            {
                State =
                    StatesRendererVE.Running,
                HostAttached = true,
                RendererEnabled =
                    _deviceVE?.IsOpenVE == true,
                Backend =
                    _lastBackendVE,
                TextureWidth =
                    _lastWidthVE,
                TextureHeight =
                    _lastHeightVE,
                PixelFormat =
                    _lastPixelFormatVE,
                RotationDegrees =
                    Volatile.Read(
                        ref _rotationDegreesVE),
                Metrics =
                    SnapshotMetricsVE(),
                UpdatedAtUtc =
                    DateTimeOffset.UtcNow,
                Message =
                    _lastWidthVE > 0 &&
                    _lastHeightVE > 0
                        ? $"Video VisionEngine {_lastWidthVE}x{_lastHeightVE} activo."
                        : current.Message,
                LastError = null
            });
    }

    private MetricsRendererVE SnapshotMetricsVE()
    {
        return new MetricsRendererVE(
            FramesQueued:
                Interlocked.Read(
                    ref _framesQueuedVE),

            FramesPresented:
                Interlocked.Read(
                    ref _framesPresentedVE),

            FramesDropped:
                Interlocked.Read(
                    ref _framesDroppedVE),

            RenderErrors:
                Interlocked.Read(
                    ref _renderErrorsVE),

            QueueDepth:
                _queueVE.CountVE,

            LastFrameAgeMs:
                _lastFrameAgeMsVE,

            LastRenderLatencyMs:
                _lastRenderLatencyMsVE);
    }

    private void PublishStatusVE(
        StatusRendererVE status)
    {
        EventHandler<StatusRendererVE>?
            handler;

        lock (_gateVE)
        {
            _statusVE =
                status;

            handler =
                StatusChangedVE;
        }

        handler?
            .Invoke(
                this,
                status);
    }

    // ============================================================
    // STOP
    // ============================================================

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        if (_disposedVE)
        {
            return;
        }

        _startedVE =
            false;

        int dropped =
            _queueVE.DrainVE();

        Interlocked.Add(
            ref _framesDroppedVE,
            dropped);

        SurfaceRendererVE? surface =
            GetSurfaceVE();

        if (surface is not null)
        {
            await surface
                .Dispatcher
                .InvokeAsync(
                    ReleaseNativeOnUiVE,
                    DispatcherPriority.Send,
                    cancellationToken);
        }
        else
        {
            ReleaseNativeOnUiVE();
        }

        PublishStatusVE(
            StatusRendererVE
                .CreateInitialVE() with
            {
                HostAttached =
                    surface is not null,

                Metrics =
                    SnapshotMetricsVE(),

                Message =
                    "Renderer VisionEngine detenido."
            });
    }

    private void ReleaseNativeVE(
        Dispatcher dispatcher)
    {
        if (dispatcher.CheckAccess())
        {
            ReleaseNativeOnUiVE();
        }
        else
        {
            dispatcher.Invoke(
                ReleaseNativeOnUiVE);
        }
    }

    private void ReleaseNativeOnUiVE()
    {
        UnhookCompositionEventsVE();

        _frameRendererVE?
            .Dispose();

        _frameRendererVE =
            null;

        _deviceVE?
            .Dispose();

        _deviceVE =
            null;
    }

    private SurfaceRendererVE? GetSurfaceVE()
    {
        lock (_gateVE)
        {
            return _surfaceVE;
        }
    }

    private void ThrowIfDisposedVE()
    {
        ObjectDisposedException.ThrowIf(
            _disposedVE,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposedVE)
        {
            return;
        }

        await StopAsync()
            .ConfigureAwait(false);

        _queueVE.Dispose();

        _disposedVE =
            true;

        PublishStatusVE(
            StatusVE with
            {
                State =
                    StatesRendererVE.Disposed,
                RendererEnabled = false,
                UpdatedAtUtc =
                    DateTimeOffset.UtcNow,
                Message =
                    "Renderer VisionEngine liberado."
            });
    }
}