using NOVORA.VisionEngine.Audio;

namespace NOVORA.VisionEngine.Integration;

/// <summary>
/// Audio Android -> NOVORA.
///
/// SourceAudioVE ya permite seleccionar fuentes de micrófono
/// al inicio de la sesión.
///
/// PC microphone -> Android todavía NO está implementado.
/// </summary>
public sealed class MicrophoneIntegrationVE
{
    public bool AndroidMicrophoneAtSessionStartVE =>
        true;

    public bool PcMicrophoneToAndroidVE =>
        false;

    public IReadOnlyList<SourceAudioVE> AndroidMicrophoneSourcesVE { get; } =
        new[]
        {
            SourceAudioVE.Microphone,
            SourceAudioVE.MicrophoneUnprocessed,
            SourceAudioVE.MicrophoneCamcorder,
            SourceAudioVE.MicrophoneVoiceRecognition,
            SourceAudioVE.MicrophoneVoiceCommunication
        };
}