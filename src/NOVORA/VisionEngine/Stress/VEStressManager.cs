using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Recovery;

namespace NOVORA.VisionEngine.Stress;

public sealed class VEStressManager
{
    private readonly VEPerformanceManager _performanceVE;

    public VEStressManager(VEPerformanceManager? performance = null)
    {
        _performanceVE = performance ?? new VEPerformanceManager();
    }

    public async Task<VEStressResult> RunAsync(
        Func<VEMetricsSnapshot> metricsProvider,
        Func<int> recoveryAttemptsProvider,
        TimeSpan duration,
        TimeSpan? sampleInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metricsProvider);
        ArgumentNullException.ThrowIfNull(recoveryAttemptsProvider);
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));

        TimeSpan interval = sampleInterval ?? TimeSpan.FromMilliseconds(500);
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(sampleInterval));

        VEStressSession session = new();
        VEStressTransport transport = new();
        VEStressDecoder decoder = new();
        long peakMemory = 0;
        double peakCpu = 0;
        DateTimeOffset deadline = DateTimeOffset.UtcNow + duration;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VEMetricsSnapshot metrics = metricsProvider();
            session.AddSampleVE();
            transport.ObserveVE(metrics.Transport);
            decoder.ObserveVE(metrics.Video);
            _performanceVE.EvaluateVE(metrics);
            peakMemory = Math.Max(peakMemory, metrics.WorkingSetBytes);
            peakCpu = Math.Max(peakCpu, metrics.ProcessCpuPercent);
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }

        bool success = decoder.HasProgressVE && decoder.DecodeErrorsVE == 0 && transport.DisconnectsVE == 0;
        string message = success
            ? "StressVE terminó con progreso de frames y sin errores de decode/transporte."
            : "StressVE detectó pérdida de progreso, errores de decode o desconexiones.";

        return new VEStressResult(
            success,
            session.DurationVE,
            session.SamplesVE,
            decoder.StartFramesVE,
            decoder.EndFramesVE,
            decoder.DecodeErrorsVE,
            recoveryAttemptsProvider(),
            peakMemory,
            peakCpu,
            message);
    }
}
