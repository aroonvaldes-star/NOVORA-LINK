using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using NOVORA.NVIDIA;
using NOVORA.Service;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Renderer;
using NOVORA.VisionEngine.Server;

namespace NOVORA.ProfileHarness;

public static class NLTestProgram
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length is < 2 or > 5) { Console.Error.WriteLine("Uso: <serial> <resultado.json> [Gaming|Balanced|Video|Battery] [FPS] [Automatic|Disabled]"); return 64; }
        var app = new System.Windows.Application();
        var window = new Window { Title = "NOVORA — medición de perfiles VE", Width = 520, Height = 850 };
        int exit = 1;
        bool runCompleted = false;
        window.Closed += (_, _) => Console.Error.WriteLine($"WINDOW_CLOSED beforeRunCompleted={!runCompleted}");
        window.Loaded += async (_, _) =>
        {
            try { exit = await RunAsync(window, args[0], Path.GetFullPath(args[1]), args.Length >= 3 && args[2] != "All" ? Enum.Parse<VEPerformanceProfile>(args[2]) : null,
                args.Length >= 4 && args[3] != "Default" ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : null,
                args.Length >= 5 ? Enum.Parse<NLNVIDIAProfile>(args[4]) : NLNVIDIAProfile.Disabled); }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { runCompleted = true; app.Shutdown(); }
        };
        app.Run(window);
        return exit;
    }

    private static async Task<int> RunAsync(Window window, string serial, string output, VEPerformanceProfile? selected, int? fps, NLNVIDIAProfile nvidiaProfile)
    {
        if (fps.HasValue && (fps.Value < 15 || fps.Value > 240)) throw new ArgumentOutOfRangeException(nameof(fps));
        var paths = new NLServiceNovoraPaths();
        var nvidia = new NLNVIDIAManager(paths);
        var gpuResults = new List<object>();
        foreach (var profile in Enum.GetValues<NLNVIDIAProfile>())
        {
            var timer = Stopwatch.StartNew();
            nvidia.SetProfileVE(profile);
            gpuResults.Add(new { Requested = profile.ToString(), DetectionMilliseconds = timer.Elapsed.TotalMilliseconds, Status = nvidia.StatusVE });
        }
        var runs = new List<object>();
        void Save(bool completed = false) => File.WriteAllText(output, JsonSerializer.Serialize(new {
            TimestampUtc = DateTimeOffset.UtcNow, Serial = serial, Completed = completed,
            Method = "2 rounds; first-frame readiness; 3s warmup; 12s measurement. Same user-controlled moving content, not deterministic replay. Render counters are event-published snapshots. No glass-to-glass measurement.",
            NVIDIA = gpuResults, Runs = runs
            , GamepadEnabled = false, AudioEnabled = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        Save();
        int failures = 0;
        for (int round = 1; round <= 2; round++)
        foreach (var profile in selected.HasValue ? [selected.Value] : Enum.GetValues<VEPerformanceProfile>())
        {
            var host = new VERendererHost();
            window.Content = host;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            await using var engine = new VECoreEngine(paths);
            engine.AttachRendererHostVE(host);
            await engine.RuntimeVE.ExInEngine.SetEnabledAsync(false);
            engine.RuntimeVE.NvidiaVE.SetProfileVE(nvidiaProfile);
            engine.RuntimeVE.PerformanceVE.SetProfileVE(profile);
            var options = VEServerOptions.CreateForProfileVE(profile) with { AudioEnabled = false, AudioPlaybackEnabled = false };
            if (fps.HasValue) options = options with { MaxFps = fps.Value };
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new object();
            bool measuring = false;
            var ticks = new List<long>();
            int width = 0, height = 0;
            engine.RuntimeVE.VideoVE.FrameDecodedVE += (_, frame) => {
                lock (gate) {
                    width = frame.Width; height = frame.Height;
                    if (measuring) ticks.Add(Stopwatch.GetTimestamp());
                }
                ready.TrySetResult();
            };
            try
            {
                Console.WriteLine($"START round={round} profile={profile} bitrate={options.VideoBitRate} max={options.MaxSize} fps={options.MaxFps}");
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var initialized = await engine.InitializeAsync(deadline.Token);
                if (!initialized.Success) throw new InvalidOperationException(initialized.Message, initialized.Exception);
                var start = await engine.StartAsync(serial, options, deadline.Token);
                if (!start.Success) throw new InvalidOperationException(start.Message, start.Exception);
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await Task.Delay(TimeSpan.FromSeconds(3));
                var beforeVideo = engine.RuntimeVE.VideoVE.StatusVE.Stats;
                var beforeRender = engine.RuntimeVE.RendererVE.StatusVE.Metrics;
                using var process = Process.GetCurrentProcess();
                var cpuBefore = process.TotalProcessorTime;
                var timer = Stopwatch.StartNew();
                lock (gate) measuring = true;
                await Task.Delay(TimeSpan.FromSeconds(12));
                long[] captured;
                lock (gate) { measuring = false; captured = ticks.ToArray(); }
                var seconds = timer.Elapsed.TotalSeconds;
                var video = engine.RuntimeVE.VideoVE.StatusVE;
                var render = engine.RuntimeVE.RendererVE.StatusVE;
                var intervals = captured.Zip(captured.Skip(1), (a,b) => (b-a)*1000d/Stopwatch.Frequency).Order().ToArray();
                var decodedFps = captured.Length / seconds;
                var presented = render.Metrics.FramesPresented - beforeRender.FramesPresented;
                bool passed = captured.Length > 0 && presented > 0 &&
                    video.Stats.DecodeErrors == beforeVideo.DecodeErrors && render.Metrics.RenderErrors == beforeRender.RenderErrors;
                if (!passed) failures++;
                runs.Add(new { Round = round, Profile = profile.ToString(), Requested = options,
                    Seconds = seconds, Width = width, Height = height, DecodedFrames = captured.Length,
                    DecodedFps = decodedFps, PresentedFpsApprox = presented / seconds,
                    ReceivedMbpsApprox = (video.Stats.BytesReceived-beforeVideo.BytesReceived)*8/seconds/1_000_000,
                    DecodeErrors = video.Stats.DecodeErrors-beforeVideo.DecodeErrors,
                    RenderErrors = render.Metrics.RenderErrors-beforeRender.RenderErrors,
                    DroppedFrames = render.Metrics.FramesDropped-beforeRender.FramesDropped,
                    DecodeArrivalP95Ms = intervals.Length == 0 ? 0 : intervals[(int)Math.Floor((intervals.Length-1)*.95)],
                    CpuPercent = (process.TotalProcessorTime-cpuBefore).TotalSeconds / seconds / Environment.ProcessorCount * 100,
                    RendererBackend = render.Backend, DecodeBackend = video.DecoderName, video.NvdecActive, video.DecoderFallbackReason,
                    VideoState = video.State.ToString(), VideoError = video.LastError,
                    NvidiaRequested = nvidiaProfile.ToString(), NvidiaRuntime = engine.RuntimeVE.NvidiaVE.StatusVE, Passed = passed });
                Save();
                Console.WriteLine($"RESULT {profile} decode={decodedFps:F2} present~={presented/seconds:F2} size={width}x{height} passed={passed}");
            }
            catch (Exception ex) { failures++; runs.Add(new { Round=round, Profile=profile.ToString(), Error=ex.ToString(), Passed=false }); Console.Error.WriteLine(ex.Message); }
            finally {
                await engine.StopAsync();
                await engine.DisposeAsync();
                window.Content = null;
                host.Dispose();
                Save();
            }
        }
        Save(completed: true);
        return failures == 0 ? 0 : 1;
    }
}
