namespace NOVORA.VisionEngine.Audio;

/// <summary>
/// PCM16LE estéreo a 48 kHz listo para reproducción. VisionEngine mantiene
/// audio separado del renderer de video.
/// </summary>
public sealed record VEAudioFrame(
    long Sequence,
    long? SourcePresentationTimeUs,
    int SampleRate,
    int Channels,
    int SamplesPerChannel,
    byte[] Pcm16Le,
    DateTimeOffset DecodedAtUtc);
