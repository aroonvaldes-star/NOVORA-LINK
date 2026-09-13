using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Runtime;
using NOVORA.Models;
using NOVORA.Services;
using NOVORA.Remote;

namespace NOVORA;

public partial class MainWindow
{
    private static readonly TimeSpan RemoteLinkPrepareTimeoutNV =
        TimeSpan.FromSeconds(15);

    private ServerRemoteNV? _remoteServerNV;
    private string? _remoteSerialNV;
    private bool _remoteConfigurationRunningNV;

    private CancellationTokenSource? _remoteLinkStartCtsNV;
    private Task<ResultCoreLE>? _remoteLinkStartTaskNV;
    private string? _remoteLinkStartSerialNV;

    private async Task ConfigureRemoteAndroidForSelectedDeviceNVAsync()
    {
        if (_closing ||
            _remoteConfigurationRunningNV)
        {
            return;
        }

        _remoteConfigurationRunningNV =
            true;

        try
        {
            _remoteServerNV ??=
                new ServerRemoteNV(
                    HandleRemoteCommandNVAsync);

            string? serial =
                _viewModel.RemoteAndroidEnabled && _viewModel.Device is
                {
                    Connected: true
                } device &&
                !string.IsNullOrWhiteSpace(
                    device.Serial)
                    ? device.Serial.Trim()
                    : null;

            if (string.Equals(
                    _remoteSerialNV,
                    serial,
                    StringComparison.OrdinalIgnoreCase) &&
                (serial is null || _remoteServerNV.IsRunningNV))
            {
                return;
            }

            string? oldSerial =
                _remoteSerialNV;

            _remoteSerialNV =
                null;

            await _remoteServerNV.StopAsync().ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(
                    oldSerial))
            {
                try
                {
                    await _adb
                        .ExecuteRawAsync(
                            new[]
                            {
                                "-s",
                                oldSerial,
                                "reverse",
                                "--remove",
                                $"tcp:{ProtocolRemoteNV.DefaultDevicePortNV}"
                            })
                        .ConfigureAwait(true);
                }
                catch
                {
                }

                try
                {
                    await _adb
                        .ExecuteRawAsync(
                            new[]
                            {
                                "-s",
                                oldSerial,
                                "reverse",
                                "--remove",
                                $"tcp:{ProtocolRemoteNV.DefaultFileTransferPortNV}"
                            })
                        .ConfigureAwait(true);
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(
                    serial))
            {
                return;
            }

            await _adb
                .StartServerAsync()
                .ConfigureAwait(true);

            await _remoteServerNV.StartAsync().ConfigureAwait(true);

            await _adb
                .ExecuteRawAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "reverse",
                        $"tcp:{ProtocolRemoteNV.DefaultDevicePortNV}",
                        $"tcp:{ProtocolRemoteNV.DefaultDevicePortNV}"
                    })
                .ConfigureAwait(true);

