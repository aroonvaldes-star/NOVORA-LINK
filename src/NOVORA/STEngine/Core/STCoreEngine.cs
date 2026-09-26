using NOVORA.Contracts.Stability;

namespace NOVORA.STEngine.Core;

/// <summary>Observador bajo demanda. No controla motores ni aplica recomendaciones.</summary>
public sealed class STCoreEngine
{
    private readonly object _gateST = new();
    private STCoreSnapshot _lastSnapshotST = STCoreSnapshot.EmptyST();
    private STCoreSnapshotNovora _lastNovoraSnapshotST = STCoreSnapshotNovora.EmptyST();

    public STCoreSnapshot LastSnapshotST { get { lock (_gateST) return _lastSnapshotST; } }
    public STCoreSnapshotNovora LastNovoraSnapshotST { get { lock (_gateST) return _lastNovoraSnapshotST; } }

    public STCoreSnapshotNovora CaptureNovoraST(
        NLStabilityVisionSnapshot? vision,
        IReadOnlyList<NLStabilityLinkSnapshot>? linkDevices,
        NLStabilityExInSnapshot? exIn = null)
    {
        STCoreSnapshot visionSnapshot = vision is null ? LastSnapshotST : AnalyzeST(vision);
        return AnalyzeNovoraST(visionSnapshot, linkDevices ?? Array.Empty<NLStabilityLinkSnapshot>(), exIn);
    }

    public STCoreSnapshot AnalyzeST(NLStabilityVisionSnapshot vision)
    {
        ArgumentNullException.ThrowIfNull(vision);
        STCoreSnapshot snapshot = !vision.IsRunning || !vision.IsStreaming
            ? BuildWaitingSnapshotST(vision)
            : BuildSnapshotST(vision);
        lock (_gateST) _lastSnapshotST = snapshot;
        return snapshot;
    }

    public STCoreSnapshotNovora AnalyzeNovoraST(
        STCoreSnapshot vision,
        IReadOnlyList<NLStabilityLinkSnapshot> linkDevices,
        NLStabilityExInSnapshot? exIn = null)
    {
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(linkDevices);
        STCoreSnapshotNovora snapshot = BuildNovoraSnapshotST(vision, linkDevices, exIn);
        lock (_gateST) _lastNovoraSnapshotST = snapshot;
        return snapshot;
    }

    private static STCoreSnapshot BuildSnapshotST(NLStabilityVisionSnapshot vision)
    {
        List<string> observations = [];
        if (vision.VideoDecodeErrors > 0) observations.Add("Video reporta errores de decodificacion.");
        if (vision.AudioDecodeErrors > 0 || vision.AudioPlaybackErrors > 0) observations.Add("Audio reporta errores de decodificacion o reproduccion.");
        if (vision.ControlErrors > 0) observations.Add("Control reporta errores de input.");
        if (vision.FramesPerSecond > 0 && vision.FramesPerSecond < 24) observations.Add("FPS bajo para stream interactivo.");
        if (vision.FramesDecoded == 0 && vision.VideoConnected) observations.Add("Hay canal de video, pero aun no hay frames decodificados.");
        if (vision.ProcessCpuPercent >= 85) observations.Add("CPU del proceso elevada.");

        STCoreState state = vision.PerformanceSeverity switch
        {
            NLStabilitySeverity.Critical => STCoreState.Critical,
            NLStabilitySeverity.Degraded => STCoreState.Degraded,
            NLStabilitySeverity.Watch => STCoreState.Watch,
            _ => observations.Count == 0 ? STCoreState.Healthy : STCoreState.Watch
        };
        if (vision.VideoDecodeErrors > 0 || vision.ControlErrors > 0)
            state = state < STCoreState.Degraded ? STCoreState.Degraded : state;

        bool reduce = vision.ShouldReduceTelemetry || state >= STCoreState.Degraded;
        return new STCoreSnapshot(DateTimeOffset.UtcNow, vision, state, reduce, state switch
        {
            STCoreState.Healthy => "STEngine: stream estable.",
            STCoreState.Watch => "STEngine: observar el stream; hay señales leves.",
            STCoreState.Degraded => "STEngine: stream degradado; reducir trabajo no critico.",
            STCoreState.Critical => "STEngine: condicion critica; priorizar video/control.",
            _ => "STEngine: estado desconocido."
        }, observations);
    }

