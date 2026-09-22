using NOVORA.Service;

namespace NOVORA.ExInEngine;

/// <summary>
/// Puente event-driven Windows gamepad -> SDL3 -> UHID -> Android.
/// No usa polling ni Task.Delay para leer controles.
/// </summary>
public sealed class ExInManager : IAsyncDisposable
{
    private const int MaxGamepadsVE = 8;
    private const ushort FirstUhidIdVE = 3;
    private const ushort PointerCompanionOffsetVE = 0x100;
    private const ushort UhidVendorIdVE = 0x045E;
    private const ushort UhidProductIdVE = 0x028E;
    private const string UhidDeviceNameVE = "Microsoft X-Box 360 Pad";
    private const long StatusPublishIntervalMsVE = 1000;

    private readonly IExInOutput _output;
    private readonly ExInSdl _sdlVE;
    private readonly ExInProfileStore _profilesVE;
    private readonly Dictionary<string, ExInCalibrationProfile> _loadedProfilesVE = [];
    private readonly Dictionary<string, ExInDiagnosticStatus> _diagnosticsVE = [];
    private readonly Dictionary<string, ExInBatteryStatus> _batteriesVE = [];
    private readonly ExInBatteryAlerts _batteryAlertsVE = new();
    private readonly Dictionary<uint, ExInSlot> _slotsVE = [];
    private readonly Dictionary<uint, CancellationTokenSource> _pointerRepeatVE = [];
    private readonly object _gateVE = new();
    private readonly SemaphoreSlim _modeTransitionVE = new(1, 1);
    private readonly SemaphoreSlim _privacyResyncVE = new(1, 1);

    private CancellationTokenSource? _eventsCtsVE;
    private Task? _eventsTaskVE;
    private Task _privacyResyncTaskVE = Task.CompletedTask;
    private ExInStatus _statusVE = ExInStatus.CreateInitialVE();
    private long _reportsVE;
    private long _lastStatusPublishMsVE;
    private int _privacyProtectedVE;
    private bool _disposedVE;
    private bool _calibratingVE;
    private ExInInputMode _modeVE = ExInInputMode.Game;
    private bool _calibratedVE;
    private ExInState _calibrationMinVE;
    private ExInState _calibrationMaxVE;
    private ExInState _calibrationCenterVE;
    private string? _calibrationProfileKeyVE;
    private uint? _calibrationInstanceIdVE;
    private const double CalibrationDeadzoneVE = 0.05;

    private sealed record ExInSlot(
        IntPtr Handle,
        ExInDevice Device,
        ExInState LastState,
        string PhysicalName,
        string? SdlMapping,
        string TranslationTrace,
        ExInDiagnosticSession? DiagnosticSession,
        ExInOutputGeneration OutputGeneration,
        ExInOutputGeneration? PointerCompanion,
        ExInPointerTranslator Pointer);

