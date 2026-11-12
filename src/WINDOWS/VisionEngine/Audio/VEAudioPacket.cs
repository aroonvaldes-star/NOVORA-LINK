namespace NOVORA.VisionEngine.Audio;

public sealed record VEAudioPacket(
    long? PresentationTimeUs,
    bool IsConfiguration,
    byte[] Data)
{
    public int LengthVE => Data.Length;
}
