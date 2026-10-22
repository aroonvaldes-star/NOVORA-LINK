using NOVORA.Control;
using NOVORA.ExInEngine;
using NOVORA.LinkEngine.Network;
using NOVORA.LinkEngine.Runtime;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Metrics;
using System.Net;
using System.Text.Json;

namespace NOVORA;

public partial class NLUIWindowMain
{
    internal static bool CanStartVeLanVideo(string transport, bool authorized, bool engineCanStart) =>
        transport == "LAN" && authorized && engineCanStart;
    private NLControlEngines? _lastAndroidEngineState;
    private CancellationTokenSource? _androidLinkStartCancellation;
    private Task _androidLinkStartTask = Task.CompletedTask;
    private Task _androidLinkStopTask = Task.CompletedTask;
    private LERuntimeManager? _androidOwnedLinkRuntime;
    private bool _androidLinkStarting;
    private bool _androidLinkStopping;
    private string? _androidLinkError;
    private LENetworkRelay? _androidLanRelay;
    private NLControlLanDataGateway? _androidLanDataGateway;
    private NLControlLinkOffer? _androidLanLinkOffer;
    private bool _pcVideoAuthorizationRequested;
    private long _pcVideoAuthorizationRequestId;
    private CancellationTokenSource? _pcVideoAuthorizationTimeout;
    private readonly VEMetricsCollector _androidVisionMetrics = new();
    private readonly VEObservationWindow _androidVisionObservation = new();
    private long _lastAndroidVisionTelemetryPublishTicks;

