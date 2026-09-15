using NOVORA.Control;
using NOVORA.VisionEngine.Video;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private VEMediaRecorder? _observedAndroidMedia;

    private NLControlMedia CaptureAndroidMedia()
    {
        var runtime = _visionEngineVE?.RuntimeVE;
        var media = runtime?.RecordingVE;
        if (!ReferenceEquals(media, _observedAndroidMedia))
        {
            if (_observedAndroidMedia is not null) _observedAndroidMedia.StatusChangedVE -= AndroidMediaChanged;
            _observedAndroidMedia = media;
            if (media is not null) media.StatusChangedVE += AndroidMediaChanged;
        }
        if (media is null) return new(false, false, false, "VisionEngine no está preparado.");
        var status = media.StatusVE;
        return new(media.CanCapture, media.CanRecord, status.Recording,
            status.Error ?? (status.Starting ? "Preparando grabación: esperando imagen inicial…" :
                status.Recording ? status.AudioIncluded ? "Grabación activa · video y audio del teléfono · sin micrófono." : "Grabación activa · esperando audio del teléfono · sin micrófono." :
                status.LastFile is { } file ? "Guardado: " + file :
                    !media.CanRecord ? "Para grabar: VE con H.264 y audio interno activo, sin micrófono." : "Capturas y grabaciones en Escritorio/NOVORA-Files."),
            status.Starting, status.AudioIncluded);
    }

    private void AndroidMediaChanged(object? sender, VEMediaStatus state)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_closing || !ReferenceEquals(sender, _observedAndroidMedia)) return;
            _androidControlRevision++;
            QueueAndroidControlSnapshot();
        }));
    }

    private async Task<NLControlReply> ApplyAndroidMediaAsync(NLControlRequest request)
    {
        var media = _visionEngineVE?.RuntimeVE.RecordingVE ?? throw new InvalidOperationException("VisionEngine no está disponible.");
        string message;
        switch (request.Action)
        {
            case "capture": message = "Captura guardada: " + await media.CaptureAsync(); break;
            case "startRecording": media.Start(); message = "Solicitud de grabación aceptada. Esperando la primera imagen; sin micrófono."; break;
            case "stopRecording": message = "Grabación guardada: " + await media.StopAsync(); break;
            default: throw new InvalidOperationException("Acción multimedia desconocida.");
        }
        _androidControlRevision++;
        return new(NLControlProtocol.Version, request.Id, true, message, CaptureAndroidControlSnapshot());
    }

    private async Task FinishAndroidRecordingAsync()
    {
        var media = _visionEngineVE?.RuntimeVE.RecordingVE;
        if (media?.StatusVE.Recording == true)
        {
            try { await media.StopAsync(); }
            catch (Exception) { /* Recorder retains the precise failure in its status. */ }
        }
    }
}

