using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Metrics;
using NOVORA.LinkEngine.Runtime;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Video;

namespace NOVORA.STEngine.Core;

/// <summary>
/// Motor de medición bajo demanda para NOVORA.
/// No crea timers ni polling: captura snapshots cuando el runtime lo solicita.
/// </summary>
public sealed class STCoreEngine
{
    private readonly VEMetricsCollector _collectorVE =
        new();

    private readonly object _gateST =
        new();

    private STCoreSnapshot _lastSnapshotST =
        STCoreSnapshot.EmptyST();

    private STCoreSnapshotNovora _lastNovoraSnapshotST =
        STCoreSnapshotNovora.EmptyST();

    public STCoreSnapshot LastSnapshotST
    {
        get
        {
            lock (_gateST)
            {
                return _lastSnapshotST;
            }
        }
    }

    public STCoreSnapshotNovora LastNovoraSnapshotST
    {
        get
        {
            lock (_gateST)
            {
                return _lastNovoraSnapshotST;
            }
        }
    }

    public STCoreSnapshotNovora CaptureNovoraST(
        VECoreRuntime? visionRuntime,
        LERuntimeManager? linkRuntime)
    {
        STCoreSnapshot vision =
            visionRuntime is null
                ? LastSnapshotST
                : CaptureVisionST(visionRuntime);

        IReadOnlyList<LEMetricsDeviceMetricsSnapshot> linkDevices =
            linkRuntime?.EngineLE?.Metrics.GetAllSnapshots()
            ?? Array.Empty<LEMetricsDeviceMetricsSnapshot>();

        return AnalyzeNovoraST(
            vision,
            linkDevices);
    }

    public STCoreSnapshotNovora AnalyzeNovoraST(
        STCoreSnapshot vision,
        IReadOnlyList<LEMetricsDeviceMetricsSnapshot> linkDevices)
    {
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(linkDevices);

        return StoreNovoraST(
            BuildNovoraSnapshotST(
                vision,
                linkDevices));
    }

    public STCoreSnapshot CaptureVisionST(
        VECoreRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        VEMetricsSnapshot metrics =
            _collectorVE.CaptureVE(
                runtime.VideoVE.StatusVE,
                runtime.AudioVE.StatusVE,
                runtime.ControlVE.StatusVE,
                runtime.TransportVE.StateVE,
                runtime.TransportSessionVE);

        VEPerformanceSnapshot performance =
            runtime.PerformanceVE.EvaluateVE(
                metrics);

        if (!runtime.IsRunningVE ||
            runtime.VideoVE.StatusVE.State != VEVideoStates.Streaming)
        {
            return
                StoreST(
                    BuildWaitingSnapshotST(
                        metrics,
                        performance));
        }

        return
            StoreST(
                BuildSnapshotST(
                    metrics,
                    performance));
    }

    public STCoreSnapshot AnalyzeST(
        VEMetricsSnapshot metrics,
        VEPerformanceSnapshot performance)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(performance);

