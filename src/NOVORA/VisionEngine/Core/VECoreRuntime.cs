using NOVORA.Service;
using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Device;
using NOVORA.VisionEngine.Events;
using NOVORA.NVIDIA;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Privacy;
using NOVORA.VisionEngine.Renderer;
using NOVORA.VisionEngine.Server;
using NOVORA.VisionEngine.Transport;
using NOVORA.VisionEngine.Video;
using System.IO;

namespace NOVORA.VisionEngine.Core;

public enum VEExternalSourceKind { None, UsbAppControl, NativeLan }

/// <summary>
/// Runtime Block D: Device -> Server -> transport -> decode -> renderer Direct3D11,
/// además de audio y el adaptador de control. ExInEngine y las integraciones
/// generales tienen ciclos de vida independientes.
/// </summary>
public sealed class VECoreRuntime : IAsyncDisposable
{
    private readonly NLServiceNovoraPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VEDeviceSession? _deviceSessionVE;
    private VETransportTunnel? _tunnelVE;
    private VEServerSession? _serverSessionVE;
    private VETransportSession? _transportSessionVE;
    private VEExternalSourceKind _externalSourceKindVE;
    private int _appControlCleanupQueuedVE;
    private bool _initialized;
    private bool _disposed;

    public VECoreRuntime(NLServiceNovoraPaths paths, NLServiceADB adb)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        ArgumentNullException.ThrowIfNull(adb);

        EventsVE = new VEEventsEventCore();
        PrivacyVE = new VEPrivacyManager();
        PerformanceVE = new VEPerformanceManager();
        NvidiaVE = new NLNVIDIAManager(paths);

        DeviceVE = new VEDeviceManager(adb);
        ServerVE = new VEServerManager(adb, paths);
        TransportVE = new VETransportManager(adb);
        RendererVE = new VERendererManager(paths);
        VideoVE = new VEVideoManager(paths);
        VideoVE.AttachRendererVE(RendererVE);
        AudioVE = new VEAudioManager(paths);
        RecordingVE = new VEMediaRecorder(VideoVE, AudioVE);
        ControlVE = new VEControlManager();
        VideoVE.RequestKeyFrameVE = token => ControlVE.IsReadyVE
            ? ControlVE.SendAsync(VEControlMessage.SimpleVE(VEControlType.ResetVideo), token)
            : Task.CompletedTask;

        ControlVE.SetPrivacyGatesVE(
            message => PrivacyVE.CanSendControlVE(message.Type),
            () => PrivacyVE.CanUseClipboardVE);

        DeviceVE.StatusChangedVE += Device_StatusChangedVE;
        ServerVE.StatusChangedVE += Server_StatusChangedVE;
        TransportVE.StateChangedVE += Transport_StateChangedVE;
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
    public VEDeviceManager DeviceVE { get; }
    public VEServerManager ServerVE { get; }
    public VETransportManager TransportVE { get; }
    public VEVideoManager VideoVE { get; }
    public VEMediaRecorder RecordingVE { get; }
    public VERendererManager RendererVE { get; }
    public VEAudioManager AudioVE { get; }
    public VEControlManager ControlVE { get; }
    public VEDeviceSession? DeviceSessionVE => _deviceSessionVE;
    public VETransportSession? TransportSessionVE => _transportSessionVE;
    public bool IsInitializedVE => _initialized;
    public bool IsRunningVE => _externalSourceKindVE != VEExternalSourceKind.None ||
        _deviceSessionVE is not null && _serverSessionVE is not null && _transportSessionVE is not null;
    public bool IsAppControlVideoActiveVE => _externalSourceKindVE != VEExternalSourceKind.None;
    public VEExternalSourceKind ExternalSourceKindVE => _externalSourceKindVE;

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

    public async Task StartAsync(
        string deviceSerial,
        VEServerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSerial);
        if (!_initialized) throw new InvalidOperationException("VECoreRuntime debe inicializarse antes de iniciar.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunningVE)
            {
                if (string.Equals(_deviceSessionVE?.Serial, deviceSerial.Trim(), StringComparison.OrdinalIgnoreCase)) return;
                throw new InvalidOperationException("VisionEngine ya tiene otra sesión activa.");
            }

            VEServerOptions effectiveOptions = options ?? VEServerOptions.CreateDefaultVE();
            effectiveOptions.ValidateVE();
            RecordingVE.PhoneAudioAllowed = effectiveOptions.AudioEnabled &&
                effectiveOptions.AudioSource is VEAudioSource.Output or VEAudioSource.Playback;

            try
            {
                _deviceSessionVE = await DeviceVE.OpenAsync(deviceSerial, cancellationToken).ConfigureAwait(false);
                _tunnelVE = await TransportVE.PrepareAsync(_deviceSessionVE.Serial, cancellationToken).ConfigureAwait(false);
                _serverSessionVE = await ServerVE.StartAsync(_deviceSessionVE, _tunnelVE, effectiveOptions, cancellationToken).ConfigureAwait(false);
                _transportSessionVE = await TransportVE.ConnectAsync(_tunnelVE, effectiveOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (effectiveOptions.ControlEnabled)
                    await ControlVE.StartAsync(_transportSessionVE, cancellationToken).ConfigureAwait(false);

                VideoVE.PreferNvidiaVE = NvidiaVE.BeginSessionVE() != NLNVIDIAProfile.Disabled;
                await VideoVE.StartAsync(_transportSessionVE, cancellationToken).ConfigureAwait(false);

                if (effectiveOptions.AudioEnabled)
                    await AudioVE.StartAsync(_transportSessionVE, effectiveOptions.AudioPlaybackEnabled, cancellationToken).ConfigureAwait(false);

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

        if (_transportSessionVE is not null)
        {
            try { await _transportSessionVE.DisposeAsync().ConfigureAwait(false); } catch { }
            _transportSessionVE = null;
        }
        if (_serverSessionVE is not null)
        {
            try { await _serverSessionVE.DisposeAsync().ConfigureAwait(false); } catch { }
            _serverSessionVE = null;
        }
        if (_tunnelVE is not null)
        {
            try { await TransportVE.RemoveAsync(_tunnelVE, CancellationToken.None).ConfigureAwait(false); } catch { }
            _tunnelVE = null;
        }
        _deviceSessionVE = null;
        _externalSourceKindVE = VEExternalSourceKind.None;
        DeviceVE.CloseVE();
        ServerVE.MarkStoppedVE();
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

    private void Device_StatusChangedVE(object? sender, VEDeviceStatus e) => PublishStatusEventVE(VEEventsTypeEvent.DeviceStatus);
    private void Server_StatusChangedVE(object? sender, VEServerStatus e) => PublishStatusEventVE(VEEventsTypeEvent.ServerStatus);
    private void Transport_StateChangedVE(object? sender, VETransportStates e) => PublishStatusEventVE(VEEventsTypeEvent.TransportState);
    private void Video_StatusChangedVE(object? sender, VEVideoStatus e)
    {
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
            DeviceVE.StatusChangedVE -= Device_StatusChangedVE;
            ServerVE.StatusChangedVE -= Server_StatusChangedVE;
            TransportVE.StateChangedVE -= Transport_StateChangedVE;
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
