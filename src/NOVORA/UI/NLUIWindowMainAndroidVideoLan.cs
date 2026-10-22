using System.Net;
using System.Text.Json;
using NOVORA.Control;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Exchange;
using NOVORA.VisionEngine.Transport;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private VETransportLanSession? _veLanSessionVE;
    private NLControlVeLanOffer? _veLanOfferVE;
    private VETransportLanAcceptedStreams? _veLanStreamsVE;
    private VEExchangeLanFileSender? _veLanFilesExchangeVE;
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

        if (_externalVisionWaitingSourceVE == VEExternalSourceKind.NativeLan)
            ClearExternalVisionWaitingForDeviceVE();

        SetVeLanPhase("Preparing", "Preparando listeners cifrados de VisionEngine LAN.");
        InitializeVisionEngineRuntimeVE();
        if (!_visionEngineVE!.StatusVE.IsInitialized)
        {
            VECoreResult initialized = await _visionEngineVE.InitializeAsync();
            if (!initialized.Success) return Reply(false, initialized.Message);
        }

        await StopVeLanVideoAsync("Preparando una sesión VE LAN nueva.");
        bool audioEnabled = request.Value != NLControlProtocol.VideoOnlyCapture &&
            _viewModel.SelectedAudioOutput != VisionEngine.Audio.VEAudioOutput.DisabledValueVE;
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
        long captureGeneration=NewExperimentalCaptureGeneration("LAN");
        offer=offer with { CaptureGeneration=captureGeneration,
            CaptureCapabilities=captureGeneration>0 ? ExperimentalPeer("LAN") : null,
            FilesWireVersion=captureGeneration>0 && ExperimentalPeer("LAN")?.FilesWireVersions?.Contains(2)==true ? 2 : 1 };
        SetExperimentalVisionSession(captureGeneration,"VE:LAN:"+offer.Host,offer.Bitrate,offer.Fps);
        _veLanSessionVE = session;
        _veLanOfferVE = offer;
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
            // Cada condición reporta su motivo exacto en el estado de VE LAN (visible en AppControl).
            if (_closing)
                throw new OperationCanceledException("PC cerrándose; VE LAN no se inicia.");
            if (!ReferenceEquals(_veLanSessionVE, session))
                throw new OperationCanceledException("Otra sesión VE LAN reemplazó a esta antes de recibir video.");
            if (generation != _androidControlGeneration)
                throw new OperationCanceledException(
                    $"El control de Android cambió de generación ({generation} → {_androidControlGeneration}).");
            if (_androidLanControl?.IsAuthorized != true)
                throw new OperationCanceledException("El control LAN no está autorizado; empareja AppControl de nuevo.");
            string? controlHost = _androidLanControl.Invitation.Host;
            if (!string.Equals(offer.Host, controlHost, StringComparison.OrdinalIgnoreCase))
                throw new OperationCanceledException(
                    $"El host del control LAN ({controlHost}) no coincide con la oferta de video ({offer.Host}).");

            _veLanStreamsVE = streams;
            if(streams.Files is not null && offer.FilesWireVersion==2)
            {
                await StartExperimentalFilesV2Async(streams.Files,offer.CaptureGeneration);
                if (_experimentalFilesV2 is not null)
                    await BindLanExchangeVEAsync(_experimentalFilesV2);
            }
            else _veLanFilesExchangeVE=streams.Files is null ? null : new VEExchangeLanFileSender(streams.Files,TimeSpan.FromSeconds(30),PaceExperimentalFilesAsync);
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
        catch (OperationCanceledException ex)
        {
            if (!_closing && ReferenceEquals(_veLanSessionVE, session))
            {
                string reason = string.IsNullOrWhiteSpace(ex.Message) || ex.Message.StartsWith("The operation was canceled", StringComparison.Ordinal)
                    ? "La sesión VE LAN se canceló o venció (sin motivo registrado)."
                    : ex.Message;
                SetVeLanPhase("Error", reason);
                AndroidControlStatus.Text = "VE LAN no inició: " + reason;
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
            _visionEngineVE?.RuntimeVE.ExternalSourceKindVE == VEExternalSourceKind.NativeLan ||
            _externalVisionWaitingForDeviceVE &&
                _externalVisionWaitingSourceVE == VEExternalSourceKind.NativeLan;
        if (!ownsLan) return;
        SetVeLanPhase("Stopping", reason);
        _veLanCancellationVE?.Cancel();
        if (_visionEngineVE?.RuntimeVE.ExternalSourceKindVE == VEExternalSourceKind.NativeLan)
            await _visionEngineVE.StopAsync();
        await DisposeVeLanTransportAsync();
        ClearExternalVisionWaitingForDeviceVE();
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
        VEExchangeLanFileSender? files = _veLanFilesExchangeVE;
        CancellationTokenSource? cancellation = _veLanCancellationVE;
        _veLanSessionVE = null;
        _veLanOfferVE = null;
        _veLanStreamsVE = null;
        _veLanFilesExchangeVE = null;
        _veLanCancellationVE = null;
        cancellation?.Cancel();
        await ReleaseExchangeVEAsync(NOVORA.VisionEngine.Exchange.VEExchangeRoute.NativeLan);
        try { await StopExperimentalFilesV2Async(); }
        finally
        {
            // A storage cleanup error is reported, while the other transport channels
            // still close. FILES must not leave video/audio/control alive during Stop.
            try { if (files is not null) await files.DisposeAsync(); }
            finally
            {
                try { if (streams is not null) await streams.DisposeAsync(); }
                finally
                {
                    try { if (session is not null) await session.DisposeAsync(); }
                    finally { cancellation?.Dispose(); }
                }
            }
        }
    }

    private async Task<int> QueueVisionExchangePathsAsync(
        string serial,
        IReadOnlyList<string> paths)
    {
        if (ActiveExchangeTransportVE() is { } transport)
        {
            VEExchangeResult sent = await transport.SendPathsAsync(paths, CancellationToken.None);
            if (!sent.Success)
                throw new InvalidOperationException(sent.Message + (sent.Detail is null ? string.Empty : " " + sent.Detail));
            if (transport.Route == NOVORA.VisionEngine.Exchange.VEExchangeRoute.NativeLan)
                foreach (string path in paths) RecordSentFilesVE(path);
            return (int)sent.ItemCount;
        }

        if (_visionEngineVE?.RuntimeVE.ExternalSourceKindVE == VEExternalSourceKind.NativeLan)
        {
            if (_experimentalFilesV2 is not null)
            {
                int sent = await _experimentalFilesV2.SendPathsAsync(paths);
                foreach (string path in paths) RecordSentFilesVE(path);
                return sent;
            }
            VEExchangeLanFileSender sender = _veLanFilesExchangeVE ?? throw new InvalidOperationException(
                "LAN requiere abrir Archivos en Android y seleccionar la carpeta /sdcard/NOVORA antes de iniciar VisionEngine.");
            int completed = await sender.SendPathsAsync(paths);
            foreach (string path in paths) RecordSentFilesVE(path);
            return completed;
        }

        if (_visionTransferExchangeVE is null)
            throw new InvalidOperationException("La cola de archivos VisionEngine no está disponible.");
        await _visionTransferExchangeVE.QueueAsync(serial, paths);
        return paths.Count;
    }
}
