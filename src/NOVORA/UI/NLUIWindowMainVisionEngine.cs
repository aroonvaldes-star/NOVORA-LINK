using NOVORA.VisionEngine.Control;
using NOVORA.ExInEngine;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Exchange;
using NOVORA.VisionEngine.Integration;
using NOVORA.NVIDIA;
using NOVORA.VisionEngine.Renderer;
using NOVORA.VisionEngine.Server;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private VECoreEngine? _visionEngineVE;
    private ExInCoreEngine? _exInEngine;
    private ExInControlSession? _exInControlSession;
    private Task _exInInitialization = Task.CompletedTask;
    private readonly SemaphoreSlim _exInConnectionGate = new(1, 1);
    private readonly SemaphoreSlim _exInPresentationSyncGateVE = new(1, 1);
    private VERendererHost? _visionRendererHostVE;
    private VERendererWindow? _visionPresentationWindowVE;
    private VEControlRouter? _visionInputRouterVE;
    private VEExchangeTransfer? _visionTransferExchangeVE;

    private VECoreStates? _lastVisionStateVE;

    private string? _activeVisionSerialVE;
    private string? _visionUsbSerialVE;
    private string? _visionLanSerialVE;

    private bool _lastRendererEnabledVE;
    private bool _closingPresentationVE;
    private bool _visionAudioOutputWatcherAttachedVE;
    private bool _visionRecoveryRunningVE;
    private int _visionRecoveryAttemptsVE;

    private const int MaxVisionRecoveryAttemptsVE =
        2;

    private async Task PrepareVisionLanFallbackVEAsync()
    {
        if (_closing || IsVisionEngineRunningVE())
        {
            return;
        }

        string? usbSerial =
            _viewModel.Device?.Serial?.Trim();

        if (string.IsNullOrWhiteSpace(usbSerial) ||
            usbSerial.Contains(':', StringComparison.Ordinal))
        {
            ShowTopMessage14(
                "Conecta el teléfono por USB para preparar el failover LAN.",
                NLUIMessageKind14.Warning);
            return;
        }

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
        _exInEngine.Manager.StatusChangedVE -= ShellGamepad_StatusChangedVE;
        _exInEngine.Manager.StatusChangedVE += ShellGamepad_StatusChangedVE;
        _exInEngine.Manager.BatteryAlertVE -= ShellGamepad_BatteryAlertVE;
        _exInEngine.Manager.BatteryAlertVE += ShellGamepad_BatteryAlertVE;
        _exInInitialization = _exInEngine.InitializeAsync();
        _exInControlSession ??= new ExInControlSession(_exInEngine, _paths, _adb);
        _visionEngineVE =
            new VECoreEngine(
                _paths,
                _adb);

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
        _visionEngineVE.RuntimeVE.ClipboardVE.ClipboardChangedVE +=
            VisionClipboard_ChangedVE;

        _visionEngineVE.RuntimeVE.AudioVE.SelectedOutputVE =
            _viewModel.SelectedAudioOutput;

        ApplyAdvancedVisionSettingsVE();

        /*
         * ExchangeVE se crea una sola vez junto con
         * VisionEngine.
         *
         * No genera polling.
         * Su worker permanece esperando trabajo en la cola.
         */
        _visionTransferExchangeVE =
            new VEExchangeTransfer(
                () =>
                    (_visionEngineVE?
                        .RuntimeVE
                        .PrivacyVE
                        .CanExchangeFilesVE ?? false) &&
                    (_visionEngineVE?
                        .RuntimeVE
                        .IntegrationVE
                        .StatusVE
                        .Capabilities
                        .FileTransfer ?? false) &&
                    (_visionEngineVE?
                        .RuntimeVE
                        .IntegrationVE
                        .StatusVE
                        .Capabilities
                        .DragDrop ?? false));

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
        if (_visionEngineVE is null)
        {
            return;
        }

        VECoreRuntime runtime =
            _visionEngineVE.RuntimeVE;

        _exInEngine?.SetPrivacyProtected(_viewModel.PrivacyShieldEnabled);
        runtime.PrivacyVE.SetManualShieldVE(
            _viewModel.PrivacyShieldEnabled);

        runtime.IntegrationVE.SetCapabilitiesVE(
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
                _viewModel.NvidiaProfile,
                ignoreCase: true,
                out NLNVIDIAProfile profile) || !Enum.IsDefined(profile))
        {
            profile =
                NLNVIDIAProfile.Competitive;

            _viewModel.NvidiaProfile =
                NLNVIDIAProfile.Competitive.ToString();
        }

        runtime.NvidiaVE.SetProfileVE(
            profile);

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

        string nvidia =
            $"NVIDIA {_viewModel.NvidiaProfile}";

        EngineFeatureStatus14.Text =
            $"VE · {privacy} · {integration} · {gamepad} · {nvidia}";

        EngineFeatureStatus14.ToolTip =
            "Estado de funciones avanzadas de VisionEngine. " +
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
            !_visionEngineVE
                .RuntimeVE
                .IntegrationVE
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

        VERendererWindow presentation =
            new(
                monitor,
                fullscreen);

        presentation.CloseRequestedVE +=
            VisionPresentation_CloseRequestedVE;

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
            _visionTransferExchangeVE is null)
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
            !_visionEngineVE.IsRunningVE)
        {
            _viewModel.ConnectionStatus =
                "VisionEngine debe estar activo para transferir archivos.";

            return;
        }

        if (_visionEngineVE.RuntimeVE.PrivacyVE.IsProtectedVE)
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
            await _visionTransferExchangeVE
                .QueueAsync(
                    serial,
                    e.PathsVE);
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
        if (
            !e.Control ||
            e.KeyCode !=
                System.Windows.Forms.Keys.V)
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
            !_visionEngineVE.IsRunningVE)
        {
            _viewModel.ConnectionStatus =
                "ExchangeVE requiere VisionEngine activo.";

            return;
        }

        if (_visionEngineVE.RuntimeVE.PrivacyVE.IsProtectedVE)
        {
            _viewModel.ConnectionStatus =
                "PrivacyVE: portapapeles bloqueado mientras el contenido está protegido.";
            return;
        }

        try
        {
            int count =
                await VEExchangeManager
                    .PasteAsync(
                        serial,
                        _visionTransferExchangeVE);

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
            _visionEngineVE is null ||
            !_visionEngineVE.IsRunningVE)
        {
            _viewModel.ConnectionStatus =
                "ExchangeVE requiere VisionEngine activo para recibir desde Android.";

            return;
        }

        string? serial =
            _activeVisionSerialVE;

        if (string.IsNullOrWhiteSpace(serial))
        {
            _viewModel.ConnectionStatus =
                "ExchangeVE: no hay dispositivo Android activo.";

            return;
        }

        if (_visionEngineVE.RuntimeVE.PrivacyVE.IsProtectedVE)
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
                await _visionEngineVE
                    .RuntimeVE
                    .FilesVE
                    .PullNovoraFolderAsync(
                        serial,
                        destination);

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
                VECoreResult stop =
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

            presentation.CloseRequestedVE -=
                VisionPresentation_CloseRequestedVE;

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

        if (refreshInformation)
        {
            _ =
                RefreshPerformanceOnceAsync();
        }
    }

    // ============================================================
    // TOGGLE VISIONENGINE
    // ============================================================

    private bool _visionCommandApplyingVE;

    private Task ToggleVisionEngineVEAsync() =>
        SetVisionEngineRunningVEAsync(!IsVisionEngineRunningVE());

    private async Task SetVisionEngineRunningVEAsync(bool running, Func<bool>? authorization = null)
    {
        if (_visionCommandApplyingVE || _visionRecoveryRunningVE)
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

        // ========================================================
        // DEVICE
        // ========================================================

        var device =
            _viewModel.Device;

        if (
            device is null ||
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

        /*
         * Guardamos el serial UNA VEZ.
         *
         * Este será el serial utilizado por ExchangeVE
         * durante toda la sesión.
         *
         * Cero polling adicional.
         */
        _activeVisionSerialVE =
            device.Serial.Trim();

        if (!_activeVisionSerialVE.Contains(':'))
        {
            _visionUsbSerialVE =
                _activeVisionSerialVE;
        }

        UpdateOutputProfile();

        var profile =
            _viewModel.NLModelOutputProfile ??
            throw new InvalidOperationException(
                "No se pudo calcular el perfil de salida de VisionEngine.");

        _ =
            CreateVisionPresentationVE();

        bool audioEnabled =
            _viewModel.AudioEnabled &&
            !string.Equals(
                _viewModel.SelectedAudioOutput,
                VisionEngine.Audio.VEAudioOutput.DisabledValueVE,
                StringComparison.OrdinalIgnoreCase);

            VEServerOptions options =
            BuildVisionOptionsVE(
                profile,
                audioEnabled);

        try
        {
            using CancellationTokenSource startupTimeout =
                new(TimeSpan.FromSeconds(20));

            VECoreResult start =
                await _visionEngineVE!
                    .StartAsync(
                        _activeVisionSerialVE,
                        options,
                        startupTimeout.Token);

            if (!start.Success)
            {
                _activeVisionSerialVE =
                    null;

                CloseVisionPresentationVE(
                    restoreMainWindow: true,
                    refreshInformation: true);

                throw start.Exception ??
                      new InvalidOperationException(
                          start.Message);
            }

            if (_closing || authorization?.Invoke() == false)
            {
                await _visionEngineVE.StopAsync();
                throw new InvalidOperationException("Inicio cancelado: la sesión o el dispositivo cambió.");
            }

            AttachVisionInputVE();

            _visionRecoveryAttemptsVE =
                0;

            _visionPresentationWindowVE?
                .HostVE
                .FocusInputVE();
        }
        catch (OperationCanceledException)
        {
            try
            {
                await _visionEngineVE!
                    .StopAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
            }

            _activeVisionSerialVE =
                null;

            CloseVisionPresentationVE(
                restoreMainWindow: true,
                refreshInformation: true);

            throw new TimeoutException(
                "VisionEngine no completó el arranque en 20 segundos. Se limpió la sesión USB; vuelve a iniciar una vez.");
        }
        catch
        {
            try
            {
                await _visionEngineVE!
                    .StopAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
            }

            _activeVisionSerialVE =
                null;

            CloseVisionPresentationVE(
                restoreMainWindow: true,
                refreshInformation: true);

            throw;
        }
    }

    private VEServerOptions BuildVisionOptionsVE(
        Model.NLModelOutputProfile profile,
        bool audioEnabled)
    {
        VEServerOptions options =
            VEServerOptions.CreateForProfileVE(
                _visionEngineVE!.RuntimeVE.PerformanceVE.ProfileVE)
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

        // El perfil de salida ya incorpora los límites del dispositivo.
        // No reemplazar silenciosamente los FPS/bitrate elegidos por 45 FPS/4M.
        options.ValidateVE();
        return options;
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
        RefreshSTEngineSnapshot14();

        bool stateChanged =
            _lastVisionStateVE !=
            status.State;

        bool rendererChanged =
            _lastRendererEnabledVE !=
            status.RendererEnabled;

        if (stateChanged || rendererChanged)
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                _androidControlRevision++;
                QueueAndroidControlSnapshot();
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

                        ApplySTEngineShell14();

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

                                if (_visionRecoveryRunningVE)
                                {
                                    SetVisionEngineStatus14(
                                        "RECUPERANDO",
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
        if (
            _visionRecoveryRunningVE ||
            _visionRecoveryAttemptsVE >=
                MaxVisionRecoveryAttemptsVE ||
            string.IsNullOrWhiteSpace(
                _activeVisionSerialVE) ||
            _visionEngineVE is null ||
            _closing)
        {
            _activeVisionSerialVE =
                null;

            return false;
        }

        _visionRecoveryRunningVE =
            true;

        _visionRecoveryAttemptsVE++;

        SetVisionEngineStatus14(
            "RECUPERANDO",
            failed: false);

        _viewModel.ConnectionStatus =
            $"VisionEngine recovery {_visionRecoveryAttemptsVE}/{MaxVisionRecoveryAttemptsVE}: {status.LastError ?? status.Message}";

        _ =
            RecoverVisionEngineVEAsync();

        return true;
    }

    private async Task RecoverVisionEngineVEAsync()
    {
        string? serial =
            _activeVisionSerialVE;

        try
        {
            if (
                string.IsNullOrWhiteSpace(serial) ||
                _visionEngineVE is null)
            {
                return;
            }

            string recoverySerial =
                await ResolveVisionRecoverySerialVEAsync(serial)
                    .ConfigureAwait(true);

            VECoreResult stop =
                await _visionEngineVE
                    .StopAsync(
                        preserveRendererVE: true)
                    .ConfigureAwait(true);

            if (!stop.Success)
            {
                throw stop.Exception ??
                      new InvalidOperationException(
                          stop.Message);
            }

            UpdateOutputProfile();

            var profile =
                _viewModel.NLModelOutputProfile ??
                throw new InvalidOperationException(
                    "No se pudo recalcular el perfil de salida para recovery.");

            bool audioEnabled =
                _viewModel.AudioEnabled &&
                !string.Equals(
                    _viewModel.SelectedAudioOutput,
                    VisionEngine.Audio.VEAudioOutput.DisabledValueVE,
                    StringComparison.OrdinalIgnoreCase);

            VEServerOptions options =
                BuildVisionOptionsVE(
                    profile,
                    audioEnabled);

            VECoreResult start =
                await _visionEngineVE
                    .StartAsync(
                        recoverySerial,
                        options)
                    .ConfigureAwait(true);

            if (!start.Success)
            {
                throw start.Exception ??
                      new InvalidOperationException(
                          start.Message);
            }

            AttachVisionInputVE();

            _activeVisionSerialVE =
                recoverySerial;

            _visionPresentationWindowVE?
                .HostVE
                .FocusInputVE();

            _viewModel.ConnectionStatus =
                string.Equals(
                    recoverySerial,
                    _visionLanSerialVE,
                    StringComparison.OrdinalIgnoreCase)
                    ? "VisionEngine continuó por LAN después de perder USB."
                    : "VisionEngine recovery completado.";
        }
        catch (Exception ex)
        {
            _activeVisionSerialVE =
                null;

            SetVisionEngineStatus14(
                "ERROR",
                failed: true);

            CloseVisionPresentationVE(
                restoreMainWindow: true,
                refreshInformation: true);

            _viewModel.ConnectionStatus =
                $"VisionEngine recovery falló: {ex.Message}";
        }
        finally
        {
            _visionRecoveryRunningVE =
                false;

            AndroidEngineStateChanged();
            UpdateRuntimeButtons();
        }
    }

    private async Task<string> ResolveVisionRecoverySerialVEAsync(
        string failedSerial)
    {
        List<string> candidates = [];

        void AddCandidate(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                !candidates.Contains(
                    value,
                    StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(value);
            }
        }

        bool failedOverLan =
            failedSerial.Contains(':');

        if (failedOverLan)
        {
            AddCandidate(_visionUsbSerialVE);
            AddCandidate(_visionLanSerialVE);
        }
        else
        {
            AddCandidate(_visionLanSerialVE);
            AddCandidate(_visionUsbSerialVE);
        }

        AddCandidate(failedSerial);

        using CancellationTokenSource deadline =
            new(TimeSpan.FromSeconds(5));

        foreach (string candidate in candidates)
        {
            try
            {
                if (await _adb
                        .IsDeviceOnlineAsync(
                            candidate,
                            deadline.Token)
                        .ConfigureAwait(true))
                {
                    return candidate;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Recovery prueba sólo las rutas conocidas del teléfono activo.
            }
        }

        throw new InvalidOperationException(
            "VisionEngine perdió USB y no existe una ruta LAN preparada y online para el mismo teléfono.");
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

            if (transfer is not null)
            {
                transfer.TransferStartedVE -=
                    VisionTransfer_StartedVE;

                transfer.TransferCompletedVE -=
                    VisionTransfer_CompletedVE;

                transfer.TransferFailedVE -=
                    VisionTransfer_FailedVE;

                await transfer
                    .DisposeAsync();
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

            _visionEngineVE.RuntimeVE.ClipboardVE.ClipboardChangedVE -=
                VisionClipboard_ChangedVE;

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
                transfer.TransferStartedVE -=
                    VisionTransfer_StartedVE;

                transfer.TransferCompletedVE -=
                    VisionTransfer_CompletedVE;

                transfer.TransferFailedVE -=
                    VisionTransfer_FailedVE;

                await transfer
                    .DisposeAsync();
            }
        }
    }
}