        return
            StoreST(
                BuildSnapshotST(
                    metrics,
                    performance));
    }

    private STCoreSnapshot StoreST(
        STCoreSnapshot snapshot)
    {
        lock (_gateST)
        {
            _lastSnapshotST =
                snapshot;
        }

        return snapshot;
    }

    private STCoreSnapshotNovora StoreNovoraST(
        STCoreSnapshotNovora snapshot)
    {
        lock (_gateST)
        {
            _lastNovoraSnapshotST =
                snapshot;
        }

        return snapshot;
    }

    private static STCoreSnapshot BuildSnapshotST(
        VEMetricsSnapshot metrics,
        VEPerformanceSnapshot performance)
    {
        List<string> observations =
            [];

        if (metrics.Video.DecodeErrors > 0)
        {
            observations.Add(
                "Video reporta errores de decodificacion.");
        }

        if (metrics.Audio.DecodeErrors > 0 ||
            metrics.Audio.PlaybackErrors > 0)
        {
            observations.Add(
                "Audio reporta errores de decodificacion o reproduccion.");
        }

        if (metrics.Control.Errors > 0)
        {
            observations.Add(
                "Control reporta errores de input.");
        }

        if (metrics.Video.FramesPerSecond > 0 &&
            metrics.Video.FramesPerSecond < 24)
        {
            observations.Add(
                "FPS bajo para stream interactivo.");
        }

        if (metrics.Video.FramesDecoded == 0 &&
            metrics.Transport.VideoConnected)
        {
            observations.Add(
                "Hay canal de video, pero aun no hay frames decodificados.");
        }

        if (metrics.ProcessCpuPercent >= 85)
        {
            observations.Add(
                "CPU del proceso elevada.");
        }

        STCoreState state =
            performance.Congestion switch
            {
                VEPerformanceCongestion.Critical =>
                    STCoreState.Critical,

                VEPerformanceCongestion.Severe =>
                    STCoreState.Critical,

                VEPerformanceCongestion.Moderate =>
                    STCoreState.Degraded,

                VEPerformanceCongestion.Mild =>
                    STCoreState.Watch,

                _ =>
                    observations.Count == 0
                        ? STCoreState.Healthy
                        : STCoreState.Watch
            };

        if (metrics.Video.DecodeErrors > 0 ||
            metrics.Control.Errors > 0)
        {
            state =
                state < STCoreState.Degraded
                    ? STCoreState.Degraded
                    : state;
        }

        bool reduce =
            performance.ShouldReduceTelemetry ||
            state >= STCoreState.Degraded;

        string summary =
            state switch
            {
                STCoreState.Healthy =>
                    "STEngine: stream estable.",

                STCoreState.Watch =>
                    "STEngine: observar el stream; hay señales leves.",

                STCoreState.Degraded =>
                    "STEngine: stream degradado; reducir trabajo no critico.",

                STCoreState.Critical =>
                    "STEngine: condicion critica; priorizar video/control.",

                _ =>
                    "STEngine: estado desconocido."
            };

        return new STCoreSnapshot(
            DateTimeOffset.UtcNow,
            metrics,
            performance,
            state,
            reduce,
            summary,
            observations);
    }

    private static STCoreSnapshot BuildWaitingSnapshotST(
        VEMetricsSnapshot metrics,
        VEPerformanceSnapshot performance)
    {
        return new STCoreSnapshot(
            DateTimeOffset.UtcNow,
            metrics,
            performance,
            STCoreState.Watch,
            false,
            "STEngine esperando stream activo.",
            Array.Empty<string>());
    }

    private static STCoreSnapshotNovora BuildNovoraSnapshotST(
        STCoreSnapshot vision,
        IReadOnlyList<LEMetricsDeviceMetricsSnapshot> linkDevices)
    {
        List<string> observations =
            new(vision.Observations);

        STCoreState state =
            vision.State;

        bool reduce =
            vision.ShouldReduceNonCriticalWork;

        foreach (LEMetricsDeviceMetricsSnapshot device in linkDevices)
        {
            STCoreState deviceState =
                ClassifyLinkDeviceST(
                    device,
                    observations);

            if (deviceState > state)
            {
                state =
                    deviceState;
            }

            if (deviceState >= STCoreState.Degraded)
            {
                reduce =
                    true;
            }
        }

        if (linkDevices.Count == 0)
        {
            observations.Add(
                "LinkEngine no tiene sesiones medidas en este snapshot.");
        }

        string summary =
            state switch
            {
                STCoreState.Healthy =>
                    "STEngine: NOVORA estable.",

                STCoreState.Watch =>
                    "STEngine: NOVORA en observacion.",

                STCoreState.Degraded =>
                    "STEngine: NOVORA degradado; reducir trabajo no critico.",

                STCoreState.Critical =>
                    "STEngine: NOVORA critico; priorizar rutas esenciales.",

                _ =>
                    "STEngine: estado global desconocido."
            };

        return new STCoreSnapshotNovora(
            DateTimeOffset.UtcNow,
            vision,
            linkDevices.ToArray(),
            state,
            reduce,
            summary,
            observations);
    }

    private static STCoreState ClassifyLinkDeviceST(
        LEMetricsDeviceMetricsSnapshot device,
        List<string> observations)
    {
        STCoreState state =
            device.State switch
            {
                LECoreStates.Failed =>
                    STCoreState.Critical,

                LECoreStates.Degraded =>
                    STCoreState.Degraded,

                LECoreStates.Recovering =>
                    STCoreState.Degraded,

                LECoreStates.Connecting =>
                    STCoreState.Watch,

                _ =>
                    device.Healthy
                        ? STCoreState.Healthy
                        : STCoreState.Watch
            };

        if (!device.Healthy &&
            state < STCoreState.Degraded)
        {
            state =
                STCoreState.Watch;

            observations.Add(
                $"LinkEngine {device.Serial} requiere observacion.");
        }

        if (device.LatencyMs >= 250)
        {
            observations.Add(
                $"LinkEngine {device.Serial} reporta latencia alta.");

            if (state < STCoreState.Degraded)
            {
                state =
                    STCoreState.Degraded;
            }
        }

        if (device.DnsFailures > 0)
        {
            observations.Add(
                $"LinkEngine {device.Serial} reporta fallos DNS.");

            if (state < STCoreState.Degraded)
            {
                state =
                    STCoreState.Degraded;
            }
        }

        if (device.RecoveryAttempts > device.SuccessfulRecoveries)
        {
            observations.Add(
                $"LinkEngine {device.Serial} tiene recovery pendiente.");

            if (state < STCoreState.Degraded)
            {
                state =
                    STCoreState.Degraded;
            }
        }

        if (device.State == LECoreStates.Failed)
        {
            observations.Add(
                $"LinkEngine {device.Serial} esta en fallo.");
        }

        return state;
    }
}
