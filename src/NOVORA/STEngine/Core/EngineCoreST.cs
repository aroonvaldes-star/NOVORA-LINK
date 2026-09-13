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
public sealed class EngineCoreST
{
    private readonly CollectorMetricsVE _collectorVE =
        new();

    private readonly object _gateST =
        new();

    private SnapshotCoreST _lastSnapshotST =
        SnapshotCoreST.EmptyST();

    private SnapshotNovoraST _lastNovoraSnapshotST =
        SnapshotNovoraST.EmptyST();

    public SnapshotCoreST LastSnapshotST
    {
        get
        {
            lock (_gateST)
            {
                return _lastSnapshotST;
            }
        }
    }

    public SnapshotNovoraST LastNovoraSnapshotST
    {
        get
        {
            lock (_gateST)
            {
                return _lastNovoraSnapshotST;
            }
        }
    }

    public SnapshotNovoraST CaptureNovoraST(
        RuntimeCoreVE? visionRuntime,
        ManagerRuntimeLE? linkRuntime)
    {
        SnapshotCoreST vision =
            visionRuntime is null
                ? LastSnapshotST
                : CaptureVisionST(visionRuntime);

        IReadOnlyList<DeviceMetricsSnapshotLE> linkDevices =
            linkRuntime?.EngineLE?.Metrics.GetAllSnapshots()
            ?? Array.Empty<DeviceMetricsSnapshotLE>();

        return AnalyzeNovoraST(
            vision,
            linkDevices);
    }

    public SnapshotNovoraST AnalyzeNovoraST(
        SnapshotCoreST vision,
        IReadOnlyList<DeviceMetricsSnapshotLE> linkDevices)
    {
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(linkDevices);

        return StoreNovoraST(
            BuildNovoraSnapshotST(
                vision,
                linkDevices));
    }

    public SnapshotCoreST CaptureVisionST(
        RuntimeCoreVE runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        SnapshotMetricsVE metrics =
            _collectorVE.CaptureVE(
                runtime.VideoVE.StatusVE,
                runtime.AudioVE.StatusVE,
                runtime.ControlVE.StatusVE,
                runtime.TransportVE.StateVE,
                runtime.TransportSessionVE);

        SnapshotPerformanceVE performance =
            runtime.PerformanceVE.EvaluateVE(
                metrics);

        if (!runtime.IsRunningVE ||
            runtime.VideoVE.StatusVE.State != StatesVideoVE.Streaming)
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

    public SnapshotCoreST AnalyzeST(
        SnapshotMetricsVE metrics,
        SnapshotPerformanceVE performance)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(performance);

        return
            StoreST(
                BuildSnapshotST(
                    metrics,
                    performance));
    }

    private SnapshotCoreST StoreST(
        SnapshotCoreST snapshot)
    {
        lock (_gateST)
        {
            _lastSnapshotST =
                snapshot;
        }

        return snapshot;
    }

    private SnapshotNovoraST StoreNovoraST(
        SnapshotNovoraST snapshot)
    {
        lock (_gateST)
        {
            _lastNovoraSnapshotST =
                snapshot;
        }

        return snapshot;
    }

    private static SnapshotCoreST BuildSnapshotST(
        SnapshotMetricsVE metrics,
        SnapshotPerformanceVE performance)
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

        StateCoreST state =
            performance.Congestion switch
            {
                CongestionPerformanceVE.Critical =>
                    StateCoreST.Critical,

                CongestionPerformanceVE.Severe =>
                    StateCoreST.Critical,

                CongestionPerformanceVE.Moderate =>
                    StateCoreST.Degraded,

                CongestionPerformanceVE.Mild =>
                    StateCoreST.Watch,

                _ =>
                    observations.Count == 0
                        ? StateCoreST.Healthy
                        : StateCoreST.Watch
            };

        if (metrics.Video.DecodeErrors > 0 ||
            metrics.Control.Errors > 0)
        {
            state =
                state < StateCoreST.Degraded
                    ? StateCoreST.Degraded
                    : state;
        }

        bool reduce =
            performance.ShouldReduceTelemetry ||
            state >= StateCoreST.Degraded;

