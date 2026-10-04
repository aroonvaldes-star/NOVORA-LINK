using System.Net;
using System.Text.Json;
using NOVORA.Control;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Transport;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private VETransportLanSession? _veLanSessionVE;
    private VETransportLanAcceptedStreams? _veLanStreamsVE;
    private CancellationTokenSource? _veLanCancellationVE;
    private Task _veLanAcceptTaskVE = Task.CompletedTask;
    private bool _veLanDegradedVE;
    private volatile string _veLanPhaseVE = "Ready";
    private volatile string _veLanMessageVE = "VisionEngine LAN listo.";

    internal static bool IsVeLanControlCurrent(
        long expectedGeneration,
        long currentGeneration,
        bool authorized,
        string offerHost,
        string? currentHost) =>
        expectedGeneration == currentGeneration && authorized &&
        string.Equals(offerHost, currentHost, StringComparison.OrdinalIgnoreCase);

    private void SetVeLanPhase(string phase, string message)
    {
        _veLanPhaseVE = phase;
        _veLanMessageVE = message;
        AndroidEngineStateChanged();
    }

    private async Task<NLControlReply> PrepareVeLanVideoAsync(NLControlRequest request, string transport)
    {
        NLControlReply Reply(bool success, string message, string? value = null) => new(
            NLControlProtocol.Version, request.Id, success, message,
            CaptureAndroidControlSnapshot(), Value: value);
        if (!CanStartVeLanVideo(transport, _androidLanControl?.IsAuthorized == true,
                CaptureAndroidEngines().VideoCanStart))
            return Reply(false, "VisionEngine LAN requiere una sesión LAN autorizada y disponible.");
        if (_androidLanControl?.Invitation.Host is not { } host ||
            !IPAddress.TryParse(host, out IPAddress? address))
            return Reply(false, "La dirección LAN autorizada ya no está disponible.");
        if (IsVisionEngineRunningVE() || !_veLanAcceptTaskVE.IsCompleted)
            return Reply(false, "VisionEngine ya tiene una sesión activa o en preparación.");

        SetVeLanPhase("Preparing", "Preparando listeners cifrados de VisionEngine LAN.");
        InitializeVisionEngineRuntimeVE();
        if (!_visionEngineVE!.StatusVE.IsInitialized)
        {
            VECoreResult initialized = await _visionEngineVE.InitializeAsync();
            if (!initialized.Success) return Reply(false, initialized.Message);
        }

        await StopVeLanVideoAsync("Preparando una sesión VE LAN nueva.");
        bool audioEnabled = _viewModel.SelectedAudioOutput !=
            VisionEngine.Audio.VEAudioOutput.DisabledValueVE;
        var session = new VETransportLanSession(address, GetAndroidTrustStore());
        NLControlVeLanOffer offer;
        try
        {
            offer = session.PrepareVE(ParseVideoBitrateVE(_viewModel.Bitrate),
                _viewModel.MaxSize, _viewModel.TargetFps,
                muteDeviceAudio: audioEnabled, audioEnabled);
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }

        var cancellation = new CancellationTokenSource();
        long generation = _androidControlGeneration;
        _veLanSessionVE = session;
        _veLanCancellationVE = cancellation;
        _veLanDegradedVE = false;
        _veLanAcceptTaskVE = AcceptVeLanVideoAsync(session, offer, cancellation, generation);
        SetVeLanPhase("AwaitingPermission", "Esperando permiso de captura en Android.");
        return Reply(true, "VisionEngine LAN preparado; autoriza la captura en Android.",
            JsonSerializer.Serialize(offer));
    }

    private async Task AcceptVeLanVideoAsync(
        VETransportLanSession session,
        NLControlVeLanOffer offer,
        CancellationTokenSource cancellation,
        long generation)
    {
        try
        {
            VETransportLanAcceptedStreams streams = await session.AcceptAsync(cancellation.Token);
            if (_closing || !IsVeLanControlCurrent(
                    generation, _androidControlGeneration,
                    _androidLanControl?.IsAuthorized == true,
                    offer.Host, _androidLanControl?.Invitation.Host) ||
                !ReferenceEquals(_veLanSessionVE, session))
                throw new OperationCanceledException("La sesión LAN cambió antes de recibir video.");

            _veLanStreamsVE = streams;
            _veLanDegradedVE = streams.Degraded;
            SetVeLanPhase("Connecting", "Canales VE LAN autenticados; iniciando VisionEngine.");
            _activeVisionSerialVE = "LAN:" + offer.Host;
            _ = CreateVisionPresentationVE();
            VECoreResult result = await _visionEngineVE!.StartExternalSourceAsync(
                _activeVisionSerialVE,
                VEExternalSourceKind.NativeLan,
                streams.Video,
                streams.Control,
                streams.Audio,
                audioEnabled: streams.Audio is not null,
                cancellationToken: cancellation.Token);
            CompleteAppControlVideoStartVE(result, AttachVisionInputVE,
                () => _visionPresentationWindowVE?.HostVE.FocusInputVE(),
                AndroidEngineStateChanged);
            SetVeLanPhase(result.Success
                    ? streams.Degraded ? "Degraded" : "Streaming"
                    : "Error",
                result.Success
                    ? streams.Degraded
                        ? "Video VE LAN activo; audio no disponible."
                        : "Video VE LAN activo."
                    : result.Message);
            AndroidControlStatus.Text = streams.Degraded
                ? "VisionEngine transmite por LAN; audio no disponible."
                : "VisionEngine transmite por LAN nativa.";
        }
        catch (OperationCanceledException)
        {
            if (!_closing && ReferenceEquals(_veLanSessionVE, session))
            {
                SetVeLanPhase("Error", "La sesión VE LAN se canceló o venció.");
                AndroidControlStatus.Text = "El inicio de VE LAN se canceló o venció.";
            }
        }
        catch (Exception ex)
        {
            if (!_closing && ReferenceEquals(_veLanSessionVE, session))
            {
                SetVeLanPhase("Error", ex.Message);
                AndroidControlStatus.Text = "VE LAN no se inició: " + ex.Message;
            }
        }
        finally
        {
            if (_visionEngineVE?.RuntimeVE.ExternalSourceKindVE != VEExternalSourceKind.NativeLan &&
                ReferenceEquals(_veLanSessionVE, session))
                await DisposeVeLanTransportAsync();
            AndroidEngineStateChanged();
        }
    }

    private async Task StopVeLanVideoAsync(string reason = "VisionEngine LAN detenido.")
    {
        bool ownsLan = _veLanSessionVE is not null ||
            _visionEngineVE?.RuntimeVE.ExternalSourceKindVE == VEExternalSourceKind.NativeLan;
        if (!ownsLan) return;
        SetVeLanPhase("Stopping", reason);
        _veLanCancellationVE?.Cancel();
        if (_visionEngineVE?.RuntimeVE.ExternalSourceKindVE == VEExternalSourceKind.NativeLan)
            await _visionEngineVE.StopAsync();
        await DisposeVeLanTransportAsync();
        _activeVisionSerialVE = null;
        _veLanDegradedVE = false;
        CloseVisionPresentationVE(restoreMainWindow: true, refreshInformation: true);
        if (!_closing) AndroidControlStatus.Text = reason;
        SetVeLanPhase(_androidLanControl?.IsAuthorized == true ? "Ready" : "Disconnected",
            _androidLanControl?.IsAuthorized == true ? "VisionEngine LAN listo." : "Control LAN desconectado.");
    }

    private async Task DisposeVeLanTransportAsync()
    {
        VETransportLanSession? session = _veLanSessionVE;
        VETransportLanAcceptedStreams? streams = _veLanStreamsVE;
        CancellationTokenSource? cancellation = _veLanCancellationVE;
        _veLanSessionVE = null;
        _veLanStreamsVE = null;
        _veLanCancellationVE = null;
        cancellation?.Cancel();
        if (streams is not null) await streams.DisposeAsync();
        if (session is not null) await session.DisposeAsync();
        cancellation?.Dispose();
    }
}