    private static STCoreSnapshot BuildWaitingSnapshotST(NLStabilityVisionSnapshot vision)
        => new(DateTimeOffset.UtcNow, vision, STCoreState.Watch, false,
            "STEngine esperando stream activo.", Array.Empty<string>());

    private static STCoreSnapshotNovora BuildNovoraSnapshotST(
        STCoreSnapshot vision,
        IReadOnlyList<NLStabilityLinkSnapshot> linkDevices,
        NLStabilityExInSnapshot? exIn)
    {
        List<string> observations = new(vision.Observations);
        STCoreState state = vision.State;
        bool reduce = vision.ShouldReduceNonCriticalWork;

        foreach (NLStabilityLinkSnapshot device in linkDevices)
        {
            STCoreState deviceState = ClassifyLinkDeviceST(device, observations);
            if (deviceState > state) state = deviceState;
            if (deviceState >= STCoreState.Degraded) reduce = true;
        }
        if (linkDevices.Count == 0) observations.Add("LinkEngine no tiene sesiones medidas en este snapshot.");

        if (exIn is null || exIn.State == NLStabilityEngineState.Stopped)
            observations.Add("ExInEngine no tiene control activo en este snapshot.");
        else if (exIn.State == NLStabilityEngineState.Failed)
        {
            observations.Add($"ExInEngine esta en fallo: {exIn.LastError ?? "sin detalle"}");
            if (state < STCoreState.Degraded) state = STCoreState.Degraded;
            reduce = true;
        }
        else if (exIn.State == NLStabilityEngineState.Running && exIn.ConnectedGamepads == 0)
        {
            observations.Add("ExInEngine esta activo, pero no reporta mandos fisicos.");
            if (state < STCoreState.Watch) state = STCoreState.Watch;
        }

        if (exIn?.HasUncorrectableCalibration == true)
        {
            observations.Add("ExInEngine reporta diagnostico que no se corrige solo con calibracion.");
            if (state < STCoreState.Degraded) state = STCoreState.Degraded;
        }
        if (exIn?.HasLowBattery == true)
        {
            observations.Add("ExInEngine reporta bateria baja en un control fisico.");
            if (state < STCoreState.Watch) state = STCoreState.Watch;
        }

        return new STCoreSnapshotNovora(DateTimeOffset.UtcNow, vision, linkDevices.ToArray(), exIn, state, reduce,
            state switch
            {
                STCoreState.Healthy => "STEngine: NOVORA estable.",
                STCoreState.Watch => "STEngine: NOVORA en observacion.",
                STCoreState.Degraded => "STEngine: NOVORA degradado; reducir trabajo no critico.",
                STCoreState.Critical => "STEngine: NOVORA critico; priorizar rutas esenciales.",
                _ => "STEngine: estado global desconocido."
            }, observations);
    }

    private static STCoreState ClassifyLinkDeviceST(NLStabilityLinkSnapshot device, List<string> observations)
    {
        STCoreState state = device.State switch
        {
            NLStabilityEngineState.Failed => STCoreState.Critical,
            NLStabilityEngineState.Degraded or NLStabilityEngineState.Recovering => STCoreState.Degraded,
            NLStabilityEngineState.Starting => STCoreState.Watch,
            _ => device.Healthy ? STCoreState.Healthy : STCoreState.Watch
        };
        if (!device.Healthy && state < STCoreState.Degraded) observations.Add($"LinkEngine {device.Serial} requiere observacion.");
        if (device.LatencyMs >= 250)
        {
            observations.Add($"LinkEngine {device.Serial} reporta latencia alta.");
            if (state < STCoreState.Degraded) state = STCoreState.Degraded;
        }
        if (device.DnsFailures > 0)
        {
            observations.Add($"LinkEngine {device.Serial} reporta fallos DNS.");
            if (state < STCoreState.Degraded) state = STCoreState.Degraded;
        }
        if (device.RecoveryAttempts > device.SuccessfulRecoveries)
        {
            observations.Add($"LinkEngine {device.Serial} tiene recovery pendiente.");
            if (state < STCoreState.Degraded) state = STCoreState.Degraded;
        }
        if (device.State == NLStabilityEngineState.Failed) observations.Add($"LinkEngine {device.Serial} esta en fallo.");
        return state;
    }
}
