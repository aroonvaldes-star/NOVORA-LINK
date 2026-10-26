using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using System.Threading.Channels;
using NOVORA.NVIDIA;
using NOVORA.Service;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Optimization;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Video;

var checks = new List<(string Name, Func<Task> Check)>();
void Add(string name, Action check) => checks.Add((name, () => { check(); return Task.CompletedTask; }));
var capabilities = new VEAccelerationCapabilities(true, true, true, true, true, true);
string source = args.Length > 0 ? args[0] : Path.GetFullPath("src/NOVORA");
var paths = new NLServiceNovoraPaths(source);
var metrics = VEMetricsSnapshot.EmptyVE() with { WorkingSetBytes = 120_000_000, ProcessCpuPercent = 96 };
Add("Only implemented hardware is recommended", () =>
{
    var optimizer = new VEDynamicLatencyOptimizer(new(true, false, true, false, true, true), new());
    Require(optimizer.OptimizeForLowestLatency(metrics).AccelerationRequest.Backend == VEAccelerationBackend.Software,
        "Intel/D3D11/AMD discovery must not imply an implemented decoder.");
});
Add("NVIDIA retains priority when supported", () =>
{
    var optimizer = new VEDynamicLatencyOptimizer(capabilities, new());
    Require(optimizer.OptimizeForLowestLatency(metrics).AccelerationRequest.Backend == VEAccelerationBackend.NvidiaNvdec,
        "Supported NVDEC remains the hardware candidate.");
});
Add("Observation preserves the explicit performance profile", () =>
{
    var performance = new VEPerformanceManager();
    performance.SetProfileVE(VEPerformanceProfile.Video);
    new VEDynamicLatencyOptimizer(capabilities, performance).OptimizeForLowestLatency(metrics);
    Require(performance.ProfileVE == VEPerformanceProfile.Video, "Optimization must not overwrite the explicit quality profile.");
});
Add("Observation preserves bitrate owned by existing policies", () =>
{
    var performance = new VEPerformanceManager();
    int before = performance.ObserveVE(metrics).RecommendedVideoBitrate;
    new VEDynamicLatencyOptimizer(capabilities, performance).OptimizeForLowestLatency(metrics);
    Require(performance.ObserveVE(metrics).RecommendedVideoBitrate == before, "Optimization must not also adapt LE/capture bitrate.");
});
Add("Explicit profiles and NVIDIA opt-out remain authoritative", () =>
{
    var optimizer = new VEDynamicLatencyOptimizer(capabilities, new());
    foreach (var profile in Enum.GetValues<VEProfile>().Where(p => p != VEProfile.Automatic))
    {
        var options = Optimize(optimizer, metrics, profile, false);
        Require(options.Profile == profile && options.PerformanceProfile == VEProfileOptions.CreateVE(profile).PerformanceProfile,
            "Every explicit profile must survive congestion.");
        Require(options.AccelerationRequest.Backend == VEAccelerationBackend.Software, "NVIDIA opt-out must survive hardware detection.");
    }
    Require(Optimize(optimizer, metrics, VEProfile.Software, true).AccelerationRequest.Backend == VEAccelerationBackend.Software,
        "Software profile prohibits hardware even with NVIDIA preference enabled.");
});
Add("Unsupported backend reaches the real software decoder", () =>
{
    MethodInfo initialize = typeof(VEVideoDecoder).GetMethod("InitializeVE", [typeof(VEProtocolCodec), typeof(VEAccelerationRequest), typeof(bool)])
        ?? throw new InvalidOperationException("Decoder has no typed acceleration/low-delay contract.");
    foreach (var backend in new[] { VEAccelerationBackend.Intel, VEAccelerationBackend.Amd, VEAccelerationBackend.D3D11VA })
    foreach (var codec in new[] { VEProtocolCodec.H264, VEProtocolCodec.H265, VEProtocolCodec.Av1, VEProtocolCodec.Vp8, VEProtocolCodec.Vp9 })
    {
        using var decoder = new VEVideoDecoder(paths);
        initialize.Invoke(decoder, [codec, new VEAccelerationRequest(backend), true]);
        Require(decoder.IsInitializedVE && decoder.DecoderNameVE == codec.GetFfmpegDecoderNameVE(),
            $"Unsupported {backend} must open FFmpeg software for {codec}.");
        Require(!string.IsNullOrWhiteSpace(decoder.FallbackReasonVE), "Unsupported backend must report why software was used.");
        Require((bool)Property(decoder, "LowDelayEnabledVE"), $"Real av_opt_set low_delay must succeed for software {codec}.");
        initialize.Invoke(decoder, [codec, new VEAccelerationRequest(VEAccelerationBackend.Software), false]);
        Require((bool)Property(decoder, "LowDelayEnabledVE"), "Repeated initialization of an opened codec must not silently replace its settings.");
    }
});
foreach (VEExternalSourceKind kind in new[] { VEExternalSourceKind.UsbAppControl, VEExternalSourceKind.NativeLan })
    checks.Add(($"Real {kind} startup, observation, stop/restart and disable", () => CheckRuntime(kind)));