    public ExInManager(IExInOutput output, NLServiceNovoraPaths paths)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        ArgumentNullException.ThrowIfNull(paths);
        _sdlVE = new ExInSdl(paths);
        _profilesVE = new ExInProfileStore(paths.ExInProfilesDirectory);
    }

    public event EventHandler<ExInStatus>? StatusChangedVE;
    public event EventHandler<ExInBatteryAlert>? BatteryAlertVE;

    public ExInStatus StatusVE
    {
        get
        {
            lock (_gateVE)
            {
                if (!IsPrivacyProtectedVE) return _statusVE;
                return _statusVE with { Diagnostics = _statusVE.Diagnostics.Select(value => value.ProtectVE()).ToArray() };
            }
        }
    }

    public bool IsPrivacyProtectedVE => Volatile.Read(ref _privacyProtectedVE) != 0;
    public ExInInputMode ModeVE { get { lock (_gateVE) return _modeVE; } }

    public ExInLiveSnapshot LiveSnapshotVE
    {
        get
        {
            lock (_gateVE)
            {
                ExInSlot? slot = _slotsVE.Values.FirstOrDefault();
                bool calibrated = slot?.Device.Identity is { } identity && _loadedProfilesVE.ContainsKey(identity.ProfileKey);
                bool protectedVE = IsPrivacyProtectedVE;
                ExInState visibleState = protectedVE ? default : slot?.LastState ?? default;
                ExInState correctedState = protectedVE || slot is null ? default : ApplyCalibrationVE(slot.Device, slot.LastState);
                ExInCalibrationDetails? calibration = null;
                if (!protectedVE && slot?.Device.Identity is { } slotIdentity &&
                    _loadedProfilesVE.TryGetValue(slotIdentity.ProfileKey, out ExInCalibrationProfile? profile))
                    calibration = new(slotIdentity.ProfileKey, "No reportado por SDL3", profile.Deadzone,
                        profile.Center, profile.Minimum, profile.Maximum);
                return new(slot?.Device, slot?.PhysicalName ?? "Sin control físico", visibleState, correctedState, _calibratingVE && !protectedVE, calibrated,
                    CalibrationDeadzoneVE, slot is null ? "Esperando un control físico." :
                    protectedVE ? "Estado del control oculto por Privacy Shield." :
                    _calibratingVE ? "Mueve ambos sticks y gatillos por todo su recorrido." :
                    calibrated ? "Calibración persistente activa para este control." : "Control detectado; calibración opcional.", calibration,
                    protectedVE ? null : slot?.SdlMapping,
                    protectedVE ? "Traducción oculta por Privacy Shield." : slot?.TranslationTrace ?? "Sin eventos traducidos.");
            }
        }
    }

    public bool BeginCalibrationVE()
    {
        lock (_gateVE)
        {
            ExInSlot? slot = _slotsVE.Values.FirstOrDefault();
            if (slot?.Device.Identity is not { } identity || IsPrivacyProtectedVE) return false;
            _calibrationCenterVE = slot.LastState;
            _calibrationMinVE = slot.LastState;
            _calibrationMaxVE = slot.LastState;
            _calibratingVE = true;
            _calibratedVE = false;
            _calibrationProfileKeyVE = identity.ProfileKey;
            _calibrationInstanceIdVE = slot.Device.InstanceId;
        }
        PublishVE(ExInStates.Running, null);
        return true;
    }

    public bool FinishCalibrationVE()
    {
        bool saved;
        lock (_gateVE)
        {
            if (!_calibratingVE) return false;
            _calibratingVE = false;
            ExInCalibrationProfile? profile = _calibrationProfileKeyVE is null
                ? null
                : ExInCalibrationProfile.CreateVE(
                    _calibrationProfileKeyVE,
                    _calibrationCenterVE,
                    _calibrationMinVE,
                    _calibrationMaxVE);
            saved = profile is not null && profile.IsValidVE(profile.ProfileKey) && _profilesVE.SaveVE(profile);
            if (saved && profile is not null)
                _loadedProfilesVE[profile.ProfileKey] = profile;
            _calibratedVE = saved;
            _calibrationProfileKeyVE = null;
            _calibrationInstanceIdVE = null;
        }
        PublishVE(ExInStates.Running, null);
        return saved;
    }

    public void ResetCalibrationVE()
    {
        string? profileKey;
        lock (_gateVE)
        {
            profileKey = _slotsVE.Values.FirstOrDefault()?.Device.Identity?.ProfileKey;
            _calibratingVE = false;
            _calibratedVE = false;
            _calibrationProfileKeyVE = null;
            _calibrationInstanceIdVE = null;
            if (profileKey is not null) _loadedProfilesVE.Remove(profileKey);
            if (profileKey is not null) _profilesVE.DeleteVE(profileKey);
        }
        PublishVE(ExInStates.Running, null);
    }

    public bool BeginDiagnosticsVE()
    {
        lock (_gateVE)
        {
            ExInSlot? slot = _slotsVE.Values.FirstOrDefault();
            if (slot?.Device.Identity is not { } identity || slot.DiagnosticSession is not null || IsPrivacyProtectedVE)
                return false;
            ExInDiagnosticSession session = new(identity.ProfileKey);
            session.ObserveVE(slot.LastState, ApplyCalibrationVE(slot.Device, slot.LastState), CurrentDiagnosticTimeVE());
            _slotsVE[slot.Device.InstanceId] = slot with { DiagnosticSession = session };
            _diagnosticsVE.Remove(identity.ProfileKey);
        }
        PublishVE(ExInStates.Running, null);
        return true;
    }

    public bool BeginDiagnosticTravelVE()
    {
        lock (_gateVE)
        {
            ExInSlot? slot = _slotsVE.Values.FirstOrDefault(value => value.DiagnosticSession?.PhaseVE == ExInDiagnosticPhase.Rest);
            if (slot?.DiagnosticSession is null || IsPrivacyProtectedVE) return false;
            slot.DiagnosticSession.BeginTravelVE(CurrentDiagnosticTimeVE());
        }
        PublishVE(ExInStates.Running, null);
        return true;
    }

    public ExInDiagnosticStatus? FinishDiagnosticsVE()
    {
        ExInDiagnosticStatus? result;
        lock (_gateVE)
        {
            ExInSlot? slot = _slotsVE.Values.FirstOrDefault(value => value.DiagnosticSession?.PhaseVE == ExInDiagnosticPhase.Travel);
            if (slot?.DiagnosticSession is null || slot.Device.Identity is not { } identity || IsPrivacyProtectedVE) return null;
            result = slot.DiagnosticSession.CompleteVE(CurrentDiagnosticTimeVE());
            _diagnosticsVE[identity.ProfileKey] = result;
            _slotsVE[slot.Device.InstanceId] = slot with { DiagnosticSession = null };
        }
        PublishVE(ExInStates.Running, null);
        return result;
    }

    public bool CancelDiagnosticsVE()
    {
        bool cancelled = false;
        lock (_gateVE)
        {
            foreach ((uint id, ExInSlot slot) in _slotsVE.ToArray())
            {
                if (slot.DiagnosticSession is null) continue;
                _slotsVE[id] = slot with { DiagnosticSession = null };
                cancelled = true;
            }
        }
        if (cancelled) PublishVE(ExInStates.Running, null);
        return cancelled;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        if (_eventsTaskVE is not null)
            return;

        _sdlVE.InitializeVE();
        cancellationToken.ThrowIfCancellationRequested();

        _eventsCtsVE = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await DiscoverInitialVE(_eventsCtsVE.Token).ConfigureAwait(false);

        CancellationToken eventsToken = _eventsCtsVE.Token;

        // SDL_WaitEventTimeout is a blocking native wait. Start the event pump
        // away from WPF's synchronization context so it cannot stall the UI.
        _eventsTaskVE = Task.Run(
            () => RunEventsVE(eventsToken),
            CancellationToken.None);
        PublishVE(ExInStates.Running, null);
    }

    public async Task SynchronizeOutputAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        if (!_output.IsReady) return;
        await _modeTransitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ExInSlot[] slots;
            lock (_gateVE) slots = _slotsVE.Values.ToArray();
            foreach (ExInSlot slot in slots)
            {
                await slot.OutputGeneration.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
                if (slot.OutputGeneration.ModeVE == ExInInputMode.Game && slot.PointerCompanion is not null)
                    try { await slot.PointerCompanion.SynchronizeAsync(cancellationToken).ConfigureAwait(false); } catch { }
                await SendStateVE(slot, slot.LastState, cancellationToken).ConfigureAwait(false);
                StartPointerRepeatVE(slot.Device.InstanceId);
            }
        }
        finally { _modeTransitionVE.Release(); }
    }

    internal async Task BindOutputAsync(Action bind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bind);
        ThrowIfDisposedVE();
        await _modeTransitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bind();
            if (!_output.IsReady) return;
            ExInSlot[] slots;
            lock (_gateVE) slots = _slotsVE.Values.ToArray();
            foreach (ExInSlot slot in slots)
            {
                await slot.OutputGeneration.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
                if (slot.OutputGeneration.ModeVE == ExInInputMode.Game && slot.PointerCompanion is not null)
                    try { await slot.PointerCompanion.SynchronizeAsync(cancellationToken).ConfigureAwait(false); } catch { }
                await SendStateVE(slot, slot.LastState, cancellationToken).ConfigureAwait(false);
                StartPointerRepeatVE(slot.Device.InstanceId);
            }
        }
        finally { _modeTransitionVE.Release(); }
    }

    internal async Task UnbindOutputAsync(Action unbind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unbind);
        await _modeTransitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ExInSlot[] slots;
            lock (_gateVE) slots = _slotsVE.Values.ToArray();
            foreach (ExInSlot slot in slots)
            {
                try { await slot.OutputGeneration.DetachAsync(cancellationToken).ConfigureAwait(false); }
                catch when (!cancellationToken.IsCancellationRequested) { }
                if (slot.PointerCompanion is not null)
                    try { await slot.PointerCompanion.DetachAsync(cancellationToken).ConfigureAwait(false); }
                    catch when (!cancellationToken.IsCancellationRequested) { }
            }
        }
        finally
        {
            unbind();
            _modeTransitionVE.Release();
        }
    }

    public async Task DestroyOutputDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (!_output.IsReady) return;
        await _modeTransitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ExInSlot[] slots;
            lock (_gateVE) slots = _slotsVE.Values.ToArray();
            foreach (ExInSlot slot in slots)
            {
                try { await slot.OutputGeneration.DetachAsync(cancellationToken).ConfigureAwait(false); }
                catch when (!cancellationToken.IsCancellationRequested) { }
                if (slot.PointerCompanion is not null)
                    try { await slot.PointerCompanion.DetachAsync(cancellationToken).ConfigureAwait(false); }
                    catch when (!cancellationToken.IsCancellationRequested) { }
            }
        }
        finally { _modeTransitionVE.Release(); }
    }

    public async Task<ExInModeResult> SetModeAsync(ExInInputMode mode, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        await _modeTransitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposedVE();
            ExInSlot[] slots;
            ExInInputMode previous;
            lock (_gateVE) { slots = _slotsVE.Values.ToArray(); previous = _modeVE; }
            List<ExInSlot> changed = [];
            try
            {
                foreach (ExInSlot slot in slots)
                {
                    ExInModeResult result = await SetSlotModeVEAsync(slot, mode, cancellationToken).ConfigureAwait(false);
                    if (!result.Success)
                    {
                        bool restored = await RollbackModeVEAsync(changed, previous).ConfigureAwait(false);
                        string message = restored ? result.Message : result.Message + " Rollback global incompleto.";
                        return result with { ActiveMode = previous, Message = message };
                    }
                    changed.Add(slot);
                }
            }
            catch (Exception ex)
            {
                bool restored = await RollbackModeVEAsync(changed, previous).ConfigureAwait(false);
                if (!restored)
                    throw new InvalidOperationException("La transición falló y no fue posible restaurar todos los controles.", ex);
                throw;
            }
            lock (_gateVE)
            {
                _modeVE = mode;
                foreach (ExInSlot slot in slots)
                    if (_slotsVE.TryGetValue(slot.Device.InstanceId, out ExInSlot? current))
                        _slotsVE[slot.Device.InstanceId] = current with { Device = slot.OutputGeneration.ProfileVE.Device };
            }
            foreach (ExInSlot slot in slots)
            {
                if (mode == ExInInputMode.Ui) StartPointerRepeatVE(slot.Device.InstanceId);
                else StopPointerRepeatVE(slot.Device.InstanceId);
            }
            PublishVE(ExInStates.Running, null);
            long generation = slots.Length == 0 ? 0 : slots.Max(value => value.OutputGeneration.GenerationVE);
            return new(true, mode, generation, "Modo de control actualizado.");
        }
        finally { _modeTransitionVE.Release(); }
    }

    private async Task<bool> RollbackModeVEAsync(IEnumerable<ExInSlot> slots, ExInInputMode previous)
    {
        bool restored = true;
        foreach (ExInSlot slot in slots.Reverse())
        {
            try
            {
                ExInModeResult result = await SetSlotModeVEAsync(slot, previous, CancellationToken.None).ConfigureAwait(false);
                restored &= result.Success;
            }
            catch { restored = false; }
        }
        return restored;
    }

    private async Task<ExInModeResult> SetSlotModeVEAsync(
        ExInSlot slot,
        ExInInputMode mode,
        CancellationToken cancellationToken)
    {
        ExInInputMode previous = slot.OutputGeneration.ModeVE;
        if (previous == mode)
            return new(true, mode, slot.OutputGeneration.GenerationVE, "El modo ya está activo.");

        if (mode == ExInInputMode.Ui && slot.PointerCompanion is not null)
            await slot.PointerCompanion.DetachAsync(cancellationToken).ConfigureAwait(false);

        ExInModeResult result;
        try
        {
            result = await slot.OutputGeneration.SetModeAsync(mode, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (previous == ExInInputMode.Game && slot.PointerCompanion is not null)
                try { await slot.PointerCompanion.SynchronizeAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
        if (!result.Success)
        {
            if (previous == ExInInputMode.Game && slot.PointerCompanion is not null)
                try { await slot.PointerCompanion.SynchronizeAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            return result;
        }

        if (mode == ExInInputMode.Game && slot.PointerCompanion is not null)
        {
            try
            {
                await slot.PointerCompanion.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try { await slot.PointerCompanion.DetachAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                if (ex is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
                    return result with { Message = "Modo Juego activo; el puntero del touchpad no está disponible.", Error = ex.Message };
                ExInModeResult restored = await slot.OutputGeneration.SetModeAsync(previous, CancellationToken.None).ConfigureAwait(false);
                if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(
                        "Cambio de modo cancelado después de restaurar la salida anterior.",
                        ex,
                        cancellationToken);
                string message = restored.Success
                    ? "No fue posible activar el puntero del DualShock 4; se restauró el modo anterior."
                    : "Falló el puntero del DualShock 4 y el rollback del modo anterior.";
                return new(false, restored.ActiveMode, result.Generation, message, ex.Message);
            }
        }

        return result;
    }

    public void SetPrivacyProtectedVE(bool protectedVE)
    {
        if (_disposedVE) return;
        int next = protectedVE ? 1 : 0;
        int previous = Interlocked.Exchange(ref _privacyProtectedVE, next);
        if (previous == next) return;

        if (protectedVE)
        {
            lock (_gateVE)
            {
                foreach ((uint id, ExInSlot slot) in _slotsVE.ToArray())
                {
                    StopPointerRepeatLockedVE(id);
                    slot.Pointer.ResetTouchVE();
                    if (slot.DiagnosticSession is not null)
                        _slotsVE[id] = slot with { DiagnosticSession = null };
                }
            }
        }

        Task resync = ResyncPrivacyVE(protectedVE);
        lock (_gateVE) _privacyResyncTaskVE = resync;
    }

    public async Task ResyncConnectedDevicesVEAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();

        if (_eventsTaskVE is null)
        {
            return;
        }

        IReadOnlyList<uint> detected =
            _sdlVE.GetDetectedIdsVE();

        HashSet<uint> detectedSet =
            detected.ToHashSet();

        uint[] stale;

        lock (_gateVE)
        {
            stale =
                _slotsVE.Keys
                    .Where(
                        id =>
                            !detectedSet.Contains(id))
                    .ToArray();
        }

        foreach (uint id in stale)
        {
            await RemoveDeviceVE(
                    id,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (uint id in detected)
        {
            await OpenDeviceVE(
                    id,
                    cancellationToken)
                .ConfigureAwait(false);

            await RefreshDeviceVE(
                    id,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        PublishVE(
            ExInStates.Running,
            null);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts = _eventsCtsVE;
        Task? task = _eventsTaskVE;
        _eventsCtsVE = null;
        _eventsTaskVE = null;

        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
        }

        if (task is not null)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch { }
        }

        cts?.Dispose();
        await CloseAllVE().ConfigureAwait(false);
        PublishVE(ExInStates.Stopped, null);
    }

    private async Task RunEventsVE(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ExInEvent message in _sdlVE.ReadEventsVE(cancellationToken).ConfigureAwait(false))
            {
                switch (message.Type)
                {
                    case ExInTypeEvent.Added:
                        await OpenDeviceVE(message.InstanceId, cancellationToken).ConfigureAwait(false);
                        break;

                    case ExInTypeEvent.Removed:
                        await RemoveDeviceVE(message.InstanceId, cancellationToken).ConfigureAwait(false);
                        break;

                    case ExInTypeEvent.Remapped:
                        await RefreshDeviceVE(message.InstanceId, cancellationToken).ConfigureAwait(false);
                        break;

                    case ExInTypeEvent.Axis:
                        await ApplyAxisVE(message, cancellationToken).ConfigureAwait(false);
                        break;

                    case ExInTypeEvent.Button:
                        await ApplyButtonVE(message, cancellationToken).ConfigureAwait(false);
                        break;

                    case ExInTypeEvent.Battery:
                        ApplyBatteryVE(message);
                        break;

                    case ExInTypeEvent.TouchDown:
                    case ExInTypeEvent.TouchMotion:
                    case ExInTypeEvent.TouchUp:
                        await ApplyTouchpadVE(message, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            PublishVE(ExInStates.Failed, ex.Message);
        }
    }

    private async Task DiscoverInitialVE(CancellationToken cancellationToken)
    {
        foreach (uint id in _sdlVE.GetDetectedIdsVE())
            await OpenDeviceVE(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task OpenDeviceVE(uint id, CancellationToken cancellationToken)
    {
        await _modeTransitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await OpenDeviceCoreVE(id, cancellationToken).ConfigureAwait(false); }
        finally { _modeTransitionVE.Release(); }
    }

    private async Task OpenDeviceCoreVE(uint id, CancellationToken cancellationToken)
    {
        lock (_gateVE)
        {
            if (_slotsVE.ContainsKey(id) || _slotsVE.Count >= MaxGamepadsVE)
                return;
        }

        IntPtr handle = _sdlVE.OpenVE(id);
        if (handle == IntPtr.Zero) return;

        ExInOutputGeneration? generation = null;
        ExInOutputGeneration? pointerCompanion = null;
        try
        {
            ushort uhidId = AllocateUhidIdVE();
            ExInControllerIdentity identity = _sdlVE.GetIdentityVE(handle);
            string physicalName = identity.Name;
            string? sdlMapping = _sdlVE.GetMappingVE(handle);
            ExInOutputProfile FactoryVE(ExInInputMode mode) => ExInOutputProfile.CreateVE(mode, identity, id, uhidId);
            generation = new(_output, FactoryVE(_modeVE), FactoryVE);
            ExInDevice device = generation.ProfileVE.Device;
            if (identity.Family == ExInControllerFamily.DualShock4)
            {
                ushort pointerId = checked((ushort)(uhidId + PointerCompanionOffsetVE));
                ExInOutputProfile pointerProfile = ExInOutputProfile.CreateVE(ExInInputMode.Ui, identity, id, pointerId);
                pointerCompanion = new(_output, pointerProfile, _ => pointerProfile);
            }

            ExInCalibrationProfile? profile = _profilesVE.LoadVE(identity.ProfileKey);
            ExInBatteryStatus battery = _sdlVE.GetBatteryStatusVE(handle, identity);

            await generation.CreateAsync(cancellationToken).ConfigureAwait(false);
            if (_modeVE == ExInInputMode.Game && pointerCompanion is not null)
                try { await pointerCompanion.CreateAsync(cancellationToken).ConfigureAwait(false); } catch { }

            ExInState state = _sdlVE.ReadStateVE(handle);
            ExInBatteryAlert? batteryAlert;

            lock (_gateVE)
            {
                if (profile is not null) _loadedProfilesVE[identity.ProfileKey] = profile;
                _batteriesVE[identity.ProfileKey] = battery;
                batteryAlert = _batteryAlertsVE.UpdateVE(battery);
                _slotsVE[id] = new ExInSlot(
                    handle, device, state, physicalName, sdlMapping, BuildTranslationTraceVE(state), null,
                    generation, pointerCompanion, new ExInPointerTranslator());
            }

            PublishBatteryAlertVE(batteryAlert);

            ExInSlot created;
            lock (_gateVE) created = _slotsVE[id];
            await SendStateVE(created, state, cancellationToken).ConfigureAwait(false);
            PublishVE(ExInStates.Running, null);
        }
        catch
        {
            if (generation is not null)
                try { await generation.DisposeAsync().ConfigureAwait(false); } catch { }
            if (pointerCompanion is not null)
                try { await pointerCompanion.DisposeAsync().ConfigureAwait(false); } catch { }
            _sdlVE.CloseVE(handle);
            throw;
        }
    }

    private async Task RemoveDeviceVE(uint id, CancellationToken cancellationToken)
    {
        await _modeTransitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await RemoveDeviceCoreVE(id, cancellationToken).ConfigureAwait(false); }
        finally { _modeTransitionVE.Release(); }
    }

    private async Task RemoveDeviceCoreVE(uint id, CancellationToken cancellationToken)
    {
        ExInSlot? slot;
        lock (_gateVE)
        {
            if (!_slotsVE.Remove(id, out slot))
                return;
            StopPointerRepeatLockedVE(id);
            if (slot?.Device.Identity is { } identity)
                _batteriesVE.Remove(identity.ProfileKey);
        }

        if (slot is null) return;

        try
        {
            await slot.OutputGeneration.DisposeAsync().ConfigureAwait(false);
        }
        catch { }
        finally
        {
            if (slot.PointerCompanion is not null)
                try { await slot.PointerCompanion.DisposeAsync().ConfigureAwait(false); } catch { }
            _sdlVE.CloseVE(slot.Handle);
            PublishVE(ExInStates.Running, null);
        }
    }

    private async Task RefreshDeviceVE(uint id, CancellationToken cancellationToken)
    {
        ExInSlot? slot;
        lock (_gateVE) _slotsVE.TryGetValue(id, out slot);
        if (slot is null) return;

        ExInState state = _sdlVE.ReadStateVE(slot.Handle);
        string? mapping = _sdlVE.GetMappingVE(slot.Handle);
        lock (_gateVE)
        {
            if (_slotsVE.TryGetValue(id, out ExInSlot? current))
                _slotsVE[id] = current with
                {
                    LastState = state,
                    SdlMapping = mapping,
                    TranslationTrace = BuildTranslationTraceVE(state)
                };
        }

        await SendStateVE(slot, state, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyAxisVE(ExInEvent message, CancellationToken cancellationToken)
    {
        ExInSlot? slot;
        ExInState next;

        lock (_gateVE)
        {
            if (!_slotsVE.TryGetValue(message.InstanceId, out slot) || slot is null)
                return;

            next = _sdlVE.ReadStateVE(slot.Handle);
            if (next == slot.LastState) return;
            _slotsVE[message.InstanceId] = slot with
            {
                LastState = next,
                TranslationTrace = BuildTranslationTraceVE(next)
            };
            CaptureCalibrationVE(message.InstanceId, next);
            ObserveDiagnosticVE(slot, next);
        }

        await SendStateVE(slot, next, cancellationToken).ConfigureAwait(false);
        StartPointerRepeatVE(message.InstanceId);
        MaybePublishVE();
    }

    private async Task ApplyButtonVE(ExInEvent message, CancellationToken cancellationToken)
    {
        ExInButtons flag = ButtonFlagVE(message.Button);
        if (flag == ExInButtons.None) return;

        ExInSlot? slot;
        ExInState next;

        lock (_gateVE)
        {
            if (!_slotsVE.TryGetValue(message.InstanceId, out slot) || slot is null)
                return;

            next = _sdlVE.ReadStateVE(slot.Handle);
            if (next == slot.LastState) return;
            _slotsVE[message.InstanceId] = slot with
            {
                LastState = next,
                TranslationTrace = BuildTranslationTraceVE(next)
            };
            ObserveDiagnosticVE(slot, next);
        }

        await SendStateVE(slot, next, cancellationToken).ConfigureAwait(false);
        if (flag == ExInButtons.Touchpad && slot.OutputGeneration.ModeVE == ExInInputMode.Game &&
            slot.PointerCompanion is not null && !IsPrivacyProtectedVE)
        {
            ExInPointerReport click = new(0, 0, 0, 0, message.Pressed ? (byte)1 : (byte)0, ExInUiAction.None);
            try
            {
                if (await slot.PointerCompanion.SendPointerAsync(click, cancellationToken).ConfigureAwait(false))
                    Interlocked.Increment(ref _reportsVE);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                try { await slot.PointerCompanion.DetachAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            }
        }
        MaybePublishVE();
    }

    private static string BuildTranslationTraceVE(ExInState state)
    {
        byte[] report = ExInReport.BuildVE(state);
        return $"SDL LX={state.LeftX} LY={state.LeftY} RX={state.RightX} RY={state.RightY} " +
               $"LT={state.LeftTrigger} RT={state.RightTrigger} BTN=0x{(uint)state.Buttons:X4}\n" +
               $"HID {Convert.ToHexString(report)}";
    }

    private async Task SendStateVE(ExInSlot slot, ExInState state, CancellationToken cancellationToken)
    {
        ExInState effective = IsPrivacyProtectedVE
            ? default
            : ApplyCalibrationVE(slot.Device, state);

        bool sent;
        if (slot.OutputGeneration.ModeVE == ExInInputMode.Ui)
        {
            bool calibrated = slot.Device.Identity is { } identity && HasCalibrationVE(identity.ProfileKey);
            ExInPointerReport pointer = slot.Pointer.FromXboxVE(effective, calibrated || IsPrivacyProtectedVE);
            if (slot.Device.Identity?.Family == ExInControllerFamily.DualShock4 &&
                effective.Buttons.HasFlag(ExInButtons.Touchpad))
                pointer = pointer with { Buttons = (byte)(pointer.Buttons | 1) };
            sent = await slot.OutputGeneration.SendPointerAsync(pointer, cancellationToken).ConfigureAwait(false);
            if (sent && pointer.Action != ExInUiAction.None && _output is IExInUiOutput uiOutput)
                await uiOutput.SendUiActionAsync(pointer.Action, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            sent = await slot.OutputGeneration.SendStateAsync(effective, cancellationToken).ConfigureAwait(false);
        }

        if (!sent) return;

        Interlocked.Increment(ref _reportsVE);
    }

    private async Task ApplyTouchpadVE(ExInEvent message, CancellationToken cancellationToken)
    {
        ExInSlot? slot;
        lock (_gateVE) _slotsVE.TryGetValue(message.InstanceId, out slot);
        if (slot is null || IsPrivacyProtectedVE) return;

        ExInOutputGeneration? pointerOutput = slot.OutputGeneration.ModeVE == ExInInputMode.Ui
            ? slot.OutputGeneration
            : slot.PointerCompanion;
        if (pointerOutput is null) return;

        bool physicalClick = slot.LastState.Buttons.HasFlag(ExInButtons.Touchpad);
        ExInPointerReport report;
        try
        {
            report = message.Type switch
            {
                ExInTypeEvent.TouchDown => slot.Pointer.TouchDownVE(
                    message.Touchpad, message.Finger, message.TouchX, message.TouchY, physicalClick),
                ExInTypeEvent.TouchMotion => slot.Pointer.TouchMotionVE(
                    message.Touchpad, message.Finger, message.TouchX, message.TouchY, physicalClick),
                ExInTypeEvent.TouchUp => slot.Pointer.TouchUpVE(
                    message.Touchpad, message.Finger, physicalClick),
                _ => default
            };
        }
        catch (ArgumentException)
        {
            slot.Pointer.ResetTouchVE();
            return;
        }
        catch (InvalidOperationException)
        {
            slot.Pointer.ResetTouchVE();
            return;
        }

        try
        {
            if (await pointerOutput.SendPointerAsync(report, cancellationToken).ConfigureAwait(false))
                Interlocked.Increment(ref _reportsVE);
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            slot.Pointer.ResetTouchVE();
            if (!ReferenceEquals(pointerOutput, slot.OutputGeneration))
                try { await pointerOutput.DetachAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        }
    }

    private bool HasCalibrationVE(string profileKey)
    {
        lock (_gateVE) return _loadedProfilesVE.ContainsKey(profileKey);
    }

    private void StartPointerRepeatVE(uint instanceId)
    {
        CancellationTokenSource repeat;
        lock (_gateVE)
        {
            if (_pointerRepeatVE.ContainsKey(instanceId) ||
                !_slotsVE.TryGetValue(instanceId, out ExInSlot? slot) ||
                slot.OutputGeneration.ModeVE != ExInInputMode.Ui || IsPrivacyProtectedVE)
                return;

            ExInPointerReport initial = BuildAxesPointerVE(slot, slot.LastState);
            if (!HasPointerMotionVE(initial)) return;
            repeat = new CancellationTokenSource();
            _pointerRepeatVE[instanceId] = repeat;
        }

        _ = RepeatPointerVEAsync(instanceId, repeat);
    }

    private async Task RepeatPointerVEAsync(uint instanceId, CancellationTokenSource repeat)
    {
        try
        {
            while (true)
            {
                await Task.Delay(16, repeat.Token).ConfigureAwait(false);
                ExInSlot? slot;
                ExInState state;
                lock (_gateVE)
                {
                    if (!_slotsVE.TryGetValue(instanceId, out slot) ||
                        slot.OutputGeneration.ModeVE != ExInInputMode.Ui || IsPrivacyProtectedVE)
                        return;
                    state = slot.LastState;
                }

                ExInPointerReport report = BuildAxesPointerVE(slot, state);
                if (!HasPointerMotionVE(report)) return;
                if (!await slot.OutputGeneration.SendPointerAsync(report, repeat.Token).ConfigureAwait(false)) return;
                Interlocked.Increment(ref _reportsVE);
            }
        }
        catch (OperationCanceledException) when (repeat.IsCancellationRequested) { }
        catch { }
        finally
        {
            lock (_gateVE)
            {
                if (_pointerRepeatVE.TryGetValue(instanceId, out CancellationTokenSource? current) &&
                    ReferenceEquals(current, repeat))
                    _pointerRepeatVE.Remove(instanceId);
            }
            repeat.Dispose();
        }
    }

    private ExInPointerReport BuildAxesPointerVE(ExInSlot slot, ExInState state)
    {
        ExInState effective = ApplyCalibrationVE(slot.Device, state);
        bool calibrated = slot.Device.Identity is { } identity && HasCalibrationVE(identity.ProfileKey);
        ExInPointerReport report = slot.Pointer.FromAxesVE(effective, calibrated);
        if (slot.Device.Identity?.Family == ExInControllerFamily.DualShock4 &&
            effective.Buttons.HasFlag(ExInButtons.Touchpad))
            report = report with { Buttons = (byte)(report.Buttons | 1) };
        return report;
    }

    private static bool HasPointerMotionVE(ExInPointerReport report)
        => report.X != 0 || report.Y != 0 || report.Wheel != 0 || report.HorizontalWheel != 0;

    private void StopPointerRepeatVE(uint instanceId)
    {
        lock (_gateVE) StopPointerRepeatLockedVE(instanceId);
    }

    private void StopPointerRepeatLockedVE(uint instanceId)
    {
        if (_pointerRepeatVE.Remove(instanceId, out CancellationTokenSource? repeat))
            try { repeat.Cancel(); } catch { }
    }

    private void CaptureCalibrationVE(uint instanceId, ExInState state)
    {
        if (!_calibratingVE || _calibrationInstanceIdVE != instanceId) return;
        _calibrationMinVE = new(
            Math.Min(_calibrationMinVE.LeftX, state.LeftX), Math.Min(_calibrationMinVE.LeftY, state.LeftY),
            Math.Min(_calibrationMinVE.RightX, state.RightX), Math.Min(_calibrationMinVE.RightY, state.RightY),
            Math.Min(_calibrationMinVE.LeftTrigger, state.LeftTrigger),
            Math.Min(_calibrationMinVE.RightTrigger, state.RightTrigger), 0);
        _calibrationMaxVE = new(
            Math.Max(_calibrationMaxVE.LeftX, state.LeftX), Math.Max(_calibrationMaxVE.LeftY, state.LeftY),
            Math.Max(_calibrationMaxVE.RightX, state.RightX), Math.Max(_calibrationMaxVE.RightY, state.RightY),
            Math.Max(_calibrationMaxVE.LeftTrigger, state.LeftTrigger),
            Math.Max(_calibrationMaxVE.RightTrigger, state.RightTrigger), 0);
    }

    private ExInState ApplyCalibrationVE(ExInDevice device, ExInState state)
    {
        lock (_gateVE)
        {
            return device.Identity is { } identity && _loadedProfilesVE.TryGetValue(identity.ProfileKey, out ExInCalibrationProfile? profile)
                ? profile.CorrectVE(state)
                : state;
        }
    }

    private async Task ResyncPrivacyVE(bool protectedVE)
    {
        await _privacyResyncVE.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsPrivacyProtectedVE != protectedVE) return;
            ExInSlot[] slots;
            lock (_gateVE) slots = _slotsVE.Values.ToArray();

            foreach (ExInSlot slot in slots)
            {
                if (protectedVE)
                {
                    slot.Pointer.ResetTouchVE();
                    if (slot.OutputGeneration.ModeVE == ExInInputMode.Ui)
                        await slot.OutputGeneration.SendPointerAsync(default, CancellationToken.None).ConfigureAwait(false);
                    else
                        await slot.OutputGeneration.SendStateAsync(default, CancellationToken.None).ConfigureAwait(false);
                    if (slot.PointerCompanion is not null)
                        await slot.PointerCompanion.SendPointerAsync(default, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    slot.Pointer.PrimeButtonsVE(slot.LastState.Buttons);
                    await SendStateVE(slot, slot.LastState, CancellationToken.None).ConfigureAwait(false);
                    StartPointerRepeatVE(slot.Device.InstanceId);
                }
            }

            PublishVE(ExInStates.Running, null);
        }
        catch
        {
            // Privacy nunca debe derribar Video/Audio por un fallo de resync UHID.
        }
        finally { _privacyResyncVE.Release(); }
    }

    private ushort AllocateUhidIdVE()
    {
        lock (_gateVE)
        {
            HashSet<ushort> used = _slotsVE.Values.Select(slot => slot.Device.UhidId).ToHashSet();
            return Enumerable.Range(FirstUhidIdVE, MaxGamepadsVE)
                .Select(value => checked((ushort)value))
                .First(value => !used.Contains(value));
        }
    }

    private static ExInButtons ButtonFlagVE(byte button)
        => button switch
        {
            0 => ExInButtons.South,
            1 => ExInButtons.East,
            2 => ExInButtons.West,
            3 => ExInButtons.North,
            4 => ExInButtons.Back,
            5 => ExInButtons.Guide,
            6 => ExInButtons.Start,
            7 => ExInButtons.LeftStick,
            8 => ExInButtons.RightStick,
            9 => ExInButtons.LeftShoulder,
            10 => ExInButtons.RightShoulder,
            11 => ExInButtons.DPadUp,
            12 => ExInButtons.DPadDown,
            13 => ExInButtons.DPadLeft,
            14 => ExInButtons.DPadRight,
            20 => ExInButtons.Touchpad,
            _ => ExInButtons.None
        };

    private async Task CloseAllVE()
    {
        ExInSlot[] slots;
        lock (_gateVE)
        {
            slots = _slotsVE.Values.ToArray();
            foreach (CancellationTokenSource repeat in _pointerRepeatVE.Values)
                try { repeat.Cancel(); } catch { }
            _pointerRepeatVE.Clear();
            _slotsVE.Clear();
        }

        foreach (ExInSlot slot in slots)
        {
            try { await slot.OutputGeneration.DisposeAsync().ConfigureAwait(false); } catch { }
            if (slot.PointerCompanion is not null)
                try { await slot.PointerCompanion.DisposeAsync().ConfigureAwait(false); } catch { }

            _sdlVE.CloseVE(slot.Handle);
        }
    }

    private void MaybePublishVE()
    {
        long now = Environment.TickCount64;
        long previous = Interlocked.Read(ref _lastStatusPublishMsVE);
        if (previous != 0 && now - previous < StatusPublishIntervalMsVE) return;
        if (Interlocked.CompareExchange(ref _lastStatusPublishMsVE, now, previous) != previous) return;
        PublishVE(ExInStates.Running, null);
    }

    private void PublishVE(ExInStates state, string? error)
    {
        ExInStatus status;
        EventHandler<ExInStatus>? handler;

        lock (_gateVE)
        {
            string message = state switch
            {
                ExInStates.Failed => "Falló ExInEngine.",
                ExInStates.Stopped => "ExInEngine detenido.",
                _ when _slotsVE.Count == 0 => "ExInEngine activo; esperando control físico.",
                _ when _calibratingVE => "ExInEngine calibrando el control físico.",
                _ => $"ExInEngine activo: {_slotsVE.Count} control(es)."
            };

            status = new ExInStatus(
                state,
                _slotsVE.Count,
                Interlocked.Read(ref _reportsVE),
                DateTimeOffset.UtcNow,
                message,
                error,
                _diagnosticsVE.Values
                    .Select(value => IsPrivacyProtectedVE ? value.ProtectVE() : value)
                    .ToArray(),
                _batteriesVE.Values.ToArray());

            _statusVE = status;
            handler = StatusChangedVE;
        }

        handler?.Invoke(this, status);
    }

    private void ThrowIfDisposedVE()
        => ObjectDisposedException.ThrowIf(_disposedVE, this);

    private void ApplyBatteryVE(ExInEvent message)
    {
        ExInBatteryAlert? alert;
        lock (_gateVE)
        {
            if (!_slotsVE.TryGetValue(message.InstanceId, out ExInSlot? slot) || slot.Device.Identity is not { } identity)
                return;
            ExInBatteryStatus status = ExInBatteryStatus.CreateVE(
                identity.ProfileKey,
                identity.Name,
                message.BatteryPercent,
                message.BatteryState);
            _batteriesVE[identity.ProfileKey] = status;
            alert = _batteryAlertsVE.UpdateVE(status);
        }
        PublishBatteryAlertVE(alert);
        PublishVE(ExInStates.Running, null);
    }

    private void PublishBatteryAlertVE(ExInBatteryAlert? alert)
    {
        if (alert is not null) BatteryAlertVE?.Invoke(this, alert);
    }

    private static TimeSpan CurrentDiagnosticTimeVE()
        => TimeSpan.FromMilliseconds(Environment.TickCount64);

    private void ObserveDiagnosticVE(ExInSlot slot, ExInState state)
    {
        if (IsPrivacyProtectedVE || slot.DiagnosticSession is not { } session) return;
        TimeSpan now = CurrentDiagnosticTimeVE();
        if (session.IsExpiredVE(now))
        {
            if (_slotsVE.TryGetValue(slot.Device.InstanceId, out ExInSlot? current))
                _slotsVE[slot.Device.InstanceId] = current with { DiagnosticSession = null };
            return;
        }
        session.ObserveVE(state, ApplyCalibrationVE(slot.Device, state), now);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposedVE) return;
        await _modeTransitionVE.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposedVE) return;
            _disposedVE = true;
            Task privacy;
            lock (_gateVE) privacy = _privacyResyncTaskVE;
            try { await privacy.ConfigureAwait(false); } catch { }
            await StopAsync().ConfigureAwait(false);
            _sdlVE.Dispose();
        }
        finally
        {
            _modeTransitionVE.Release();
            _privacyResyncVE.Dispose();
        }
    }
}