    private NLControlEngines CaptureAndroidEngines()
    {
        bool videoRunning = IsVisionEngineRunningVE();
        var videoState = _visionEngineVE?.StatusVE.State;
        bool videoBusy = _visionCommandApplyingVE || videoState is
            VECoreStates.Initializing or VECoreStates.Starting or VECoreStates.Stopping or VECoreStates.Disposed;
        var device = _viewModel.Device;
        var link = _linkEngineRuntimeLE;
        var linkSession = link?.SessionLE;
        bool usbEligible = _androidControl?.IsAuthorized == true && _androidControlSerial is not null &&
            _androidControlSerial == device.Serial && device.Connected && !device.IsWifiConnection;
        bool linkUsesSelectedDevice = linkSession is not null && string.Equals(
            linkSession.Serial, device.Serial, StringComparison.OrdinalIgnoreCase);
        bool linkUsesOtherDevice = link?.EngineLE is not null && !linkUsesSelectedDevice;
        bool lanEligible = _androidLanControl?.IsAuthorized == true;
        VEExternalSourceKind externalSource = _visionEngineVE?.RuntimeVE.ExternalSourceKindVE ??
            VEExternalSourceKind.None;
        bool lanWaitingForDevice = _externalVisionWaitingForDeviceVE &&
            _externalVisionWaitingSourceVE == VEExternalSourceKind.NativeLan;
        bool lanLifecycle = !lanWaitingForDevice && (_veLanSessionVE is not null ||
            externalSource == VEExternalSourceKind.NativeLan ||
            _veLanPhaseVE is "Preparing" or "AwaitingPermission" or "Connecting" or
                "Degraded" or "Stopping");
        bool lanStateVisible = lanLifecycle || lanWaitingForDevice || _veLanPhaseVE == "Error";
        string videoTransport = lanStateVisible ? "LAN" :
            externalSource == VEExternalSourceKind.UsbAppControl || videoRunning ? "USB" : "";
        string videoPhase = lanStateVisible ? _veLanPhaseVE :
            !AndroidControlAuthorized ? "Disconnected" : videoBusy ? "Preparing" :
            videoRunning ? "Streaming" : "Ready";
        bool lanRunning = _androidLanDataGateway?.IsConnected == true;
        bool lanPrepared = _androidLanDataGateway is not null;
        bool linkCanTakeOver = (usbEligible || lanEligible) && linkUsesOtherDevice && !_androidLinkStarting && !_androidLinkStopping;
        bool linkCanStop = (lanPrepared || _androidOwnedLinkRuntime is not null && linkUsesSelectedDevice) && !_androidLinkStopping;
        string linkMessage = _androidLinkError ?? linkSession?.Message ?? "LinkEngine detenido.";
        if (linkCanTakeOver) linkMessage += " Hay otra sesión activa; este teléfono puede tomar LinkEngine sin reiniciar NOVORA.";
        if (!usbEligible && !lanEligible) linkMessage += " Conecta este teléfono mediante USB o LAN autorizada.";
        else if (lanPrepared) linkMessage = lanRunning
            ? "LinkEngine transporta la VPN por LAN cifrada."
            : "LinkEngine LAN preparado; esperando el canal DATA de Android.";
        ExInStatus? exIn = _exInEngine?.Status;
        string exInState = !_viewModel.ExInEnabled ? "NotDetected" : exIn?.State == ExInStates.Failed ? "Error" :
            exIn?.ConnectedGamepads > 0 ? "Detected" : exIn?.State == ExInStates.Running ? "Active" : "NotDetected";
        string exInMessage = !_viewModel.ExInEnabled
            ? "ExInEngine desactivado en PC."
            : exIn?.Message ?? "ExInEngine todavía no está inicializado.";
        bool videoSourceAvailable = device.Connected && !string.IsNullOrWhiteSpace(device.Serial) || lanEligible;
        NLControlVisionTelemetry? visionTelemetry = null;
        var visionRuntime = _visionEngineVE?.RuntimeVE;
        bool visionStreaming = videoRunning && _visionEngineVE?.StatusVE.VideoStreaming == true;
        if (visionStreaming && visionRuntime is not null)
        {
            VEMetricsSnapshot metrics = _androidVisionMetrics.CaptureVE(
                visionRuntime.VideoVE.StatusVE, visionRuntime.AudioVE.StatusVE,
                visionRuntime.ControlVE.StatusVE, VisionEngine.Transport.VETransportStates.Connected);
            var renderer = visionRuntime.RendererVE.StatusVE;
            if (renderer.TextureWidth > 0 && renderer.TextureHeight > 0)
            {
                VEObservationWindowSnapshot observation = _androidVisionObservation.Observe(
                    isStreaming: true, metrics.CapturedAtUtc);
                VECoreStatus status = _visionEngineVE!.StatusVE;
                visionTelemetry = new(renderer.TextureWidth, renderer.TextureHeight,
                    metrics.Video.FramesPerSecond, metrics.Video.MegabitsPerSecond, metrics.CapturedAtUtc,
                    status.VideoFramesDecoded, status.VideoFramesRendered, status.RendererFramesDropped,
                    status.VideoDecodeErrors, status.RendererErrors,
                    observation.IsActive, observation.IsCompleted, observation.RemainingSeconds);
            }
            else
            {
                _androidVisionObservation.Observe(isStreaming: false, metrics.CapturedAtUtc);
            }
        }
        else
        {
            _androidVisionObservation.Observe(isStreaming: false, DateTimeOffset.UtcNow);
        }
        return new NLControlEngines(
            !_closing && !videoBusy && !videoRunning && !lanLifecycle && videoSourceAvailable &&
                _viewModel.SelectedMonitor is not null,
            !_closing && !videoBusy && videoRunning,
            !_closing && (usbEligible && link is not null && (link.EngineLE is null || linkCanTakeOver) ||
                lanEligible && !lanPrepared && link?.EngineLE is null) && !_androidLinkStarting && !_androidLinkStopping,
            !_closing && linkCanStop,
            lanRunning || link?.IsRunningLE == true,
            _androidLinkStopping ? "Stopping" : _androidLinkStarting ? "Starting" :
                linkSession?.State.ToString() ?? LERuntimeState.Stopped.ToString(),
            linkMessage,
            device.Connected ? device.FriendlyName : lanEligible ? "Android autorizado por LAN" : "Sin dispositivo Android seleccionado en PC",
            videoBusy ? "Ocupado" : videoRunning ? "Activo" : "Detenido",
            lanStateVisible ? _veLanMessageVE : !videoSourceAvailable ? "Conecta y selecciona el teléfono en PC o vincúlalo por LAN." :
                _viewModel.SelectedMonitor is null ? "Selecciona un monitor en PC." :
                videoBusy ? "VisionEngine está cambiando de estado." : "",
            exInState,
            exInMessage,
            linkCanTakeOver,
            _pcVideoAuthorizationRequested,
            videoTransport,
            videoPhase,
            lanStateVisible && _veLanDegradedVE,
            null,
            visionTelemetry,
            _pcVideoAuthorizationRequested ? _pcVideoAuthorizationRequestId : 0,
            !_closing && _exInEngine is not null && exInState != "Active");
    }