int failed = 0;
foreach (var check in checks)
{
    try { await check.Check(); Console.WriteLine("PASS " + check.Name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + check.Name + ": " + (ex.InnerException ?? ex).Message); }
}
Console.WriteLine($"Checks={checks.Count}; Passed={checks.Count - failed}; Failed={failed}");
return failed == 0 ? 0 : 1;

async Task CheckRuntime(VEExternalSourceKind kind)
{
    await using var runtime = new VECoreRuntime(paths, new NLServiceADB(paths));
    runtime.NvidiaVE.SetProfileVE(NLNVIDIAProfile.Disabled);
    runtime.PerformanceVE.SetProfileVE(VEPerformanceProfile.Video);
    var enabled = typeof(VECoreRuntime).GetProperty("DynamicLatencyOptimizationEnabledVE")
        ?? throw new InvalidOperationException("The real runtime has no optimizer enable/disable integration.");
    Require(!(bool)enabled.GetValue(runtime)!, "Runtime optimization defaults off until the experimental control enables it.");
    enabled.SetValue(runtime, true);
    await runtime.InitializeAsync();
    await using var video = new InputStream(VideoHeader());
    await using var control = new InputStream([]);
    await runtime.StartExternalSourceAsync(kind, video, control, null, audioEnabled: false);
    Require(runtime.IsRunningVE && runtime.VideoVE.StatusVE.DecoderName == "h264", "Explicit software startup must open the real decoder.");
    var first = Property(runtime, "LatencyOptimizationVE");
    var observed = (VEMetricsSnapshot)Property(first, "Metrics");
    Require(observed.WorkingSetBytes > 0 && observed.Transport.ControlConnected,
        "Startup must use captured process and live control metrics, not EmptyVE.");
    Require((bool)Property(first, "LowDelayRequested"), "Enabled optimizer must configure low-delay on startup.");
    Require((bool)Property(runtime.VideoVE, "LowDelayEnabledVE"), "VideoManager must expose applied decoder low-delay.");
    var options = (VEProfileOptions)Property(first, "Options");
    Require(options.Profile == VEProfile.Software && options.AccelerationRequest.Backend == VEAccelerationBackend.Software,
        "Startup optimization respects the profile supplied by the existing UI contract.");
    Require(runtime.PerformanceVE.ProfileVE == VEPerformanceProfile.Video, "Runtime does not overwrite the performance profile.");
    video.Append(ConfigurationPacket());
    await WaitUntil(() => NullableProperty(runtime, "LatencyOptimizationVE") is { } snapshot &&
        ((VEMetricsSnapshot)Property(snapshot, "Metrics")).Video.PacketsReceived == 1);
    var next = (VEMetricsSnapshot)Property(Property(runtime, "LatencyOptimizationVE"), "Metrics");
    Require(next.Video.PacketsReceived == 1 && next.Transport.VideoConnected, "Reevaluation observes actual packet/status counters.");
    Require(runtime.VideoVE.StatusVE.DecoderName == "h264" && runtime.PerformanceVE.ProfileVE == VEPerformanceProfile.Video,
        "Observation must not reopen the decoder or change the selected profile.");
    enabled.SetValue(runtime, false);
    Require(NullableProperty(runtime, "LatencyOptimizationVE") is null, "Disabling stops and clears optimization diagnostics.");
    Require((bool)Property(runtime.VideoVE, "LowDelayEnabledVE"), "A live toggle must not reinitialize an active decoder.");
    await runtime.StopAsync();
    Require(!runtime.IsRunningVE && NullableProperty(runtime, "LatencyOptimizationVE") is null, "Stop clears session optimizer state.");
    await using var secondVideo = new InputStream(VideoHeader());
    await using var secondControl = new InputStream([]);
    await runtime.StartExternalSourceAsync(kind, secondVideo, secondControl, null, audioEnabled: false);
    Require(NullableProperty(runtime, "LatencyOptimizationVE") is null && !(bool)Property(runtime.VideoVE, "LowDelayEnabledVE"),
        "Disabled restart returns to existing software initialization.");
    await runtime.StopAsync();
    enabled.SetValue(runtime, true);
    await using var thirdVideo = new InputStream(VideoHeader());
    await using var thirdControl = new InputStream([]);
    await runtime.StartExternalSourceAsync(kind, thirdVideo, thirdControl, null, audioEnabled: false);
    Require(NullableProperty(runtime, "LatencyOptimizationVE") is not null, "Enabled restart invokes the optimizer afresh.");
    var fresh = (VEMetricsSnapshot)Property(Property(runtime, "LatencyOptimizationVE"), "Metrics");
    Require(fresh.Video.PacketsReceived == 0 && fresh.Video.DecodeErrors == 0, "Restart observes new-session counters.");
    await runtime.StopAsync();
}

