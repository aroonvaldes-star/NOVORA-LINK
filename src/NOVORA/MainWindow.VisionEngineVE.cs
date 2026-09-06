using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Renderer;
using NOVORA.VisionEngine.Server;
using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace NOVORA;

public partial class MainWindow
{
    private EngineCoreVE? _visionEngineVE;
    private HostRendererVE? _visionRendererHostVE;
    private WindowRendererVE? _visionPresentationWindowVE;
    private RouterControlVE? _visionInputRouterVE;
    private StatesCoreVE? _lastVisionStateVE;
    private bool _lastRendererEnabledVE;
    private bool _closingPresentationVE;
    private bool _visionAudioOutputWatcherAttachedVE;

    private void InitializeVisionEngineRuntimeVE()
    {
        if (_visionEngineVE is not null)
        {
            return;
        }

        _visionEngineVE =
            new EngineCoreVE(
                _paths,
                _adb);

        _visionEngineVE.StatusChangedVE +=
            VisionEngine_StatusChangedVE;

        _visionEngineVE.RuntimeVE.AudioVE.SelectedOutputVE =
            _viewModel.SelectedAudioOutput;

        if (!_visionAudioOutputWatcherAttachedVE)
        {
            _viewModel.PropertyChanged +=
                VisionAudioOutput_PropertyChangedVE;

            _visionAudioOutputWatcherAttachedVE =
                true;
        }
    }

    private void VisionAudioOutput_PropertyChangedVE(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName !=
            nameof(
                ViewModels.MainViewModel.SelectedAudioOutput)
        )
        {
            return;
        }

        if (
            _visionEngineVE is null
        )
        {
            return;
        }

