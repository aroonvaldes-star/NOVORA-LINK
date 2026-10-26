using NOVORA.Contracts.Input;
using NOVORA.Service;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NOVORA.ExInEngine;

/// <summary>
/// Puente event-driven Windows gamepad -> SDL3 -> UHID -> Android.
/// Bucle de salida independiente con intervalo nominal de 8 ms; conserva los flancos observados.
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
    private const int MaxDiagnosticSamplesVE = 512;
    private const int MaxPendingButtonEdgesVE = 256;
    private static readonly long CalibrationLivePublishIntervalTicksVE =
        Math.Max(1, (Stopwatch.Frequency + 59) / 60);

    private readonly INLInputOutput _output;
    private readonly ExInSdl _sdlVE;
    private readonly ExInProfileStore _profilesVE;
    private readonly Dictionary<string, ExInCalibrationProfile> _loadedProfilesVE = [];
    private readonly Dictionary<string, ExInPaddleMappings> _paddleMappingsVE = [];
    private readonly Dictionary<string, ExInDiagnosticStatus> _diagnosticsVE = [];
    private readonly Dictionary<string, ExInBatteryStatus> _batteriesVE = [];
    private readonly ExInBatteryAlerts _batteryAlertsVE = new();

    private readonly Dictionary<uint, ExInSlot> _slotsVE = [];
    private uint? _selectedInstanceVE;
    private bool _forwardSelectedOnlyVE;
    // Slots muted by "forward selected only" that already received a neutral report.
    private readonly HashSet<uint> _mutedNeutralSentVE = [];

    private readonly object _gateVE = new();
    private readonly SemaphoreSlim _modeTransitionVE = new(1, 1);
    private readonly SemaphoreSlim _privacyResyncVE = new(1, 1);
    private readonly SemaphoreSlim _calibrationOutputSyncVE = new(1, 1);

    private CancellationTokenSource? _eventsCtsVE;
    private Task? _eventsTaskVE;
    private Task? _outputTaskVE;
    private int _workerFailedVE;
    private volatile bool _runDesiredVE;

    private Task _privacyResyncTaskVE = Task.CompletedTask;

    private ExInStatus _statusVE = ExInStatus.CreateInitialVE();
    private long _reportsVE;
    private long _lastStatusPublishMsVE;
    private long _lastCalibrationLivePublishTicksVE;
    private int _privacyProtectedVE;
    private bool _disposedVE;
    private bool _calibratingVE;
    private long _calibrationOutputGenerationVE;
    private long _inputEpochVE;
    private ExInInputMode _modeVE = ExInInputMode.Game;
    private double _pointerSensitivityVE = ExInPointerSensitivity.DefaultVE;
    private bool _calibratedVE;

    private ExInState _calibrationMinVE;
    private ExInState _calibrationMaxVE;
    private ExInState _calibrationCenterVE;
    private string? _calibrationProfileKeyVE;
    private uint? _calibrationInstanceIdVE;

    private const double CalibrationDeadzoneVE = 0.05;

    private readonly Queue<long> _sampleTicksVE = new();
    private long _sampleSequenceVE;
    private DateTimeOffset? _sampledAtUtcVE;
    private double _pollingHzVE;
    private double _processingMsVE;
    private double _jitterMsVE;

    private sealed record ExInSlot(
        IntPtr Handle,
        ExInDevice Device,
        ExInState LastState,
        string PhysicalName,
        ExInDiagnosticSession? DiagnosticSession,
        ExInOutputGeneration OutputGeneration,
        ExInOutputGeneration? PointerCompanion,
        ExInPointerTranslator Pointer)
    {
        // The record snapshots share the same queue; access is protected by _gateVE.
        public Queue<ExInState> ButtonEdges { get; } = new();
    }

    public ExInManager(INLInputOutput output, NLServiceNovoraPaths paths)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        ArgumentNullException.ThrowIfNull(paths);
        _sdlVE = new ExInSdl(paths);
        _profilesVE = new ExInProfileStore(paths.ExInProfilesDirectory);
    }

    public event EventHandler<ExInStatus>? StatusChangedVE;
    public event EventHandler<ExInLiveSnapshot>? LiveSnapshotChangedVE;
    public event EventHandler<ExInBatteryAlert>? BatteryAlertVE;
    public event EventHandler<ExInOutputIssue>? OutputIssueVE;

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

    public double PointerSensitivityVE
    {
        get { lock (_gateVE) return _pointerSensitivityVE; }
    }

    public void SetPointerSensitivityVE(double value)
    {
        ExInPointerSensitivity.ValidateVE(value);
        lock (_gateVE)
            _pointerSensitivityVE = value;
    }

    /// <summary>The controller shown, calibrated and configured in the UI. Caller holds _gateVE.</summary>
    private ExInSlot? SelectedSlotLockedVE()
    {
        if (_selectedInstanceVE is uint id && _slotsVE.TryGetValue(id, out ExInSlot? selected))
            return selected;
        // Deterministic fallback: Dictionary order is not stable across removals.
        ExInSlot? first = null;
        foreach (ExInSlot slot in _slotsVE.Values)
            if (first is null || slot.Device.InstanceId < first.Device.InstanceId) first = slot;
        return first;
    }

    public IReadOnlyList<ExInControllerChoice> ControllersVE
    {
        get
        {
            lock (_gateVE)
                return _slotsVE.Values
                    .OrderBy(slot => slot.Device.InstanceId)
                    .Select((slot, index) => new ExInControllerChoice(slot.Device.InstanceId,
                        $"{index + 1}. {slot.PhysicalName}"))
                    .ToArray();
        }
    }

    public uint? SelectedControllerVE
    {
        get { lock (_gateVE) return SelectedSlotLockedVE()?.Device.InstanceId; }
    }

    /// <summary>Selects the controller followed by identity, calibration, diagnostics and paddles.</summary>
    public bool SelectControllerVE(uint instanceId)
    {
        lock (_gateVE)
        {
            if (_calibratingVE || !_slotsVE.ContainsKey(instanceId)) return false;
            if (_selectedInstanceVE == instanceId) return true;
            _selectedInstanceVE = instanceId;
        }
        WakeOutputVE();
        PublishLiveSnapshotVE();
        return true;
    }

    /// <summary>When set, only the selected controller reaches the phone; the others stay neutral.</summary>
    public bool ForwardSelectedOnlyVE
    {
        get { lock (_gateVE) return _forwardSelectedOnlyVE; }
        set
        {
            lock (_gateVE) _forwardSelectedOnlyVE = value;
            WakeOutputVE();
        }
    }

    public ExInPaddleMappings PaddleMappingsVE
    {
        get
        {
            lock (_gateVE)
            {
                ExInSlot? slot = SelectedSlotLockedVE();
                return slot?.Device.Identity is { } identity && _paddleMappingsVE.TryGetValue(identity.ProfileKey, out ExInPaddleMappings? mappings)
                    ? mappings
                    : ExInPaddleMappings.DefaultVE;
            }
        }
    }

    public bool TrySetPaddleMappingVE(ExInPaddle paddle, ExInButtons target)
    {
        if (!ExInPaddleMappings.IsAssignableVE(target)) return false;
        string profileKey;
        ExInPaddleMappings updated;

        lock (_gateVE)
        {
            if (_calibratingVE || IsPrivacyProtectedVE || SelectedSlotLockedVE()?.Device.Identity is not { } identity)
                return false;

            profileKey = identity.ProfileKey;
            ExInPaddleMappings current = _paddleMappingsVE.TryGetValue(profileKey, out ExInPaddleMappings? mappings)
                ? mappings
                : ExInPaddleMappings.DefaultVE;

            updated = current.WithVE(paddle, target);
        }

        if (!_profilesVE.SavePaddleMappingsVE(profileKey, updated)) return false;

        lock (_gateVE) _paddleMappingsVE[profileKey] = updated;
        return true;
    }

    public ExInLiveSnapshot LiveSnapshotVE
    {
        get
        {
            lock (_gateVE)
            {
                ExInSlot? slot = SelectedSlotLockedVE();
                bool calibrated = slot?.Device.Identity is { } identity && _loadedProfilesVE.ContainsKey(identity.ProfileKey);
                bool protectedVE = IsPrivacyProtectedVE;
                ExInState visibleState = protectedVE ? default : slot?.LastState ?? default;
                ExInState correctedState = protectedVE || slot is null ? default : ApplyCalibrationVE(slot.Device, slot.LastState);
                ExInCalibrationDetails? calibration = null;
                if (!protectedVE && slot?.Device.Identity is { } slotIdentity &&
                    _loadedProfilesVE.TryGetValue(slotIdentity.ProfileKey, out ExInCalibrationProfile? profile))
                    calibration = new(slotIdentity.ProfileKey, "No reportado por SDL3", profile.Deadzone,
                        profile.Center, profile.Minimum, profile.Maximum);
                ExInCalibrationProgress? progress = _calibratingVE && !protectedVE
                    ? CalibrationProgressVE(_calibrationMinVE, _calibrationMaxVE)
                    : null;
                return new(slot?.Device, slot?.PhysicalName ?? "Sin control físico", visibleState, correctedState, _calibratingVE && !protectedVE, calibrated,
                    CalibrationDeadzoneVE, slot is null ? "Esperando un control físico." :
                    protectedVE ? "Estado del control oculto por Privacy Shield." :
                    _calibratingVE ? "Mueve ambos sticks y gatillos por todo su recorrido." :
                    calibrated ? "Calibración persistente activa para este control." : "Control detectado; calibración opcional.", calibration,
                    progress, _sampleSequenceVE, _sampledAtUtcVE, _pollingHzVE, _processingMsVE, _jitterMsVE);
            }
        }
    }

    internal static ExInCalibrationProgress CalibrationProgressVE(ExInState minimum, ExInState maximum)
    {
        static int Axis(short min, short max) => (int)Math.Clamp(Math.Round((max - min) / 65535d * 100d), 0, 100);
        static int Trigger(short min, short max) => (int)Math.Clamp(Math.Round((max - min) / 32767d * 100d), 0, 100);
        return new(
            Math.Min(Axis(minimum.LeftX, maximum.LeftX), Axis(minimum.LeftY, maximum.LeftY)),
            Math.Min(Axis(minimum.RightX, maximum.RightX), Axis(minimum.RightY, maximum.RightY)),
            Trigger(minimum.LeftTrigger, maximum.LeftTrigger),
            Trigger(minimum.RightTrigger, maximum.RightTrigger));
    }

    private void RecordSampleVE(long receivedTicks)
    {
        long nowTicks = Stopwatch.GetTimestamp();
        _sampleTicksVE.Enqueue(nowTicks);
        long cutoff = nowTicks - (long)(Stopwatch.Frequency * 2d);
        while (_sampleTicksVE.Count > 1 && _sampleTicksVE.Peek() < cutoff)
            _sampleTicksVE.Dequeue();
        while (_sampleTicksVE.Count > MaxDiagnosticSamplesVE)
            _sampleTicksVE.Dequeue();

        long[] ticks = _sampleTicksVE.ToArray();
        if (ticks.Length > 1)
        {
            double duration = (ticks[^1] - ticks[0]) / (double)Stopwatch.Frequency;
            _pollingHzVE = duration > 0 ? (ticks.Length - 1) / duration : 0;
            double meanMs = duration * 1000d / (ticks.Length - 1);
            _jitterMsVE = ticks.Zip(ticks.Skip(1), (a, b) =>
                    Math.Abs((b - a) * 1000d / Stopwatch.Frequency - meanMs))
                .DefaultIfEmpty(0).Average();
        }

        _processingMsVE = Math.Max(0, (nowTicks - receivedTicks) * 1000d / Stopwatch.Frequency);
        _sampledAtUtcVE = DateTimeOffset.UtcNow;
        _sampleSequenceVE++;
    }

    public bool BeginCalibrationVE()
    {
        lock (_gateVE)
        {
            ExInSlot? slot = SelectedSlotLockedVE();
            if (slot?.Device.Identity is not { } identity || IsPrivacyProtectedVE) return false;
            _calibrationCenterVE = slot.LastState;
            _calibrationMinVE = slot.LastState;
            _calibrationMaxVE = slot.LastState;
            _calibratingVE = true;
            _calibratedVE = false;
            _calibrationProfileKeyVE = identity.ProfileKey;
            _calibrationInstanceIdVE = slot.Device.InstanceId;
            _calibrationOutputGenerationVE++;
            _inputEpochVE++;
            foreach (ExInSlot current in _slotsVE.Values) current.ButtonEdges.Clear();
        }
        RequestCalibrationOutputSyncVE();
        PublishVE(ExInStates.Running, null);
        PublishLiveSnapshotVE();
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
            _calibrationOutputGenerationVE++;
            _inputEpochVE++;
            foreach (ExInSlot current in _slotsVE.Values) current.ButtonEdges.Clear();
        }
        RequestCalibrationOutputSyncVE();
        PublishVE(ExInStates.Running, null);
        PublishLiveSnapshotVE();
        return saved;
    }

    public void ResetCalibrationVE()
    {
        string? profileKey;
        lock (_gateVE)
        {
            profileKey = SelectedSlotLockedVE()?.Device.Identity?.ProfileKey;
            _calibratingVE = false;
            _calibratedVE = false;
            _calibrationProfileKeyVE = null;
            _calibrationInstanceIdVE = null;
            _calibrationOutputGenerationVE++;
            _inputEpochVE++;
            foreach (ExInSlot current in _slotsVE.Values) current.ButtonEdges.Clear();
            if (profileKey is not null) _loadedProfilesVE.Remove(profileKey);
            if (profileKey is not null) _profilesVE.DeleteAllVE(profileKey);
        }
        RequestCalibrationOutputSyncVE();
        PublishVE(ExInStates.Running, null);
        PublishLiveSnapshotVE();
    }

    public bool BeginDiagnosticsVE()
    {
        lock (_gateVE)
        {
            ExInSlot? slot = SelectedSlotLockedVE();
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
        if (_eventsTaskVE is not null) return;

        _runDesiredVE = true;
        Interlocked.Exchange(ref _workerFailedVE, 0);
        try
        {
            _sdlVE.InitializeVE();
            cancellationToken.ThrowIfCancellationRequested();
            _eventsCtsVE = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await DiscoverInitialVE(_eventsCtsVE.Token).ConfigureAwait(false);
            CancellationToken eventsToken = _eventsCtsVE.Token;
            _eventsTaskVE = Task.Run(() => RunEventsVE(eventsToken), CancellationToken.None);
            _outputTaskVE = Task.Run(() => RunOutputLoopVEAsync(eventsToken), CancellationToken.None);
            PublishVE(ExInStates.Running, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            FailWorkerVE(ex);
            throw;
        }
    }

    public async Task SynchronizeDevicesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        if (_eventsTaskVE is null) return;

        foreach (uint id in _sdlVE.GetDetectedIdsVE())
        {
            bool known;
            lock (_gateVE)
                known = _slotsVE.ContainsKey(id);

            if (!known)
                await OpenDeviceVE(id, cancellationToken).ConfigureAwait(false);
        }
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
                {
                    try { await slot.PointerCompanion.SynchronizeAsync(cancellationToken).ConfigureAwait(false); }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        ReportOutputIssueVE(slot, "SynchronizePointerCompanion", ex, recoverable: true);
                    }
                }

                await SendStateVE(slot, slot.LastState, cancellationToken).ConfigureAwait(false);
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
            lock (_gateVE)
            {
                _inputEpochVE++;
                slots = _slotsVE.Values.ToArray();
                foreach (ExInSlot slot in slots) slot.ButtonEdges.Clear();
            }

            foreach (ExInSlot slot in slots)
            {
                await slot.OutputGeneration.SynchronizeAsync(cancellationToken).ConfigureAwait(false);

                if (slot.OutputGeneration.ModeVE == ExInInputMode.Game && slot.PointerCompanion is not null)
                {
                    try { await slot.PointerCompanion.SynchronizeAsync(cancellationToken).ConfigureAwait(false); }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        ReportOutputIssueVE(slot, "BindPointerCompanion", ex, recoverable: true);
                    }
                }

                await SendStateVE(slot, slot.LastState, cancellationToken).ConfigureAwait(false);
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
            lock (_gateVE)
            {
                _inputEpochVE++;
                slots = _slotsVE.Values.ToArray();
                foreach (ExInSlot slot in slots) slot.ButtonEdges.Clear();
            }

            foreach (ExInSlot slot in slots)
            {
                try { await slot.OutputGeneration.DetachAsync(cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    ReportOutputIssueVE(slot, "DetachOutput", ex, recoverable: true);
                }

                if (slot.PointerCompanion is not null)
                {
                    try { await slot.PointerCompanion.DetachAsync(cancellationToken).ConfigureAwait(false); }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        ReportOutputIssueVE(slot, "DetachPointerCompanion", ex, recoverable: true);
                    }
                }
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
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    ReportOutputIssueVE(slot, "DestroyOutput", ex, recoverable: true);
                }

                if (slot.PointerCompanion is not null)
                {
                    try { await slot.PointerCompanion.DetachAsync(cancellationToken).ConfigureAwait(false); }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        ReportOutputIssueVE(slot, "DestroyPointerCompanion", ex, recoverable: true);
                    }
                }
            }
        }
        finally { _modeTransitionVE.Release(); }
    }

    public Task<ExInModeResult> SetModeAsync(ExInInputMode mode, CancellationToken cancellationToken = default)
        => SetModeVEAsync(mode, cancellationToken);

    private async Task<ExInModeResult> SetModeVEAsync(ExInInputMode? requestedMode, CancellationToken cancellationToken)
    {
        ThrowIfDisposedVE();
        await _modeTransitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposedVE();

            ExInSlot[] slots;
            ExInInputMode previous;
            ExInInputMode mode;

            lock (_gateVE)
            {
                if (_calibratingVE)
                    return new(false, _modeVE, 0, "La calibración está activa. Termínala o restablécela antes de cambiar el modo.");

                slots = _slotsVE.Values.ToArray();
                previous = _modeVE;
                mode = requestedMode ?? (previous == ExInInputMode.Game ? ExInInputMode.Ui : ExInInputMode.Game);
                _inputEpochVE++;
                foreach (ExInSlot slot in slots) slot.ButtonEdges.Clear();
            }

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
                {
                    if (_slotsVE.TryGetValue(slot.Device.InstanceId, out ExInSlot? current))
                        _slotsVE[slot.Device.InstanceId] = current with { Device = slot.OutputGeneration.ProfileVE.Device };
                }
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

        lock (_gateVE)
        {
            _inputEpochVE++;
            foreach (ExInSlot slot in _slotsVE.Values) slot.ButtonEdges.Clear();
        }

        if (protectedVE)
        {
            lock (_gateVE)
            {
                foreach ((uint id, ExInSlot slot) in _slotsVE.ToArray())
                {
                    slot.ButtonEdges.Clear();
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
        if (_eventsTaskVE is null) return;

        IReadOnlyList<uint> detected = _sdlVE.GetDetectedIdsVE();
        HashSet<uint> detectedSet = detected.ToHashSet();

        uint[] stale;
        lock (_gateVE)
        {
            stale = _slotsVE.Keys
                .Where(id => !detectedSet.Contains(id))
                .ToArray();
        }

        foreach (uint id in stale)
        {
            await RemoveDeviceVE(id, cancellationToken).ConfigureAwait(false);
        }

        foreach (uint id in detected)
        {
            await OpenDeviceVE(id, cancellationToken).ConfigureAwait(false);
            await RefreshDeviceVE(id, cancellationToken).ConfigureAwait(false);
        }

        // Slots opened before the Android output was ready never created their UHID device;
        // RefreshDeviceVE only sends state, which SendStateAsync drops silently while uncreated.
        await SynchronizeOutputAsync(cancellationToken).ConfigureAwait(false);

        PublishVE(ExInStates.Running, null);
    }

    public async Task StopAsync()
    {
        _runDesiredVE = false;
        CancellationTokenSource? cts = _eventsCtsVE;
        Task? taskEvents = _eventsTaskVE;
        Task? taskOutput = _outputTaskVE;

        _eventsCtsVE = null;
        _eventsTaskVE = null;
        _outputTaskVE = null;

        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
        }

        if (taskEvents is not null)
        {
            try { await taskEvents.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch { }
        }

        if (taskOutput is not null)
        {
            try { await taskOutput.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch { }
        }

        cts?.Dispose();
        await CloseAllVE().ConfigureAwait(false);
        Interlocked.Exchange(ref _workerFailedVE, 0);
        PublishVE(ExInStates.Stopped, null);
    }

    // Input changes wake the output loop at once; the interval is only a fallback heartbeat.
    // A PeriodicTimer(8 ms) on Windows fires about every 12 ms, which added that delay to every report.
    private static readonly TimeSpan OutputFallbackIntervalVE = TimeSpan.FromMilliseconds(8);
    private readonly SemaphoreSlim _outputWakeVE = new(0, 1);

    private void WakeOutputVE()
    {
        if (_outputWakeVE.CurrentCount == 0)
        {
            try { _outputWakeVE.Release(); }
            catch (SemaphoreFullException) { }
        }
    }

    private async Task RunOutputLoopVEAsync(CancellationToken cancellationToken)
    {
        // The fallback wait and the HID pipe scheduling follow the system timer resolution.
        using IDisposable timerResolution = NLServiceTimerResolution.Acquire();
        try
        {
            while (true)
            {
                await _outputWakeVE.WaitAsync(OutputFallbackIntervalVE, cancellationToken).ConfigureAwait(false);
                // Removal, rebinding and mode changes hold the same semaphore.
                // No output generation can be disposed between this snapshot and send.
                await _modeTransitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    ExInSlot[] slots;
                    lock (_gateVE) slots = _slotsVE.Values.ToArray();
                    foreach (ExInSlot slot in slots)
                    {
                        ExInState[] edges;
                        ExInState latest;
                        long epoch;
                        bool muted;
                        lock (_gateVE)
                        {
                            latest = _slotsVE[slot.Device.InstanceId].LastState;
                            epoch = _inputEpochVE;
                            edges = slot.ButtonEdges.ToArray();
                            slot.ButtonEdges.Clear();
                            muted = _forwardSelectedOnlyVE &&
                                SelectedSlotLockedVE()?.Device.InstanceId != slot.Device.InstanceId;
                            if (!muted) _mutedNeutralSentVE.Remove(slot.Device.InstanceId);
                        }
                        if (muted)
                        {
                            // Release whatever was held when the controller was deselected, once.
                            bool first;
                            lock (_gateVE) first = _mutedNeutralSentVE.Add(slot.Device.InstanceId);
                            if (first)
                                await SendStateVE(slot, default, cancellationToken, includePointerMotion: false, expectedInputEpoch: epoch).ConfigureAwait(false);
                            continue;
                        }
                        foreach (ExInState edge in edges)
                            await SendStateVE(slot, latest with { Buttons = edge.Buttons }, cancellationToken, includePointerMotion: false, expectedInputEpoch: epoch).ConfigureAwait(false);
                        await SendStateVE(slot, latest, cancellationToken, expectedInputEpoch: epoch).ConfigureAwait(false);
                    }
                }
                finally { _modeTransitionVE.Release(); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            FailWorkerVE(ex);
        }
    }

    private void FailWorkerVE(Exception exception)
    {
        if (!_runDesiredVE) return;
        if (Interlocked.Exchange(ref _workerFailedVE, 1) == 0)
        {
            try { _eventsCtsVE?.Cancel(); } catch (ObjectDisposedException) { }
            PublishVE(ExInStates.Failed, exception.Message);
        }
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
                        ApplyAxisVE(message);
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
            if (!cancellationToken.IsCancellationRequested)
                FailWorkerVE(new IOException("La fuente de eventos ExIn terminó inesperadamente."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            FailWorkerVE(ex);
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

            ExInOutputProfile FactoryVE(ExInInputMode mode) => ExInOutputProfile.CreateVE(mode, identity, id, uhidId);
            generation = new(_output, FactoryVE(_modeVE), FactoryVE);
            ExInDevice device = generation.ProfileVE.Device;

            if (identity.Family == ExInControllerFamily.DualShock4)
            {
                ushort pointerId = checked((ushort)(uhidId + PointerCompanionOffsetVE));
                ExInOutputProfile pointerProfile = ExInOutputProfile.CreateVE(ExInInputMode.Ui, identity, id, pointerId);
                pointerCompanion = new(_output, pointerProfile, _ => pointerProfile);
            }

            // A connected controller's saved profile must never be adopted by another one.
            string[] keysInUse;
            lock (_gateVE)
                keysInUse = _slotsVE.Values
                    .Select(slot => slot.Device.Identity?.ProfileKey)
                    .OfType<string>()
                    .ToArray();
            ExInCalibrationProfile? profile = _profilesVE.LoadVE(identity.ProfileKey, keysInUse);
            ExInPaddleMappings paddleMappings = _profilesVE.LoadPaddleMappingsVE(identity.ProfileKey);
            ExInBatteryStatus battery = _sdlVE.GetBatteryStatusVE(handle, identity);

            await generation.CreateAsync(cancellationToken).ConfigureAwait(false);

            if (_modeVE == ExInInputMode.Game && pointerCompanion is not null)
                try { await pointerCompanion.CreateAsync(cancellationToken).ConfigureAwait(false); } catch { }

            ExInState state = _sdlVE.ReadStateVE(handle);
            ExInBatteryAlert? batteryAlert;
            ExInPointerTranslator pointer = new();

            lock (_gateVE)
            {
                if (profile is not null) _loadedProfilesVE[identity.ProfileKey] = profile;
                _paddleMappingsVE[identity.ProfileKey] = paddleMappings;
                _batteriesVE[identity.ProfileKey] = battery;

                batteryAlert = _batteryAlertsVE.UpdateVE(battery);

                _slotsVE[id] = new ExInSlot(
                    handle, device, state, physicalName, null,
                    generation, pointerCompanion, pointer);
            }

            PublishBatteryAlertVE(batteryAlert);

            ExInSlot created;
            lock (_gateVE) created = _slotsVE[id];

            await SendStateVE(created, state, cancellationToken).ConfigureAwait(false);

            PublishVE(ExInStates.Running, null);
        }
        catch
        {
            lock (_gateVE) _slotsVE.Remove(id);
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

            slot.ButtonEdges.Clear();
            _mutedNeutralSentVE.Remove(id);

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
        await _modeTransitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await RefreshDeviceCoreVE(id, cancellationToken).ConfigureAwait(false); }
        finally { _modeTransitionVE.Release(); }
    }

    private async Task RefreshDeviceCoreVE(uint id, CancellationToken cancellationToken)
    {
        ExInSlot? slot;
        lock (_gateVE) _slotsVE.TryGetValue(id, out slot);
        if (slot is null) return;

        ExInState state = _sdlVE.ReadStateVE(slot.Handle);
        lock (_gateVE)
        {
            if (_slotsVE.TryGetValue(id, out ExInSlot? current))
                _slotsVE[id] = current with
                {
                    LastState = state
                };
        }

        await SendStateVE(slot, state, cancellationToken).ConfigureAwait(false);
    }

    private void ApplyAxisVE(ExInEvent message)
    {
        long receivedTicks = Stopwatch.GetTimestamp();

        lock (_gateVE)
        {
            if (!_slotsVE.TryGetValue(message.InstanceId, out ExInSlot? slot) || slot is null)
                return;

            // SDL may already expose a later button state. Button events own buttons.
            ExInState next = _sdlVE.ReadStateVE(slot.Handle) with { Buttons = slot.LastState.Buttons };
            if (next == slot.LastState) return;

            _slotsVE[message.InstanceId] = slot with
            {
                LastState = next
            };

            CaptureCalibrationVE(message.InstanceId, next);
            ObserveDiagnosticVE(slot, next);
            RecordSampleVE(receivedTicks);
            WakeOutputVE();
        }

        MaybePublishVE();
    }

    private async Task ApplyButtonVE(ExInEvent message, CancellationToken cancellationToken)
    {
        long receivedTicks = Stopwatch.GetTimestamp();
        ExInButtons flag = ButtonFlagVE(message.Button);
        if (flag == ExInButtons.None) return;

        ExInSlot? slot;
        ExInState next;
        bool toggleMode;

        lock (_gateVE)
        {
            if (!_slotsVE.TryGetValue(message.InstanceId, out slot) || slot is null)
                return;

            ExInButtons previousButtons = slot.LastState.Buttons;
            next = slot.LastState with
            {
                Buttons = message.Pressed ? previousButtons | flag : previousButtons & ~flag
            };
            if (next == slot.LastState) return;

            toggleMode = !IsPrivacyProtectedVE && !_calibratingVE && next.Buttons.HasFlag(ExInButtons.Back) &&
                         next.Buttons.HasFlag(ExInButtons.Start) &&
                         !(previousButtons.HasFlag(ExInButtons.Back) &&
                           previousButtons.HasFlag(ExInButtons.Start));

            _slotsVE[message.InstanceId] = slot with
            {
                LastState = next
            };

            if (!IsPrivacyProtectedVE && !_calibratingVE && _output.IsReady)
            {
                if (slot.ButtonEdges.Count >= MaxPendingButtonEdgesVE)
                    throw new InvalidOperationException("La salida ExIn no pudo consumir los flancos del control.");
                slot.ButtonEdges.Enqueue(next);
            }

            ObserveDiagnosticVE(slot, next);
            RecordSampleVE(receivedTicks);
            WakeOutputVE();
        }

        if (toggleMode)
        {
            await SetModeVEAsync(null, cancellationToken).ConfigureAwait(false);
        }

        MaybePublishVE();
    }

    private async Task SendStateVE(ExInSlot slot, ExInState state, CancellationToken cancellationToken, bool includePointerMotion = true, long? expectedInputEpoch = null)
    {
        bool appsLocked;
        lock (_gateVE) appsLocked = _calibratingVE || (expectedInputEpoch.HasValue && expectedInputEpoch.Value != _inputEpochVE);

        ExInState effective = IsPrivacyProtectedVE || appsLocked
            ? default
            : ApplyCalibrationVE(slot.Device, state);

        effective = ApplyPaddleMappingsVE(slot.Device, effective);

        bool sent;
        if (slot.OutputGeneration.ModeVE == ExInInputMode.Ui)
        {
            bool calibrated = slot.Device.Identity is { } identity && HasCalibrationVE(identity.ProfileKey);
            ExInPointerReport pointer;
            lock (_gateVE)
                pointer = slot.Pointer.FromXboxVE(effective,
                    calibrated || IsPrivacyProtectedVE || appsLocked, _pointerSensitivityVE);
            if (!includePointerMotion) pointer = pointer with { X = 0, Y = 0, Wheel = 0, HorizontalWheel = 0 };

            if (slot.Device.Identity?.Family == ExInControllerFamily.DualShock4 &&
                effective.Buttons.HasFlag(ExInButtons.Touchpad))
                pointer = pointer with { Buttons = (byte)(pointer.Buttons | 1) };

            sent = await slot.OutputGeneration.SendPointerAsync(pointer, cancellationToken).ConfigureAwait(false);

            if (sent && pointer.Action != ExInUiAction.None && !IsPrivacyProtectedVE && !IsCalibrationInputLockedVE() &&
                _output is INLInputUiOutput uiOutput)
                await uiOutput.SendUiActionAsync((NLInputUiAction)pointer.Action, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            sent = await slot.OutputGeneration.SendStateAsync(effective, cancellationToken).ConfigureAwait(false);

            if (slot.PointerCompanion is not null && !IsPrivacyProtectedVE)
            {
                ExInPointerReport pointer;
                lock (_gateVE)
                    pointer = slot.Pointer.FromAxesVE(effective, inputAlreadyCalibrated: true, sensitivity: _pointerSensitivityVE);
                if (!includePointerMotion) pointer = pointer with { X = 0, Y = 0, Wheel = 0, HorizontalWheel = 0 };

                if (effective.Buttons.HasFlag(ExInButtons.Touchpad))
                    pointer = pointer with { Buttons = (byte)(pointer.Buttons | 1) };

                try
                {
                    if (await slot.PointerCompanion.SendPointerAsync(pointer, cancellationToken).ConfigureAwait(false))
                        Interlocked.Increment(ref _reportsVE);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    ReportOutputIssueVE(slot, "SendPointerCompanion", ex, recoverable: true);
                    try { await slot.PointerCompanion.DetachAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    FailWorkerVE(ex);
                }
            }
        }

        if (!sent) return;
        Interlocked.Increment(ref _reportsVE);
    }

    private async Task ApplyTouchpadVE(ExInEvent message, CancellationToken cancellationToken)
    {
        await _modeTransitionVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await ApplyTouchpadCoreVE(message, cancellationToken).ConfigureAwait(false); }
        finally { _modeTransitionVE.Release(); }
    }

    private async Task ApplyTouchpadCoreVE(ExInEvent message, CancellationToken cancellationToken)
    {
        ExInSlot? slot;
        lock (_gateVE) _slotsVE.TryGetValue(message.InstanceId, out slot);

        if (slot is null || IsPrivacyProtectedVE || IsCalibrationInputLockedVE()) return;

        ExInOutputGeneration? pointerOutput = slot.OutputGeneration.ModeVE == ExInInputMode.Ui
            ? slot.OutputGeneration
            : slot.PointerCompanion;

        if (pointerOutput is null) return;

        bool physicalClick = slot.LastState.Buttons.HasFlag(ExInButtons.Touchpad);
        ExInPointerReport report;

        try
        {
            lock (_gateVE)
            {
                if (IsPrivacyProtectedVE || _calibratingVE) return;
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
        }
        catch (ArgumentException)
        {
            lock (_gateVE) slot.Pointer.ResetTouchVE();
            return;
        }
        catch (InvalidOperationException)
        {
            lock (_gateVE) slot.Pointer.ResetTouchVE();
            return;
        }

        try
        {
            if (await pointerOutput.SendPointerAsync(report, cancellationToken).ConfigureAwait(false))
                Interlocked.Increment(ref _reportsVE);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ReportOutputIssueVE(slot, "SendTouchpad", ex, recoverable: true);
            lock (_gateVE) slot.Pointer.ResetTouchVE();
            if (!ReferenceEquals(pointerOutput, slot.OutputGeneration))
                try { await pointerOutput.DetachAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            FailWorkerVE(ex);
        }
    }

    private bool HasCalibrationVE(string profileKey)
    {
        lock (_gateVE) return _loadedProfilesVE.ContainsKey(profileKey);
    }

    private bool IsCalibrationInputLockedVE()
    {
        lock (_gateVE) return _calibratingVE;
    }

    private void RequestCalibrationOutputSyncVE()
    {
        long generation;
        lock (_gateVE) generation = _calibrationOutputGenerationVE;
        _ = SynchronizeCalibrationOutputVEAsync(generation);
    }

    private async Task SynchronizeCalibrationOutputVEAsync(long generation)
    {
        await _calibrationOutputSyncVE.WaitAsync().ConfigureAwait(false);
        await _modeTransitionVE.WaitAsync().ConfigureAwait(false);
        try
        {
            ExInSlot[] slots;
            bool blocked;
            lock (_gateVE)
            {
                if (generation != _calibrationOutputGenerationVE) return;
                blocked = _calibratingVE;
                slots = _slotsVE.Values.ToArray();

                foreach (ExInSlot slot in slots) slot.ButtonEdges.Clear();
            }

            foreach (ExInSlot slot in slots)
            {
                try
                {
                    lock (_gateVE) slot.Pointer.ResetTouchVE();
                    if (blocked)
                    {
                        if (slot.OutputGeneration.ModeVE == ExInInputMode.Ui)
                            await slot.OutputGeneration.SendPointerAsync(default, CancellationToken.None).ConfigureAwait(false);
                        else
                            await slot.OutputGeneration.SendStateAsync(default, CancellationToken.None).ConfigureAwait(false);

                        if (slot.PointerCompanion is not null)
                            await slot.PointerCompanion.SendPointerAsync(default, CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        await SendStateVE(slot, slot.LastState, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    ReportOutputIssueVE(slot, "SynchronizeCalibrationOutput", ex, recoverable: true);
                    FailWorkerVE(ex);
                }
            }
        }
        finally
        {
            _modeTransitionVE.Release();
            _calibrationOutputSyncVE.Release();
        }
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

    private ExInState ApplyPaddleMappingsVE(ExInDevice device, ExInState state)
    {
        lock (_gateVE)
        {
            return device.Identity is { } identity && _paddleMappingsVE.TryGetValue(identity.ProfileKey, out ExInPaddleMappings? mappings)
                ? mappings.ApplyVE(state)
                : ExInPaddleMappings.DefaultVE.ApplyVE(state);
        }
    }

    private async Task ResyncPrivacyVE(bool protectedVE)
    {
        await _privacyResyncVE.WaitAsync().ConfigureAwait(false);
        await _modeTransitionVE.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsPrivacyProtectedVE != protectedVE) return;

            ExInSlot[] slots;
            lock (_gateVE) slots = _slotsVE.Values.ToArray();

            foreach (ExInSlot slot in slots)
            {
                if (protectedVE)
                {
                    lock (_gateVE) slot.Pointer.ResetTouchVE();
                    if (slot.OutputGeneration.ModeVE == ExInInputMode.Ui)
                        await slot.OutputGeneration.SendPointerAsync(default, CancellationToken.None).ConfigureAwait(false);
                    else
                        await slot.OutputGeneration.SendStateAsync(default, CancellationToken.None).ConfigureAwait(false);

                    if (slot.PointerCompanion is not null)
                        await slot.PointerCompanion.SendPointerAsync(default, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    lock (_gateVE) slot.Pointer.PrimeButtonsVE(slot.LastState.Buttons);
                    await SendStateVE(slot, slot.LastState, CancellationToken.None).ConfigureAwait(false);
                }
            }

            PublishVE(ExInStates.Running, null);
        }
        catch (Exception ex)
        {
            // Privacy nunca debe derribar Video/Audio por un fallo de resync UHID.
            FailWorkerVE(ex);
        }
        finally
        {
            _modeTransitionVE.Release();
            _privacyResyncVE.Release();
        }
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
            16 => ExInButtons.P3,
            17 => ExInButtons.P1,
            18 => ExInButtons.P4,
            19 => ExInButtons.P2,
            20 => ExInButtons.Touchpad,
            _ => ExInButtons.None
        };

    private async Task CloseAllVE()
    {
        await _modeTransitionVE.WaitAsync().ConfigureAwait(false);
        try { await CloseAllCoreVE().ConfigureAwait(false); }
        finally { _modeTransitionVE.Release(); }
    }

    private async Task CloseAllCoreVE()
    {
        ExInSlot[] slots;
        lock (_gateVE)
        {
            slots = _slotsVE.Values.ToArray();

            _slotsVE.Clear();
            _mutedNeutralSentVE.Clear();
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

        if (IsCalibrationInputLockedVE())
        {
            long liveNow = Stopwatch.GetTimestamp();
            long previousLive = Interlocked.Read(ref _lastCalibrationLivePublishTicksVE);

            if ((previousLive == 0 || liveNow - previousLive >= CalibrationLivePublishIntervalTicksVE) &&
                Interlocked.CompareExchange(ref _lastCalibrationLivePublishTicksVE, liveNow, previousLive) == previousLive)
                PublishLiveSnapshotVE();
        }

        long previous = Interlocked.Read(ref _lastStatusPublishMsVE);
        if (previous != 0 && now - previous < StatusPublishIntervalMsVE) return;

        if (Interlocked.CompareExchange(ref _lastStatusPublishMsVE, now, previous) != previous) return;

        PublishVE(ExInStates.Running, null);
    }

    private void PublishLiveSnapshotVE()
    {
        EventHandler<ExInLiveSnapshot>? handler = LiveSnapshotChangedVE;
        handler?.Invoke(this, LiveSnapshotVE);
    }

    private void ReportOutputIssueVE(ExInSlot slot, string operation, Exception exception, bool recoverable)
    {
        EventHandler<ExInOutputIssue>? handler = OutputIssueVE;
        if (handler is null) return;

        try
        {
            handler(this, new ExInOutputIssue(
                slot.Device.InstanceId,
                operation,
                exception.Message,
                DateTimeOffset.UtcNow,
                recoverable));
        }
        catch
        {
            // Los observadores no pueden interrumpir el procesamiento del motor.
        }
    }

    private void PublishVE(ExInStates state, string? error)
    {
        ExInStatus status;
        EventHandler<ExInStatus>? handler;

        lock (_gateVE)
        {
            if (state == ExInStates.Running && (!_runDesiredVE || Volatile.Read(ref _workerFailedVE) != 0)) return;
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
        _disposedVE = true;
        // Cancel workers before waiting for the transition semaphore they also use.
        await StopAsync().ConfigureAwait(false);
        Task privacy;
        lock (_gateVE) privacy = _privacyResyncTaskVE;
        try { await privacy.ConfigureAwait(false); } catch { }
        _sdlVE.Dispose();
    }
}
