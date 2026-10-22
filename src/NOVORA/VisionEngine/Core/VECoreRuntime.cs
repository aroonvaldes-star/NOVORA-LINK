using NOVORA.Service;
using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Events;
using NOVORA.NVIDIA;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Optimization;
using NOVORA.VisionEngine.Privacy;
using NOVORA.VisionEngine.Renderer;
using NOVORA.VisionEngine.Transport;
using NOVORA.VisionEngine.Video;
using System.IO;

namespace NOVORA.VisionEngine.Core;

public enum VEExternalSourceKind { None, UsbAppControl, NativeLan }

/// <summary>
/// Runtime de VisionEngine: recibe video, audio y control de App Control,
/// decodifica y presenta en Direct3D11. ExInEngine y las integraciones
/// generales tienen ciclos de vida independientes.
/// </summary>
public sealed class VECoreRuntime : IAsyncDisposable
{
    private readonly NLServiceNovoraPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VEExternalSourceKind _externalSourceKindVE;
    private int _appControlCleanupQueuedVE;
    private bool _initialized;
    private bool _disposed;
    private VEMetricsCollector _optimizationMetricsVE = new();

    public VECoreRuntime(NLServiceNovoraPaths paths, NLServiceADB adb)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        ArgumentNullException.ThrowIfNull(adb);

        EventsVE = new VEEventsEventCore();
        PrivacyVE = new VEPrivacyManager();
        PerformanceVE = new VEPerformanceManager();
        NvidiaVE = new NLNVIDIAManager(paths);

        RendererVE = new VERendererManager(paths);
        VideoVE = new VEVideoManager(paths);
        AudioVE = new VEAudioManager(paths);
        RecordingVE = new VEMediaRecorder(VideoVE, AudioVE);
        ControlVE = new VEControlManager();
        VideoVE.ConfigureLatencyOptimizationVE(
            () => new VEDynamicLatencyOptimizer(DecoderCapabilitiesVE(), PerformanceVE),
            () => _optimizationMetricsVE.CaptureVE(VideoVE.StatusVE, AudioVE.StatusVE, ControlVE.StatusVE,
                ControlVE.IsReadyVE ? VETransportStates.Connected : VETransportStates.Stopped),
            () => VEProfileOptions.FromLegacyNvidiaVE(NvidiaVE.ProfileVE.ToString()));
        VideoVE.AttachRendererVE(RendererVE);
        VideoVE.RequestKeyFrameVE = token => ControlVE.IsReadyVE
            ? ControlVE.SendAsync(VEControlMessage.SimpleVE(VEControlType.ResetVideo), token)
            : Task.CompletedTask;

        ControlVE.SetPrivacyGatesVE(
            message => PrivacyVE.CanSendControlVE(message.Type),
            () => PrivacyVE.CanUseClipboardVE);

