namespace NOVORA.VisionEngine.Audio;

/// <summary>
/// Fuentes de audio expuestas por el servidor Android de scrcpy 4.1.
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

public static class VEAudioExtensionsSource
{
    public static string GetServerNameVE(this VEAudioSource source)
        => source switch
        {
            VEAudioSource.Output => "output",
            VEAudioSource.Microphone => "mic",
            VEAudioSource.Playback => "playback",
            VEAudioSource.MicrophoneUnprocessed => "mic-unprocessed",
            VEAudioSource.MicrophoneCamcorder => "mic-camcorder",
            VEAudioSource.MicrophoneVoiceRecognition => "mic-voice-recognition",
            VEAudioSource.MicrophoneVoiceCommunication => "mic-voice-communication",
            VEAudioSource.VoiceCall => "voice-call",
            VEAudioSource.VoiceCallUplink => "voice-call-uplink",
            VEAudioSource.VoiceCallDownlink => "voice-call-downlink",
            VEAudioSource.VoicePerformance => "voice-performance",
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
}
