using NOVORA.VisionEngine.Control;
using NOVORA.Control;
using NOVORA.ExInEngine;
using NOVORA.Integration;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Exchange;
using NOVORA.VisionEngine.Integration;
using NOVORA.VisionEngine.Performance;
using NOVORA.NVIDIA;
using NOVORA.VisionEngine.Renderer;
using NOVORA.Service;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private VECoreEngine? _visionEngineVE;
    private NLIntegrationRuntime? _integrationRuntime;
    private ExInCoreEngine? _exInEngine;
    private ExInControlSession? _exInControlSession;
    private Task _exInInitialization = Task.CompletedTask;
    private readonly SemaphoreSlim _exInConnectionGate = new(1, 1);
    private readonly SemaphoreSlim _exInPresentationSyncGateVE = new(1, 1);
    private VERendererHost? _visionRendererHostVE;
    private VERendererWindow? _visionPresentationWindowVE;
    private VEControlRouter? _visionInputRouterVE;
    private VEExchangeTransfer? _visionTransferExchangeVE;
    private long _integrationFilesSentCountVE;
    private long _integrationFilesSentBytesVE;
    private long _integrationFilesReceivedCountVE;
    private long _integrationFilesReceivedBytesVE;

    private VECoreStates? _lastVisionStateVE;

    private string? _activeVisionSerialVE;
    private string? _visionUsbSerialVE;
    private string? _visionLanSerialVE;
    private bool _visionLanPreparingVE;
    private string? _visionLanAttemptUsbSerialVE;
    private long _visionLanAttemptEpochVE = -1;

    private bool _lastRendererEnabledVE;
    private bool _closingPresentationVE;
    private bool _visionAudioOutputWatcherAttachedVE;
    private bool _externalVisionWaitingForDeviceVE;
    private VEExternalSourceKind _externalVisionWaitingSourceVE;


    internal static string? ResolvePreparedVisionLanSerialVE(
        IEnumerable<NLControlUsbCandidate> candidates,
        IReadOnlySet<string>? online)
    {
        string[] lanSerials = candidates
            .Where(candidate => candidate.Connected && candidate.Wifi &&
                !string.IsNullOrWhiteSpace(candidate.Serial) &&
                (online is null || online.Contains(candidate.Serial)))
            .Select(candidate => candidate.Serial.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        return lanSerials.Length == 1 ? lanSerials[0] : null;
    }

    private async Task PrepareVisionLanFallbackVEAsync(
        string? usbSerial = null,
        long automaticUsbEpoch = -1,
        bool automatic = false)
    {
        if (_closing || _visionLanPreparingVE)
        {
            return;
        }

        usbSerial =
            string.IsNullOrWhiteSpace(usbSerial)
                ? _viewModel.Device?.Serial?.Trim()
                : usbSerial.Trim();

        if (string.IsNullOrWhiteSpace(usbSerial) ||
            usbSerial.Contains(':', StringComparison.Ordinal))
        {
            if (!automatic) await ConnectWirelessDebuggingVEAsync();
            return;
        }

        string? announcedLanSerial = ResolvePreparedVisionLanSerialVE(
            _viewModel.Devices.Select(device => new NLControlUsbCandidate(
                device.Serial,
                device.Connected,
                device.IsWifiConnection)),
            _automaticUsbOnline);

        if (!string.IsNullOrWhiteSpace(announcedLanSerial))
        {
            _visionUsbSerialVE = usbSerial;
            _visionLanSerialVE = announcedLanSerial;
            QueueAutomaticUsb();
            ShowTopMessage14(
                $"Failover LAN listo para {announcedLanSerial}.",
                NLUIMessageKind14.Success);
            return;
        }

        if (automatic &&
            automaticUsbEpoch >= 0 &&
            _visionLanAttemptEpochVE == automaticUsbEpoch &&
            string.Equals(_visionLanAttemptUsbSerialVE, usbSerial, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_visionLanSerialVE) &&
            await _adb.IsDeviceOnlineAsync(_visionLanSerialVE).ConfigureAwait(true))
        {
            QueueAutomaticUsb();
            return;
        }

        if (automatic && automaticUsbEpoch >= 0)
        {
            _visionLanAttemptUsbSerialVE = usbSerial;
            _visionLanAttemptEpochVE = automaticUsbEpoch;
        }

        _visionLanPreparingVE = true;
        WifiAdbButton.IsEnabled = false;
        ShowTopMessage14(
            "Preparando la ruta LAN del mismo teléfono…",
            NLUIMessageKind14.Info);

        try
        {
            using CancellationTokenSource deadline =
                new(TimeSpan.FromSeconds(20));

            string lanSerial =
                await _adb
                    .ConnectOverWifiAsync(
                        usbSerial,
                        cancellationToken: deadline.Token)
                    .ConfigureAwait(true);

            if (!await _adb
                    .IsDeviceOnlineAsync(
                        lanSerial,
                        deadline.Token)
                    .ConfigureAwait(true))
            {
                throw new InvalidOperationException(
                    "La ruta ADB por LAN no quedó disponible.");
            }

            _visionUsbSerialVE = usbSerial;
            _visionLanSerialVE = lanSerial;
            QueueAutomaticUsb();

            ShowTopMessage14(
                $"Failover LAN listo para {lanSerial}.",
                NLUIMessageKind14.Success);
        }
        catch (OperationCanceledException)
        {
            ShowTopMessage14(
                "La preparación del failover LAN agotó el tiempo de espera.",
                NLUIMessageKind14.Warning);
        }
        catch (Exception ex)
        {
            _visionLanSerialVE = null;
            ShowTopMessage14(
                $"No se pudo preparar el failover LAN: {ex.Message}",
                NLUIMessageKind14.Error);
        }
        finally
        {
            _visionLanPreparingVE = false;
            UpdateRuntimeButtons();
        }
    }

    private async Task ConnectWirelessDebuggingVEAsync()
    {
        string? discoveredEndpoint = null;
        try
        {
            using var discoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var endpoints = await _adb.GetWirelessDebuggingEndpointsAsync(discoveryTimeout.Token);
            if (endpoints.Count == 1) discoveredEndpoint = endpoints[0];
        }
        catch (Exception) { /* El destino también puede introducirse manualmente. */ }
        var body = new System.Windows.Controls.StackPanel();
        var instruction = new System.Windows.Controls.TextBlock
        {
            Text = "Escribe la Dirección IP y puerto de la pantalla principal de Depuración inalámbrica del teléfono. El puerto de vinculación es diferente.",
            TextWrapping = TextWrapping.Wrap
        };
        ApplyNovoraText(instruction);
        body.Children.Add(instruction);
        var input = new System.Windows.Controls.TextBox
        {
            Margin = new Thickness(0, 14, 0, 14),
            FontSize = 16,
            Text = discoveredEndpoint ?? string.Empty
        };
        input.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "InputBackgroundBrush");
        input.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "InputForegroundBrush");
        body.Children.Add(input);
        var dialog = CreateNovoraDialog("ADB inalámbrico", 520, body);
        var connect = new System.Windows.Controls.Button { Content = "CONECTAR ADB", Padding = new Thickness(12) };
        ApplyNovoraActionButton(connect);
        connect.Click += (_, _) => dialog.DialogResult = true;
        body.Children.Add(connect);
        if (dialog.ShowDialog() != true || _closing) return;

        _visionLanPreparingVE = true;
        WifiAdbButton.IsEnabled = false;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string serial = await _adb.ConnectWirelessDebuggingAsync(input.Text, deadline.Token);
            await RefreshDevicesAsync(force: true);
            var connected = _viewModel.Devices.FirstOrDefault(device =>
                string.Equals(device.Serial, serial, StringComparison.OrdinalIgnoreCase));
            if (connected is null) throw new InvalidOperationException("ADB conectó, pero NOVORA no pudo enumerar ese teléfono.");
            _viewModel.Device = connected;
            await EnsureExInStandaloneAsync();
            ShowTopMessage14($"ADB inalámbrico conectado a {serial}.", NLUIMessageKind14.Success);
        }
        catch (Exception ex)
        {
            ShowTopMessage14("No se pudo conectar ADB inalámbrico: " + ex.Message, NLUIMessageKind14.Error);
        }
        finally
        {
            _visionLanPreparingVE = false;
            UpdateRuntimeButtons();
        }
    }

    // ============================================================
    // INITIALIZATION
    // ============================================================

    private void InitializeVisionEngineRuntimeVE()
    {
        if (_visionEngineVE is not null)
        {
            return;
        }

        _exInEngine ??= new ExInCoreEngine(_paths);
        int pointerSensitivity = Math.Clamp(new NLServiceSettings().Load().ExInPointerSensitivity, 10, 75);
        _exInEngine.Manager.SetPointerSensitivityVE(pointerSensitivity / 100d);
        _exInEngine.Manager.StatusChangedVE -= ShellGamepad_StatusChangedVE;
        _exInEngine.Manager.StatusChangedVE += ShellGamepad_StatusChangedVE;
        _exInEngine.Manager.LiveSnapshotChangedVE -= ShellGamepad_LiveSnapshotChangedVE;
        _exInEngine.Manager.LiveSnapshotChangedVE += ShellGamepad_LiveSnapshotChangedVE;
        _exInEngine.Manager.BatteryAlertVE -= ShellGamepad_BatteryAlertVE;
        _exInEngine.Manager.BatteryAlertVE += ShellGamepad_BatteryAlertVE;
        _exInInitialization = _exInEngine.InitializeAsync();
        _exInControlSession ??= new ExInControlSession(_exInEngine, _paths, _adb);
        _visionEngineVE =
            new VECoreEngine(
                _paths,
                _adb);
        _integrationRuntime = new NLIntegrationRuntime(
            _adb,
            _visionEngineVE.RuntimeVE.ControlVE,
            _visionEngineVE.RuntimeVE.PrivacyVE);

        _visionEngineVE.StatusChangedVE +=
            VisionEngine_StatusChangedVE;
        _visionEngineVE.RuntimeVE.PrivacyVE.StatusChangedVE -= ShellPrivacy_StatusChangedVE;
        _visionEngineVE.RuntimeVE.PrivacyVE.StatusChangedVE += ShellPrivacy_StatusChangedVE;
        _visionEngineVE.RuntimeVE.NvidiaVE.StatusChangedVE += ShellNvidia_StatusChangedVE;

        /*
         * ANDROID -> WINDOWS CLIPBOARD
         *
         * Event-driven:
         *
         * Android
         *   -> control channel
         *   -> VEControlManager.ClipboardChangedVE
         *   -> VEExchangeClipboard.ClipboardChangedVE
         *   -> Windows Clipboard
         *
         * Sin polling.
         * Sin historial.
         */
        _integrationRuntime.Clipboard.ClipboardChangedVE +=
            VisionClipboard_ChangedVE;
        _integrationRuntime.Capabilities.StatusChangedVE +=
            ShellIntegration_StatusChangedVE;

        _visionEngineVE.RuntimeVE.AudioVE.SelectedOutputVE =
            _viewModel.SelectedAudioOutput;

        ApplyAdvancedVisionSettingsVE();

        // La cola pertenece al runtime de integraciones y sigue disponible
        // aunque VisionEngine no tenga una sesión de video activa.
        _visionTransferExchangeVE = _integrationRuntime.Transfer;

        _visionTransferExchangeVE.TransferStartedVE +=
            VisionTransfer_StartedVE;

        _visionTransferExchangeVE.TransferCompletedVE +=
            VisionTransfer_CompletedVE;

        _visionTransferExchangeVE.TransferFailedVE +=
            VisionTransfer_FailedVE;

        if (!_visionAudioOutputWatcherAttachedVE)
        {
            _viewModel.PropertyChanged +=
                VisionAudioOutput_PropertyChangedVE;

            _visionAudioOutputWatcherAttachedVE =
                true;
        }
    }

    private void ApplyAdvancedVisionSettingsVE()
    {
        if (_visionEngineVE is null || _integrationRuntime is null)
        {
            return;
        }

        VECoreRuntime runtime =
            _visionEngineVE.RuntimeVE;

        _exInEngine?.SetPrivacyProtected(_viewModel.PrivacyShieldEnabled);
        runtime.PrivacyVE.SetManualShieldVE(
            _viewModel.PrivacyShieldEnabled);

        _integrationRuntime.Capabilities.SetCapabilitiesVE(
            new VEIntegrationCapabilities(
                Clipboard: _viewModel.IntegrationClipboardEnabled,
                FileTransfer: _viewModel.IntegrationFileTransferEnabled,
                DragDrop: _viewModel.IntegrationDragDropEnabled,
                Applications: _viewModel.IntegrationApplicationsEnabled,
                Notifications: _viewModel.IntegrationNotificationsEnabled,
                DynamicResize: _viewModel.IntegrationDynamicResizeEnabled,
                Camera: false,
                AndroidMicrophone: true,
                PcMicrophoneToAndroid: false));

        _ = ApplyExInRuntimeStateAsync(_viewModel.ExInEnabled);

        if (!Enum.TryParse(
                _viewModel.VisionProfile,
                ignoreCase: true,
                out VEProfile profile) || !Enum.IsDefined(profile))
        {
            profile =
                VEProfile.Automatic;

            _viewModel.VisionProfile =
                VEProfile.Automatic.ToString();
        }

        VEProfileOptions unified = VEProfileOptions.CreateVE(profile);
        runtime.PerformanceVE.SetProfileVE(
            unified.PerformanceProfile);
        runtime.NvidiaVE.SetProfileVE(
            unified.ToLegacyNvidiaVE());

        if (runtime.IsRunningVE)
        {
            _ = ApplyExInRuntimeStateAsync(_viewModel.ExInEnabled);
        }

        UpdateAdvancedFeatureStatus14();
    }

    private async Task ApplyExInRuntimeStateAsync(bool enabled)
    {
        try
        {
            if (_exInEngine is null) return;
            await _exInEngine.SetEnabledAsync(enabled).ConfigureAwait(true);
            await EnsureExInStandaloneAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (!_closing)
            {
                _viewModel.ConnectionStatus =
                    $"ExInEngine: {ex.Message}";
            }
        }
    }

    private void UpdateAdvancedFeatureStatus14()
    {
        if (EngineFeatureStatus14 is null)
        {
            return;
        }

        string privacy =
            _viewModel.PrivacyShieldEnabled
                ? "Privacy ON"
                : "Privacy OFF";

        bool integrationEnabled =
            _viewModel.IntegrationClipboardEnabled ||
            _viewModel.IntegrationFileTransferEnabled ||
            _viewModel.IntegrationDragDropEnabled ||
            _viewModel.IntegrationApplicationsEnabled ||
            _viewModel.IntegrationNotificationsEnabled ||
            _viewModel.IntegrationDynamicResizeEnabled;

        string integration =
            integrationEnabled
                ? "Integración ON"
                : "Integración OFF";

        string gamepad = _viewModel.ExInEnabled ? "ExIn ON" : "ExIn OFF";

        string visionProfile =
            $"VE {_viewModel.VisionProfile}";

        EngineFeatureStatus14.Text =
            $"NOVORA · {privacy} · {integration} · {gamepad} · {visionProfile}";

        EngineFeatureStatus14.ToolTip =
            "Estado de funciones independientes y aceleración de VisionEngine. " +
            "Los cambios se administran en Configuración.";
    }

    // ============================================================
    // CLIPBOARD ANDROID -> WINDOWS
    // ============================================================

    private void VisionClipboard_ChangedVE(
        object? sender,
        string text)
    {
        /*
         * Este callback viene del control channel.
         *
         * NO consulta Android.
         * NO usa timer.
         * NO usa Task.Delay.
         * NO hace polling.
         *
         * Sólo reacciona cuando Android reporta un cambio real.
         */

        if (
            _closing ||
            _visionEngineVE is null)
        {
            return;
        }

        if (
            !_visionEngineVE
                .RuntimeVE
                .PrivacyVE
                .CanUseClipboardVE ||
            _integrationRuntime is null ||
            !_integrationRuntime
                .Capabilities
                .StatusVE
                .Capabilities
                .Clipboard)
        {
            return;
        }

        /*
         * Clipboard de WPF requiere el hilo STA/UI.
         *
         * El reader del canal de control puede estar en otro hilo,
         * por eso hacemos marshal al Dispatcher existente.
         */
        _ =
            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        if (
                            _closing ||
                            _visionEngineVE is null)
                        {
                            return;
                        }

                        if (
                            !_visionEngineVE
                                .RuntimeVE
                                .PrivacyVE
                                .CanUseClipboardVE)
                        {
                            return;
                        }

                        try
                        {
                            if (string.IsNullOrEmpty(text))
                            {
                                System.Windows.Clipboard.Clear();
                            }
                            else
                            {
                                System.Windows.Clipboard.SetText(
                                    text,
                                    System.Windows.TextDataFormat.UnicodeText);
                            }

                            /*
                             * Nunca mostramos ni registramos el contenido.
                             */
                            _viewModel.ConnectionStatus =
                                "Clipboard Android → Windows actualizado.";
                        }
                        catch (
                            System.Runtime.InteropServices.ExternalException)
                        {
                            /*
                             * Otra aplicación puede tener temporalmente
                             * bloqueado el clipboard.
                             *
                             * No hacemos retries periódicos:
                             * el próximo cambio real de Android volverá
                             * a producir su propio evento.
                             */
                            _viewModel.ConnectionStatus =
                                "Clipboard Windows ocupado; cambio no aplicado.";
                        }
                    }));
    }
    // ============================================================
    // AUDIO OUTPUT
    // ============================================================

    private void VisionAudioOutput_PropertyChangedVE(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName !=
            nameof(
                ViewModel.NLViewModelMain.SelectedAudioOutput))
        {
            return;
        }

        if (_visionEngineVE is null)
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
                    VisionEngine.Audio.VEAudioOutput.DisabledValueVE,
                    StringComparison.OrdinalIgnoreCase))
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

    // ============================================================
    // INITIALIZE ON LOADED
    // ============================================================

    private async Task InitializeVisionEngineOnLoadedVEAsync()
    {
        InitializeVisionEngineRuntimeVE();

        VECoreResult result =
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

    // ============================================================
    // RENDERER HOST
    // ============================================================

    private void AttachVisionRendererHostVE(
        VERendererHost host)
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
            _visionRendererHostVE.InputFocusGainedVE -=
                VisionRendererHost_InputFocusGainedVE;

            _visionRendererHostVE.KeyDownVE -=
                VisionExchange_KeyDownVE;

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

        _visionRendererHostVE.InputFocusGainedVE +=
            VisionRendererHost_InputFocusGainedVE;

        _visionRendererHostVE.KeyDownVE +=
            VisionExchange_KeyDownVE;

        _visionEngineVE!
            .AttachRendererHostVE(
                host);
    }

    private async void VisionRendererHost_InputFocusGainedVE(
        object? sender,
        EventArgs e)
    {
        if (
            _closing ||
            _visionEngineVE is null ||
            !_visionEngineVE.IsRunningVE)
        {
            return;
        }

        await SynchronizeExInDevicesAfterPresentationAsync();
    }

    // ============================================================
    // INPUT
    // ============================================================

    private void AttachVisionInputVE()
    {
        if (
            _visionEngineVE is null ||
            _visionRendererHostVE is null ||
            !_visionEngineVE.RuntimeVE.ControlVE.IsReadyVE)
        {
            return;
        }

        DetachVisionInputVE();

        VEControlRouter router =
            new(
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
        VEControlRouter? router =
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
        _ =
            Dispatcher.BeginInvoke(
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

    // ============================================================
    // PRESENTATION
    // ============================================================

    private VERendererHost CreateVisionPresentationVE()
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
                ViewModel.NLViewModelMain.VideoPresentationModeFullscreen,
                StringComparison.OrdinalIgnoreCase);
        bool maximized =
            string.Equals(
                _viewModel.VideoPresentationMode,
                ViewModel.NLViewModelMain.VideoPresentationModeMaximized,
                StringComparison.OrdinalIgnoreCase);

        VERendererWindow presentation =
            new(
                monitor,
                fullscreen,
                maximized);

        presentation.CloseRequestedVE +=
            VisionPresentation_CloseRequestedVE;

        presentation.ExitFullscreenRequestedVE +=
            VisionPresentation_ExitFullscreenRequestedVE;

        /*
         * Drag & Drop de la ventana real de VisionEngine.
         */
        presentation.FilesDroppedVE +=
            VisionPresentation_FilesDroppedVE;

        presentation.DragEnteredVE +=
            VisionPresentation_DragEnteredVE;

        presentation.DragEndedVE +=
            VisionPresentation_DragEndedVE;

        _visionPresentationWindowVE =
            presentation;

        presentation.Show();

        AttachVisionRendererHostVE(
            presentation.HostVE);

        _ = SynchronizeExInDevicesAfterPresentationAsync();

        /*
         * El polling informativo sólo se suspende
         * en fullscreen.
         *
         * LinkEngine y el pipeline crítico de
         * VisionEngine continúan activos.
         */
        _informationalPollingSuspended14 =
            fullscreen;

        /*
         * NLUIWindowMain deja de competir visualmente
         * con la presentación.
         *
         * VERendererWindow no tiene Owner,
         * por lo que permanece visible.
         */
        WindowState =
            WindowState.Minimized;

        presentation.Activate();

        presentation.HostVE
            .FocusInputVE();

        return
            presentation.HostVE;
    }

    private async Task SynchronizeExInDevicesAfterPresentationAsync()
    {
        if (!await _exInPresentationSyncGateVE.WaitAsync(0).ConfigureAwait(true))
            return;

        try
        {
            await _exInInitialization.ConfigureAwait(true);
            if (!_closing && _exInEngine is not null)
                await _exInEngine.Manager.ResyncConnectedDevicesVEAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (!_closing)
                _viewModel.ConnectionStatus = "ExInEngine: " + ex.Message;
        }
        finally
        {
            _exInPresentationSyncGateVE.Release();
        }
    }

    // ============================================================
    // DRAG & DROP
    // ============================================================

    private void IntegrationFiles_DragOver(
        object sender,
        System.Windows.DragEventArgs e)
    {
        e.Effects =
            !_closing &&
            _integrationRuntime is not null &&
            _viewModel.IntegrationFileTransferEnabled &&
            _viewModel.IntegrationDragDropEnabled &&
            e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
                ? System.Windows.DragDropEffects.Copy
                : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void IntegrationFiles_Drop(
        object sender,
        System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        if (_closing || _integrationRuntime is null || _visionTransferExchangeVE is null)
            return;

        if (!_viewModel.IntegrationFileTransferEnabled || !_viewModel.IntegrationDragDropEnabled)
        {
            _viewModel.ConnectionStatus = "Activa Archivos y Drag & Drop en Configuración.";
            return;
        }

        if (_integrationRuntime.Privacy.IsProtectedVE)
        {
            _viewModel.ConnectionStatus = "Privacidad bloqueó la transferencia de archivos.";
            return;
        }

        string? serial = _activeVisionSerialVE ?? _viewModel.Device?.Serial?.Trim();
        if (string.IsNullOrWhiteSpace(serial))
        {
            _viewModel.ConnectionStatus = "Conecta un teléfono antes de enviar archivos.";
            return;
        }

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            return;

        try
        {
            await QueueVisionExchangePathsAsync(serial, paths);
            _viewModel.ConnectionStatus = paths.Length == 1
                ? "Archivo agregado a la transferencia."
                : $"{paths.Length} elementos agregados a la transferencia.";
        }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus = $"Archivos NOVORA: {ex.Message}";
        }
    }

    private async void VisionPresentation_FilesDroppedVE(
        object? sender,
        VEExchangeFilesDroppedEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        if (
            e.CountVE == 0 ||
            _visionTransferExchangeVE is null ||
            _integrationRuntime is null)
        {
            return;
        }

        /*
         * NO consultamos:
         *
         * - adb devices
         * - NLServiceDeviceIdentity
         * - ViewModel
         * - timers
         * - polling
         *
         * Utilizamos el serial almacenado al iniciar
         * la sesión de VisionEngine.
         */
        string? serial =
            _activeVisionSerialVE;

        if (string.IsNullOrWhiteSpace(serial))
        {
            _viewModel.ConnectionStatus =
                "VisionEngine: no hay dispositivo activo para transferir.";

            return;
        }

        if (
            _visionEngineVE is null ||
            _integrationRuntime is null ||
            !_visionEngineVE.IsRunningVE)
        {
            _viewModel.ConnectionStatus =
                "VisionEngine debe estar activo para transferir archivos.";

            return;
        }

        if (_integrationRuntime.Privacy.IsProtectedVE)
        {
            _viewModel.ConnectionStatus =
                "PrivacyVE: transferencia bloqueada mientras el contenido está protegido.";
            return;
        }

        try
        {
            _viewModel.ConnectionStatus =
                e.CountVE == 1
                    ? "VisionEngine: archivo recibido para transferencia."
                    : $"VisionEngine: {e.CountVE} elementos recibidos para transferencia.";

            /*
             * QueueAsync únicamente introduce la solicitud
             * en la cola.
             *
             * El adb push NO ocurre en el Dispatcher WPF.
             */
            await QueueVisionExchangePathsAsync(serial, e.PathsVE);
        }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus =
                $"ExchangeVE: {ex.Message}";
        }
    }

    private void VisionPresentation_DragEnteredVE(
        object? sender,
        EventArgs e)
    {
        if (_closing)
        {
            return;
        }

        _viewModel.ConnectionStatus =
            "VisionEngine: suelta para enviar a /sdcard/NOVORA/";
    }

    private void VisionPresentation_DragEndedVE(
        object? sender,
        EventArgs e)
    {
        /*
         * No hacemos RefreshPerformance ni ninguna
         * consulta ADB aquí.
         *
         * Es un evento puramente visual.
         */
    }

    // ============================================================
    // TRANSFER EVENTS
    // ============================================================

    private void VisionTransfer_StartedVE(
        object? sender,
        VEExchangeTransferStartedEventArgs e)
    {
        _ =
            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        if (_closing)
                        {
                            return;
                        }

                        string name =
                            Path.GetFileName(
                                e.LocalPathVE);

                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name =
                                e.LocalPathVE;
                        }

                        _viewModel.ConnectionStatus =
                            $"Enviando {name} → /sdcard/NOVORA/";
                    }));
    }

    private void VisionTransfer_CompletedVE(
        object? sender,
        VEExchangeTransferCompletedEventArgs e)
    {
        _ =
            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        if (_closing)
                        {
                            return;
                        }

                        string name =
                            Path.GetFileName(
                                e.LocalPathVE);

                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name =
                                e.LocalPathVE;
                        }

                        double seconds =
                            Math.Max(
                                0.001,
                                e.ElapsedVE.TotalSeconds);

                        RecordSentFilesVE(
                            e.LocalPathVE);

                        _viewModel.ConnectionStatus =
                            $"{name} enviado a /sdcard/NOVORA/ en {seconds:0.00}s";
                    }));
    }

    private void VisionTransfer_FailedVE(
        object? sender,
        VEExchangeTransferFailedEventArgs e)
    {
        _ =
            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        if (_closing)
                        {
                            return;
                        }

                        string name =
                            Path.GetFileName(
                                e.LocalPathVE);

                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name =
                                e.LocalPathVE;
                        }

                        _viewModel.ConnectionStatus =
                            $"Error enviando {name}: {e.ErrorVE}";
                    }));
    }

    private void RecordSentFilesVE(
        string path)
    {
        (long count, long bytes) =
            GetFileTransferStatsVE(
                path);

        if (count <= 0)
        {
            return;
        }

        _integrationFilesSentCountVE +=
            count;

        _integrationFilesSentBytesVE +=
            bytes;

        UpdateFileTransferSummaryVE();
    }

    private void RecordReceivedFilesVE(
        long count,
        long bytes)
    {
        if (count <= 0)
        {
            return;
        }

        _integrationFilesReceivedCountVE +=
            count;

        _integrationFilesReceivedBytesVE +=
            Math.Max(
                0,
                bytes);

        UpdateFileTransferSummaryVE();
    }

    private void UpdateFileTransferSummaryVE()
    {
        IntegrationFilesSentText14.Text =
            FormatFileTransferSummaryVE(
                _integrationFilesSentCountVE,
                _integrationFilesSentBytesVE);

        IntegrationFilesReceivedText14.Text =
            FormatFileTransferSummaryVE(
                _integrationFilesReceivedCountVE,
                _integrationFilesReceivedBytesVE);
    }

    private static (long Count, long Bytes) GetFileTransferStatsVE(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return (
                    1,
                    new FileInfo(path).Length);
            }

            if (!Directory.Exists(path))
            {
                return (0, 0);
            }

            long count =
                0;

            long bytes =
                0;

            foreach (string file in Directory.EnumerateFiles(
                         path,
                         "*",
                         SearchOption.AllDirectories))
            {
                count++;
                bytes +=
                    new FileInfo(file).Length;
            }

            return (count, bytes);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }

    private static string FormatFileTransferSummaryVE(
        long count,
        long bytes)
    {
        string files =
            count == 1
                ? "1 archivo"
                : $"{count:N0} archivos";

        string size =
            bytes switch
            {
                < 1024 =>
                    $"{bytes:N0} B",

                < 1024L * 1024L =>
                    $"{bytes / 1024d:0.0} KB",

                < 1024L * 1024L * 1024L =>
                    $"{bytes / (1024d * 1024d):0.0} MB",

                _ =>
                    $"{bytes / (1024d * 1024d * 1024d):0.0} GB"
            };

        return
            $"{files} · {size}";
    }

    // ============================================================
    // PRESENTATION CLOSE REQUEST
    // ============================================================

    // ============================================================
    // EXCHANGEVE CTRL+V
    // ============================================================

    private async void VisionExchange_KeyDownVE(
        object? sender,
        System.Windows.Forms.KeyEventArgs e)
    {
        if (!e.Control || e.KeyCode is not (
                System.Windows.Forms.Keys.C or System.Windows.Forms.Keys.V))
        {
            return;
        }

        /*
         * Ctrl+V pertenece a ExchangeVE mientras la
         * presentación VisionEngine esté activa.
         *
         * Evitamos mandar además la misma combinación
         * al control Android.
         */
        e.Handled =
            true;

        e.SuppressKeyPress =
            true;

        if (e.KeyCode == System.Windows.Forms.Keys.C)
        {
            if (_visionEngineVE is null || !_visionEngineVE.IsRunningVE)
                return;
            try
            {
                await _integrationRuntime!.Clipboard.GetTextAsync(
                    NOVORA.VisionEngine.Control.VEControlCopy.Copy,
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                _viewModel.ConnectionStatus = $"Clipboard Android → Windows: {ex.Message}";
            }
            return;
        }

        if (
            _closing ||
            _visionTransferExchangeVE is null)
        {
            return;
        }

        string? serial =
            _activeVisionSerialVE;

        if (string.IsNullOrWhiteSpace(serial))
        {
            _viewModel.ConnectionStatus =
                "ExchangeVE: no hay dispositivo VisionEngine activo.";

            return;
        }

        if (
            _visionEngineVE is null ||
            _integrationRuntime is null ||
            !_visionEngineVE.IsRunningVE)
        {
            _viewModel.ConnectionStatus =
                "ExchangeVE requiere VisionEngine activo.";

            return;
        }

        if (_integrationRuntime.Privacy.IsProtectedVE)
        {
            _viewModel.ConnectionStatus =
                "PrivacyVE: portapapeles bloqueado mientras el contenido está protegido.";
            return;
        }

        try
        {
            IReadOnlyList<VEExchangeRequest> requests = VEExchangeClipboard.ReadVE();
            var paths = new List<string>();
            int count = 0;
            foreach (VEExchangeRequest request in requests)
            {
                switch (request.KindVE)
                {
                    case VEExchangeKind.Text:
                    case VEExchangeKind.Url:
                        await _integrationRuntime.Clipboard.SetTextAsync(request.ValueVE, paste: true);
                        count++;
                        break;
                    case VEExchangeKind.File:
                    case VEExchangeKind.Directory:
                    case VEExchangeKind.Apk:
                        paths.Add(request.ValueVE);
                        count++;
                        break;
                }
            }

            if (paths.Count > 0)
                await QueueVisionExchangePathsAsync(serial, paths);

            if (count == 0)
            {
                _viewModel.ConnectionStatus =
                    "ExchangeVE: el portapapeles no contiene contenido compatible.";

                return;
            }

            _viewModel.ConnectionStatus =
                count == 1
                    ? "ExchangeVE: contenido del portapapeles enviado."
                    : $"ExchangeVE: {count} elementos del portapapeles procesados.";
        }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus =
                $"ExchangeVE: {ex.Message}";
        }
    }

    private async void ReceiveNovoraFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (
            _closing ||
            _integrationRuntime is null)
        {
            _viewModel.ConnectionStatus =
                "Archivos NOVORA no está disponible.";

            return;
        }

        string? serial =
            _activeVisionSerialVE ?? _viewModel.Device?.Serial?.Trim();

        if (string.IsNullOrWhiteSpace(serial))
        {
            _viewModel.ConnectionStatus =
                "ExchangeVE: no hay dispositivo Android activo.";

            return;
        }

        if (_integrationRuntime.Privacy.IsProtectedVE)
        {
            _viewModel.ConnectionStatus =
                "PrivacyVE bloqueó la recepción de archivos mientras el contenido está protegido.";

            return;
        }

        string destination = NOVORA.Control.NLControlFileStorage.PcRoot;

        _viewModel.ConnectionStatus =
            "ExchangeVE: recibiendo /sdcard/NOVORA desde Android...";

        try
        {
            VEExchangeResult result =
                await _integrationRuntime
                    .Files
                    .PullNovoraFolderAsync(
                        serial,
                        destination);

            if (result.Success)
            {
                RecordReceivedFilesVE(
                    result.ItemCount,
                    result.Bytes);
            }

            _viewModel.ConnectionStatus =
                result.Success
                    ? $"ExchangeVE: carpeta NOVORA recibida en {result.Destination}."
                    : $"ExchangeVE: {result.Message} {result.Detail}".Trim();
        }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus =
                $"ExchangeVE: {ex.Message}";
        }
    }
    private async void VisionPresentation_CloseRequestedVE(
        object? sender,
        EventArgs e)
    {
        if (
            _closingPresentationVE ||
            _closing)
        {
            return;
        }

        _closingPresentationVE =
            true;

        try
        {
            if (
                _visionEngineVE is not null &&
                _visionEngineVE.IsRunningVE)
            {
                if (_veLanSessionVE is not null ||
                    _visionEngineVE.RuntimeVE.ExternalSourceKindVE == VEExternalSourceKind.NativeLan)
                {
                    await StopVeLanVideoAsync();
                }
                else
                {
                    VECoreResult stop = await _visionEngineVE.StopAsync();
                    if (!stop.Success)
                        _viewModel.ConnectionStatus = stop.Message;
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
            _activeVisionSerialVE =
                null;

            CloseVisionPresentationVE(
                restoreMainWindow: true,
                refreshInformation: true);

            UpdateRuntimeButtons();

            _closingPresentationVE =
                false;
        }
    }

    private void VisionPresentation_ExitFullscreenRequestedVE(
        object? sender,
        EventArgs e)
    {
        if (_closing || _closingPresentationVE ||
            !ReferenceEquals(sender, _visionPresentationWindowVE))
            return;

        _viewModel.VideoPresentationMode =
            ViewModel.NLViewModelMain.VideoPresentationModeMaximized;
        SaveSettingsFromViewModel14();
        CreateVisionPresentationVE();
        AndroidEngineStateChanged();
    }

    // ============================================================
    // CLOSE PRESENTATION
    // ============================================================

    private void CloseVisionPresentationVE(
        bool restoreMainWindow,
        bool refreshInformation)
    {
        VERendererWindow? presentation =
            _visionPresentationWindowVE;

        _visionPresentationWindowVE =
            null;

        DetachVisionInputVE();

        if (presentation is not null)
        {
            presentation.HostVE.InputFocusGainedVE -=
                VisionRendererHost_InputFocusGainedVE;

            presentation.HostVE.KeyDownVE -=
                VisionExchange_KeyDownVE;

            presentation.CloseRequestedVE -=
                VisionPresentation_CloseRequestedVE;

            presentation.ExitFullscreenRequestedVE -=
                VisionPresentation_ExitFullscreenRequestedVE;

            presentation.FilesDroppedVE -=
                VisionPresentation_FilesDroppedVE;

            presentation.DragEnteredVE -=
                VisionPresentation_DragEnteredVE;

            presentation.DragEndedVE -=
                VisionPresentation_DragEndedVE;

            try
            {
                presentation.CloseFromOwnerVE();
            }
            catch
            {
            }
        }

        if (
            _visionRendererHostVE is not null &&
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

        if (
            !restoreMainWindow ||
            _closing)
        {
            return;
        }

        if (
            WindowState ==
            WindowState.Minimized)
        {
            WindowState =
                WindowState.Normal;
        }

        Show();
        Activate();

    }

    // ============================================================
    // TOGGLE VISIONENGINE
    // ============================================================

    private bool _visionCommandApplyingVE;

    private Task ToggleVisionEngineVEAsync()
    {
        if (IsVisionEngineRunningVE())
            return SetVisionEngineRunningVEAsync(false);
        if (_pcVideoAuthorizationRequested)
        {
            ClearPcVideoAuthorizationRequest();
            _viewModel.ConnectionStatus = "Solicitud de VisionEngine cancelada.";
            return Task.CompletedTask;
        }
        return RequestAndroidVideoFromPcAsync();
    }

    private async Task SetVisionEngineRunningVEAsync(bool running, Func<bool>? authorization = null)
    {
        if (_visionCommandApplyingVE)
            throw new InvalidOperationException("VisionEngine está cambiando de estado.");
        _visionCommandApplyingVE = true;
        _viewModel.ConnectionStatus = running
            ? "Iniciando VisionEngine…"
            : "Deteniendo VisionEngine…";
        UpdateRuntimeButtons();
        AndroidEngineStateChanged();
        try
        {
            await SetVisionEngineRunningCoreVEAsync(running, authorization);
            _viewModel.ConnectionStatus = running
                ? "VisionEngine activo."
                : "VisionEngine detenido.";
        }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus = ex.Message;
            throw;
        }
        finally
        {
            _visionCommandApplyingVE = false;
            UpdateRuntimeButtons();
            AndroidEngineStateChanged();
        }
    }

    private async Task EnsureExInStandaloneAsync()
    {
        if (_exInControlSession is null || _exInEngine is null) return;
        await _exInConnectionGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var device = _viewModel.Device;
            string? serial = ResolveExInSerialVE(
                device.Connected ? device.Serial : null,
                AndroidControlAuthorized ? _androidControlSerial : null,
                _activeVisionSerialVE);
            bool shouldRun = !_closing && _viewModel.ExInEnabled &&
                !string.IsNullOrWhiteSpace(serial);
            if (!shouldRun)
            {
                await _exInControlSession.StopAsync().ConfigureAwait(true);
                return;
            }
            await _exInInitialization.ConfigureAwait(true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _exInControlSession.StartAsync(serial!, timeout.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _viewModel.ConnectionStatus = "ExInEngine no completó la sesión de control en 20 segundos.";
        }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus = "ExInEngine: " + ex.Message;
        }
        finally
        {
            _exInConnectionGate.Release();
            AndroidEngineStateChanged();
        }
    }

    internal static string? ResolveExInSerialVE(
        string? connectedDeviceSerial,
        string? authorizedUsbSerial,
        string? activeVisionSerial)
        => new[] { connectedDeviceSerial, authorizedUsbSerial, activeVisionSerial }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?
            .Trim();

    internal static string ResolveVisionStartSerialVE(
        string selectedSerial,
        string? preparedUsbSerial,
        string? preparedLanSerial,
        bool linkUsesSelectedUsb,
        bool preparedLanOnline)
    {
        string selected = selectedSerial.Trim();
        if (!linkUsesSelectedUsb || selected.Contains(':') || !preparedLanOnline ||
            string.IsNullOrWhiteSpace(preparedLanSerial) || !preparedLanSerial.Contains(':') ||
            !string.Equals(selected, preparedUsbSerial?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return selected;
        }

        return preparedLanSerial.Trim();
    }

    internal static string? PreserveVisionFailoverRouteVE(
        string? currentRoute,
        string? capturedRoute)
        => string.IsNullOrWhiteSpace(currentRoute)
            ? capturedRoute?.Trim()
            : currentRoute.Trim();

    private async Task PreserveVisionFailoverDuringRestartVEAsync(Func<Task> restartStream)
    {
        ArgumentNullException.ThrowIfNull(restartStream);
        string? capturedUsb = _visionUsbSerialVE;
        string? capturedLan = _visionLanSerialVE;

        try
        {
            await restartStream().ConfigureAwait(true);
        }
        finally
        {
            _visionUsbSerialVE = PreserveVisionFailoverRouteVE(_visionUsbSerialVE, capturedUsb);
            _visionLanSerialVE = PreserveVisionFailoverRouteVE(_visionLanSerialVE, capturedLan);
        }
    }

    private async Task<string> ResolveVisionStartSerialVEAsync(string selectedSerial)
    {
        string selected = selectedSerial.Trim();
        bool linkUsesSelectedUsb = _linkEngineRuntimeLE?.IsRunningLE == true &&
            string.Equals(_linkEngineRuntimeLE.SessionLE?.Serial, selected, StringComparison.OrdinalIgnoreCase);
        bool preparedLanOnline = false;

        if (linkUsesSelectedUsb &&
            string.Equals(_visionUsbSerialVE, selected, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_visionLanSerialVE))
        {
            try
            {
                preparedLanOnline = await _adb
                    .IsDeviceOnlineAsync(_visionLanSerialVE)
                    .ConfigureAwait(true);
            }
            catch
            {
                // LAN es una optimización; USB sigue siendo el fallback de VE.
            }
        }

        return ResolveVisionStartSerialVE(
            selected,
            _visionUsbSerialVE,
            _visionLanSerialVE,
            linkUsesSelectedUsb,
            preparedLanOnline);
    }

    private async Task SetVisionEngineRunningCoreVEAsync(bool running, Func<bool>? authorization)
    {
        if (_closing || authorization?.Invoke() == false)
            throw new InvalidOperationException("Sesión cerrada o dispositivo cambiado.");
        if (running == IsVisionEngineRunningVE()) return;
        InitializeVisionEngineRuntimeVE();

        if (running && !_visionEngineVE!.IsInitializedVE)
        {
            VECoreResult initialize =
                await _visionEngineVE
                    .InitializeAsync();

            if (!initialize.Success)
            {
                throw initialize.Exception ??
                      new InvalidOperationException(
                          initialize.Message);
            }
        }

        if (_closing || authorization?.Invoke() == false)
            throw new InvalidOperationException("Sesión cerrada o dispositivo cambiado.");

        // ========================================================
        // STOP
        // ========================================================

        if (!running)
        {
            if (_veLanSessionVE is not null ||
                _visionEngineVE!.RuntimeVE.ExternalSourceKindVE == VEExternalSourceKind.NativeLan)
            {
                await StopVeLanVideoAsync();
                await EnsureExInStandaloneAsync();
                return;
            }

            VECoreResult stop =
                await _visionEngineVE!
                    .StopAsync();

            _activeVisionSerialVE =
                null;

            CloseVisionPresentationVE(
                restoreMainWindow: true,
                refreshInformation: true);

            if (!stop.Success)
            {
                throw stop.Exception ??
                      new InvalidOperationException(
                          stop.Message);
            }

            await EnsureExInStandaloneAsync();

            return;
        }

        await RequestAndroidVideoFromPcAsync();
    }

    // ============================================================
    // RUNNING
    // ============================================================

    private bool IsVisionEngineRunningVE()
    {
        return
            _visionEngineVE?.IsRunningVE ==
            true;
    }

    // ============================================================
    // BITRATE
    // ============================================================

    private static int ParseVideoBitrateVE(
        string? value)
    {
        string normalized =
            Service.NLServiceBitrate.Normalize(
                value);

        if (!normalized.EndsWith(
                "M",
                StringComparison.OrdinalIgnoreCase))
        {
            return
                10_000_000;
        }

        string number =
            normalized[..^1];

        if (
            !double.TryParse(
                number,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double mbps) ||
            mbps <= 0)
        {
            return
                10_000_000;
        }

        return
            checked(
                (int)Math.Round(
                    mbps * 1_000_000d,
                    MidpointRounding.AwayFromZero));
    }

    // ============================================================
    // STATUS
    // ============================================================

    private void VisionEngine_StatusChangedVE(
        object? sender,
        VECoreStatus status)
    {

        bool stateChanged =
            _lastVisionStateVE !=
            status.State;

        bool rendererChanged =
            _lastRendererEnabledVE !=
            status.RendererEnabled;

        if (stateChanged || rendererChanged || ShouldPublishAndroidVisionTelemetry(status))
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                AndroidEngineStateChanged();
            }));

        _lastVisionStateVE =
            status.State;

        _lastRendererEnabledVE =
            status.RendererEnabled;

        if (
            !stateChanged &&
            !rendererChanged &&
            status.State !=
                VECoreStates.Failed)
        {
            return;
        }

        _ =
            Dispatcher.BeginInvoke(
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
                            case VECoreStates.Failed:

                                if (TryStartVisionRecoveryVE(
                                        status))
                                {
                                    return;
                                }

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

                            case VECoreStates.Running:

                                SetVisionEngineStatus14(
                                    status.RendererEnabled
                                        ? "OK"
                                        : "INICIANDO",
                                    failed: false);

                                break;

                            case VECoreStates.Starting:
                            case VECoreStates.Initializing:

                                SetVisionEngineStatus14(
                                    "INICIANDO",
                                    failed: false);

                                break;

                            case VECoreStates.Stopping:

                                SetVisionEngineStatus14(
                                    "DETENIENDO",
                                    failed: false);

                                break;

                            case VECoreStates.Stopped:
                            case VECoreStates.Ready:

                                if (_externalVisionWaitingForDeviceVE)
                                {
                                    SetVisionEngineStatus14(
                                        "ESPERANDO ANDROID",
                                        failed: false);
                                    break;
                                }

                                _activeVisionSerialVE =
                                    null;

                                SetVisionEngineStatus14(
                                    "OK",
                                    failed: false);

                                if (
                                    _visionPresentationWindowVE
                                    is not null)
                                {
                                    CloseVisionPresentationVE(
                                        restoreMainWindow: true,
                                        refreshInformation: true);
                                }

                                break;
                        }
                    }));
    }

    private bool TryStartVisionRecoveryVE(
        VECoreStatus status)
    {
        bool nativeLan = _veLanSessionVE is not null ||
            _visionEngineVE?.RuntimeVE.ExternalSourceKindVE == VEExternalSourceKind.NativeLan ||
            _externalVisionWaitingForDeviceVE &&
                _externalVisionWaitingSourceVE == VEExternalSourceKind.NativeLan;
        if (nativeLan)
        {
            _ = StopVeLanVideoAsync(
                "Android se desconectó. VisionEngine LAN se cerró.");
            return false;
        }

        if (TrySuspendExternalVisionForDeviceReconnectVE(status))
            return true;

        if (_visionEngineVE?.RuntimeVE.IsAppControlVideoActiveVE == true)
        {
            _activeVisionSerialVE = null;
            SetVisionEngineStatus14(
                "ERROR",
                failed: true);
            _viewModel.ConnectionStatus =
                $"VisionEngine AppControl se detuvo: {status.LastError ?? status.Message}. Autoriza una sesión nueva desde Android.";
            _ = StopAppControlVideoSourceAsync();
            return false;
        }

        _activeVisionSerialVE = null;
        _viewModel.ConnectionStatus =
            "La captura terminó. Inicia una sesión nueva desde App Control.";
        return false;
    }

    private bool TrySuspendExternalVisionForDeviceReconnectVE(VECoreStatus status)
    {
        if (_closing) return false;
        if (_externalVisionWaitingForDeviceVE) return true;

        VEExternalSourceKind source = _appControlVideoTransportVE is not null
            ? VEExternalSourceKind.UsbAppControl
            : VEExternalSourceKind.None;
        if (source == VEExternalSourceKind.None) return false;

        _externalVisionWaitingForDeviceVE = true;
        _externalVisionWaitingSourceVE = source;
        SetVisionEngineStatus14("ESPERANDO ANDROID", failed: false);
        _viewModel.ConnectionStatus = source == VEExternalSourceKind.NativeLan
            ? "Android dejó de transmitir por LAN. VisionEngine permanece abierto y espera su reconexión."
            : "Android dejó de transmitir por USB. VisionEngine permanece abierto y espera su reconexión.";
        _ = SuspendExternalVisionForDeviceReconnectAsync(source, status);
        return true;
    }

    private async Task SuspendExternalVisionForDeviceReconnectAsync(
        VEExternalSourceKind source,
        VECoreStatus status)
    {
        try
        {
            if (_visionEngineVE?.IsRunningVE == true)
                await _visionEngineVE.StopAsync(preserveRendererVE: true);

            if (source == VEExternalSourceKind.NativeLan)
            {
                await DisposeVeLanTransportAsync();
                SetVeLanPhase("WaitingForAndroid",
                    "Android se desconectó. VisionEngine permanece abierto y espera la reconexión LAN.");
            }
            else
            {
                await DisposeAppControlVideoTransportAsync();
            }
        }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus =
                "VisionEngine espera Android, pero no pudo limpiar el flujo anterior: " + ex.Message;
        }
        finally
        {
            if (!_closing && _externalVisionWaitingForDeviceVE)
            {
                SetVisionEngineStatus14("ESPERANDO ANDROID", failed: false);
                AndroidEngineStateChanged();
                UpdateRuntimeButtons();
            }
        }
    }

    private void ClearExternalVisionWaitingForDeviceVE()
    {
        _externalVisionWaitingForDeviceVE = false;
        _externalVisionWaitingSourceVE = VEExternalSourceKind.None;
    }

    private void RequestWaitingExternalVisionFromAndroid()
    {
        if (!_externalVisionWaitingForDeviceVE || _closing) return;
        try { _ = RequestAndroidVideoFromPcAsync(); }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus =
                "VisionEngine sigue esperando Android: " + ex.Message;
        }
    }

    // ============================================================
    // STATUS UI
    // ============================================================

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

    // ============================================================
    // SHUTDOWN
    // ============================================================

    private async Task ShutdownVisionEngineRuntimeVEAsync()
    {
        /*
         * Cerramos ExchangeVE incluso si VECoreEngine
         * todavía no fue creado correctamente.
         */
        VEExchangeTransfer? transfer =
            _visionTransferExchangeVE;

        _visionTransferExchangeVE =
            null;

        _activeVisionSerialVE =
            null;

        if (_visionEngineVE is null)
        {
            CloseVisionPresentationVE(
                restoreMainWindow: false,
                refreshInformation: false);

            if (_integrationRuntime is not null)
            {
                _integrationRuntime.Clipboard.ClipboardChangedVE -=
                    VisionClipboard_ChangedVE;
                _integrationRuntime.Capabilities.StatusChangedVE -=
                    ShellIntegration_StatusChangedVE;
                if (transfer is not null)
                {
                    transfer.TransferStartedVE -= VisionTransfer_StartedVE;
                    transfer.TransferCompletedVE -= VisionTransfer_CompletedVE;
                    transfer.TransferFailedVE -= VisionTransfer_FailedVE;
                }
                await _integrationRuntime.DisposeAsync();
                _integrationRuntime = null;
            }

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

            if (_integrationRuntime is not null)
            {
                _integrationRuntime.Clipboard.ClipboardChangedVE -=
                    VisionClipboard_ChangedVE;
                _integrationRuntime.Capabilities.StatusChangedVE -=
                    ShellIntegration_StatusChangedVE;
                await _integrationRuntime.DisposeAsync();
                _integrationRuntime = null;
            }

            _visionEngineVE.StatusChangedVE -=
                VisionEngine_StatusChangedVE;
            _visionEngineVE.RuntimeVE.PrivacyVE.StatusChangedVE -= ShellPrivacy_StatusChangedVE;
            _visionEngineVE.RuntimeVE.NvidiaVE.StatusChangedVE -= ShellNvidia_StatusChangedVE;

            await _visionEngineVE
                .DisposeAsync();

            _visionEngineVE =
                null;

            _visionRendererHostVE =
                null;

            _visionInputRouterVE =
                null;

            if (transfer is not null)
            {
                transfer.TransferStartedVE -= VisionTransfer_StartedVE;
                transfer.TransferCompletedVE -= VisionTransfer_CompletedVE;
                transfer.TransferFailedVE -= VisionTransfer_FailedVE;
            }
        }
    }
}

