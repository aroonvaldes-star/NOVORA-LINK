namespace NOVORA.VisionEngine.Audio;

public sealed record VEAudioStats(
    long PacketsReceived,
    long BytesReceived,
    long FramesDecoded,
    long PcmBytesProduced,
    long DecodeErrors,
    long PlaybackErrors);
