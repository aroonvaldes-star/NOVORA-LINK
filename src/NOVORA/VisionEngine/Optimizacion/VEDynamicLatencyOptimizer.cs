using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Video;

namespace NOVORA.VisionEngine.Optimization;

/// <summary>
/// Selección inicial de baja latencia y observación de carga. Las recomendaciones
/// no modifican la captura Android, el bitrate LE ni el perfil elegido por el usuario.
/// </summary>
public sealed class VEDynamicLatencyOptimizer
{
    private readonly VEAccelerationCapabilities _capabilities;
    private readonly VEPerformanceManager _performanceManager;

    public VEDynamicLatencyOptimizer(
        VEAccelerationCapabilities capabilities,
        VEPerformanceManager performanceManager)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _performanceManager = performanceManager ?? throw new ArgumentNullException(nameof(performanceManager));
    }

    public VEProfileOptions OptimizeForLowestLatency(VEMetricsSnapshot currentMetrics)
        => OptimizeForLowestLatency(currentMetrics, VEProfile.Automatic, preferNvidia: true);

    public VEProfileOptions OptimizeForLowestLatency(
        VEMetricsSnapshot currentMetrics,
        VEProfile requestedProfile,
        bool preferNvidia)
    {
        ArgumentNullException.ThrowIfNull(currentMetrics);
        VEProfileOptions requested = VEProfileOptions.CreateVE(requestedProfile);
        // Sólo observar: la adaptación de captura/bitrate tiene su propio owner y ACK.
        VEPerformanceSnapshot performance = _performanceManager.ObserveVE(currentMetrics);
        VEAccelerationBackend backend = preferNvidia && requestedProfile != VEProfile.Software &&
            _capabilities.SupportsVE(VEAccelerationBackend.NvidiaNvdec)
                ? VEAccelerationBackend.NvidiaNvdec
                : VEAccelerationBackend.Software;

        // Intel, AMD y D3D11VA todavía no tienen inicialización en VEVideoDecoder.
        // Detectar APIs/GPU no autoriza a anunciar un backend que no se puede abrir.
        VEPerformanceProfile recommendation = requested.PerformanceProfile;
        if (requestedProfile == VEProfile.Automatic)
        {
            recommendation = performance.Congestion switch
            {
                >= VEPerformanceCongestion.Severe => VEPerformanceProfile.Battery,
                VEPerformanceCongestion.Moderate => VEPerformanceProfile.Video,
                _ => backend == VEAccelerationBackend.NvidiaNvdec
                    ? VEPerformanceProfile.Gaming
                    : VEPerformanceProfile.Balanced
            };
        }

        return requested with
        {
            PerformanceProfile = recommendation,
            AccelerationRequest = new VEAccelerationRequest(backend)
        };
    }

    /// <summary>Abre el decoder una vez, conservando su fallback software.</summary>
    public VEProfileOptions ApplyToDecoder(
        VEVideoDecoder decoder,
        VEProtocolCodec codec,
        VEMetricsSnapshot currentMetrics,
        VEProfile requestedProfile,
        bool preferNvidia)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        VEProfileOptions options = OptimizeForLowestLatency(currentMetrics, requestedProfile, preferNvidia);
        decoder.InitializeVE(codec, options.AccelerationRequest, lowLatency: true);
        return options;
    }
}

/// <summary>Diagnóstico observado; no prueba beneficio de latencia ni captura adaptada.</summary>
public sealed record VELatencyOptimizationSnapshot(
    VEMetricsSnapshot Metrics,
    VEProfileOptions Options,
    bool LowDelayRequested);
