using NOVORA.Control;
using NOVORA.ExInEngine;
using NOVORA.LinkEngine.Network;
using NOVORA.LinkEngine.Runtime;
using NOVORA.VisionEngine.Core;
using System.Net;
using System.Text.Json;

namespace NOVORA;

public partial class NLUIWindowMain
{
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
    private CancellationTokenSource? _pcVideoAuthorizationTimeout;

    private NLControlEngines CaptureAndroidEngines()
    {
        bool videoRunning = IsVisionEngineRunningVE();
        var videoState = _visionEngineVE?.StatusVE.State;
        bool videoBusy = _visionCommandApplyingVE || _visionRecoveryRunningVE || videoState is
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
        return new NLControlEngines(
            !_closing && !videoBusy && !videoRunning && device.Connected &&
                !string.IsNullOrWhiteSpace(device.Serial) && _viewModel.SelectedMonitor is not null,
            !_closing && !videoBusy && videoRunning,
            !_closing && (usbEligible && link is not null && (link.EngineLE is null || linkCanTakeOver) ||
                lanEligible && !lanPrepared && link?.EngineLE is null) && !_androidLinkStarting && !_androidLinkStopping,
            !_closing && linkCanStop,
            lanRunning || link?.IsRunningLE == true,
            _androidLinkStopping ? "Stopping" : _androidLinkStarting ? "Starting" :
                linkSession?.State.ToString() ?? LERuntimeState.Stopped.ToString(),
            linkMessage,
            device.Connected ? device.FriendlyName : "Sin dispositivo Android seleccionado en PC",
            videoBusy ? "Ocupado" : videoRunning ? "Activo" : "Detenido",
            !device.Connected ? "Conecta y selecciona el teléfono en PC." :
                _viewModel.SelectedMonitor is null ? "Selecciona un monitor en PC." :
                videoBusy ? "VisionEngine está cambiando de estado." : "",
            exInState,
            exInMessage,
            linkCanTakeOver,
            _pcVideoAuthorizationRequested);
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
        NLControlEngines current = CaptureAndroidEngines();
        if (current == _lastAndroidEngineState) return;
        _lastAndroidEngineState = current;
        _androidControlRevision++;
        QueueAndroidControlSnapshot();
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
            case "startVideo":
                if (IsVisionEngineRunningVE()) return Reply(true, "VisionEngine ya está iniciado.");
                await SetVisionEngineRunningVEAsync(true, SameDevice);
                return Reply(Authorized() && IsVisionEngineRunningVE(),
                    IsVisionEngineRunningVE() ? "VisionEngine iniciado." : "VisionEngine no se inició.");
            case "stopVideo":
                if (!IsVisionEngineRunningVE()) return Reply(true, "VisionEngine ya está detenido.");
                if (_visionEngineVE?.RuntimeVE.IsAppControlVideoActiveVE == true)
                {
                    await StopAppControlVideoSourceAsync();
                    return Reply(Authorized() && !IsVisionEngineRunningVE(), "VisionEngine detenido.");
                }
                await SetVisionEngineRunningVEAsync(false, Authorized);
                return Reply(Authorized() && !IsVisionEngineRunningVE(), "VisionEngine detenido.");
            case "restartVideo":
                if (_visionEngineVE?.RuntimeVE.IsAppControlVideoActiveVE == true)
                {
                    await PreserveVisionFailoverDuringRestartVEAsync(StopAppControlVideoSourceAsync);
                    return Reply(false,
                        "VisionEngine AppControl se detuvo. Inícialo otra vez para renovar el permiso de captura de Android.");
                }
                await PreserveVisionFailoverDuringRestartVEAsync(async () =>
                {
                    await SetVisionEngineRunningVEAsync(false, Authorized);
                    if (!SameDevice()) return;
                    await SetVisionEngineRunningVEAsync(true, SameDevice);
                });
                if (!SameDevice() || !IsVisionEngineRunningVE())
                    return Reply(false, "Video detenido; la sesión o el dispositivo cambió.");
                return Reply(Authorized() && IsVisionEngineRunningVE(), "Reinicio de VisionEngine completado.");
            case "startLink":
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

    private Task RequestAppControlVideoFromPcAsync()
    {
        var device = _viewModel.Device;
        bool authorizedUsb = AndroidControlSessionOpen && _androidControl?.IsAuthorized == true &&
            _androidControlSerial == device.Serial && device.Connected && !device.IsWifiConnection;
        if (!authorizedUsb)
            throw new InvalidOperationException(
                "VisionEngine nativo requiere AppControl conectado y autorizado por USB.");
        if (_viewModel.SelectedMonitor is null)
            throw new InvalidOperationException("Selecciona el monitor que compartirá VisionEngine.");

        _pcVideoAuthorizationTimeout?.Cancel();
        _pcVideoAuthorizationTimeout?.Dispose();
        _pcVideoAuthorizationTimeout = new CancellationTokenSource();
        _pcVideoAuthorizationRequested = true;
        _viewModel.ConnectionStatus = "Autoriza compartir pantalla en Android.";
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
