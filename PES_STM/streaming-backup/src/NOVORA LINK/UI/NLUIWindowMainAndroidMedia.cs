using NOVORA.Control;
using NOVORA.LinkEngine.Egress;
using NOVORA.VisionEngine.Video;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private VEMediaRecorder? _observedAndroidMedia;
    private LEEgressLiveSession? _observedAndroidLive;
    private System.Windows.Threading.DispatcherTimer? _androidLiveTicker;
    private long _androidLiveLastBytes;
    private double _androidLiveKbps;

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
        var live = runtime?.LiveVE;
        if (!ReferenceEquals(live, _observedAndroidLive))
        {
            if (_observedAndroidLive is not null) _observedAndroidLive.StatusChangedLE -= AndroidLiveChanged;
            _observedAndroidLive = live;
            if (live is not null) live.StatusChangedLE += AndroidLiveChanged;
        }
        if (media is null || live is null) return new(false, false, false, "VisionEngine no está preparado.");
        var status = media.StatusVE;
        var liveStatus = live.StatusLE;
        bool configured = LEEgressTargetStore.Load()?.IsComplete == true;
        return new(media.CanCapture, media.CanRecord, status.Recording,
            status.Error ?? (status.Starting ? "Preparando grabación: esperando imagen inicial…" :
                status.Recording ? status.AudioIncluded ? "Grabación activa · video y audio del teléfono · sin micrófono." : "Grabación activa · esperando audio del teléfono · sin micrófono." :
                status.LastFile is { } file ? "Guardado: " + file :
                    !media.CanRecord ? "Para grabar: VE con H.264 y audio interno activo, sin micrófono." : "Capturas y grabaciones en Escritorio/NOVORA-Files."),
            status.Starting, status.AudioIncluded,
            configured && !liveStatus.Active && live.CanStartLE, liveStatus.Active, liveStatus.State.ToString(),
            DescribeLive(liveStatus, configured));
    }

    private string DescribeLive(LEEgressStatus status, bool configured) => status.State switch
    {
        LEEgressStates.Connecting => "Conectando con el servidor de transmisión…",
        LEEgressStates.Live => $"EN VIVO {(DateTimeOffset.Now - (status.LiveSince ?? DateTimeOffset.Now)):hh\\:mm\\:ss} · {_androidLiveKbps / 1000:0.0} Mbps" +
            (status.AudioIncluded ? " · con audio" : " · sin audio") + (status.DroppedVideo > 0 ? $" · {status.DroppedVideo} descartados" : ""),
        LEEgressStates.Reconnecting => $"Reconectando ({status.Reconnects}): {status.Error}",
        LEEgressStates.Paused => "Privacidad activa: transmisión en pausa.",
        LEEgressStates.Error => "Transmisión detenida: " + status.Error,
        _ => configured ? "Listo para transmitir en vivo." : "Configura servidor y clave en Ajustes › Transmisión en vivo."
    };

    private void AndroidLiveChanged(object? sender, LEEgressStatus state)
    {
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            if (_closing || !ReferenceEquals(sender, _observedAndroidLive)) return;
            // Telemetría a 1 Hz mientras la transmisión está activa; nunca por frame.
            if (state.Active && _androidLiveTicker is null)
            {
                _androidLiveLastBytes = 0;
                _androidLiveTicker = new(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
                _androidLiveTicker.Tick += (_, _) =>
                {
                    var current = _observedAndroidLive?.StatusLE;
                    if (current is null || !current.Active) { _androidLiveTicker?.Stop(); _androidLiveTicker = null; _androidLiveKbps = 0; }
                    else { _androidLiveKbps = Math.Max(0, current.BytesSent - _androidLiveLastBytes) * 8 / 1000.0; _androidLiveLastBytes = current.BytesSent; }
                    _androidControlRevision++;
                    QueueAndroidControlSnapshot();
                };
                _androidLiveTicker.Start();
            }
            QueueStreamingRefresh14();
            _androidControlRevision++;
            QueueAndroidControlSnapshot();
        }));
    }

    private void AndroidMediaChanged(object? sender, VEMediaStatus state)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_closing || !ReferenceEquals(sender, _observedAndroidMedia)) return;
            QueueStreamingRefresh14();
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
            case "startLive":
                var target = LEEgressTargetStore.Load() ?? throw new InvalidOperationException("Configura servidor y clave en Ajustes › Transmisión en vivo.");
                _visionEngineVE!.RuntimeVE.LiveVE.Start(target);
                message = "Conectando la transmisión en vivo…";
                break;
            case "stopLive": await _visionEngineVE!.RuntimeVE.LiveVE.StopAsync(); message = "Transmisión en vivo detenida."; break;
            default: throw new InvalidOperationException("Acción multimedia desconocida.");
        }
        _androidControlRevision++;
        return new(NLControlProtocol.Version, request.Id, true, message, CaptureAndroidControlSnapshot());
    }

    private async Task FinishAndroidRecordingAsync()
    {
        if (_visionEngineVE?.RuntimeVE.LiveVE is { StatusLE.Active: true } live) await live.StopAsync();
        var media = _visionEngineVE?.RuntimeVE.RecordingVE;
        if (media?.StatusVE.Recording == true)
        {
            try { await media.StopAsync(); }
            catch (Exception ex)
            {
                _ = ex;
                // Recorder retains the precise failure in its status.
            }
        }
    }
}

