using NOVORA.VisionEngine.Audio;

namespace NOVORA.VisionEngine.Integration;

/// <summary>
/// Audio Android -> NOVORA.
///
/// VEAudioSource ya permite seleccionar fuentes de micrófono
/// al inicio de la sesión.
///
/// PC microphone -> Android todavía NO está implementado.
/// </summary>
public sealed class VEIntegrationMicrophone
{
    public bool AndroidMicrophoneAtSessionStartVE =>
        true;

    public bool PcMicrophoneToAndroidVE =>
        false;

    public IReadOnlyList<VEAudioSource> AndroidMicrophoneSourcesVE { get; } =
        new[]
        {
            VEAudioSource.Microphone,
            VEAudioSource.MicrophoneUnprocessed,
            VEAudioSource.MicrophoneCamcorder,
            VEAudioSource.MicrophoneVoiceRecognition,
            VEAudioSource.MicrophoneVoiceCommunication
        };
}