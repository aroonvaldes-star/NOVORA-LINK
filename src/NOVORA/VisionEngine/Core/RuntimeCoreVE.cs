using NOVORA.Services;
using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Device;
using NOVORA.VisionEngine.Exchange;
using NOVORA.VisionEngine.Events;
using NOVORA.VisionEngine.Gamepad;
using NOVORA.VisionEngine.Integration;
using NOVORA.VisionEngine.Nvidia;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Privacy;
using NOVORA.VisionEngine.Renderer;
using NOVORA.VisionEngine.Server;
using NOVORA.VisionEngine.Transport;
using NOVORA.VisionEngine.Video;

namespace NOVORA.VisionEngine.Core;

/// <summary>
/// Runtime Block D: Device -> Server -> transport -> decode -> renderer Direct3D11,
/// además de audio, control, gamepad y exchange.
/// </summary>
public sealed class RuntimeCoreVE : IAsyncDisposable
{
    private readonly NovoraPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SessionDeviceVE? _deviceSessionVE;
    private TunnelTransportVE? _tunnelVE;
    private SessionServerVE? _serverSessionVE;
    private SessionTransportVE? _transportSessionVE;
    private bool _initialized;
    private bool _disposed;

    public RuntimeCoreVE(NovoraPaths paths, AdbService adb)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        ArgumentNullException.ThrowIfNull(adb);

        EventsVE = new EventCoreVE();
        PrivacyVE = new ManagerPrivacyVE();
        IntegrationVE = new ManagerIntegrationVE();
        PerformanceVE = new ManagerPerformanceVE();
        NvidiaVE = new ManagerNvidiaVE(paths);

        DeviceVE = new ManagerDeviceVE(adb);
        ServerVE = new ManagerServerVE(adb, paths);
        TransportVE = new ManagerTransportVE(adb);
        RendererVE = new ManagerRendererVE(paths);
        VideoVE = new ManagerVideoVE(paths);
        VideoVE.AttachRendererVE(RendererVE);
        AudioVE = new ManagerAudioVE(paths);
        ControlVE = new ManagerControlVE();
        GamepadVE = new ManagerGamepadVE(ControlVE, paths);

        ControlVE.SetPrivacyGatesVE(
            message => PrivacyVE.CanSendControlVE(message.Type),
            () => PrivacyVE.CanUseClipboardVE);

        ClipboardVE = new ClipboardExchangeVE(
            ControlVE,
            () =>
                PrivacyVE.CanUseClipboardVE &&
                IntegrationVE.StatusVE.Capabilities.Clipboard);

        FilesVE = new FileExchangeVE(
            adb,
            ControlVE,
            () =>
                PrivacyVE.CanExchangeFilesVE &&
                IntegrationVE.StatusVE.Capabilities.FileTransfer);

        ImagesVE = new ImageExchangeVE(FilesVE);
        MediaVE = new MediaExchangeVE(FilesVE);

        ClipboardIntegrationVE = new ClipboardIntegrationVE(ClipboardVE, PrivacyVE, IntegrationVE);
        DragDropIntegrationVE = new DragDropIntegrationVE(FilesVE, PrivacyVE, IntegrationVE);
        ShareIntegrationVE = new ShareIntegrationVE(FilesVE, PrivacyVE, IntegrationVE);
        AppIntegrationVE = new AppIntegrationVE(ControlVE, PrivacyVE, IntegrationVE);
        NotificationIntegrationVE = new NotificationIntegrationVE(ControlVE, PrivacyVE, IntegrationVE);
        VirtualDisplayIntegrationVE = new VirtualDisplayIntegrationVE(ControlVE, PrivacyVE, IntegrationVE);
        CameraIntegrationVE = new CameraIntegrationVE();
        MicrophoneIntegrationVE = new MicrophoneIntegrationVE();
        WindowIntegrationVE = new WindowIntegrationVE();

