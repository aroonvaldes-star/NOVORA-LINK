using NOVORA.VisionEngine.Metrics;

namespace NOVORA.VisionEngine.Stress;

public sealed class DecoderStressVE
{
    private long _startFramesVE = -1;
    private long _endFramesVE;
    private long _decodeErrorsVE;

    public void ObserveVE(VideoMetricsVE video)
    {
        ArgumentNullException.ThrowIfNull(video);
        if (_startFramesVE < 0)
            Interlocked.CompareExchange(ref _startFramesVE, video.FramesDecoded, -1);
        Interlocked.Exchange(ref _endFramesVE, video.FramesDecoded);
        Interlocked.Exchange(ref _decodeErrorsVE, video.DecodeErrors);
    }

    public long StartFramesVE => Math.Max(0, Interlocked.Read(ref _startFramesVE));
    public long EndFramesVE => Interlocked.Read(ref _endFramesVE);
    public long DecodeErrorsVE => Interlocked.Read(ref _decodeErrorsVE);
    public bool HasProgressVE => EndFramesVE > StartFramesVE;
}
