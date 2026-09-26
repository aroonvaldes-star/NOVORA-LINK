using NOVORA.Control;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Transport;
using System.Text.Json;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private VETransportAppControl? _appControlVideoTransportVE;
    private CancellationTokenSource? _appControlVideoCancellationVE;
    private Task _appControlVideoTaskVE = Task.CompletedTask;
    private bool _androidVideoReverseOwned;

    internal static bool HasAppControlVideoMapping(string output)
    {
        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] columns = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length >= 3 &&
                columns[^2] == $"tcp:{VETransportAppControl.DevicePortVE}" &&
                columns[^1] == $"tcp:{VETransportAppControl.DevicePortVE}")
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
        var transport = new VETransportAppControl();
        VETransportAppControlOffer offer;
        try { offer = transport.PrepareVE(); }
        catch
        {
            await transport.DisposeAsync();
            throw;
        }

        var cancellation = new CancellationTokenSource();
        string serial = _androidControlSerial;
        long generation = _androidControlGeneration;
        _appControlVideoTransportVE = transport;
        _appControlVideoCancellationVE = cancellation;
        _appControlVideoTaskVE = AcceptAppControlVideoAsync(transport, offer, cancellation, serial, generation);
        var sourceOffer = new NLControlVideoSourceOffer(VETransportAppControl.DevicePortVE, offer.Token,
            ParseVideoBitrateVE(_viewModel.Bitrate), _viewModel.MaxSize, _viewModel.TargetFps);
        return Reply(true, "VisionEngine está esperando el flujo de pantalla autorizado.",
            JsonSerializer.Serialize(sourceOffer));
    }

    private async Task AcceptAppControlVideoAsync(VETransportAppControl transport,
        VETransportAppControlOffer offer, CancellationTokenSource cancellation, string serial, long generation)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            Stream stream = await transport.AcceptAsync(offer, TimeSpan.FromSeconds(20), timeout.Token);
            if (_closing || generation != _androidControlGeneration ||
                _androidControl?.IsAuthorized != true || _androidControlSerial != serial)
                throw new OperationCanceledException("La sesión USB cambió antes de recibir el video.");

            _activeVisionSerialVE = serial;
            _ = CreateVisionPresentationVE();
            VECoreResult result = await _visionEngineVE!.StartAppControlAsync(serial, stream, timeout.Token);
            if (!result.Success) throw result.Exception ?? new InvalidOperationException(result.Message);
            AndroidEngineStateChanged();
        }
        catch (OperationCanceledException)
        {
            if (!_closing && ReferenceEquals(_appControlVideoTransportVE, transport))
                AndroidControlStatus.Text = "El inicio de video se canceló o agotó el tiempo de espera.";
        }
        catch (Exception ex)
        {
            if (!_closing && ReferenceEquals(_appControlVideoTransportVE, transport))
                AndroidControlStatus.Text = "Video AppControl no se inició: " + ex.Message;
        }
        finally
        {
            if (_visionEngineVE?.IsRunningVE != true)
            {
                _activeVisionSerialVE = null;
                CloseVisionPresentationVE(restoreMainWindow: true, refreshInformation: true);
                if (ReferenceEquals(_appControlVideoTransportVE, transport))
                {
                    _appControlVideoTransportVE = null;
                    _appControlVideoCancellationVE = null;
                    await transport.DisposeAsync();
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
        CancellationTokenSource? cancellation = _appControlVideoCancellationVE;
        _appControlVideoTransportVE = null;
        _appControlVideoCancellationVE = null;
        cancellation?.Cancel();
        if (transport is not null) await transport.DisposeAsync();
        cancellation?.Dispose();
    }
}