        DeviceVE.StatusChangedVE += Device_StatusChangedVE;
        ServerVE.StatusChangedVE += Server_StatusChangedVE;
        TransportVE.StateChangedVE += Transport_StateChangedVE;
        VideoVE.StatusChangedVE += Video_StatusChangedVE;
        RendererVE.StatusChangedVE += Renderer_StatusChangedVE;
        AudioVE.StatusChangedVE += Audio_StatusChangedVE;
        ControlVE.StatusChangedVE += Control_StatusChangedVE;
        GamepadVE.StatusChangedVE += Gamepad_StatusChangedVE;
        PrivacyVE.StatusChangedVE += Privacy_StatusChangedVE;
        IntegrationVE.StatusChangedVE += Integration_StatusChangedVE;
        NvidiaVE.StatusChangedVE += Nvidia_StatusChangedVE;
    }

    public event EventHandler? StatusChangedVE;
    public EventCoreVE EventsVE { get; }
    public ManagerPrivacyVE PrivacyVE { get; }
    public ManagerIntegrationVE IntegrationVE { get; }
    public ManagerPerformanceVE PerformanceVE { get; }
    public ManagerNvidiaVE NvidiaVE { get; }
    public ManagerDeviceVE DeviceVE { get; }
    public ManagerServerVE ServerVE { get; }
    public ManagerTransportVE TransportVE { get; }
    public ManagerVideoVE VideoVE { get; }
    public ManagerRendererVE RendererVE { get; }
    public ManagerAudioVE AudioVE { get; }
    public ManagerControlVE ControlVE { get; }
    public ManagerGamepadVE GamepadVE { get; }
    public ClipboardExchangeVE ClipboardVE { get; }
    public FileExchangeVE FilesVE { get; }
    public ImageExchangeVE ImagesVE { get; }
    public MediaExchangeVE MediaVE { get; }
    public ClipboardIntegrationVE ClipboardIntegrationVE { get; }
    public DragDropIntegrationVE DragDropIntegrationVE { get; }
    public ShareIntegrationVE ShareIntegrationVE { get; }
    public AppIntegrationVE AppIntegrationVE { get; }
    public NotificationIntegrationVE NotificationIntegrationVE { get; }
    public VirtualDisplayIntegrationVE VirtualDisplayIntegrationVE { get; }
    public CameraIntegrationVE CameraIntegrationVE { get; }
    public MicrophoneIntegrationVE MicrophoneIntegrationVE { get; }
    public WindowIntegrationVE WindowIntegrationVE { get; }
    public SessionDeviceVE? DeviceSessionVE => _deviceSessionVE;
    public SessionTransportVE? TransportSessionVE => _transportSessionVE;
    public bool IsInitializedVE => _initialized;
    public bool IsRunningVE => _deviceSessionVE is not null && _serverSessionVE is not null && _transportSessionVE is not null;
    public bool GamepadEnabledVE { get; set; } = true;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateBlockDToolsVE();
        NvidiaVE.EvaluateVE();
        ApplyPrivacyStateVE(PrivacyVE.StatusVE);
        _initialized = true;
        RaiseStatusChangedVE();
        return Task.CompletedTask;
    }

    public async Task SetGamepadEnabledVEAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();

        GamepadEnabledVE = enabled;

        if (!IsRunningVE)
        {
            return;
        }

        if (enabled)
        {
            await GamepadVE.StartAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await GamepadVE.StopAsync()
                .ConfigureAwait(false);
        }
    }

    public async Task StartAsync(
        string deviceSerial,
        OptionsServerVE? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSerial);
        if (!_initialized) throw new InvalidOperationException("RuntimeCoreVE debe inicializarse antes de iniciar.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunningVE)
            {
                if (string.Equals(_deviceSessionVE?.Serial, deviceSerial.Trim(), StringComparison.OrdinalIgnoreCase)) return;
                throw new InvalidOperationException("VisionEngine ya tiene otra sesión activa.");
            }

            OptionsServerVE effectiveOptions = options ?? OptionsServerVE.CreateDefaultVE();
            effectiveOptions.ValidateVE();

            try
            {
                _deviceSessionVE = await DeviceVE.OpenAsync(deviceSerial, cancellationToken).ConfigureAwait(false);
                _tunnelVE = await TransportVE.PrepareAsync(_deviceSessionVE.Serial, cancellationToken).ConfigureAwait(false);
                _serverSessionVE = await ServerVE.StartAsync(_deviceSessionVE, _tunnelVE, effectiveOptions, cancellationToken).ConfigureAwait(false);
                _transportSessionVE = await TransportVE.ConnectAsync(_tunnelVE, effectiveOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (effectiveOptions.ControlEnabled)
                    await ControlVE.StartAsync(_transportSessionVE, cancellationToken).ConfigureAwait(false);

                await VideoVE.StartAsync(_transportSessionVE, cancellationToken).ConfigureAwait(false);

                if (effectiveOptions.AudioEnabled)
                    await AudioVE.StartAsync(_transportSessionVE, effectiveOptions.AudioPlaybackEnabled, cancellationToken).ConfigureAwait(false);

                if (effectiveOptions.ControlEnabled && GamepadEnabledVE)
                    await GamepadVE.StartAsync(cancellationToken).ConfigureAwait(false);

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

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StopInternalAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task StopInternalAsync()
    {
        try { await GamepadVE.StopAsync().ConfigureAwait(false); } catch { }
        try { await AudioVE.StopAsync().ConfigureAwait(false); } catch { }
        try { await VideoVE.StopAsync().ConfigureAwait(false); } catch { }
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
        DeviceVE.CloseVE();
        ServerVE.MarkStoppedVE();
        RaiseStatusChangedVE();
    }

    private void ValidateBlockDToolsVE()
    {
        _paths.ValidateAdbTools();
        string[] required =
        [
            _paths.ScrcpyServer,
            Path.Combine(_paths.ToolsDirectory, "avcodec-62.dll"),
            Path.Combine(_paths.ToolsDirectory, "avutil-60.dll"),
            Path.Combine(_paths.ToolsDirectory, "swresample-6.dll"),
            Path.Combine(_paths.ToolsDirectory, "SDL3.dll")
        ];
        string[] missing = required.Where(path => !File.Exists(path)).Select(Path.GetFileName).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
        if (missing.Length > 0)
            throw new FileNotFoundException("VisionEngine Block D no puede iniciar porque faltan Tools: " + string.Join(", ", missing) + ".");
    }

    private void Device_StatusChangedVE(object? sender, StatusDeviceVE e) => PublishStatusEventVE(TypeEventVE.DeviceStatus);
    private void Server_StatusChangedVE(object? sender, StatusServerVE e) => PublishStatusEventVE(TypeEventVE.ServerStatus);
    private void Transport_StateChangedVE(object? sender, StatesTransportVE e) => PublishStatusEventVE(TypeEventVE.TransportState);
    private void Video_StatusChangedVE(object? sender, StatusVideoVE e) => PublishStatusEventVE(TypeEventVE.VideoStatus);
    private void Renderer_StatusChangedVE(object? sender, StatusRendererVE e) => PublishStatusEventVE(TypeEventVE.RendererStatus);
    private void Audio_StatusChangedVE(object? sender, StatusAudioVE e) => PublishStatusEventVE(TypeEventVE.AudioStatus);
    private void Control_StatusChangedVE(object? sender, StatusControlVE e) => PublishStatusEventVE(TypeEventVE.ControlStatus);
    private void Gamepad_StatusChangedVE(object? sender, StatusGamepadVE e) => PublishStatusEventVE(TypeEventVE.GamepadStatus);
    private void Integration_StatusChangedVE(object? sender, StatusIntegrationVE e) => PublishStatusEventVE(TypeEventVE.IntegrationStatus);
    private void Nvidia_StatusChangedVE(object? sender, StatusNvidiaVE e) => PublishStatusEventVE(TypeEventVE.NvidiaStatus);

    private void Privacy_StatusChangedVE(object? sender, StatusPrivacyVE e)
    {
        ApplyPrivacyStateVE(e);
        PublishStatusEventVE(TypeEventVE.PrivacyStatus);
    }

    private void ApplyPrivacyStateVE(StatusPrivacyVE status)
    {
        bool protectedVE = status.State == StatesPrivacyVE.Protected;
        RendererVE.SetPrivacyProtectedVE(protectedVE);
        AudioVE.SetPrivacyProtectedVE(protectedVE);
        GamepadVE.SetPrivacyProtectedVE(protectedVE);
    }

    private void PublishStatusEventVE(TypeEventVE type)
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
            ClipboardVE.Dispose();
            await GamepadVE.DisposeAsync().ConfigureAwait(false);
            await AudioVE.DisposeAsync().ConfigureAwait(false);
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
            GamepadVE.StatusChangedVE -= Gamepad_StatusChangedVE;
            PrivacyVE.StatusChangedVE -= Privacy_StatusChangedVE;
            IntegrationVE.StatusChangedVE -= Integration_StatusChangedVE;
            NvidiaVE.StatusChangedVE -= Nvidia_StatusChangedVE;
            _disposed = true;
            _gate.Dispose();
        }
    }
}
