namespace NOVORA.VisionEngine.Audio;

/// <summary>
/// Fuentes de audio expuestas por el servidor Android de scrcpy 4.1.
/// </summary>
public enum SourceAudioVE
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

public static class ExtensionsSourceAudioVE
{
    public static string GetServerNameVE(this SourceAudioVE source)
        => source switch
        {
            SourceAudioVE.Output => "output",
            SourceAudioVE.Microphone => "mic",
            SourceAudioVE.Playback => "playback",
            SourceAudioVE.MicrophoneUnprocessed => "mic-unprocessed",
            SourceAudioVE.MicrophoneCamcorder => "mic-camcorder",
            SourceAudioVE.MicrophoneVoiceRecognition => "mic-voice-recognition",
            SourceAudioVE.MicrophoneVoiceCommunication => "mic-voice-communication",
            SourceAudioVE.VoiceCall => "voice-call",
            SourceAudioVE.VoiceCallUplink => "voice-call-uplink",
            SourceAudioVE.VoiceCallDownlink => "voice-call-downlink",
            SourceAudioVE.VoicePerformance => "voice-performance",
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
}