        string summary =
            state switch
            {
                StateCoreST.Healthy =>
                    "STEngine: stream estable.",

                StateCoreST.Watch =>
                    "STEngine: observar el stream; hay señales leves.",

                StateCoreST.Degraded =>
                    "STEngine: stream degradado; reducir trabajo no critico.",

                StateCoreST.Critical =>
                    "STEngine: condicion critica; priorizar video/control.",

                _ =>
                    "STEngine: estado desconocido."
            };

        return new SnapshotCoreST(
            DateTimeOffset.UtcNow,
            metrics,
            performance,
            state,
            reduce,
            summary,
            observations);
    }

    private static SnapshotCoreST BuildWaitingSnapshotST(
        SnapshotMetricsVE metrics,
        SnapshotPerformanceVE performance)
    {
        return new SnapshotCoreST(
            DateTimeOffset.UtcNow,
            metrics,
            performance,
            StateCoreST.Watch,
            false,
            "STEngine esperando stream activo.",
            Array.Empty<string>());
    }

    private static SnapshotNovoraST BuildNovoraSnapshotST(
        SnapshotCoreST vision,
        IReadOnlyList<DeviceMetricsSnapshotLE> linkDevices)
    {
        List<string> observations =
            new(vision.Observations);

        StateCoreST state =
            vision.State;

        bool reduce =
            vision.ShouldReduceNonCriticalWork;

        foreach (DeviceMetricsSnapshotLE device in linkDevices)
        {
            StateCoreST deviceState =
                ClassifyLinkDeviceST(
                    device,
                    observations);

            if (deviceState > state)
            {
                state =
                    deviceState;
            }

            if (deviceState >= StateCoreST.Degraded)
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
                StateCoreST.Healthy =>
                    "STEngine: NOVORA estable.",

                StateCoreST.Watch =>
                    "STEngine: NOVORA en observacion.",

                StateCoreST.Degraded =>
                    "STEngine: NOVORA degradado; reducir trabajo no critico.",

                StateCoreST.Critical =>
                    "STEngine: NOVORA critico; priorizar rutas esenciales.",

                _ =>
                    "STEngine: estado global desconocido."
            };

        return new SnapshotNovoraST(
            DateTimeOffset.UtcNow,
            vision,
            linkDevices.ToArray(),
            state,
            reduce,
            summary,
            observations);
    }

    private static StateCoreST ClassifyLinkDeviceST(
        DeviceMetricsSnapshotLE device,
        List<string> observations)
    {
        StateCoreST state =
            device.State switch
            {
                StatesCoreLE.Failed =>
                    StateCoreST.Critical,

                StatesCoreLE.Degraded =>
                    StateCoreST.Degraded,

                StatesCoreLE.Recovering =>
                    StateCoreST.Degraded,

                StatesCoreLE.Connecting =>
                    StateCoreST.Watch,

                _ =>
                    device.Healthy
                        ? StateCoreST.Healthy
                        : StateCoreST.Watch
            };

        if (!device.Healthy &&
            state < StateCoreST.Degraded)
        {
            state =
                StateCoreST.Watch;

            observations.Add(
                $"LinkEngine {device.Serial} requiere observacion.");
        }

        if (device.LatencyMs >= 250)
        {
            observations.Add(
                $"LinkEngine {device.Serial} reporta latencia alta.");

            if (state < StateCoreST.Degraded)
            {
                state =
                    StateCoreST.Degraded;
            }
        }

        if (device.DnsFailures > 0)
        {
            observations.Add(
                $"LinkEngine {device.Serial} reporta fallos DNS.");

            if (state < StateCoreST.Degraded)
            {
                state =
                    StateCoreST.Degraded;
            }
        }

        if (device.RecoveryAttempts > device.SuccessfulRecoveries)
        {
            observations.Add(
                $"LinkEngine {device.Serial} tiene recovery pendiente.");

            if (state < StateCoreST.Degraded)
            {
                state =
                    StateCoreST.Degraded;
            }
        }

        if (device.State == StatesCoreLE.Failed)
        {
            observations.Add(
                $"LinkEngine {device.Serial} esta en fallo.");
        }

        return state;
    }
}