    // Existing engine/device events publish only changes relevant to the control UI.
    // Heartbeats do not invalidate pending settings by themselves.
    private void AndroidEngineStateChanged()
    {
        if (_closing) return;
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(AndroidEngineStateChanged));
            return;
        }
        UpdateHomeLanConnectionButton();
        SyncLinkMeasurementsLE();
        NLControlEngines current = CaptureAndroidEngines();
        VisionLiveTelemetryText14.Text = current.VisionTelemetry is { } telemetry
            ? $"Monitor VE: {FormatVisionObservation(telemetry)} · {telemetry.Width} × {telemetry.Height} · " +
              $"{telemetry.FramesPerSecond:F1} FPS · {telemetry.MegabitsPerSecond:F1} Mbps · " +
              $"decodificados {telemetry.FramesDecoded:N0}, renderizados {telemetry.FramesRendered:N0}, " +
              $"perdidos {telemetry.FramesDropped:N0}, errores {telemetry.DecodeErrors + telemetry.RenderErrors:N0}."
            : current.VideoState == "Activo"
                ? "La pantalla se está preparando."
                : "La pantalla aún no se está mostrando.";

        if (current.VideoState == "Activo" &&
            _visionEngineVE?.RuntimeVE is { } latencyRuntime &&
            latencyRuntime.RendererVE.StatusVE.Metrics is { FramesPresented: > 0 } latency)
        {
            VisionLiveTelemetryText14.Text +=
                $" Latencia: recepción→pantalla {latency.LastReceiveToPresentMs:F1} ms · " +
                $"decodificado→pantalla {latency.LastFrameAgeMs:F1} ms";
            // Capture→screen needs the phone clock, reported by the control echo (Experimental observation).
            if (latencyRuntime.ControlVE.DeviceClockOffsetUsVE is long offsetUs &&
                latency.LastPresentedSourcePtsUs > 0 && latency.LastPresentedAtTimestamp > 0)
            {
                double presentedUs = latency.LastPresentedAtTimestamp * 1_000_000d / System.Diagnostics.Stopwatch.Frequency;
                double captureToScreenMs = (presentedUs + offsetUs - latency.LastPresentedSourcePtsUs) / 1000d;
                if (captureToScreenMs is >= 0 and < 5000)
                    VisionLiveTelemetryText14.Text += $" · captura→pantalla {captureToScreenMs:F0} ms";
            }
            VisionLiveTelemetryText14.Text += ".";
        }

        if (current.VideoState == "Activo" &&
            _visionEngineVE?.RuntimeVE.RendererVE.StatusVE.Metrics is { DynamicPresentationEnabled: true } timing)
            VisionLiveTelemetryText14.Text +=
                $" Presentación dinámica: {timing.PresentedFps:F1} FPS / {timing.TargetRefreshRateHz:F2} Hz · " +
                $"intervalo {timing.LastPresentIntervalMs:F1} ms · variación {timing.PresentationJitterMs:F1} ms · " +
                $"recuperaciones {timing.CompositionFallbacks:N0}.";

        if (current == _lastAndroidEngineState) return;
        _lastAndroidEngineState = current;
        _androidControlRevision++;
        QueueAndroidControlSnapshot();
    }

    private static string FormatVisionObservation(NLControlVisionTelemetry telemetry)
    {
        if (telemetry.ObservationCompleted) return "observación completada (20 min)";
        if (!telemetry.ObservationActive) return "observación pendiente";
        TimeSpan remaining = TimeSpan.FromSeconds(telemetry.ObservationSecondsRemaining);
        return $"{remaining.Minutes:D2}:{remaining.Seconds:D2} restantes";
    }

    private bool ShouldPublishAndroidVisionTelemetry(VECoreStatus status)
    {
        if (!status.VideoStreaming) return false;

        long nowTicks = DateTime.UtcNow.Ticks;
        long previousTicks = Interlocked.Read(ref _lastAndroidVisionTelemetryPublishTicks);
        if (nowTicks - previousTicks < TimeSpan.TicksPerSecond) return false;
        return Interlocked.CompareExchange(
            ref _lastAndroidVisionTelemetryPublishTicks, nowTicks, previousTicks) == previousTicks;
    }

    private async Task<NLControlReply> ApplyAndroidEngineActionAsync(NLControlRequest request, string transport)
    {
        long generation = _androidControlGeneration;
        string serial = _viewModel.Device.Serial;
        bool Authorized() => !_closing && AndroidControlSessionOpen && generation == _androidControlGeneration;
        bool SameDevice() => Authorized() && _viewModel.Device.Connected && _viewModel.Device.Serial == serial;
        NLControlReply Reply(bool success, string message) => new(NLControlProtocol.Version,
            request.Id, success, message, CaptureAndroidControlSnapshot());

        switch (request.Action)
        {
            case "startAppVideo":
                ClearPcVideoAuthorizationRequest();
                return await PrepareAppControlVideoAsync(request);
            case "startVideoLan":
                ClearPcVideoAuthorizationRequest();
                return await PrepareVeLanVideoAsync(request, transport);
            case "stopVideo":
                if (!IsVisionEngineRunningVE()) return Reply(true, "VisionEngine ya está detenido.");
                if (_visionEngineVE?.RuntimeVE.ExternalSourceKindVE == VEExternalSourceKind.NativeLan)
                {
                    await StopVeLanVideoAsync();
                    return Reply(Authorized() && !IsVisionEngineRunningVE(), "VisionEngine LAN detenido.");
                }
                if (_visionEngineVE?.RuntimeVE.IsAppControlVideoActiveVE == true)
                {
                    await StopAppControlVideoSourceAsync();
                    return Reply(Authorized() && !IsVisionEngineRunningVE(), "VisionEngine detenido.");
                }
                return Reply(false, "La sesión de video no pertenece a App Control.");
            case "restartVideo":
                if (_visionEngineVE?.RuntimeVE.ExternalSourceKindVE == VEExternalSourceKind.NativeLan)
                {
                    await PreserveVisionFailoverDuringRestartVEAsync(() => StopVeLanVideoAsync(
                        "VisionEngine LAN detenido; vuelve a iniciarlo para renovar el permiso de captura."));
                    return Reply(Authorized() && !IsVisionEngineRunningVE(),
                        "VisionEngine LAN se detuvo. Android solicitará la captura para reiniciarlo.");
                }
                if (_visionEngineVE?.RuntimeVE.IsAppControlVideoActiveVE == true)
                {
                    await PreserveVisionFailoverDuringRestartVEAsync(StopAppControlVideoSourceAsync);
                    return Reply(Authorized() && !IsVisionEngineRunningVE(),
                        "VisionEngine se detuvo. Android solicitará la captura para reiniciarlo.");
                }
                return Reply(false, "Solicita una nueva captura desde App Control.");
            case "startLink":
                // La ruta pedida por el bloque de AppControl debe ser la sesión activa.
                if (request.Value is "USB" or "LAN" && request.Value != transport)
                    return Reply(false, $"LinkEngine por {request.Value} no está activo: la sesión actual es {transport}.");
                if (!CaptureAndroidEngines().LinkCanStart)
                    return Reply(false, "LinkEngine requiere una sesión USB o LAN autorizada y disponible.");
                if (transport == "LAN")
                {
                    NLControlLinkOffer offer = await StartAndroidLanLinkAsync();
                    return new(NLControlProtocol.Version, request.Id, true,
                        "LinkEngine LAN preparado. Android conservará la VPN mientras este canal DATA esté disponible.",
                        CaptureAndroidControlSnapshot(), Value: JsonSerializer.Serialize(offer));
                }
                var startRuntime = _linkEngineRuntimeLE!;
                bool handoff = startRuntime.EngineLE is not null && !string.Equals(
                    startRuntime.SessionLE?.Serial, serial, StringComparison.OrdinalIgnoreCase);
                if (handoff)
                {
                    _androidLinkStopping = true;
                    _androidLinkStartCancellation?.Cancel();
                    AndroidEngineStateChanged();
                    try
                    {
                        await _androidLinkStartTask;
                        var stopped = await startRuntime.StopAsync();
                        if (!stopped.Success) return Reply(false, "No se liberó la sesión LinkEngine anterior: " + stopped.Message);
                        _androidOwnedLinkRuntime = null;
                    }
                    finally
                    {
                        _androidLinkStopping = false;
                        AndroidEngineStateChanged();
                    }
                    if (!SameDevice()) return Reply(false, "La sesión USB cambió durante la transferencia de LinkEngine.");
                }
                var cancellation = new CancellationTokenSource();
                _androidOwnedLinkRuntime = startRuntime;
                _androidLinkStartCancellation = cancellation;
                _androidLinkStarting = true;
                _androidLinkError = null;
                AndroidEngineStateChanged();
                _androidLinkStartTask = StartAndroidLinkAsync(startRuntime, serial, generation, cancellation);
                return Reply(true, handoff
                    ? "Transferencia de LinkEngine aceptada. La sesión anterior fue liberada; esperando el túnel de este teléfono."
                    : "Inicio de LinkEngine aceptado. Esperando el cliente Android y la confirmación real del túnel.");
            case "stopLink":
                if (_androidLanDataGateway is not null || _androidLanRelay is not null)
                {
                    _ = StopAndroidLanLinkAsync();
                    return Reply(true, "Detención de LinkEngine LAN aceptada.");
                }
                if (_androidOwnedLinkRuntime is null)
                    return Reply(_linkEngineRuntimeLE?.EngineLE is null, "No hay una sesión LinkEngine iniciada por este control para detener.");
                _ = StopAndroidOwnedLinkAsync();
                return Reply(true, "Detención de LinkEngine aceptada. Consulta el estado confirmado del motor.");
            default:
                return Reply(false, "Acción de motor desconocida.");
        }
    }

    private async Task<NLControlLinkOffer> StartAndroidLanLinkAsync()
    {
        if (_androidLanLinkOffer is not null && _androidLanDataGateway is not null)
            return _androidLanLinkOffer;
        if (_androidLanControl?.Invitation.Host is not { } host || !IPAddress.TryParse(host, out IPAddress? address))
            throw new InvalidOperationException("La dirección LAN autorizada ya no está disponible.");
        _androidLinkStarting = true;
        _androidLinkError = null;
        AndroidEngineStateChanged();
        try
        {
            var relay = new LENetworkRelay();
            await relay.StartAsync();
            var gateway = new NLControlLanDataGateway(address, GetAndroidTrustStore());
            gateway.StateChanged += AndroidLanDataGateway_StateChanged;
            NLControlLinkOffer offer = gateway.Start();
            _androidLanRelay = relay;
            _androidLanDataGateway = gateway;
            _androidLanLinkOffer = offer;
            return offer;
        }
        catch
        {
            await StopAndroidLanLinkAsync();
            throw;
        }
        finally
        {
            _androidLinkStarting = false;
            AndroidEngineStateChanged();
        }
    }

    private void AndroidLanDataGateway_StateChanged(object? sender, EventArgs e) => AndroidEngineStateChanged();

    private async Task StopAndroidLanLinkAsync()
    {
        _androidLinkStopping = true;
        AndroidEngineStateChanged();
        NLControlLanDataGateway? gateway = _androidLanDataGateway;
        LENetworkRelay? relay = _androidLanRelay;
        _androidLanDataGateway = null;
        _androidLanRelay = null;
        _androidLanLinkOffer = null;
        try
        {
            if (gateway is not null)
            {
                gateway.StateChanged -= AndroidLanDataGateway_StateChanged;
                await gateway.DisposeAsync();
            }
            if (relay is not null) await relay.DisposeAsync();
        }
        finally
        {
            _androidLinkStopping = false;
            AndroidEngineStateChanged();
        }
    }

    internal static bool CanRequestAndroidVideoFromPc(bool usbAuthorized, bool lanAuthorized) =>
        usbAuthorized || lanAuthorized;

    private Task RequestAndroidVideoFromPcAsync()
    {
        var device = _viewModel.Device;
        bool authorizedUsb = AndroidControlSessionOpen && _androidControl?.IsAuthorized == true &&
            _androidControlSerial == device.Serial && device.Connected;
        bool authorizedLan = _androidLanControl?.IsAuthorized == true;
        if (!CanRequestAndroidVideoFromPc(authorizedUsb, authorizedLan))
            throw new InvalidOperationException(
                "Sin ruta lista: inicia «Iniciar por LAN/Wi-Fi» en el teléfono, o conecta USB con depuración activa.");
        if (_viewModel.SelectedMonitor is null)
            throw new InvalidOperationException("Selecciona el monitor que compartirá VisionEngine.");

        _pcVideoAuthorizationTimeout?.Cancel();
        _pcVideoAuthorizationTimeout?.Dispose();
        _pcVideoAuthorizationTimeout = new CancellationTokenSource();
        _pcVideoAuthorizationRequestId++;
        _pcVideoAuthorizationRequested = true;
        _viewModel.ConnectionStatus = authorizedUsb && authorizedLan
            ? "Autoriza compartir pantalla en Android. USB y LAN listas: USB es la ruta preferida (más fluida con depuración)."
            : authorizedUsb
            ? "Autoriza compartir pantalla en Android (ruta USB)."
            : "Autoriza compartir pantalla en Android (ruta LAN, sin depuración).";
        UpdateRuntimeButtons();
        AndroidEngineStateChanged();
        _ = ExpirePcVideoAuthorizationRequestAsync(_pcVideoAuthorizationTimeout.Token);
        return Task.CompletedTask;
    }

    private async Task ExpirePcVideoAuthorizationRequestAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (!_pcVideoAuthorizationRequested) return;
                ClearPcVideoAuthorizationRequest();
                _viewModel.ConnectionStatus = "La autorización de Android venció. Intenta iniciar de nuevo.";
            });
        }
        catch (OperationCanceledException) { }
    }

    private void ClearPcVideoAuthorizationRequest()
    {
        if (!_pcVideoAuthorizationRequested && _pcVideoAuthorizationTimeout is null) return;
        _pcVideoAuthorizationRequested = false;
        _pcVideoAuthorizationTimeout?.Cancel();
        _pcVideoAuthorizationTimeout?.Dispose();
        _pcVideoAuthorizationTimeout = null;
        UpdateRuntimeButtons();
        AndroidEngineStateChanged();
    }

    private async Task StartAndroidLinkAsync(LERuntimeManager runtime, string serial, long generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            var result = await runtime.StartAsync(serial, cancellation.Token);
            if (!result.Success) _androidLinkError = result.Message;
            if (_closing || generation != _androidControlGeneration || !_viewModel.Device.Connected ||
                _viewModel.Device.Serial != serial || cancellation.IsCancellationRequested)
                await runtime.StopAsync();
        }
        catch (OperationCanceledException) { _androidLinkError = "Inicio de LinkEngine cancelado."; }
        catch (Exception ex) { _androidLinkError = "LinkEngine no se inició: " + ex.Message; }
        finally
        {
            // Runtime may retain resources after cancellation; release only this operation's runtime.
            if (!runtime.IsRunningLE)
            {
                try { await runtime.StopAsync(); }
                catch (Exception ex) { _androidLinkError = "No se pudo cerrar LinkEngine: " + ex.Message; }
                if (runtime.EngineLE is null && ReferenceEquals(_androidOwnedLinkRuntime, runtime))
                    _androidOwnedLinkRuntime = null;
            }
            if (ReferenceEquals(_androidLinkStartCancellation, cancellation))
            {
                _androidLinkStartCancellation = null;
                _androidLinkStarting = false;
            }
            cancellation.Dispose();
            AndroidEngineStateChanged();
        }
    }

    private Task StopAndroidOwnedLinkAsync()
    {
        if (_androidLanDataGateway is not null || _androidLanRelay is not null)
            return StopAndroidLanLinkAsync();
        if (!_androidLinkStopTask.IsCompleted) return _androidLinkStopTask;
        _androidLinkStartCancellation?.Cancel();
        var runtime = _androidOwnedLinkRuntime;
        if (runtime is null) return Task.CompletedTask;
        _androidLinkStopping = true;
        AndroidEngineStateChanged();
        return _androidLinkStopTask = StopAndroidOwnedLinkCoreAsync(runtime);
    }

    private async Task StopAndroidOwnedLinkCoreAsync(LERuntimeManager runtime)
    {
        try
        {
            await _androidLinkStartTask;
            var result = await runtime.StopAsync();
            if (!result.Success) _androidLinkError = result.Message;
            if (runtime.EngineLE is null && ReferenceEquals(_androidOwnedLinkRuntime, runtime))
                _androidOwnedLinkRuntime = null;
        }
        catch (Exception ex) { _androidLinkError = "No se pudo detener LinkEngine: " + ex.Message; }
        finally { _androidLinkStopping = false; AndroidEngineStateChanged(); }
    }
}
