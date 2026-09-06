namespace NOVORA.VisionEngine.Audio;

public sealed record PacketAudioVE(
    long? PresentationTimeUs,
    bool IsConfiguration,
    byte[] Data)
{
    public int LengthVE => Data.Length;
}
