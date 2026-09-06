using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Recovery;
using NOVORA.VisionEngine.Server;
using NOVORA.VisionEngine.Stress;
using NOVORA.VisionEngine.Video;
using System.Diagnostics;

if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine(
        "Uso: dotnet run --project tests\\NOVORA.VisionEngine.BlockCHarness -- <ADB_SERIAL> [SEGUNDOS]");
    return 64;
}

string serial = args[0].Trim();
int seconds =
    args.Length >= 2 && int.TryParse(args[1], out int parsed) && parsed >= 10
        ? parsed
        : 60;

using CancellationTokenSource stopCts = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stopCts.Cancel();
};

await using EngineCoreVE engine = new();
CollectorMetricsVE collector = new();
ManagerPerformanceVE performance = new(initialBitrate: 8_000_000);
PolicyRecoveryVE recoveryPolicy = PolicyRecoveryVE.CreateDefaultVE();
MonitorRecoveryVE monitor = new(recoveryPolicy);
OptionsServerVE options = OptionsServerVE.CreateDefaultVE();
SnapshotMetricsVE latestMetrics = SnapshotMetricsVE.EmptyVE();
SnapshotPerformanceVE latestPerformance = performance.EvaluateVE(latestMetrics);

async Task<ResultRecoveryVE> RecoverSessionAsync(
    ScopeRecoveryVE requestedScope,
    int attempt,
    CancellationToken cancellationToken)
{
    Stopwatch sw = Stopwatch.StartNew();
    try
    {
        // scrcpy usa sockets separados, pero un socket que terminó no puede
        // renegociarse de forma aislada con la misma instancia del servidor.
        // Block C recupera únicamente la sesión VisionEngine (no LinkEngine,
        // no ADB global, no NOVORA) para reconstruir los tres canales limpios.
        ResultCoreVE stop = await engine.StopAsync(cancellationToken);
        if (!stop.Success)
            return ResultRecoveryVE.FailVE(requestedScope, attempt, sw.Elapsed, stop.Message, stop.Exception);

        ResultCoreVE start = await engine.StartAsync(serial, options, cancellationToken);
        if (!start.Success)
            return ResultRecoveryVE.FailVE(requestedScope, attempt, sw.Elapsed, start.Message, start.Exception);

        return ResultRecoveryVE.OkVE(
            requestedScope,
            attempt,
            sw.Elapsed,
            $"VisionEngine reconstruyó su sesión por fallo de {requestedScope}.");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return ResultRecoveryVE.FailVE(
            requestedScope,
            attempt,
            sw.Elapsed,
            "RecoveryVE no pudo reconstruir la sesión VisionEngine.",
            ex);
    }
}

await using ManagerRecoveryVE recovery = new(recoveryPolicy, RecoverSessionAsync);
recovery.RecoveryCompletedVE += (_, result) =>
{
    Console.WriteLine(
        $"[RECOVERY] scope={result.Scope} attempt={result.Attempt} " +
        $"success={result.Success} duration={result.Duration.TotalMilliseconds:F0}ms " +
        result.Message);
};

SnapshotMetricsVE CaptureMetricsVE()
{
    latestMetrics = collector.CaptureVE(
        engine.RuntimeVE.VideoVE.StatusVE,
        engine.RuntimeVE.AudioVE.StatusVE,
        engine.RuntimeVE.ControlVE.StatusVE,
        engine.RuntimeVE.TransportVE.StateVE,
        engine.RuntimeVE.TransportSessionVE);

    latestPerformance = performance.EvaluateVE(latestMetrics);
    return latestMetrics;
}

HealthRecoveryVE CaptureHealthVE()
{
    _ = CaptureMetricsVE();
    return monitor.EvaluateVE(
        engine.RuntimeVE.VideoVE.StatusVE,
        engine.RuntimeVE.AudioVE.StatusVE,
        engine.RuntimeVE.ControlVE.StatusVE,
        engine.RuntimeVE.TransportVE.StateVE,
        audioExpected: options.AudioEnabled,
        controlExpected: options.ControlEnabled);
}

Console.WriteLine("NOVORA VisionEngine Block C - RECOVERY / METRICS / PERFORMANCE / STRESS");
Console.WriteLine($"Device   : {serial}");
Console.WriteLine($"Duration : {seconds}s");
Console.WriteLine("Renderer : DISABLED by design");
Console.WriteLine("Reproduce video y audio en el Galaxy A56 5G durante el stress test.");
Console.WriteLine();

ResultCoreVE initialize = await engine.InitializeAsync(stopCts.Token);
if (!initialize.Success)
{
    Console.Error.WriteLine(initialize.Message);
    Console.Error.WriteLine(initialize.Exception?.Message);
    return 1;
}

ResultCoreVE startResult = await engine.StartAsync(serial, options, stopCts.Token);
if (!startResult.Success)
{
    Console.Error.WriteLine(startResult.Message);
    Console.Error.WriteLine(startResult.Exception?.Message);
    return 2;
}

