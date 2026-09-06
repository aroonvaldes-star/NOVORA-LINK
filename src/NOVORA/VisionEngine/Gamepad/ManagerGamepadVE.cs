using NOVORA.Services;
using NOVORA.VisionEngine.Control;

namespace NOVORA.VisionEngine.Gamepad;

/// <summary>
/// Puente Windows gamepad -> SDL3 -> estado normalizado -> UHID -> Android.
/// Admite hasta ocho controles simultáneos, con IDs UHID 3..10.
///
/// El dispositivo UHID emula exactamente la identidad usada por scrcpy 4.1
/// para maximizar compatibilidad con Android: Xbox 360 045E:028E.
/// </summary>
public sealed class ManagerGamepadVE : IAsyncDisposable
{
    private const int MaxGamepadsVE = 8;
    private const ushort FirstUhidIdVE = 3;
    private const ushort UhidVendorIdVE = 0x045E;
    private const ushort UhidProductIdVE = 0x028E;
    private const string UhidDeviceNameVE = "Microsoft X-Box 360 Pad";
    private const int ActivePollDelayMsVE = 8;
    private const int IdleDiscoveryDelayMsVE = 100;
    private const int DiscoveryIntervalMsVE = 1000;
    private const int StatusIntervalMsVE = 1000;

    private readonly ManagerControlVE _controlVE;
    private readonly SdlGamepadVE _sdlVE;
    private readonly Dictionary<uint, SlotVE> _slotsVE = [];
    private readonly object _gateVE = new();

    private CancellationTokenSource? _pumpCtsVE;
    private Task? _pumpTaskVE;
    private StatusGamepadVE _statusVE = StatusGamepadVE.CreateInitialVE();
    private long _reportsVE;
    private bool _disposedVE;

    private sealed record SlotVE(
        IntPtr Handle,
        DeviceGamepadVE Device,
        StateGamepadVE LastState,
        string PhysicalName);

    public ManagerGamepadVE(
        ManagerControlVE control,
        NovoraPaths paths)
    {
        _controlVE =
            control
            ?? throw new ArgumentNullException(
                nameof(control));

        _sdlVE =
            new SdlGamepadVE(
                paths
                ?? throw new ArgumentNullException(
                    nameof(paths)));
    }

    public event EventHandler<StatusGamepadVE>? StatusChangedVE;

    public StatusGamepadVE StatusVE
    {
        get
        {
            lock (_gateVE)
            {
                return _statusVE;
            }
        }
    }

    public Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();

        if (!_controlVE.IsReadyVE)
        {
            throw new InvalidOperationException(
                "GamepadVE necesita ControlVE activo.");
        }

        if (_pumpTaskVE is not null)
        {
            return Task.CompletedTask;
        }

        _sdlVE.InitializeVE();
        cancellationToken.ThrowIfCancellationRequested();

        _pumpCtsVE =
            new CancellationTokenSource();

        _pumpTaskVE =
            PumpVE(
                _pumpCtsVE.Token);

        PublishVE(
            StatesGamepadVE.Running,
            null);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts =
            _pumpCtsVE;

        Task? task =
            _pumpTaskVE;

        _pumpCtsVE =
            null;

        _pumpTaskVE =
            null;

        cts?.Cancel();

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        cts?.Dispose();

        await CloseAllVE()
            .ConfigureAwait(false);

