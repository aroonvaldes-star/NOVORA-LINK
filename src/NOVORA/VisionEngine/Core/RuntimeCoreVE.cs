using NOVORA.Services;
using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Device;
using NOVORA.VisionEngine.Exchange;
using NOVORA.VisionEngine.Gamepad;
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

        DeviceVE = new ManagerDeviceVE(adb);
        ServerVE = new ManagerServerVE(adb, paths);
        TransportVE = new ManagerTransportVE(adb);
        RendererVE = new ManagerRendererVE(paths);
        VideoVE = new ManagerVideoVE(paths);
        VideoVE.AttachRendererVE(RendererVE);
        AudioVE = new ManagerAudioVE(paths);
        ControlVE = new ManagerControlVE();
        GamepadVE = new ManagerGamepadVE(ControlVE, paths);
        ClipboardVE = new ClipboardExchangeVE(ControlVE);
        FilesVE = new FileExchangeVE(adb, ControlVE);
        ImagesVE = new ImageExchangeVE(FilesVE);
        MediaVE = new MediaExchangeVE(FilesVE);

        DeviceVE.StatusChangedVE += Device_StatusChangedVE;
        ServerVE.StatusChangedVE += Server_StatusChangedVE;
        TransportVE.StateChangedVE += Transport_StateChangedVE;
        VideoVE.StatusChangedVE += Video_StatusChangedVE;
        RendererVE.StatusChangedVE += Renderer_StatusChangedVE;
        AudioVE.StatusChangedVE += Audio_StatusChangedVE;
        ControlVE.StatusChangedVE += Control_StatusChangedVE;
        GamepadVE.StatusChangedVE += Gamepad_StatusChangedVE;
    }

    public event EventHandler? StatusChangedVE;
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
    public SessionDeviceVE? DeviceSessionVE => _deviceSessionVE;
    public SessionTransportVE? TransportSessionVE => _transportSessionVE;
    public bool IsInitializedVE => _initialized;
    public bool IsRunningVE => _deviceSessionVE is not null && _serverSessionVE is not null && _transportSessionVE is not null;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateBlockDToolsVE();
        _initialized = true;
        RaiseStatusChangedVE();
        return Task.CompletedTask;
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

                if (effectiveOptions.ControlEnabled)
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

    private void Device_StatusChangedVE(object? sender, StatusDeviceVE e) => RaiseStatusChangedVE();
    private void Server_StatusChangedVE(object? sender, StatusServerVE e) => RaiseStatusChangedVE();
    private void Transport_StateChangedVE(object? sender, StatesTransportVE e) => RaiseStatusChangedVE();
    private void Video_StatusChangedVE(object? sender, StatusVideoVE e) => RaiseStatusChangedVE();
    private void Renderer_StatusChangedVE(object? sender, StatusRendererVE e) => RaiseStatusChangedVE();
    private void Audio_StatusChangedVE(object? sender, StatusAudioVE e) => RaiseStatusChangedVE();
    private void Control_StatusChangedVE(object? sender, StatusControlVE e) => RaiseStatusChangedVE();
    private void Gamepad_StatusChangedVE(object? sender, StatusGamepadVE e) => RaiseStatusChangedVE();
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
            _disposed = true;
            _gate.Dispose();
        }
    }
}
