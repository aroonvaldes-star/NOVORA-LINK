using NOVORA.Service;
using NOVORA.VisionEngine.Control;

namespace NOVORA.VisionEngine.Gamepad;

/// <summary>
/// Puente event-driven Windows gamepad -> SDL3 -> UHID -> Android.
/// No usa polling ni Task.Delay para leer controles.
/// </summary>
public sealed class VEGamepadManager : IAsyncDisposable
{
    private const int MaxGamepadsVE = 8;
    private const ushort FirstUhidIdVE = 3;
    private const ushort UhidVendorIdVE = 0x045E;
    private const ushort UhidProductIdVE = 0x028E;
    private const string UhidDeviceNameVE = "Microsoft X-Box 360 Pad";
    private const long StatusPublishIntervalMsVE = 1000;

    private readonly VEControlManager _controlVE;
    private readonly VEGamepadSdl _sdlVE;
    private readonly Dictionary<uint, VEGamepadSlot> _slotsVE = [];
    private readonly object _gateVE = new();

    private CancellationTokenSource? _eventsCtsVE;
    private Task? _eventsTaskVE;
    private VEGamepadStatus _statusVE = VEGamepadStatus.CreateInitialVE();
    private long _reportsVE;
    private long _lastStatusPublishMsVE;
    private int _privacyProtectedVE;
    private bool _disposedVE;

    private sealed record VEGamepadSlot(
        IntPtr Handle,
        VEGamepadDevice Device,
        VEGamepadState LastState,
        string PhysicalName);

    public VEGamepadManager(VEControlManager control, NLServiceNovoraPaths paths)
    {
        _controlVE = control ?? throw new ArgumentNullException(nameof(control));
        _sdlVE = new VEGamepadSdl(paths ?? throw new ArgumentNullException(nameof(paths)));
    }

    public event EventHandler<VEGamepadStatus>? StatusChangedVE;

    public VEGamepadStatus StatusVE
    {
        get { lock (_gateVE) return _statusVE; }
    }

    public bool IsPrivacyProtectedVE => Volatile.Read(ref _privacyProtectedVE) != 0;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        if (!_controlVE.IsReadyVE)
            throw new InvalidOperationException("GamepadVE necesita ControlVE activo.");

        if (_eventsTaskVE is not null)
            return;

        _sdlVE.InitializeVE();
        cancellationToken.ThrowIfCancellationRequested();

        _eventsCtsVE = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await DiscoverInitialVE(_eventsCtsVE.Token).ConfigureAwait(false);

