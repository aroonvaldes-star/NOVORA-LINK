using NOVORA.Control;
using NOVORA.LinkEngine.Runtime;
using NOVORA.STEngine.Core;
using NOVORA.VisionEngine.Core;

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
        bool linkCanStop = _androidOwnedLinkRuntime is not null && !_androidLinkStopping;
        string linkMessage = _androidLinkError ?? linkSession?.Message ?? "LinkEngine detenido.";
        if (!usbEligible) linkMessage += " Para iniciar Internet USB, conecta este teléfono mediante el control USB autorizado.";
        var stSnapshot = RefreshSTEngineSnapshot14();
        string stState = stSnapshot.State switch
        {
            STCoreState.Healthy => "Active",
            STCoreState.Watch => "Detected",
            STCoreState.Degraded => "Error",
            STCoreState.Critical => "Error",
            _ => "NotDetected"
        };
        string exInState = _viewModel.GamepadEnabled
            ? (device.Connected ? "Detected" : "NotDetected")
            : "NotDetected";
        string exInMessage = _viewModel.GamepadEnabled
            ? "ExInEngine habilitado. La detección y calibración dependen de entradas reales del control."
            : "ExInEngine desactivado en PC; no se simula detección de mando.";
        return new NLControlEngines(
            !_closing && !videoBusy && !videoRunning && device.Connected &&
                !string.IsNullOrWhiteSpace(device.Serial) && _viewModel.SelectedMonitor is not null,
            !_closing && !videoBusy && videoRunning,
            !_closing && usbEligible && !_androidLinkStarting && !_androidLinkStopping &&
                _androidOwnedLinkRuntime is null && link is not null && link.EngineLE is null,
            !_closing && linkCanStop,
            link?.IsRunningLE == true,
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
            stState,
            $"STEngine: {stSnapshot.Summary}");
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

    private async Task<NLControlReply> ApplyAndroidEngineActionAsync(NLControlRequest request)
    {
        long generation = _androidControlGeneration;
        string serial = _viewModel.Device.Serial;
        bool Authorized() => !_closing && AndroidControlSessionOpen && generation == _androidControlGeneration;
        bool SameDevice() => Authorized() && _viewModel.Device.Connected && _viewModel.Device.Serial == serial;
        NLControlReply Reply(bool success, string message) => new(NLControlProtocol.Version,
            request.Id, success, message, CaptureAndroidControlSnapshot());

        switch (request.Action)
        {
            case "startVideo":
                if (IsVisionEngineRunningVE()) return Reply(true, "VisionEngine ya está iniciado.");
                await SetVisionEngineRunningVEAsync(true, SameDevice);
                return Reply(Authorized() && IsVisionEngineRunningVE(),
                    IsVisionEngineRunningVE() ? "VisionEngine iniciado." : "VisionEngine no se inició.");
            case "stopVideo":
                if (!IsVisionEngineRunningVE()) return Reply(true, "VisionEngine ya está detenido.");
                await SetVisionEngineRunningVEAsync(false, Authorized);
                return Reply(Authorized() && !IsVisionEngineRunningVE(), "VisionEngine detenido.");
            case "restartVideo":
                await SetVisionEngineRunningVEAsync(false, Authorized);
                if (!SameDevice()) return Reply(false, "Video detenido; la sesión o el dispositivo cambió.");
                await SetVisionEngineRunningVEAsync(true, SameDevice);
                return Reply(Authorized() && IsVisionEngineRunningVE(), "Reinicio de VisionEngine completado.");
            case "startLink":
                if (!CaptureAndroidEngines().LinkCanStart)
                    return Reply(false, "LinkEngine requiere el teléfono seleccionado y autorizado por control USB, sin otra sesión del motor.");
                var startRuntime = _linkEngineRuntimeLE!;
                var cancellation = new CancellationTokenSource();
                _androidOwnedLinkRuntime = startRuntime;
                _androidLinkStartCancellation = cancellation;
                _androidLinkStarting = true;
                _androidLinkError = null;
                AndroidEngineStateChanged();
                _androidLinkStartTask = StartAndroidLinkAsync(startRuntime, serial, generation, cancellation);
                return Reply(true, "Inicio de LinkEngine aceptado. Esperando el cliente Android y la confirmación real del túnel.");
            case "stopLink":
                if (_androidOwnedLinkRuntime is null)
                    return Reply(_linkEngineRuntimeLE?.EngineLE is null, "No hay una sesión LinkEngine iniciada por este control para detener.");
                _ = StopAndroidOwnedLinkAsync();
                return Reply(true, "Detención de LinkEngine aceptada. Consulta el estado confirmado del motor.");
            default:
                return Reply(false, "Acción de motor desconocida.");
        }
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
