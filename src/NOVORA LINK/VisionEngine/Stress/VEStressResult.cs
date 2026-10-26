namespace NOVORA.VisionEngine.Stress;

public sealed record VEStressResult(
    bool Success,
    TimeSpan Duration,
    int Samples,
    long StartFrames,
    long EndFrames,
    long DecodeErrors,
    int RecoveryAttempts,
    long PeakWorkingSetBytes,
    double PeakCpuPercent,
    string Message);
