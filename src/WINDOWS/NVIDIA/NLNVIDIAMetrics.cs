namespace NOVORA.NVIDIA;

public sealed record NLNVIDIAMetrics(
    long PacketsSubmitted,
    long FramesDecoded,
    long FramesDropped,
    long Fallbacks,
    double LastDecodeMilliseconds,
    double MaxDecodeMilliseconds)
{
    public static NLNVIDIAMetrics EmptyVE() => new(0, 0, 0, 0, 0, 0);
}