        _eventsTaskVE = RunEventsVE(_eventsCtsVE.Token);
        PublishVE(VEGamepadStates.Running, null);
    }

    public void SetPrivacyProtectedVE(bool protectedVE)
    {
        int next = protectedVE ? 1 : 0;
        int previous = Interlocked.Exchange(ref _privacyProtectedVE, next);
        if (previous == next) return;

        _ = ResyncPrivacyVE(protectedVE);
    }

    public async Task ResyncConnectedDevicesVEAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();

        if (_eventsTaskVE is null ||
            !_controlVE.IsReadyVE)
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
            VEGamepadStates.Running,
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
        PublishVE(VEGamepadStates.Stopped, null);
    }

    private async Task RunEventsVE(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (VEGamepadEvent message in _sdlVE.ReadEventsVE(cancellationToken).ConfigureAwait(false))
            {
                switch (message.Type)
                {
                    case VEGamepadTypeEvent.Added:
                        await OpenDeviceVE(message.InstanceId, cancellationToken).ConfigureAwait(false);
                        break;

                    case VEGamepadTypeEvent.Removed:
                        await RemoveDeviceVE(message.InstanceId, cancellationToken).ConfigureAwait(false);
                        break;

                    case VEGamepadTypeEvent.Remapped:
                        await RefreshDeviceVE(message.InstanceId, cancellationToken).ConfigureAwait(false);
                        break;

                    case VEGamepadTypeEvent.Axis:
                        await ApplyAxisVE(message, cancellationToken).ConfigureAwait(false);
                        break;

                    case VEGamepadTypeEvent.Button:
                        await ApplyButtonVE(message, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            PublishVE(VEGamepadStates.Failed, ex.Message);
        }
    }

    private async Task DiscoverInitialVE(CancellationToken cancellationToken)
    {
        foreach (uint id in _sdlVE.GetDetectedIdsVE())
            await OpenDeviceVE(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task OpenDeviceVE(uint id, CancellationToken cancellationToken)
    {
        lock (_gateVE)
        {
            if (_slotsVE.ContainsKey(id) || _slotsVE.Count >= MaxGamepadsVE)
                return;
        }

        IntPtr handle = _sdlVE.OpenVE(id);
        if (handle == IntPtr.Zero) return;

        try
        {
            ushort uhidId = AllocateUhidIdVE();
            string physicalName = _sdlVE.GetNameVE(handle);
            VEGamepadDevice device = new(
                id,
                uhidId,
                UhidDeviceNameVE,
                UhidVendorIdVE,
                UhidProductIdVE,
                DateTimeOffset.UtcNow);

            await _controlVE.SendAsync(
                    VEControlMessage.UhidCreateVE(
                        uhidId,
                        UhidVendorIdVE,
                        UhidProductIdVE,
                        UhidDeviceNameVE,
                        VEGamepadDescriptor.GetVE()),
                    cancellationToken)
                .ConfigureAwait(false);

            VEGamepadState state = _sdlVE.ReadStateVE(handle);

            lock (_gateVE)
                _slotsVE[id] = new VEGamepadSlot(handle, device, state, physicalName);

            await SendStateVE(device, state, cancellationToken).ConfigureAwait(false);
            PublishVE(VEGamepadStates.Running, null);
        }
        catch
        {
            _sdlVE.CloseVE(handle);
            throw;
        }
    }

    private async Task RemoveDeviceVE(uint id, CancellationToken cancellationToken)
    {
        VEGamepadSlot? slot;
        lock (_gateVE)
        {
            if (!_slotsVE.Remove(id, out slot))
                return;
        }

        if (slot is null) return;

        try
        {
            if (_controlVE.IsReadyVE)
                await _controlVE.SendAsync(VEControlMessage.UhidDestroyVE(slot.Device.UhidId), cancellationToken).ConfigureAwait(false);
        }
        catch { }
        finally
        {
            _sdlVE.CloseVE(slot.Handle);
            PublishVE(VEGamepadStates.Running, null);
        }
    }

    private async Task RefreshDeviceVE(uint id, CancellationToken cancellationToken)
    {
        VEGamepadSlot? slot;
        lock (_gateVE) _slotsVE.TryGetValue(id, out slot);
        if (slot is null) return;

        VEGamepadState state = _sdlVE.ReadStateVE(slot.Handle);
        lock (_gateVE)
        {
            if (_slotsVE.TryGetValue(id, out VEGamepadSlot? current))
                _slotsVE[id] = current with { LastState = state };
        }

        await SendStateVE(slot.Device, state, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyAxisVE(VEGamepadEvent message, CancellationToken cancellationToken)
    {
        VEGamepadSlot? slot;
        VEGamepadState next;

        lock (_gateVE)
        {
            if (!_slotsVE.TryGetValue(message.InstanceId, out slot) || slot is null)
                return;

            VEGamepadState current = slot.LastState;
            next = message.Axis switch
            {
                0 => current with { LeftX = message.AxisValue },
                1 => current with { LeftY = message.AxisValue },
                2 => current with { RightX = message.AxisValue },
                3 => current with { RightY = message.AxisValue },
                4 => current with { LeftTrigger = Math.Max((short)0, message.AxisValue) },
                5 => current with { RightTrigger = Math.Max((short)0, message.AxisValue) },
                _ => current
            };

            if (next == current) return;
            _slotsVE[message.InstanceId] = slot with { LastState = next };
        }

        await SendStateVE(slot.Device, next, cancellationToken).ConfigureAwait(false);
        MaybePublishVE();
    }

    private async Task ApplyButtonVE(VEGamepadEvent message, CancellationToken cancellationToken)
    {
        VEGamepadButtons flag = ButtonFlagVE(message.Button);
        if (flag == VEGamepadButtons.None) return;

        VEGamepadSlot? slot;
        VEGamepadState next;

        lock (_gateVE)
        {
            if (!_slotsVE.TryGetValue(message.InstanceId, out slot) || slot is null)
                return;

            VEGamepadButtons buttons = message.Pressed
                ? slot.LastState.Buttons | flag
                : slot.LastState.Buttons & ~flag;

            next = slot.LastState with { Buttons = buttons };
            if (next == slot.LastState) return;
            _slotsVE[message.InstanceId] = slot with { LastState = next };
        }

        await SendStateVE(slot.Device, next, cancellationToken).ConfigureAwait(false);
        MaybePublishVE();
    }

    private async Task SendStateVE(VEGamepadDevice device, VEGamepadState state, CancellationToken cancellationToken)
    {
        VEGamepadState effective = IsPrivacyProtectedVE
            ? default
            : state;

        await _controlVE.SendAsync(
                VEControlMessage.UhidInputVE(
                    device.UhidId,
                    VEGamepadReport.BuildVE(effective)),
                cancellationToken)
            .ConfigureAwait(false);

        Interlocked.Increment(ref _reportsVE);
    }

    private async Task ResyncPrivacyVE(bool protectedVE)
    {
        try
        {
            VEGamepadSlot[] slots;
            lock (_gateVE) slots = _slotsVE.Values.ToArray();

            foreach (VEGamepadSlot slot in slots)
            {
                VEGamepadState state = protectedVE ? default : slot.LastState;
                await _controlVE.SendAsync(
                        VEControlMessage.UhidInputVE(
                            slot.Device.UhidId,
                            VEGamepadReport.BuildVE(state)),
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Interlocked.Increment(ref _reportsVE);
            }

            PublishVE(VEGamepadStates.Running, null);
        }
        catch
        {
            // Privacy nunca debe derribar Video/Audio por un fallo de resync UHID.
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

    private static VEGamepadButtons ButtonFlagVE(byte button)
        => button switch
        {
            0 => VEGamepadButtons.South,
            1 => VEGamepadButtons.East,
            2 => VEGamepadButtons.West,
            3 => VEGamepadButtons.North,
            4 => VEGamepadButtons.Back,
            5 => VEGamepadButtons.Guide,
            6 => VEGamepadButtons.Start,
            7 => VEGamepadButtons.LeftStick,
            8 => VEGamepadButtons.RightStick,
            9 => VEGamepadButtons.LeftShoulder,
            10 => VEGamepadButtons.RightShoulder,
            11 => VEGamepadButtons.DPadUp,
            12 => VEGamepadButtons.DPadDown,
            13 => VEGamepadButtons.DPadLeft,
            14 => VEGamepadButtons.DPadRight,
            _ => VEGamepadButtons.None
        };

    private async Task CloseAllVE()
    {
        VEGamepadSlot[] slots;
        lock (_gateVE)
        {
            slots = _slotsVE.Values.ToArray();
            _slotsVE.Clear();
        }

        foreach (VEGamepadSlot slot in slots)
        {
            if (_controlVE.IsReadyVE)
            {
                try
                {
                    await _controlVE.SendAsync(
                            VEControlMessage.UhidDestroyVE(slot.Device.UhidId),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch { }
            }

            _sdlVE.CloseVE(slot.Handle);
        }
    }

    private void MaybePublishVE()
    {
        long now = Environment.TickCount64;
        long previous = Interlocked.Read(ref _lastStatusPublishMsVE);
        if (previous != 0 && now - previous < StatusPublishIntervalMsVE) return;
        if (Interlocked.CompareExchange(ref _lastStatusPublishMsVE, now, previous) != previous) return;
        PublishVE(VEGamepadStates.Running, null);
    }

    private void PublishVE(VEGamepadStates state, string? error)
    {
        VEGamepadStatus status;
        EventHandler<VEGamepadStatus>? handler;

        lock (_gateVE)
        {
            string message = state switch
            {
                VEGamepadStates.Failed => "Falló Gamepad VisionEngine.",
                VEGamepadStates.Stopped => "Gamepad VisionEngine detenido.",
                _ when _slotsVE.Count == 0 => "Gamepad VisionEngine activo; esperando control físico.",
                _ => $"Gamepad VisionEngine activo: {_slotsVE.Count} control(es)."
            };

            status = new VEGamepadStatus(
                state,
                _slotsVE.Count,
                Interlocked.Read(ref _reportsVE),
                DateTimeOffset.UtcNow,
                message,
                error);

            _statusVE = status;
            handler = StatusChangedVE;
        }

        handler?.Invoke(this, status);
    }

    private void ThrowIfDisposedVE()
        => ObjectDisposedException.ThrowIf(_disposedVE, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposedVE) return;
        await StopAsync().ConfigureAwait(false);
        _sdlVE.Dispose();
        _disposedVE = true;
    }
}
