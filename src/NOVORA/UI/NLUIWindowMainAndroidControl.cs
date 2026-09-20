using NOVORA.Control;
using NOVORA.Service;
using NOVORA.ViewModel;
using NOVORA.VisionEngine.Performance;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private NLControlTrustServer? _androidControl;
    private volatile bool _allowUsbRemember = true;
    private void UsbRemember_Changed(object sender, RoutedEventArgs e) => _allowUsbRemember = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
    private NLControlTrustServer? _androidLanControl;
    private NLControlLanDiscovery? _androidLanDiscovery;
    private bool AndroidControlSessionOpen => _androidControl is not null || _androidLanControl is not null;
    private string? _androidControlSerial;
    private long _androidControlRevision;
    private bool _androidControlPublishing;
    private bool _androidControlPreparing;
    private bool _androidControlApplying;
    private bool _androidControlReverseOwned;
    private long _androidControlGeneration;
    private NLControlFileReceiver? _androidFileReceiver;
    private void ResetAndroidFileTransfer()
    {
        _androidFileReceiver?.Dispose();
        _androidFileReceiver = null;
    }

    private void InitializeAndroidControl()
    {
        // NOVORA_AUTOUSB_V1
        InitializeAutomaticUsb();
        _viewModel.PropertyChanged += AndroidControlPropertyChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += AndroidMonitorsChanged;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= AndroidMonitorsChanged;
    }

    private void AndroidMonitorsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_closing) return;
        var monitors = _monitorService.GetMonitors();
        string? selected = _viewModel.SelectedMonitor?.DeviceName;
        _viewModel.Monitors = monitors;
        _viewModel.SelectedMonitor = monitors.FirstOrDefault(m => m.DeviceName == selected) ?? _monitorService.GetBestMonitor(monitors);
    }));

    private void AndroidControlPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NLViewModelMain.Device) or nameof(NLViewModelMain.SelectedMonitor))
            AndroidEngineStateChanged();
        if (e.PropertyName == nameof(NLViewModelMain.Device)) QueueAutomaticUsb();
        if (e.PropertyName is nameof(NLViewModelMain.Bitrate) or nameof(NLViewModelMain.TargetFps) or
            nameof(NLViewModelMain.SelectedMonitor) or nameof(NLViewModelMain.Monitors) or
            nameof(NLViewModelMain.MaxSize) or nameof(NLViewModelMain.SelectedAudioOutput) or nameof(NLViewModelMain.AudioOutputOptions))
        {
            _androidControlRevision++;
            QueueAndroidControlSnapshot();
        }
    }

    private void QueueAndroidControlSnapshot()
    {
        if (_androidControlPublishing || _closing || !AndroidControlSessionOpen) return;
        _androidControlPublishing = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _androidControlPublishing = false;
            if (!_closing)
            {
                var snapshot = CaptureAndroidControlSnapshot();
                _androidControl?.Publish(snapshot);
                _androidLanControl?.Publish(snapshot);
            }
        }));
    }

    private NLControlSnapshot CaptureAndroidControlSnapshot() => new(
        _androidControlRevision, Environment.MachineName, "1.4.0", _viewModel.Bitrate,
        (_visionEngineVE?.RuntimeVE.PerformanceVE.ProfileVE ?? VEPerformanceProfile.Gaming).ToString(),
        _viewModel.SelectedAudioOutput, GetAndroidControlAudioStatus(),
        IsVisionEngineRunningVE(),
        _viewModel.BitrateOptions.Select(o => new NLControlOption(o.Value, o.Label)).ToArray(),
        new[] { new NLControlOption("Gaming", "Juegos"), new NLControlOption("Balanced", "Equilibrado"),
            new NLControlOption("Video", "Video"), new NLControlOption("Battery", "Ahorro") },
        _viewModel.AudioOutputOptions.Select(o => new NLControlOption(o.Value, o.Label)).ToArray(),
        CaptureAndroidEngines(),
        new NLControlVideoSettings(_viewModel.MaxSize.ToString(), _viewModel.TargetFps.ToString(),
            new[] { 480, 720, 960, 1080, 1280, 1440, 1600, 1920, 2160, _viewModel.MaxSize }.Distinct().Order().Select(v => new NLControlOption(v.ToString(), v + " px · lado mayor")).ToArray(),
            new[] { 15, 24, 30, 45, 60, _viewModel.TargetFps }.Distinct().Order().Select(v => new NLControlOption(v.ToString(), v + " FPS")).ToArray(),
            _viewModel.SelectedMonitor?.DeviceName ?? "",
            _viewModel.Monitors.Select(m => new NLControlOption(m.DeviceName, m.DisplayLabel)).ToArray(), true),
        CaptureAndroidMedia(), true);

    private string GetAndroidControlAudioStatus()
    {
        var audio = _visionEngineVE?.RuntimeVE.AudioVE;
        return audio?.StatusVE is { PlaybackEnabled: true, State: NOVORA.VisionEngine.Audio.VEAudioStates.Streaming }
            ? audio.ActiveOutputVE : "Sin reproducción activa; salida solo configurada";
    }

    private Task<NLControlReply> HandleAndroidControlAsync(NLControlRequest request, long generation) =>
        Dispatcher.InvokeAsync(() => generation == _androidControlGeneration && AndroidControlSessionOpen
            ? ApplyAndroidControlAsync(request)
            : Task.FromResult(new NLControlReply(NLControlProtocol.Version, request.Id, false, "Sesión revocada."))).Task.Unwrap();

    private async Task<NLControlReply> ApplyAndroidControlAsync(NLControlRequest request)
    {
        NLControlReply Reply(bool success, string message) => new(NLControlProtocol.Version, request.Id,
            success, message, CaptureAndroidControlSnapshot());
        if (_closing || !AndroidControlSessionOpen) return Reply(false, "Sesión cerrada.");
        if (_androidControlApplying) return Reply(false, "Hay un cambio en curso.");
        string? error = NLControlCommands.Validate(request, CaptureAndroidControlSnapshot());
        if (error is not null) return Reply(false, error);
        _androidControlApplying = true;
        try
        {
            switch (request.Action)
            {
                case "applyVideoSettings":
                    return await ApplyAndroidVideoSettingsAsync(request);
                case "file.begin":
                case "file.chunk":
                case "file.end":
                case "file.cancel":
                    _androidFileReceiver ??= new NLControlFileReceiver(NLControlFileStorage.PcRoot);
                    var receiver = _androidFileReceiver;
                    return Reply(true, await Task.Run(() => receiver.HandleAsync(request.Action, request.Value, CancellationToken.None)));
                case "pair":
                    ResetAndroidFileTransfer();
                    await FinishAndroidRecordingAsync();
                    goto case "get";
                case "get":
                    return Reply(true, "Conectado a NOVORA PC. Bitrate y perfil requieren iniciar o reiniciar el video.");
                case "bitrate":
                    _viewModel.Bitrate = request.Value!;
                    break;
                case "resolution":
                    _viewModel.MaxSize = int.Parse(request.Value!, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "fps":
                    _viewModel.TargetFps = int.Parse(request.Value!, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "capture":
                case "startRecording":
                case "stopRecording":
                    return await ApplyAndroidMediaAsync(request);
                case "profile":
                    var profile = Enum.Parse<VEPerformanceProfile>(request.Value!);
                    NLServiceVideoProfile.ApplyVE(_viewModel, profile);
                    _visionEngineVE!.RuntimeVE.PerformanceVE.SetProfileVE(profile);
                    _androidControlRevision++;
                    break;
                case "audio":
                    _viewModel.RefreshAudioOutputOptions(_paths);
                    if (!_viewModel.AudioOutputOptions.Any(o => o.Value == request.Value))
                        return Reply(false, "La salida ya no está disponible.");
                    // Call the real audio engine before updating the view, so failures are not hidden by its UI handler.
                    bool playbackWasActive = _visionEngineVE!.RuntimeVE.AudioVE.StatusVE is
                        { PlaybackEnabled: true, State: NOVORA.VisionEngine.Audio.VEAudioStates.Streaming };
                    _visionEngineVE.RuntimeVE.AudioVE.SelectedOutputVE = request.Value!;
                    _viewModel.SelectedAudioOutput = request.Value!;
                    SaveSettingsFromViewModel14();
                    return Reply(true, request.Value == NOVORA.VisionEngine.Audio.VEAudioOutput.DisabledValueVE || !playbackWasActive
                        ? "Salida guardada; requiere iniciar o reiniciar el video para activar esta configuración de audio."
                        : "Salida aceptada por AudioVE; consulta la salida activa mostrada abajo.");
                case "startVideo":
                case "stopVideo":
                case "restartVideo":
                case "startLink":
                case "stopLink":
                    return await ApplyAndroidEngineActionAsync(request);
            }
            RecalculateOutputProfile14();
            SaveSettingsFromViewModel14();
            return Reply(true, "Configuración guardada. Se aplica al iniciar o reiniciar el video.");
        }
        catch (Exception ex)
        {
            return Reply(false, "No se completó el cambio: " + ex.Message);
        }
        finally
        {
            _androidControlApplying = false;
            QueueAndroidControlSnapshot();
        }
    }

    // Optional recovery button. Initial USB connection no longer depends on this click.
    private void PrepareAndroidControl_Click(object sender, RoutedEventArgs e)
    {
        _automaticUsb.Retry();
        QueueAutomaticUsb();
    }

    private async void StopAndroidControl_Click(object sender, RoutedEventArgs e)
    {
        _automaticUsb.Pause();
        await StopAndroidControlAsync();
    }

    private async Task StopAndroidControlAsync(bool preserveTrustListening = false)
    {
        _androidControlGeneration++;
        ResetAndroidFileTransfer();
        Task finishRecording = FinishAndroidRecordingAsync();
        Task stopOwnedLink = StopAndroidOwnedLinkAsync();
        NLControlTrustServer? server = _androidControl;
        NLControlTrustServer? lan = _androidLanControl;
        NLControlLanDiscovery? discovery = _androidLanDiscovery;
        _androidLanControl = null;
        _androidLanDiscovery = null;
        string? serial = _androidControlSerial;
        bool owned = _androidControlReverseOwned;
        _androidControlReverseOwned = false;
        _androidControl = null;
        _androidControlSerial = null;
        if (discovery is not null) await discovery.DisposeAsync();
        if (lan is not null) await lan.DisposeAsync();
        if (server is not null) await server.DisposeAsync();
        await stopOwnedLink;
        await finishRecording;
        if (serial is not null && owned)
        {
            try { await _adb.ExecuteRawAsync(new[] { "-s", serial, "reverse", "--remove", "tcp:27214" }); }
            catch (Exception ex)
            {
                _ = ex;
                // Phone may have been unplugged. Listener and authorization are already closed.
            }
        }
        string? persistenceError = null;
        if (!preserveTrustListening && _androidTrustStore?.LastHost is { } host)
        {
            try { _androidTrustStore.SetListening(host, false); }
            catch (Exception) { persistenceError = "Control detenido; no se pudo guardar la desactivación para el próximo inicio."; }
        }
        if (!_closing) AndroidControlStatus.Text = persistenceError ?? "Control detenido.";
    }
}