        VideoVE.StatusChangedVE += Video_StatusChangedVE;
        RendererVE.StatusChangedVE += Renderer_StatusChangedVE;
        AudioVE.StatusChangedVE += Audio_StatusChangedVE;
        ControlVE.StatusChangedVE += Control_StatusChangedVE;
        PrivacyVE.StatusChangedVE += Privacy_StatusChangedVE;
        NvidiaVE.StatusChangedVE += Nvidia_StatusChangedVE;
    }

    public event EventHandler? StatusChangedVE;
    public VEEventsEventCore EventsVE { get; }
    public VEPrivacyManager PrivacyVE { get; }
    public VEPerformanceManager PerformanceVE { get; }
    public NLNVIDIAManager NvidiaVE { get; }
    public VEVideoManager VideoVE { get; }
    public VEMediaRecorder RecordingVE { get; }
    public VERendererManager RendererVE { get; }
    public VEAudioManager AudioVE { get; }
    public VEControlManager ControlVE { get; }
    public bool IsInitializedVE => _initialized;
    public bool IsRunningVE => _externalSourceKindVE != VEExternalSourceKind.None;
    public bool IsAppControlVideoActiveVE => _externalSourceKindVE != VEExternalSourceKind.None;
    public VEExternalSourceKind ExternalSourceKindVE => _externalSourceKindVE;
    public bool DynamicLatencyOptimizationEnabledVE
    {
        get => VideoVE.DynamicLatencyOptimizationEnabledVE;
        set => VideoVE.DynamicLatencyOptimizationEnabledVE = value;
    }
    public VELatencyOptimizationSnapshot? LatencyOptimizationVE => VideoVE.LatencyOptimizationVE;

    private VEAccelerationCapabilities DecoderCapabilitiesVE()
    {
        NLNVIDIACapabilities nvidia = NvidiaVE.StatusVE.Capabilities;
        return new(false, nvidia.NvdecApiAvailable, false,
            nvidia.CudaInitialized && nvidia.CudaDeviceCount > 0, false, false);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRuntimeToolsVE();
        NvidiaVE.EvaluateVE();
        ApplyPrivacyStateVE(PrivacyVE.StatusVE);
        _initialized = true;
        RaiseStatusChangedVE();
        return Task.CompletedTask;
    }

    public async Task StartAppControlAsync(
        Stream videoStream,
        Stream controlStream,
        Stream? audioStream,
        bool audioEnabled = true,
        CancellationToken cancellationToken = default)
        => await StartExternalSourceAsync(VEExternalSourceKind.UsbAppControl,
            videoStream, controlStream, audioStream, audioEnabled, cancellationToken).ConfigureAwait(false);

    public async Task StartExternalSourceAsync(
        VEExternalSourceKind sourceKind,
        Stream videoStream,
        Stream controlStream,
        Stream? audioStream,
        bool audioEnabled = true,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        if (sourceKind == VEExternalSourceKind.None)
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        ArgumentNullException.ThrowIfNull(videoStream);
        ArgumentNullException.ThrowIfNull(controlStream);
        if (audioEnabled) ArgumentNullException.ThrowIfNull(audioStream);
        if (!_initialized) throw new InvalidOperationException("VECoreRuntime debe inicializarse antes de iniciar.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunningVE) throw new InvalidOperationException("VisionEngine ya tiene una sesión activa.");
            try
            {
                RecordingVE.PhoneAudioAllowed = audioEnabled;
                _optimizationMetricsVE = new VEMetricsCollector();
                await ControlVE.StartNativeAsync(controlStream, cancellationToken).ConfigureAwait(false);
                if (audioEnabled)
                    await AudioVE.StartAsync(audioStream!, playbackEnabled: true, cancellationToken).ConfigureAwait(false);
                VideoVE.PreferNvidiaVE = NvidiaVE.BeginSessionVE() != NLNVIDIAProfile.Disabled;
                await VideoVE.StartAsync(videoStream, cancellationToken).ConfigureAwait(false);
                _externalSourceKindVE = sourceKind;
                RaiseStatusChangedVE();
            }
            catch
            {
                await StopInternalAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default,
        bool preserveRendererVE = false)
    {
        ThrowIfDisposedVE();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StopInternalAsync(preserveRendererVE).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task StopInternalAsync(
        bool preserveRendererVE = false)
    {
        try { await AudioVE.StopAsync().ConfigureAwait(false); } catch { }
        try { await VideoVE.StopAsync(preserveRendererVE).ConfigureAwait(false); } catch { }
        if (RecordingVE.StatusVE.Recording) { try { await RecordingVE.StopAsync().ConfigureAwait(false); } catch { } }
        try { await ControlVE.StopAsync().ConfigureAwait(false); } catch { }

        _externalSourceKindVE = VEExternalSourceKind.None;
        RaiseStatusChangedVE();
    }

    private void ValidateRuntimeToolsVE()
    {
        _paths.ValidateAdbTools();
        string[] required =
        [
            Path.Combine(_paths.ToolsDirectory, "avcodec-62.dll"),
            Path.Combine(_paths.ToolsDirectory, "avutil-60.dll"),
            Path.Combine(_paths.ToolsDirectory, "swresample-6.dll"),
            Path.Combine(_paths.ToolsDirectory, "SDL3.dll")
        ];
        string[] missing = required.Where(path => !File.Exists(path)).Select(Path.GetFileName).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
        if (missing.Length > 0)
            throw new FileNotFoundException("VisionEngine Block D no puede iniciar porque faltan Tools: " + string.Join(", ", missing) + ".");
    }

    private int _latencyObservationQueuedVE;

    private void Video_StatusChangedVE(object? sender, VEVideoStatus e)
    {
        // This event is raised on the decode thread. The optimizer snapshot reads process
        // CPU/memory (system calls that can stall for ms), so it runs on the pool, one at a time.
        if (Interlocked.Exchange(ref _latencyObservationQueuedVE, 1) == 0)
            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                try { VideoVE.ObserveLatencyOptimizationVE(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"VE latency observation: {ex.Message}"); }
                finally { Volatile.Write(ref _latencyObservationQueuedVE, 0); }
            }, null);
        NvidiaVE.UpdateDecoderVE(e);
        PublishStatusEventVE(VEEventsTypeEvent.VideoStatus);
        if (_externalSourceKindVE != VEExternalSourceKind.None &&
            e.State is (VEVideoStates.EndOfStream or VEVideoStates.Failed) &&
            Interlocked.Exchange(ref _appControlCleanupQueuedVE, 1) == 0)
            _ = StopAppControlAfterVideoEndAsync();
    }

    private async Task StopAppControlAfterVideoEndAsync()
    {
        try { await StopAsync().ConfigureAwait(false); }
        catch { }
        finally { Interlocked.Exchange(ref _appControlCleanupQueuedVE, 0); }
    }
    private void Renderer_StatusChangedVE(object? sender, VERendererStatus e) => PublishStatusEventVE(VEEventsTypeEvent.RendererStatus);
    private void Audio_StatusChangedVE(object? sender, VEAudioStatus e) => PublishStatusEventVE(VEEventsTypeEvent.AudioStatus);
    private void Control_StatusChangedVE(object? sender, VEControlStatus e) => PublishStatusEventVE(VEEventsTypeEvent.ControlStatus);
    private void Nvidia_StatusChangedVE(object? sender, NLNVIDIAStatus e) => PublishStatusEventVE(VEEventsTypeEvent.NvidiaStatus);

    private void Privacy_StatusChangedVE(object? sender, VEPrivacyStatus e)
    {
        ApplyPrivacyStateVE(e);
        PublishStatusEventVE(VEEventsTypeEvent.PrivacyStatus);
    }

    private void ApplyPrivacyStateVE(VEPrivacyStatus status)
    {
        bool protectedVE = status.State == VEPrivacyStates.Protected;
        RecordingVE.SetProtected(protectedVE);
        RendererVE.SetPrivacyProtectedVE(protectedVE);
        AudioVE.SetPrivacyProtectedVE(protectedVE);
    }

    private void PublishStatusEventVE(VEEventsTypeEvent type)
    {
        EventsVE.PublishVE(type);
        RaiseStatusChangedVE();
    }

    private void RaiseStatusChangedVE() => StatusChangedVE?.Invoke(this, EventArgs.Empty);
    private void ThrowIfDisposedVE() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { await StopInternalAsync().ConfigureAwait(false); }
            finally { _gate.Release(); }
            await AudioVE.DisposeAsync().ConfigureAwait(false);
            await RecordingVE.DisposeAsync().ConfigureAwait(false);
            await VideoVE.DisposeAsync().ConfigureAwait(false);
            await RendererVE.DisposeAsync().ConfigureAwait(false);
            await ControlVE.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            VideoVE.StatusChangedVE -= Video_StatusChangedVE;
            RendererVE.StatusChangedVE -= Renderer_StatusChangedVE;
            AudioVE.StatusChangedVE -= Audio_StatusChangedVE;
            ControlVE.StatusChangedVE -= Control_StatusChangedVE;
            PrivacyVE.StatusChangedVE -= Privacy_StatusChangedVE;
            NvidiaVE.StatusChangedVE -= Nvidia_StatusChangedVE;
            _disposed = true;
            _gate.Dispose();
        }
    }
}