static VEProfileOptions Optimize(VEDynamicLatencyOptimizer optimizer, VEMetricsSnapshot metrics, VEProfile profile, bool prefer)
{
    var method = typeof(VEDynamicLatencyOptimizer).GetMethod("OptimizeForLowestLatency", [typeof(VEMetricsSnapshot), typeof(VEProfile), typeof(bool)])
        ?? throw new InvalidOperationException("Optimizer cannot preserve explicit profile/NVIDIA preference.");
    return (VEProfileOptions)method.Invoke(optimizer, [metrics, profile, prefer])!;
}
static object Property(object value, string name) => NullableProperty(value, name) ?? throw new InvalidOperationException("Missing/null " + name);
static object? NullableProperty(object value, string name) => (value.GetType().GetProperty(name)
    ?? throw new InvalidOperationException("Missing " + name)).GetValue(value);
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static async Task WaitUntil(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!condition()) await Task.Delay(10, timeout.Token);
}
static byte[] VideoHeader()
{
    byte[] bytes = new byte[16];
    BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)VEProtocolCodec.H264);
    bytes[4] = 0x80;
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 1920);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), 1080);
    return bytes;
}
static byte[] ConfigurationPacket()
{
    byte[] bytes = new byte[15];
    BinaryPrimitives.WriteUInt64BigEndian(bytes, VEProtocolConstants.PacketFlagConfigVE);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 3);
    bytes[12] = 1; bytes[13] = 2; bytes[14] = 3;
    return bytes;
}

// Deterministic stream boundary: production Core, Control, Protocol, VideoManager,
// optimizer and native FFmpeg run unchanged. No WPF window, device or ADB process.
sealed class InputStream : Stream
{
    private readonly Channel<byte[]> _input = Channel.CreateUnbounded<byte[]>();
    private byte[] _current = [];
    private int _offset;
    public InputStream(byte[] header) { if (header.Length > 0) Append(header); }
    public void Append(byte[] bytes) => _input.Writer.TryWrite(bytes);
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_offset == _current.Length) { _current = await _input.Reader.ReadAsync(cancellationToken); _offset = 0; }
        int count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, count).CopyTo(buffer); _offset += count; return count;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public override void Write(byte[] buffer, int offset, int count) { }
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Flush() { }
}
