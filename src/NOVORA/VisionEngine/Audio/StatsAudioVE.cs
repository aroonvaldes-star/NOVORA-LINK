namespace NOVORA.VisionEngine.Audio;

public sealed record StatsAudioVE(
    long PacketsReceived,
    long BytesReceived,
    long FramesDecoded,
    long PcmBytesProduced,
    long DecodeErrors,
    long PlaybackErrors);