        PublishVE(
            StatesGamepadVE.Stopped,
            null);
    }

    private async Task PumpVE(
        CancellationToken cancellationToken)
    {
        try
        {
            long nextDiscovery =
                0;

            long nextStatus =
                0;

            while (!cancellationToken.IsCancellationRequested)
            {
                long now =
                    Environment.TickCount64;

                if (now >= nextDiscovery)
                {
                    await DiscoverVE(
                            cancellationToken)
                        .ConfigureAwait(false);

                    nextDiscovery =
                        Environment.TickCount64 +
                        DiscoveryIntervalMsVE;
                }

                SlotVE[] slots;

                lock (_gateVE)
                {
                    slots =
                        _slotsVE.Values.ToArray();
                }

                foreach (SlotVE slot in slots)
                {
                    StateGamepadVE state =
                        _sdlVE.ReadStateVE(
                            slot.Handle);

                    if (state ==
                        slot.LastState)
                    {
                        continue;
                    }

                    await _controlVE.SendAsync(
                            MessageControlVE.UhidInputVE(
                                slot.Device.UhidId,
                                ReportGamepadVE.BuildVE(
                                    state)),
                            cancellationToken)
                        .ConfigureAwait(false);

                    Interlocked.Increment(
                        ref _reportsVE);

                    lock (_gateVE)
                    {
                        if (_slotsVE.ContainsKey(
                                slot.Device.InstanceId))
                        {
                            _slotsVE[slot.Device.InstanceId] =
                                slot with
                                {
                                    LastState = state
                                };
                        }
                    }
                }

                if (Environment.TickCount64 >=
                    nextStatus)
                {
                    PublishVE(
                        StatesGamepadVE.Running,
                        null);

                    nextStatus =
                        Environment.TickCount64 +
                        StatusIntervalMsVE;
                }

                int delay =
                    slots.Length == 0
                        ? IdleDiscoveryDelayMsVE
                        : ActivePollDelayMsVE;

                await Task.Delay(
                        delay,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            PublishVE(
                StatesGamepadVE.Failed,
                ex.Message);
        }
    }

    private async Task DiscoverVE(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<uint> detected =
            _sdlVE.GetDetectedIdsVE();

        HashSet<uint> seen =
            detected.ToHashSet();

        foreach (uint id in detected)
        {
            bool exists;

            lock (_gateVE)
            {
                exists =
                    _slotsVE.ContainsKey(
                        id);
            }

            if (exists)
            {
                continue;
            }

            IntPtr handle =
                _sdlVE.OpenVE(
                    id);

            if (handle == IntPtr.Zero)
            {
                continue;
            }

            ushort uhidId;

            lock (_gateVE)
            {
                if (_slotsVE.Count >=
                    MaxGamepadsVE)
                {
                    _sdlVE.CloseVE(
                        handle);

                    continue;
                }

                HashSet<ushort> used =
                    _slotsVE.Values
                        .Select(
                            slot =>
                                slot.Device.UhidId)
                        .ToHashSet();

                uhidId =
                    Enumerable.Range(
                            FirstUhidIdVE,
                            MaxGamepadsVE)
                        .Select(
                            value =>
                                checked((ushort)value))
                        .First(
                            value =>
                                !used.Contains(value));
            }

            string physicalName =
                _sdlVE.GetNameVE(
                    handle);

            var device =
                new DeviceGamepadVE(
                    id,
                    uhidId,
                    UhidDeviceNameVE,
                    UhidVendorIdVE,
                    UhidProductIdVE,
                    DateTimeOffset.UtcNow);

            await _controlVE.SendAsync(
                    MessageControlVE.UhidCreateVE(
                        uhidId,
                        UhidVendorIdVE,
                        UhidProductIdVE,
                        UhidDeviceNameVE,
                        DescriptorGamepadVE.GetVE()),
                    cancellationToken)
                .ConfigureAwait(false);

            StateGamepadVE state =
                _sdlVE.ReadStateVE(
                    handle);

            await _controlVE.SendAsync(
                    MessageControlVE.UhidInputVE(
                        uhidId,
                        ReportGamepadVE.BuildVE(
                            state)),
                    cancellationToken)
                .ConfigureAwait(false);

            lock (_gateVE)
            {
                _slotsVE[id] =
                    new SlotVE(
                        handle,
                        device,
                        state,
                        physicalName);
            }
        }

        SlotVE[] removed;

        lock (_gateVE)
        {
            removed =
                _slotsVE.Values
                    .Where(
                        slot =>
                            !seen.Contains(
                                slot.Device.InstanceId))
                    .ToArray();
        }

        foreach (SlotVE slot in removed)
        {
            try
            {
                await _controlVE.SendAsync(
                        MessageControlVE.UhidDestroyVE(
                            slot.Device.UhidId),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
            }

            _sdlVE.CloseVE(
                slot.Handle);

            lock (_gateVE)
            {
                _slotsVE.Remove(
                    slot.Device.InstanceId);
            }
        }
    }

    private async Task CloseAllVE()
    {
        SlotVE[] slots;

        lock (_gateVE)
        {
            slots =
                _slotsVE.Values.ToArray();
        }

        foreach (SlotVE slot in slots)
        {
            if (_controlVE.IsReadyVE)
            {
                try
                {
                    await _controlVE.SendAsync(
                            MessageControlVE.UhidDestroyVE(
                                slot.Device.UhidId),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                }
            }

            _sdlVE.CloseVE(
                slot.Handle);
        }

        lock (_gateVE)
        {
            _slotsVE.Clear();
        }
    }

    private void PublishVE(
        StatesGamepadVE state,
        string? error)
    {
        StatusGamepadVE status;
        EventHandler<StatusGamepadVE>? handler;

        lock (_gateVE)
        {
            string message =
                state switch
                {
                    StatesGamepadVE.Failed =>
                        "Falló Gamepad VisionEngine.",

                    StatesGamepadVE.Stopped =>
                        "Gamepad VisionEngine detenido.",

                    _ when _slotsVE.Count == 0 =>
                        "Gamepad VisionEngine activo; esperando control físico.",

                    _ =>
                        $"Gamepad VisionEngine activo: {_slotsVE.Count} control(es)."
                };

            status =
                new StatusGamepadVE(
                    state,
                    _slotsVE.Count,
                    Interlocked.Read(
                        ref _reportsVE),
                    DateTimeOffset.UtcNow,
                    message,
                    error);

            _statusVE =
                status;

            handler =
                StatusChangedVE;
        }

        handler?.Invoke(
            this,
            status);
    }

    private void ThrowIfDisposedVE()
        => ObjectDisposedException.ThrowIf(
            _disposedVE,
            this);

    public async ValueTask DisposeAsync()
    {
        if (_disposedVE)
        {
            return;
        }

        await StopAsync()
            .ConfigureAwait(false);

        _sdlVE.Dispose();
        _disposedVE = true;
    }
}
