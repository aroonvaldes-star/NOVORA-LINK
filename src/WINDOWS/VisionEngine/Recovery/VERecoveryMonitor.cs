using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Transport;
using NOVORA.VisionEngine.Video;

namespace NOVORA.VisionEngine.Recovery;

public sealed class VERecoveryMonitor
{
    private readonly VERecoveryPolicy _policyVE;
    private long _lastVideoFramesVE;
    private long _lastAudioFramesVE;
    private DateTimeOffset _lastVideoProgressUtcVE = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastAudioProgressUtcVE = DateTimeOffset.UtcNow;

    public VERecoveryMonitor(VERecoveryPolicy policy)
    {
        _policyVE = policy ?? throw new ArgumentNullException(nameof(policy));
        _policyVE.ValidateVE();
    }

    public VERecoveryHealth EvaluateVE(
        VEVideoStatus video,
        VEAudioStatus audio,
        VEControlStatus control,
        VETransportStates transportState,
        bool audioExpected,
        bool controlExpected)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(control);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (transportState == VETransportStates.Failed)
            return VERecoveryHealth.DegradedVE(VERecoveryScope.Session, "El transporte VisionEngine reportó fallo.");

        // RendererEnabled es funcionamiento normal en Block D.

        if (video.State == VEVideoStates.Failed)
            return VERecoveryHealth.DegradedVE(VERecoveryScope.Video, video.LastError ?? "Falló VideoVE.");

        if (video.Stats.DecodeErrors > 0)
            return VERecoveryHealth.DegradedVE(VERecoveryScope.Video, "VideoVE registró errores de decodificación.");

        if (video.Stats.FramesDecoded != _lastVideoFramesVE)
        {
            _lastVideoFramesVE = video.Stats.FramesDecoded;
            _lastVideoProgressUtcVE = now;
        }
        else if (video.State == VEVideoStates.Streaming && now - _lastVideoProgressUtcVE >= _policyVE.NoProgressTimeout)
        {
            return VERecoveryHealth.DegradedVE(VERecoveryScope.Video, "VideoVE permanece conectado sin producir frames.");
        }

        if (audioExpected)
        {
            if (audio.State == VEAudioStates.Failed || audio.Stats.DecodeErrors > 0 || audio.Stats.PlaybackErrors > 0)
                return VERecoveryHealth.DegradedVE(VERecoveryScope.Audio, audio.LastError ?? "Falló AudioVE.");

            if (audio.Stats.FramesDecoded != _lastAudioFramesVE)
            {
                _lastAudioFramesVE = audio.Stats.FramesDecoded;
                _lastAudioProgressUtcVE = now;
            }
            else if (audio.State == VEAudioStates.Streaming && now - _lastAudioProgressUtcVE >= _policyVE.NoProgressTimeout)
            {
                return VERecoveryHealth.DegradedVE(VERecoveryScope.Audio, "AudioVE permanece conectado sin producir frames.");
            }
        }

        if (controlExpected && (control.State == VEControlStates.Failed || control.Stats.Errors > 0))
            return VERecoveryHealth.DegradedVE(VERecoveryScope.Control, control.LastError ?? "Falló ControlVE.");

        return VERecoveryHealth.HealthyVE();
    }
}
