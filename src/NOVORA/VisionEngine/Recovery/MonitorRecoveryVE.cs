using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Transport;
using NOVORA.VisionEngine.Video;

namespace NOVORA.VisionEngine.Recovery;

public sealed class MonitorRecoveryVE
{
    private readonly PolicyRecoveryVE _policyVE;
    private long _lastVideoFramesVE;
    private long _lastAudioFramesVE;
    private DateTimeOffset _lastVideoProgressUtcVE = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastAudioProgressUtcVE = DateTimeOffset.UtcNow;

    public MonitorRecoveryVE(PolicyRecoveryVE policy)
    {
        _policyVE = policy ?? throw new ArgumentNullException(nameof(policy));
        _policyVE.ValidateVE();
    }

    public HealthRecoveryVE EvaluateVE(
        StatusVideoVE video,
        StatusAudioVE audio,
        StatusControlVE control,
        StatesTransportVE transportState,
        bool audioExpected,
        bool controlExpected)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(control);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (transportState == StatesTransportVE.Failed)
            return HealthRecoveryVE.DegradedVE(ScopeRecoveryVE.Session, "El transporte VisionEngine reportó fallo.");

        // RendererEnabled es funcionamiento normal en Block D.

        if (video.State == StatesVideoVE.Failed)
            return HealthRecoveryVE.DegradedVE(ScopeRecoveryVE.Video, video.LastError ?? "Falló VideoVE.");

        if (video.Stats.DecodeErrors > 0)
            return HealthRecoveryVE.DegradedVE(ScopeRecoveryVE.Video, "VideoVE registró errores de decodificación.");

        if (video.Stats.FramesDecoded != _lastVideoFramesVE)
        {
            _lastVideoFramesVE = video.Stats.FramesDecoded;
            _lastVideoProgressUtcVE = now;
        }
        else if (video.State == StatesVideoVE.Streaming && now - _lastVideoProgressUtcVE >= _policyVE.NoProgressTimeout)
        {
            return HealthRecoveryVE.DegradedVE(ScopeRecoveryVE.Video, "VideoVE permanece conectado sin producir frames.");
        }

        if (audioExpected)
        {
            if (audio.State == StatesAudioVE.Failed || audio.Stats.DecodeErrors > 0 || audio.Stats.PlaybackErrors > 0)
                return HealthRecoveryVE.DegradedVE(ScopeRecoveryVE.Audio, audio.LastError ?? "Falló AudioVE.");

            if (audio.Stats.FramesDecoded != _lastAudioFramesVE)
            {
                _lastAudioFramesVE = audio.Stats.FramesDecoded;
                _lastAudioProgressUtcVE = now;
            }
            else if (audio.State == StatesAudioVE.Streaming && now - _lastAudioProgressUtcVE >= _policyVE.NoProgressTimeout)
            {
                return HealthRecoveryVE.DegradedVE(ScopeRecoveryVE.Audio, "AudioVE permanece conectado sin producir frames.");
            }
        }

        if (controlExpected && (control.State == StatesControlVE.Failed || control.Stats.Errors > 0))
            return HealthRecoveryVE.DegradedVE(ScopeRecoveryVE.Control, control.LastError ?? "Falló ControlVE.");

        return HealthRecoveryVE.HealthyVE();
    }
}
