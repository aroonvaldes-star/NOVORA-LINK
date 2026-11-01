namespace NOVORA.VisionEngine.Audio;

/// <summary>
/// Fuentes de audio disponibles para la captura Android.
/// </summary>
public enum VEAudioSource
{
    Output,
    Microphone,
    Playback,
    MicrophoneUnprocessed,
    MicrophoneCamcorder,
    MicrophoneVoiceRecognition,
    MicrophoneVoiceCommunication,
    VoiceCall,
    VoiceCallUplink,
    VoiceCallDownlink,
    VoicePerformance
}