        try
        {
            _visionEngineVE
                .RuntimeVE
                .AudioVE
                .SelectedOutputVE =
                _viewModel.SelectedAudioOutput;

            if (
                _visionEngineVE.IsRunningVE &&
                !string.Equals(
                    _viewModel.SelectedAudioOutput,
                    VisionEngine.Audio.OutputAudioVE.DisabledValueVE,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                string active =
                    _visionEngineVE
                        .RuntimeVE
                        .AudioVE
                        .ActiveOutputVE;

                _viewModel.ConnectionStatus =
                    $"Audio VisionEngine → {active}";
            }
        }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus =
                $"Audio VisionEngine: {ex.Message}";
        }
    }

    private async Task InitializeVisionEngineOnLoadedVEAsync()
    {
        InitializeVisionEngineRuntimeVE();

        ResultCoreVE result =
            await _visionEngineVE!
                .InitializeAsync();

        if (!result.Success)
        {
            throw result.Exception ??
                  new InvalidOperationException(
                      result.Message);
        }

        SetVisionEngineStatus14(
            "OK",
            failed: false);
    }

    private void AttachVisionRendererHostVE(
        HostRendererVE host)
    {
        ArgumentNullException.ThrowIfNull(
            host);

        InitializeVisionEngineRuntimeVE();

        if (ReferenceEquals(
                _visionRendererHostVE,
                host))
        {
            return;
        }

        if (_visionRendererHostVE is not null)
        {
            try
            {
                _visionEngineVE!
                    .DetachRendererHostVE();
            }
            catch
            {
            }
        }

        _visionRendererHostVE =
            host;

        _visionEngineVE!
            .AttachRendererHostVE(
                host);
    }

    private void AttachVisionInputVE()
    {
        if (_visionEngineVE is null ||
            _visionRendererHostVE is null ||
            !_visionEngineVE.RuntimeVE.ControlVE.IsReadyVE)
        {
            return;
        }

        DetachVisionInputVE();

        var router =
            new RouterControlVE(
                _visionEngineVE.RuntimeVE.ControlVE,
                _visionEngineVE.RuntimeVE.RendererVE);

        router.InputErrorVE +=
            VisionInputRouter_InputErrorVE;

        router.AttachVE(
            _visionRendererHostVE);

        _visionInputRouterVE =
            router;

        _visionRendererHostVE.FocusInputVE();
    }

    private void DetachVisionInputVE()
    {
        RouterControlVE? router =
            _visionInputRouterVE;

        _visionInputRouterVE =
            null;

        if (router is null)
        {
            return;
        }

        router.InputErrorVE -=
            VisionInputRouter_InputErrorVE;

        router.Dispose();
    }

    private void VisionInputRouter_InputErrorVE(
        object? sender,
        string message)
    {
        _ = Dispatcher.BeginInvoke(
            new Action(
                () =>
                {
                    if (!_closing)
                    {
                        _viewModel.ConnectionStatus =
                            $"Control VisionEngine: {message}";
                    }
                }));
    }

    private HostRendererVE CreateVisionPresentationVE()
    {
        CloseVisionPresentationVE(
            restoreMainWindow: false,
            refreshInformation: false);

        var monitor =
            _viewModel.SelectedMonitor
            ?? throw new InvalidOperationException(
                "Selecciona un monitor de salida en Configuración.");

        bool fullscreen =
            string.Equals(
                _viewModel.VideoPresentationMode,
                ViewModels.MainViewModel.VideoPresentationModeFullscreen,
                StringComparison.OrdinalIgnoreCase);

        var presentation =
            new WindowRendererVE(
                monitor,
                fullscreen);

        presentation.CloseRequestedVE +=
            VisionPresentation_CloseRequestedVE;

        _visionPresentationWindowVE =
            presentation;

        presentation.Show();

        AttachVisionRendererHostVE(
            presentation.HostVE);

        // El polling informativo sólo se suspende en fullscreen.
        // LinkEngine y el pipeline crítico de VisionEngine continúan activos.
        _informationalPollingSuspended14 =
            fullscreen;

        // MainWindow deja de competir visualmente con la presentación.
        // La ventana VE no tiene Owner, por lo que permanece visible.
        WindowState =
            WindowState.Minimized;

        presentation.Activate();
        presentation.HostVE.FocusInputVE();

        return presentation.HostVE;
    }

    private async void VisionPresentation_CloseRequestedVE(
        object? sender,
        EventArgs e)
    {
        if (_closingPresentationVE ||
            _closing)
        {
            return;
        }

        _closingPresentationVE =
            true;

        try
        {
            if (_visionEngineVE is not null &&
                _visionEngineVE.IsRunningVE)
            {
                ResultCoreVE stop =
                    await _visionEngineVE
                        .StopAsync();

                if (!stop.Success)
                {
                    _viewModel.ConnectionStatus =
                        stop.Message;
                }
            }
        }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus =
                ex.Message;
        }
        finally
        {
            CloseVisionPresentationVE(
                restoreMainWindow: true,
                refreshInformation: true);

            UpdateRuntimeButtons();

            _closingPresentationVE =
                false;
        }
    }

    private void CloseVisionPresentationVE(
        bool restoreMainWindow,
        bool refreshInformation)
    {
        WindowRendererVE? presentation =
            _visionPresentationWindowVE;

        _visionPresentationWindowVE =
            null;

        DetachVisionInputVE();

        if (presentation is not null)
        {
            presentation.CloseRequestedVE -=
                VisionPresentation_CloseRequestedVE;

            try
            {
                presentation.CloseFromOwnerVE();
            }
            catch
            {
            }
        }

        if (_visionRendererHostVE is not null &&
            _visionEngineVE is not null)
        {
            try
            {
                _visionEngineVE
                    .DetachRendererHostVE();
            }
            catch
            {
            }
        }

        _visionRendererHostVE =
            null;

        _informationalPollingSuspended14 =
            false;

        if (!restoreMainWindow ||
            _closing)
        {
            return;
        }

        if (WindowState ==
            WindowState.Minimized)
        {
            WindowState =
                WindowState.Normal;
        }

        Show();
        Activate();

        if (refreshInformation)
        {
            _ = RefreshPerformanceOnceAsync();
        }
    }

    private async Task ToggleVisionEngineVEAsync()
    {
        InitializeVisionEngineRuntimeVE();

        if (!_visionEngineVE!.IsInitializedVE)
        {
            ResultCoreVE initialize =
                await _visionEngineVE
                    .InitializeAsync();

            if (!initialize.Success)
            {
                throw initialize.Exception ??
                      new InvalidOperationException(
                          initialize.Message);
            }
        }

        if (_visionEngineVE.IsRunningVE)
        {
            ResultCoreVE stop =
                await _visionEngineVE
                    .StopAsync();

            CloseVisionPresentationVE(
                restoreMainWindow: true,
                refreshInformation: true);

            if (!stop.Success)
            {
                throw stop.Exception ??
                      new InvalidOperationException(
                          stop.Message);
            }

            return;
        }

        var device =
            _viewModel.Device;

        if (device is null ||
            !device.Connected ||
            string.IsNullOrWhiteSpace(
                device.Serial))
        {
            throw new InvalidOperationException(
                "Selecciona un dispositivo Android conectado.");
        }

        if (_viewModel.SelectedMonitor is null)
        {
            throw new InvalidOperationException(
                "Selecciona un monitor de salida en Configuración.");
        }

        UpdateOutputProfile();

        var profile =
            _viewModel.OutputProfile ??
            throw new InvalidOperationException(
                "No se pudo calcular el perfil de salida de VisionEngine.");

        _ = CreateVisionPresentationVE();

        bool audioEnabled =
            _viewModel.AudioEnabled &&
            !string.Equals(
                _viewModel.SelectedAudioOutput,
                VisionEngine.Audio.OutputAudioVE.DisabledValueVE,
                StringComparison.OrdinalIgnoreCase);

        OptionsServerVE options =
            OptionsServerVE.CreateDefaultVE()
            with
            {
                VideoBitRate =
                    ParseVideoBitrateVE(
                        profile.Bitrate),

                MaxSize =
                    profile.MaxSize,

                MaxFps =
                    profile.TargetFps,

                AudioEnabled =
                    audioEnabled,

                AudioPlaybackEnabled =
                    audioEnabled,

                ControlEnabled =
                    true
            };

        try
        {
            ResultCoreVE start =
                await _visionEngineVE
                    .StartAsync(
                        device.Serial,
                        options);

            if (!start.Success)
            {
                CloseVisionPresentationVE(
                    restoreMainWindow: true,
                    refreshInformation: true);

                throw start.Exception ??
                      new InvalidOperationException(
                          start.Message);
            }

            AttachVisionInputVE();

            _visionPresentationWindowVE?.HostVE
                .FocusInputVE();
        }
        catch
        {
            CloseVisionPresentationVE(
                restoreMainWindow: true,
                refreshInformation: true);

            throw;
        }
    }

    private bool IsVisionEngineRunningVE()
        => _visionEngineVE?.IsRunningVE == true;

    private static int ParseVideoBitrateVE(
        string? value)
    {
        string normalized =
            Services.BitrateService.Normalize(
                value);

        if (!normalized.EndsWith(
                "M",
                StringComparison.OrdinalIgnoreCase))
        {
            return 10_000_000;
        }

        string number =
            normalized[..^1];

        if (!double.TryParse(
                number,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double mbps) ||
            mbps <= 0)
        {
            return 10_000_000;
        }

        return checked(
            (int)Math.Round(
                mbps * 1_000_000d,
                MidpointRounding.AwayFromZero));
    }

    private void VisionEngine_StatusChangedVE(
        object? sender,
        StatusCoreVE status)
    {
        bool stateChanged =
            _lastVisionStateVE !=
            status.State;

        bool rendererChanged =
            _lastRendererEnabledVE !=
            status.RendererEnabled;

        _lastVisionStateVE =
            status.State;

        _lastRendererEnabledVE =
            status.RendererEnabled;

        if (!stateChanged &&
            !rendererChanged &&
            status.State !=
                StatesCoreVE.Failed)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            new Action(
                () =>
                {
                    if (_closing)
                    {
                        return;
                    }

                    UpdateRuntimeButtons();

                    switch (status.State)
                    {
                        case StatesCoreVE.Failed:
                            SetVisionEngineStatus14(
                                "ERROR",
                                failed: true);

                            CloseVisionPresentationVE(
                                restoreMainWindow: true,
                                refreshInformation: true);

                            _viewModel.ConnectionStatus =
                                status.LastError ??
                                status.Message;
                            break;

                        case StatesCoreVE.Running:
                            SetVisionEngineStatus14(
                                status.RendererEnabled
                                    ? "OK"
                                    : "INICIANDO",
                                failed: false);
                            break;

                        case StatesCoreVE.Starting:
                        case StatesCoreVE.Initializing:
                            SetVisionEngineStatus14(
                                "INICIANDO",
                                failed: false);
                            break;

                        case StatesCoreVE.Stopping:
                            SetVisionEngineStatus14(
                                "DETENIENDO",
                                failed: false);
                            break;

                        case StatesCoreVE.Stopped:
                        case StatesCoreVE.Ready:
                            SetVisionEngineStatus14(
                                "OK",
                                failed: false);

                            if (_visionPresentationWindowVE is not null)
                            {
                                CloseVisionPresentationVE(
                                    restoreMainWindow: true,
                                    refreshInformation: true);
                            }
                            break;
                    }
                }));
    }

    private void SetVisionEngineStatus14(
        string text,
        bool failed)
    {
        VisionEngineUiStatusText.Text =
            text;

        VisionEngineStatusDot14.Fill =
            new SolidColorBrush(
                failed
                    ? System.Windows.Media.Color.FromRgb(
                        220,
                        69,
                        69)
                    : System.Windows.Media.Color.FromRgb(
                        16,
                        200,
                        120));
    }

    private async Task ShutdownVisionEngineRuntimeVEAsync()
    {
        if (_visionEngineVE is null)
        {
            CloseVisionPresentationVE(
                restoreMainWindow: false,
                refreshInformation: false);

            return;
        }

        try
        {
            if (_visionEngineVE.IsRunningVE)
            {
                await _visionEngineVE
                    .StopAsync();
            }
        }
        finally
        {
            CloseVisionPresentationVE(
                restoreMainWindow: false,
                refreshInformation: false);

            _visionEngineVE.StatusChangedVE -=
                VisionEngine_StatusChangedVE;

            await _visionEngineVE
                .DisposeAsync();

            _visionEngineVE =
                null;

            _visionRendererHostVE =
                null;

            _visionInputRouterVE =
                null;
        }
    }
}

