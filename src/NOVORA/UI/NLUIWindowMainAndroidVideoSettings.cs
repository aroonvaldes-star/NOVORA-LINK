using NOVORA.Control;
using NOVORA.Service;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Performance;
using System.Text.Json;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private async Task<NLControlReply> ApplyAndroidVideoSettingsAsync(NLControlRequest request)
    {
        var changes = JsonSerializer.Deserialize<NLControlVideoChanges>(request.Value!)!;
        long generation = _androidControlGeneration;
        string serial = _viewModel.Device.Serial;
        bool Authorized() => !_closing && AndroidControlSessionOpen && generation == _androidControlGeneration &&
            _viewModel.Device.Connected && _viewModel.Device.Serial == serial;
        NLControlReply Reply(bool ok, string message, string? value = null) => new(
            NLControlProtocol.Version, request.Id, ok, message, CaptureAndroidControlSnapshot(), Value: value);
        var before = CaptureAndroidControlSnapshot();
        VEExternalSourceKind sourceKind = _visionEngineVE?.RuntimeVE.ExternalSourceKindVE ?? VEExternalSourceKind.None;
        bool wasAppControlVideo = sourceKind == VEExternalSourceKind.UsbAppControl;
        bool wasNativeLanVideo = sourceKind == VEExternalSourceKind.NativeLan;
        var requestedMonitor = _monitorService.GetMonitors().SingleOrDefault(m => m.DeviceName == changes.Monitor);
        if (requestedMonitor is null) return Reply(false, "El monitor ya no está disponible.");
        bool presentationChanged = before.VideoSettings?.PresentationMode != changes.PresentationMode;
        bool presentationOnly = before.VideoRunning &&
            before.Profile == changes.Profile &&
            before.Bitrate == changes.Bitrate &&
            before.AudioOutput == changes.Audio &&
            before.VideoSettings?.Resolution == changes.Resolution &&
            before.VideoSettings?.Fps == changes.Fps &&
            (before.VideoSettings?.Monitor != changes.Monitor || presentationChanged);
        if (presentationOnly)
        {
            _viewModel.SelectedMonitor = requestedMonitor;
            _viewModel.VideoPresentationMode = changes.PresentationMode;
            RecalculateOutputProfile14();
            SaveSettingsFromViewModel14();
            CreateVisionPresentationVE();
            _androidControlRevision++;
            AndroidEngineStateChanged();
            return Reply(Authorized() && IsVisionEngineRunningVE(),
                IsVisionEngineRunningVE()
                    ? "Presentación actualizada. LAN, LinkEngine, VisionEngine y ExIn permanecen activos."
                    : "La presentación cambió, pero VisionEngine dejó de estar activo durante la operación.");
        }
        if (before.VideoRunning)
        {
            await PreserveVisionFailoverDuringRestartVEAsync(async () =>
            {
                if (wasNativeLanVideo) await StopVeLanVideoAsync("Aplicando ajustes de VisionEngine LAN.");
                else if (wasAppControlVideo) await StopAppControlVideoSourceAsync();
                else await SetVisionEngineRunningVEAsync(false, Authorized);
            });
        }
        if (!Authorized() || IsVisionEngineRunningVE()) return Reply(false, "No se aplicaron los ajustes: no se confirmó la detención o cambió la sesión.");
        _viewModel.RefreshAudioOutputOptions(_paths);
        var current = CaptureAndroidControlSnapshot();
        if (before.Profile != current.Profile || before.Bitrate != current.Bitrate || before.AudioOutput != current.AudioOutput ||
            before.VideoSettings?.Resolution != current.VideoSettings?.Resolution || before.VideoSettings?.Fps != current.VideoSettings?.Fps ||
            before.VideoSettings?.Monitor != current.VideoSettings?.Monitor ||
            before.VideoSettings?.PresentationMode != current.VideoSettings?.PresentationMode)
            return Reply(false, "VE detenido; los ajustes cambiaron en PC durante la operación. Revisa y vuelve a aplicar.");
        // Revalidate every value after the asynchronous stop, before changing any setting.
        string? error = NLControlCommands.Validate(request with { Revision = current.Revision }, current);
        if (error is not null) return Reply(false, error);
        var profile = Enum.Parse<VEProfile>(changes.Profile);
        VEProfileOptions unified = VEProfileOptions.CreateVE(profile);
        _visionEngineVE!.RuntimeVE.PerformanceVE.SetProfileVE(unified.PerformanceProfile);
        _visionEngineVE.RuntimeVE.NvidiaVE.SetProfileVE(unified.ToLegacyNvidiaVE());
        NLServiceVideoProfile.ApplyVE(_viewModel, profile);
        // Explicit selections override the profile defaults, so one setting cannot silently overwrite another.
        _viewModel.Bitrate = changes.Bitrate;
        _viewModel.MaxSize = int.Parse(changes.Resolution, System.Globalization.CultureInfo.InvariantCulture);
        _viewModel.TargetFps = int.Parse(changes.Fps, System.Globalization.CultureInfo.InvariantCulture);
        _viewModel.SelectedMonitor = requestedMonitor;
        _viewModel.VideoPresentationMode = changes.PresentationMode;
        _visionEngineVE.RuntimeVE.AudioVE.SelectedOutputVE = changes.Audio;
        _viewModel.SelectedAudioOutput = changes.Audio;
        RecalculateOutputProfile14();
        SaveSettingsFromViewModel14();
        _androidControlRevision++;
        return Reply(Authorized() && !IsVisionEngineRunningVE(), before.VideoRunning
            ? "Ajustes guardados. VisionEngine se detuvo; inícialo cuando estés listo."
            : "Ajustes guardados. Inicia VisionEngine cuando estés listo.");
    }
}