            await _adb
                .ExecuteRawAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "reverse",
                        $"tcp:{ProtocolRemoteNV.DefaultFileTransferPortNV}",
                        $"tcp:{ProtocolRemoteNV.DefaultFileTransferPortNV}"
                    })
                .ConfigureAwait(true);

            string reverseList =
                await _adb
                    .ExecuteRawAsync(
                        new[]
                        {
                            "-s",
                            serial,
                            "reverse",
                            "--list"
                        })
                    .ConfigureAwait(true);

            string expected =
                $"tcp:{ProtocolRemoteNV.DefaultDevicePortNV}";

            string expectedFileTransfer =
                $"tcp:{ProtocolRemoteNV.DefaultFileTransferPortNV}";

            if (!reverseList.Contains(
                    expected,
                    StringComparison.OrdinalIgnoreCase) ||
                !reverseList.Contains(
                    expectedFileTransfer,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "NOVORA no pudo verificar los canales remotos Android tcp:27182/tcp:27186.");
            }

            /*
             * DiscoveryEngine USB: el PC ya confirmó adb reverse 27182.
             * Ahora avisa al APK por un broadcast dirigido para que el
             * servicio Android conecte exactamente cuando el túnel existe.
             * No hay polling.
             */
            string authorizationResultNV = await _adb
                    .ExecuteRawAsync(
                        new[]
                        {
                            "-s",
                            serial,
                            "shell",
                            "am",
                            "broadcast",
                            "-a",
                            "com.novora.linkengine.SESSION_READY",
                            "-n",
                            "com.novora.linkengine/.SessionReceiverRemoteNV",
                            "--es",
                            "novora_session_token",
                            _remoteServerNV.SessionTokenNV
                        })
                    .ConfigureAwait(true);
            if (!authorizationResultNV.Contains("result=-1", StringComparison.Ordinal))
                throw new InvalidOperationException("Android no confirmó la autorización de sesión.");
            _remoteSerialNV = serial;
        }
        catch (Exception)
        {
            if (_remoteServerNV is not null)
                await _remoteServerNV.StopAsync().ConfigureAwait(true);
            if (!_closing)
            {
                _viewModel.ConnectionStatus =
                    "Android Remote: no se pudo autorizar la sesión del dispositivo.";
            }
        }
        finally
        {
            _remoteConfigurationRunningNV =
                false;
        }
    }

    private async Task<ResultRemoteNV> HandleRemoteCommandNVAsync(
        CommandRemoteNV command,
        string commandPayload,
        CancellationToken cancellationToken)
    {
        return await InvokeOnUiThreadNVAsync(
                async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string? serial =
                        _remoteSerialNV;

                    if (string.IsNullOrWhiteSpace(
                            serial))
                    {
                        return ResultRemoteNV.FailNV(
                            "NOVORA no tiene un Android remoto seleccionado.");
                    }

                    DeviceInfo? remoteDevice =
                        _viewModel.Devices
                            .FirstOrDefault(
                                device =>
                                    device.Connected &&
                                    string.Equals(
                                        device.Serial,
                                        serial,
                                        StringComparison.OrdinalIgnoreCase));

                    if (remoteDevice is null)
                    {
                        return ResultRemoteNV.FailNV(
                            "El dispositivo remoto ya no está conectado por ADB.");
                    }

                    _viewModel.Device =
                        remoteDevice;

                    return command switch
                    {
                        CommandRemoteNV.Status =>
                            ResultRemoteNV.OkNV(
                                BuildRemoteStatusNV()),

                        CommandRemoteNV.StartVision =>
                            await StartVisionFromRemoteNVAsync(),

                        CommandRemoteNV.StopVision =>
                            await StopVisionFromRemoteNVAsync(),

                        CommandRemoteNV.PrepareLink =>
                            await PrepareLinkFromRemoteNVAsync(
                                serial,
                                cancellationToken),

                        CommandRemoteNV.StartLink =>
                            await StartLinkFromRemoteNVAsync(
                                serial,
                                cancellationToken),

                        CommandRemoteNV.StopLink =>
                            await StopLinkFromRemoteNVAsync(),

                        CommandRemoteNV.StartAll =>
                            await StartAllFromRemoteNVAsync(
                                serial,
                                cancellationToken),

                        CommandRemoteNV.StopAll =>
                            await StopAllFromRemoteNVAsync(),

                        CommandRemoteNV.GetSettings =>
                            GetSettingsFromRemoteNV(),

                        CommandRemoteNV.SaveSettings =>
                            SaveSettingsFromRemoteNV(
                                commandPayload),

                        CommandRemoteNV.ShareFiles =>
                            ReceiveShareFilesFromRemoteNV(
                                commandPayload),

                        CommandRemoteNV.ShareFileStatus =>
                            ReceiveShareFileStatusFromRemoteNV(
                                commandPayload),

                        _ =>
                            ResultRemoteNV.FailNV(
                                "Comando remoto no soportado.")
                    };
                })
            .ConfigureAwait(false);
    }

    private async Task<ResultRemoteNV> StartVisionFromRemoteNVAsync()
    {
        if (IsVisionEngineRunningVE())
        {
            return ResultRemoteNV.OkNV(
                BuildRemoteStatusNV());
        }

        if (_viewModel.Device is not
            {
                Connected: true
            })
        {
            return ResultRemoteNV.FailNV(
                "VisionEngine requiere un Android conectado.");
        }

        if (_viewModel.SelectedMonitor is null)
        {
            return ResultRemoteNV.FailNV(
                "VisionEngine no tiene monitor de salida seleccionado.");
        }

        await ToggleVisionEngineVEAsync();

        return IsVisionEngineRunningVE()
            ? ResultRemoteNV.OkNV(
                BuildRemoteStatusNV())
            : ResultRemoteNV.FailNV(
                "VisionEngine no alcanzó estado RUNNING.");
    }

    private async Task<ResultRemoteNV> StopVisionFromRemoteNVAsync()
    {
        if (!IsVisionEngineRunningVE())
        {
            return ResultRemoteNV.OkNV(
                BuildRemoteStatusNV());
        }

        await ToggleVisionEngineVEAsync();

        return !IsVisionEngineRunningVE()
            ? ResultRemoteNV.OkNV(
                BuildRemoteStatusNV())
            : ResultRemoteNV.FailNV(
                "VisionEngine no se detuvo.");
    }

    private async Task<ResultRemoteNV> PrepareLinkFromRemoteNVAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        InitializeLinkEngineRuntimeLE();

        ManagerRuntimeLE? runtime =
            _linkEngineRuntimeLE;

        if (runtime is null)
        {
            return ResultRemoteNV.FailNV(
                "ManagerRuntimeLE no pudo inicializarse.");
        }

        SessionRuntimeLE? current =
            runtime.SessionLE;

        if (current is not null &&
            current.State is not
                StateRuntimeLE.Stopped and not
                StateRuntimeLE.Failed)
        {
            if (!string.Equals(
                    current.Serial,
                    serial,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ResultRemoteNV.FailNV(
                    "LinkEngine ya controla otro dispositivo.");
            }

            if (IsRemoteLinkReadyForAndroidNV(
                    current))
            {
                return ResultRemoteNV.OkNV(
                    "READY;" +
                    BuildRemoteStatusNV());
            }

            if (current.State is
                StateRuntimeLE.Running or
                StateRuntimeLE.StartingRecoveryMonitor or
                StateRuntimeLE.Degraded)
            {
                return ResultRemoteNV.OkNV(
                    BuildRemoteStatusNV());
            }
        }

        if (_remoteLinkStartTaskNV is not null &&
            !_remoteLinkStartTaskNV.IsCompleted)
        {
            if (!string.Equals(
                    _remoteLinkStartSerialNV,
                    serial,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ResultRemoteNV.FailNV(
                    "LinkEngine ya está preparando otro dispositivo.");
            }
        }
        else
        {
            if (_linkEngineRuntimeOperationLE)
            {
                return ResultRemoteNV.FailNV(
                    "LinkEngine tiene otra operación en curso.");
            }

            _remoteLinkStartCtsNV?.Dispose();

            _remoteLinkStartCtsNV =
                new CancellationTokenSource();

            _remoteLinkStartSerialNV =
                serial;

            _linkEngineRuntimeOperationLE =
                true;

            UpdateLinkEngineButtonLE();

            Task<ResultCoreLE> startTask =
                runtime.StartAsync(
                    serial,
                    _remoteLinkStartCtsNV.Token,
                    launchAndroidClient: false);

            _remoteLinkStartTaskNV =
                startTask;

            _ = ObserveRemoteLinkStartNVAsync(
                runtime,
                startTask);
        }

        var readyTcs =
            new TaskCompletionSource<SessionRuntimeLE>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        void OnStatusChangedNV(
            object? sender,
            SessionRuntimeLE session)
        {
            if (!string.Equals(
                    session.Serial,
                    serial,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (IsRemoteLinkReadyForAndroidNV(
                    session) ||
                session.State is
                    StateRuntimeLE.Failed or
                    StateRuntimeLE.Stopped)
            {
                readyTcs.TrySetResult(
                    session);
            }
        }

        runtime.StatusChangedLE +=
            OnStatusChangedNV;

        try
        {
            current =
                runtime.SessionLE;

            if (current is not null &&
                string.Equals(
                    current.Serial,
                    serial,
                    StringComparison.OrdinalIgnoreCase) &&
                (IsRemoteLinkReadyForAndroidNV(
                    current) ||
                 current.State is
                    StateRuntimeLE.Failed or
                    StateRuntimeLE.Stopped))
            {
                readyTcs.TrySetResult(
                    current);
            }

            using var timeoutCts =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            timeoutCts.CancelAfter(
                RemoteLinkPrepareTimeoutNV);

            SessionRuntimeLE ready;

            try
            {
                ready =
                    await readyTcs.Task
                        .WaitAsync(
                            timeoutCts.Token)
                        .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                _remoteLinkStartCtsNV?.Cancel();

                return ResultRemoteNV.FailNV(
                    "LinkEngine no preparó CONTROL/ADB reverse dentro del tiempo permitido.");
            }

            if (!IsRemoteLinkReadyForAndroidNV(
                    ready))
            {
                return ResultRemoteNV.FailNV(
                    ready.LastError ??
                    ready.Message);
            }

            return ResultRemoteNV.OkNV(
                "READY;" +
                BuildRemoteStatusNV());
        }
        finally
        {
            runtime.StatusChangedLE -=
                OnStatusChangedNV;
        }
    }

    private static bool IsRemoteLinkReadyForAndroidNV(
        SessionRuntimeLE session)
    {
        return
            session.State ==
                StateRuntimeLE.WaitingForAndroid &&
            session.TransportOpen &&
            session.ListenerStarted &&
            session.ReverseConfigured &&
            session.ReverseVerified;
    }

    private async Task ObserveRemoteLinkStartNVAsync(
        ManagerRuntimeLE runtime,
        Task<ResultCoreLE> startTask)
    {
        try
        {
            await startTask
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            try
            {
                await Dispatcher
                    .InvokeAsync(
                        () =>
                        {
                            SessionRuntimeLE? session =
                                runtime.SessionLE;

                            if (session is not null)
                            {
                                ApplyLinkEngineRuntimeSessionLE(
                                    session);
                            }

                            if (ReferenceEquals(
                                    _remoteLinkStartTaskNV,
                                    startTask))
                            {
                                _remoteLinkStartTaskNV =
                                    null;

                                _remoteLinkStartSerialNV =
                                    null;

                                _remoteLinkStartCtsNV?.Dispose();

                                _remoteLinkStartCtsNV =
                                    null;

                                _linkEngineRuntimeOperationLE =
                                    false;

                                UpdateLinkEngineButtonLE();
                            }
                        });
            }
            catch
            {
            }
        }
    }

    private async Task<ResultRemoteNV> StartLinkFromRemoteNVAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        InitializeLinkEngineRuntimeLE();

        ManagerRuntimeLE? runtime =
            _linkEngineRuntimeLE;

        if (runtime is null)
        {
            return ResultRemoteNV.FailNV(
                "ManagerRuntimeLE no pudo inicializarse.");
        }

        SessionRuntimeLE? current =
            runtime.SessionLE;

        bool active =
            current is not null &&
            current.State is not
                StateRuntimeLE.Stopped and not
                StateRuntimeLE.Failed;

        if (active)
        {
            if (string.Equals(
                    current?.Serial,
                    serial,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ResultRemoteNV.OkNV(
                    BuildRemoteStatusNV());
            }

            return ResultRemoteNV.FailNV(
                "LinkEngine ya controla otro dispositivo.");
        }

        if (_linkEngineRuntimeOperationLE)
        {
            return ResultRemoteNV.FailNV(
                "LinkEngine tiene otra operación en curso.");
        }

        _linkEngineRuntimeOperationLE =
            true;

        UpdateLinkEngineButtonLE();

        try
        {
            ResultCoreLE result =
                await runtime
                    .StartAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(true);

            SessionRuntimeLE? session =
                runtime.SessionLE;

            if (session is not null)
            {
                ApplyLinkEngineRuntimeSessionLE(
                    session);
            }

            return result.Success
                ? ResultRemoteNV.OkNV(
                    BuildRemoteStatusNV())
                : ResultRemoteNV.FailNV(
                    result.Message);
        }
        finally
        {
            _linkEngineRuntimeOperationLE =
                false;

            UpdateLinkEngineButtonLE();
        }
    }

    private async Task<ResultRemoteNV> StopLinkFromRemoteNVAsync()
    {
        CancellationTokenSource? pendingCts =
            _remoteLinkStartCtsNV;

        Task<ResultCoreLE>? pendingTask =
            _remoteLinkStartTaskNV;

        pendingCts?.Cancel();

        if (pendingTask is not null &&
            !pendingTask.IsCompleted)
        {
            try
            {
                await pendingTask
                    .WaitAsync(
                        TimeSpan.FromSeconds(5))
                    .ConfigureAwait(true);
            }
            catch
            {
            }
        }

        SessionRuntimeLE? session =
            _linkEngineRuntimeLE?
                .SessionLE;

        bool active =
            session is not null &&
            session.State is not
                StateRuntimeLE.Stopped and not
                StateRuntimeLE.Failed;

        if (!active)
        {
            return ResultRemoteNV.OkNV(
                BuildRemoteStatusNV());
        }

        await StopLinkEngineRuntimeFromUiLEAsync(
            stopAndroidVpnLE: false);

        return ResultRemoteNV.OkNV(
            BuildRemoteStatusNV());
    }

    private async Task<ResultRemoteNV> StartAllFromRemoteNVAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        ResultRemoteNV link =
            await StartLinkFromRemoteNVAsync(
                serial,
                cancellationToken);

        if (!link.Success)
        {
            return link;
        }

        ResultRemoteNV vision =
            await StartVisionFromRemoteNVAsync();

        if (!vision.Success)
        {
            return vision;
        }

        return ResultRemoteNV.OkNV(
            BuildRemoteStatusNV());
    }

    private async Task<ResultRemoteNV> StopAllFromRemoteNVAsync()
    {
        ResultRemoteNV vision =
            await StopVisionFromRemoteNVAsync();

        ResultRemoteNV link =
            await StopLinkFromRemoteNVAsync();

        if (!vision.Success)
        {
            return vision;
        }

        if (!link.Success)
        {
            return link;
        }

        return ResultRemoteNV.OkNV(
            BuildRemoteStatusNV());
    }

    private ResultRemoteNV GetSettingsFromRemoteNV()
    {
        try
        {
            _viewModel.RefreshOutputCapabilityOptions();
            _viewModel.RefreshAudioOutputOptions(
                _paths);

            SettingsSnapshotRemoteNV snapshot =
                BuildSettingsSnapshotRemoteNV();

            string json =
                JsonSerializer.Serialize(
                    snapshot);

            return ResultRemoteNV.OkNV(
                json);
        }
        catch (Exception ex)
        {
            return ResultRemoteNV.FailNV(
                $"No se pudieron leer los ajustes: {ex.Message}");
        }
    }

    private ResultRemoteNV SaveSettingsFromRemoteNV(
        string commandPayload)
    {
        if (string.IsNullOrWhiteSpace(
                commandPayload))
        {
            return ResultRemoteNV.FailNV(
                "SaveSettings no recibió configuración.");
        }

        try
        {
            SettingsUpdateRemoteNV? update =
                JsonSerializer.Deserialize<SettingsUpdateRemoteNV>(
                    commandPayload);

            if (update is null)
            {
                return ResultRemoteNV.FailNV(
                    "La configuración remota es inválida.");
            }

            _viewModel.RefreshOutputCapabilityOptions();
            _viewModel.RefreshAudioOutputOptions(
                _paths);

            string theme =
                ResolveStringOptionRemoteNV(
                    _viewModel.ThemeOptions,
                    update.Theme,
                    "tema");

            string presentationMode =
                ResolveStringOptionRemoteNV(
                    _viewModel.VideoPresentationModeOptions,
                    update.VideoPresentationMode,
                    "modo de video");

            string bitrate =
                ResolveStringOptionRemoteNV(
                    _viewModel.BitrateOptions,
                    update.Bitrate,
                    "bitrate");

            int fps =
                ResolveIntOptionRemoteNV(
                    _viewModel.FpsOptions,
                    update.TargetFps,
                    "FPS");

            int maxSize =
                ResolveIntOptionRemoteNV(
                    _viewModel.ResolutionOptions,
                    update.MaxSize,
                    "resolución");

            string audio =
                ResolveStringOptionRemoteNV(
                    _viewModel.AudioOutputOptions,
                    update.SelectedAudioOutput,
                    "salida de audio");

            MonitorInfo? monitor =
                _viewModel.Monitors
                    .FirstOrDefault(
                        item =>
                            string.Equals(
                                item.DeviceName,
                                update.SelectedMonitorDeviceName,
                                StringComparison.OrdinalIgnoreCase));

            if (monitor is null)
            {
                return ResultRemoteNV.FailNV(
                    "El monitor seleccionado ya no está disponible en Windows.");
            }

            _viewModel.Theme =
                theme;

            _viewModel.VideoPresentationMode =
                presentationMode;

            _viewModel.SelectedMonitor =
                monitor;

            _viewModel.Bitrate =
                bitrate;

            _viewModel.TargetFps =
                fps;

            _viewModel.MaxSize =
                maxSize;

            _viewModel.SelectedAudioOutput =
                audio;

            ThemeService.Apply(
                _viewModel.Theme);

            UpdateOutputProfile();
            SaveSelection();
            UpdateRuntimeButtons();

            return ResultRemoteNV.OkNV(
                IsVisionEngineRunningVE()
                    ? "Ajustes guardados en NOVORA. Los cambios de video/audio se aplicarán completamente al reiniciar VisionEngine."
                    : "Ajustes guardados en NOVORA.");
        }
        catch (JsonException ex)
        {
            return ResultRemoteNV.FailNV(
                $"JSON de ajustes inválido: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ResultRemoteNV.FailNV(
                $"No se pudieron guardar los ajustes: {ex.Message}");
        }
    }

    private SettingsSnapshotRemoteNV BuildSettingsSnapshotRemoteNV()
    {
        return new SettingsSnapshotRemoteNV
        {
            DeviceNativeResolution =
                _viewModel.DeviceNativeResolutionLabel,

            DeviceMaxFps =
                _viewModel.DeviceMaxFpsLabel,

            DeviceMaxRefreshRate =
                _viewModel.DeviceMaxRefreshRateLabel,

            Theme =
                _viewModel.Theme,

            VideoPresentationMode =
                _viewModel.VideoPresentationMode,

            Bitrate =
                _viewModel.Bitrate,

            TargetFps =
                _viewModel.TargetFps,

            MaxSize =
                _viewModel.MaxSize,

            SelectedAudioOutput =
                _viewModel.SelectedAudioOutput,

            SelectedMonitorDeviceName =
                _viewModel.SelectedMonitor?.DeviceName ??
                string.Empty,

            VisionRunning =
                IsVisionEngineRunningVE(),

            Notice =
                IsVisionEngineRunningVE()
                    ? "VisionEngine está activo. Los cambios de video/audio se aplican completamente al reiniciar la transmisión."
                    : string.Empty,

            ThemeOptions =
                _viewModel.ThemeOptions
                    .Select(
                        option =>
                            new OptionStringRemoteNV(
                                option.Value,
                                option.Label))
                    .ToArray(),

            PresentationModeOptions =
                _viewModel.VideoPresentationModeOptions
                    .Select(
                        option =>
                            new OptionStringRemoteNV(
                                option.Value,
                                option.Label))
                    .ToArray(),

            Monitors =
                _viewModel.Monitors
                    .Select(
                        monitor =>
                            new MonitorRemoteNV(
                                monitor.DeviceName,
                                monitor.DisplayLabel,
                                monitor.IdentityDescription))
                    .ToArray(),

            BitrateOptions =
                _viewModel.BitrateOptions
                    .Select(
                        option =>
                            new OptionStringRemoteNV(
                                option.Value,
                                option.Label))
                    .ToArray(),

            FpsOptions =
                _viewModel.FpsOptions
                    .Select(
                        option =>
                            new OptionIntRemoteNV(
                                option.Value,
                                option.Label))
                    .ToArray(),

            ResolutionOptions =
                _viewModel.ResolutionOptions
                    .Select(
                        option =>
                            new OptionIntRemoteNV(
                                option.Value,
                                option.Label))
                    .ToArray(),

            AudioOutputOptions =
                _viewModel.AudioOutputOptions
                    .Select(
                        option =>
                            new OptionStringRemoteNV(
                                option.Value,
                                option.Label))
                    .ToArray()
        };
    }

    private static string ResolveStringOptionRemoteNV(
        IReadOnlyList<NOVORA.ViewModels.SettingOption<string>> options,
        string requested,
        string name)
    {
        NOVORA.ViewModels.SettingOption<string>? option =
            options.FirstOrDefault(
                item =>
                    string.Equals(
                        item.Value,
                        requested,
                        StringComparison.OrdinalIgnoreCase));

        if (option is null)
        {
            throw new InvalidOperationException(
                $"El {name} solicitado ya no está disponible.");
        }

        return option.Value;
    }

    private static int ResolveIntOptionRemoteNV(
        IReadOnlyList<NOVORA.ViewModels.SettingOption<int>> options,
        int requested,
        string name)
    {
        NOVORA.ViewModels.SettingOption<int>? option =
            options.FirstOrDefault(
                item =>
                    item.Value ==
                    requested);

        if (option is null)
        {
            throw new InvalidOperationException(
                $"El valor de {name} solicitado ya no está disponible.");
        }

        return option.Value;
    }

    private string BuildRemoteStatusNV()
    {
        bool vision =
            IsVisionEngineRunningVE();

        SessionRuntimeLE? linkSession =
            _linkEngineRuntimeLE?
                .SessionLE;

        bool link =
            linkSession is not null &&
            linkSession.State is not
                StateRuntimeLE.Stopped and not
                StateRuntimeLE.Failed;

        return
            $"VISION={(vision ? "ON" : "OFF")};" +
            $"LINK={(link ? "ON" : "OFF")};" +
            $"LINK_STATE={linkSession?.State.ToString() ?? "Stopped"}";
    }

    private Task<T> InvokeOnUiThreadNVAsync<T>(
        Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(
            action);

        if (Dispatcher.CheckAccess())
        {
            return action();
        }

        return Dispatcher
            .InvokeAsync(action)
            .Task
            .Unwrap();
    }

    private async Task ShutdownRemoteAndroidNVAsync()
    {
        _remoteLinkStartCtsNV?.Cancel();

        Task<ResultCoreLE>? pendingStart =
            _remoteLinkStartTaskNV;

        if (pendingStart is not null &&
            !pendingStart.IsCompleted)
        {
            try
            {
                await pendingStart
                    .WaitAsync(
                        TimeSpan.FromSeconds(5))
                    .ConfigureAwait(true);
            }
            catch
            {
            }
        }

        string? serial =
            _remoteSerialNV;

        _remoteSerialNV =
            null;

        if (!string.IsNullOrWhiteSpace(
                serial))
        {
            try
            {
                await _adb
                    .ExecuteRawAsync(
                        new[]
                            {
                                "-s",
                                serial,
                                "reverse",
                                "--remove",
                            $"tcp:{ProtocolRemoteNV.DefaultDevicePortNV}"
                        })
                    .ConfigureAwait(true);
            }
            catch
            {
            }

            try
            {
                await _adb
                    .ExecuteRawAsync(
                        new[]
                        {
                            "-s",
                            serial,
                            "reverse",
                            "--remove",
                            $"tcp:{ProtocolRemoteNV.DefaultFileTransferPortNV}"
                        })
                    .ConfigureAwait(true);
            }
            catch
            {
            }
        }

        if (_remoteServerNV is not null)
        {
            try
            {
                await _remoteServerNV
                    .StopAsync()
                    .ConfigureAwait(true);
            }
            catch
            {
            }

            await _remoteServerNV.DisposeAsync();
            _remoteServerNV = null;
        }

        await StopAndroidShareTransferServerNVAsync()
            .ConfigureAwait(true);
    }
}
