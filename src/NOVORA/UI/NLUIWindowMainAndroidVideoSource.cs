using NOVORA.Control;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Transport;
using System.Text.Json;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private VETransportAppControl? _appControlVideoTransportVE;
    private VETransportAppControl? _appControlControlTransportVE;
    private VETransportAppControl? _appControlAudioTransportVE;
    private CancellationTokenSource? _appControlVideoCancellationVE;
    private Task _appControlVideoTaskVE = Task.CompletedTask;
    private bool _androidVideoReverseOwned;
    private bool _androidControlInputReverseOwned;
    private bool _androidAudioReverseOwned;

    internal static bool HasAppControlVideoMapping(string output)
        => HasAppControlMapping(output, VETransportAppControl.DevicePortVE);

    internal static bool HasAppControlControlMapping(string output)
        => HasAppControlMapping(output, VETransportAppControl.ControlPortVE);

    internal static bool HasAppControlAudioMapping(string output)
        => HasAppControlMapping(output, VETransportAppControl.AudioPortVE);

    private static bool HasAppControlMapping(string output, int port)
    {
        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] columns = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length >= 3 &&
                columns[^2] == $"tcp:{port}" &&
                columns[^1] == $"tcp:{port}")
                return true;
        }
        return false;
    }

    private async Task<NLControlReply> PrepareAppControlVideoAsync(NLControlRequest request)
    {
        NLControlReply Reply(bool success, string message, string? value = null) => new(
            NLControlProtocol.Version, request.Id, success, message, CaptureAndroidControlSnapshot(), Value: value);
        if (_androidControl?.IsAuthorized != true || string.IsNullOrWhiteSpace(_androidControlSerial))
            return Reply(false, "El video de AppControl requiere la sesión USB autorizada.");
        if (IsVisionEngineRunningVE() || !_appControlVideoTaskVE.IsCompleted)
            return Reply(false, "VisionEngine ya tiene una sesión activa o en preparación.");

        InitializeVisionEngineRuntimeVE();
        if (!_visionEngineVE!.StatusVE.IsInitialized)
        {
            VECoreResult initialized = await _visionEngineVE.InitializeAsync();
            if (!initialized.Success) return Reply(false, initialized.Message);
        }

        await DisposeAppControlVideoTransportAsync();
        var videoTransport = new VETransportAppControl();
        var controlTransport = VETransportAppControl.CreateControlVE();
        var audioTransport = VETransportAppControl.CreateAudioVE();
        VETransportAppControlOffer videoOffer;
        VETransportAppControlOffer controlOffer;
        VETransportAppControlOffer audioOffer;
        try
        {
            videoOffer = videoTransport.PrepareVE();
            controlOffer = controlTransport.PrepareVE();
            audioOffer = audioTransport.PrepareVE();
        }
        catch
        {
            await videoTransport.DisposeAsync();
            await controlTransport.DisposeAsync();
            await audioTransport.DisposeAsync();
            throw;
        }

        var cancellation = new CancellationTokenSource();
        string serial = _androidControlSerial;
        long generation = _androidControlGeneration;
        _appControlVideoTransportVE = videoTransport;
        _appControlControlTransportVE = controlTransport;
        _appControlAudioTransportVE = audioTransport;
        _appControlVideoCancellationVE = cancellation;
        _appControlVideoTaskVE = AcceptAppControlVideoAsync(videoTransport, videoOffer,
            controlTransport, controlOffer, audioTransport, audioOffer, cancellation, serial, generation);
        var sourceOffer = new NLControlVideoSourceOffer(VETransportAppControl.DevicePortVE, videoOffer.Token,
            ParseVideoBitrateVE(_viewModel.Bitrate), _viewModel.MaxSize, _viewModel.TargetFps,
            VETransportAppControl.ControlPortVE, controlOffer.Token,
            VETransportAppControl.AudioPortVE, audioOffer.Token);
        return Reply(true, "VisionEngine está esperando el flujo de pantalla autorizado.",
            JsonSerializer.Serialize(sourceOffer));
    }

    private async Task AcceptAppControlVideoAsync(VETransportAppControl videoTransport,
        VETransportAppControlOffer videoOffer, VETransportAppControl controlTransport,
        VETransportAppControlOffer controlOffer, VETransportAppControl audioTransport,
        VETransportAppControlOffer audioOffer, CancellationTokenSource cancellation, string serial, long generation)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            Task<Stream> videoAccept = videoTransport.AcceptAsync(videoOffer, TimeSpan.FromSeconds(20), timeout.Token);
            Task<Stream> controlAccept = controlTransport.AcceptAsync(controlOffer, TimeSpan.FromSeconds(20), timeout.Token);
            Task<Stream> audioAccept = audioTransport.AcceptAsync(audioOffer, TimeSpan.FromSeconds(20), timeout.Token);
            await Task.WhenAll(videoAccept, controlAccept, audioAccept);
            Stream videoStream = await videoAccept;
            Stream controlStream = await controlAccept;
            Stream audioStream = await audioAccept;
            if (_closing || generation != _androidControlGeneration ||
                _androidControl?.IsAuthorized != true || _androidControlSerial != serial)
                throw new OperationCanceledException("La sesión USB cambió antes de recibir el video.");

            _activeVisionSerialVE = serial;
            _ = CreateVisionPresentationVE();
            VECoreResult result = await _visionEngineVE!.StartAppControlAsync(
                serial, videoStream, controlStream, audioStream, timeout.Token);
            if (!result.Success) throw result.Exception ?? new InvalidOperationException(result.Message);
            AndroidEngineStateChanged();
        }
        catch (OperationCanceledException)
        {
            if (!_closing && ReferenceEquals(_appControlVideoTransportVE, videoTransport))
                AndroidControlStatus.Text = "El inicio de video se canceló o agotó el tiempo de espera.";
        }
        catch (Exception ex)
        {
            if (!_closing && ReferenceEquals(_appControlVideoTransportVE, videoTransport))
                AndroidControlStatus.Text = "Video AppControl no se inició: " + ex.Message;
        }
        finally
        {
            if (_visionEngineVE?.IsRunningVE != true)
            {
                _activeVisionSerialVE = null;
                CloseVisionPresentationVE(restoreMainWindow: true, refreshInformation: true);
                if (ReferenceEquals(_appControlVideoTransportVE, videoTransport))
                {
                    _appControlVideoTransportVE = null;
                    _appControlControlTransportVE = null;
                    _appControlAudioTransportVE = null;
                    _appControlVideoCancellationVE = null;
                    await videoTransport.DisposeAsync();
                    await controlTransport.DisposeAsync();
                    await audioTransport.DisposeAsync();
                    cancellation.Dispose();
                }
            }
            AndroidEngineStateChanged();
        }
    }

    private async Task StopAppControlVideoSourceAsync()
    {
        bool ownsVideo = _appControlVideoTransportVE is not null ||
            _visionEngineVE?.RuntimeVE.IsAppControlVideoActiveVE == true;
        if (!ownsVideo) return;
        _appControlVideoCancellationVE?.Cancel();
        if (_visionEngineVE?.RuntimeVE.IsAppControlVideoActiveVE == true)
            await _visionEngineVE.StopAsync();
        await DisposeAppControlVideoTransportAsync();
        _activeVisionSerialVE = null;
        CloseVisionPresentationVE(restoreMainWindow: true, refreshInformation: true);
        AndroidEngineStateChanged();
    }

    private async Task DisposeAppControlVideoTransportAsync()
    {
        VETransportAppControl? transport = _appControlVideoTransportVE;
        VETransportAppControl? controlTransport = _appControlControlTransportVE;
        VETransportAppControl? audioTransport = _appControlAudioTransportVE;
        CancellationTokenSource? cancellation = _appControlVideoCancellationVE;
        _appControlVideoTransportVE = null;
        _appControlControlTransportVE = null;
        _appControlAudioTransportVE = null;
        _appControlVideoCancellationVE = null;
        cancellation?.Cancel();
        if (transport is not null) await transport.DisposeAsync();
        if (controlTransport is not null) await controlTransport.DisposeAsync();
        if (audioTransport is not null) await audioTransport.DisposeAsync();
        cancellation?.Dispose();
    }
}