await recovery.StartAsync(CaptureHealthVE, stopCts.Token);
ManagerStressVE stress = new(performance);

using PeriodicTimer consoleTimer = new(TimeSpan.FromSeconds(2));
using CancellationTokenSource consoleCts = CancellationTokenSource.CreateLinkedTokenSource(stopCts.Token);
Task consoleTask = Task.Run(async () =>
{
    try
    {
        while (await consoleTimer.WaitForNextTickAsync(consoleCts.Token))
        {
            SnapshotMetricsVE m = CaptureMetricsVE();
            Console.WriteLine(
                $"[{DateTime.Now:HH:mm:ss}] " +
                $"FPS={m.Video.FramesPerSecond:F1} " +
                $"Mbps={m.Video.MegabitsPerSecond:F2} " +
                $"CPU={m.ProcessCpuPercent:F1}% " +
                $"RAM={m.WorkingSetBytes / 1024d / 1024d:F0}MiB " +
                $"VErr={m.Video.DecodeErrors} " +
                $"AErr={m.Audio.DecodeErrors} " +
                $"Congestion={latestPerformance.Congestion} " +
                $"BitrateRec={latestPerformance.RecommendedVideoBitrate / 1_000_000d:F1}Mbps " +
                $"Recovery={recovery.StateVE} " +
                $"Renderer={m.RendererEnabled}");
        }
    }
    catch (OperationCanceledException) { }
});

ResultStressVE result;
try
{
    result = await stress.RunAsync(
        CaptureMetricsVE,
        () => recovery.AttemptsVE,
        TimeSpan.FromSeconds(seconds),
        TimeSpan.FromMilliseconds(500),
        stopCts.Token);
}
catch (OperationCanceledException)
{
    result = new ResultStressVE(
        false,
        TimeSpan.Zero,
        0,
        0,
        0,
        0,
        recovery.AttemptsVE,
        0,
        0,
        "Stress cancelado.");
}
finally
{
    consoleCts.Cancel();
    try { await consoleTask; } catch (OperationCanceledException) { }
    await recovery.StopAsync();
}

StatusVideoVE video = engine.RuntimeVE.VideoVE.StatusVE;
StatusAudioVE audio = engine.RuntimeVE.AudioVE.StatusVE;
StatusControlVE control = engine.RuntimeVE.ControlVE.StatusVE;
SnapshotMetricsVE finalMetrics = CaptureMetricsVE();
SnapshotPerformanceVE finalPerformance = performance.EvaluateVE(finalMetrics);

Console.WriteLine();
Console.WriteLine("=== BLOCK C RESULT ===");
Console.WriteLine($"Stress success       : {result.Success}");
Console.WriteLine($"Samples              : {result.Samples}");
Console.WriteLine($"Frames start/end     : {result.StartFrames} / {result.EndFrames}");
Console.WriteLine($"Decode errors        : {result.DecodeErrors}");
Console.WriteLine($"Recovery attempts    : {result.RecoveryAttempts}");
Console.WriteLine($"Peak CPU             : {result.PeakCpuPercent:F1}%");
Console.WriteLine($"Peak RAM             : {result.PeakWorkingSetBytes / 1024d / 1024d:F0} MiB");
Console.WriteLine($"Video FPS            : {finalMetrics.Video.FramesPerSecond:F1}");
Console.WriteLine($"Video bitrate        : {finalMetrics.Video.MegabitsPerSecond:F2} Mbps");
Console.WriteLine($"Congestion           : {finalPerformance.Congestion}");
Console.WriteLine($"Recommended bitrate  : {finalPerformance.RecommendedVideoBitrate / 1_000_000d:F1} Mbps");
Console.WriteLine($"Video state          : {video.State}");
Console.WriteLine($"Audio state          : {audio.State}");
Console.WriteLine($"Control state        : {control.State}");
Console.WriteLine($"Renderer enabled     : {finalMetrics.RendererEnabled}");

ResultCoreVE stopResult = await engine.StopAsync();
if (!stopResult.Success)
{
    Console.Error.WriteLine(stopResult.Message);
    return 3;
}

if (finalMetrics.RendererEnabled)
{
    Console.Error.WriteLine("FAIL: renderer activo antes de Block D.");
    return 4;
}

if (result.EndFrames <= result.StartFrames)
{
    Console.Error.WriteLine("FAIL: video no produjo progreso de frames durante stress.");
    return 5;
}

if (result.DecodeErrors != 0 || video.Stats.DecodeErrors != 0)
{
    Console.Error.WriteLine("FAIL: se detectaron errores de decode de video.");
    return 6;
}

if (audio.State == StatesAudioVE.Failed || audio.Stats.DecodeErrors != 0)
{
    Console.Error.WriteLine("FAIL: AudioVE terminó en fallo.");
    return 7;
}

if (control.State == StatesControlVE.Failed)
{
    Console.Error.WriteLine("FAIL: ControlVE terminó en fallo.");
    return 8;
}

Console.WriteLine("PASS: VisionEngine Block C completó stress headless con métricas, performance y recovery.");
return 0;
