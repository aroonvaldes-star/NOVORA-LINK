using NOVORA.Service;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace NOVORA.ExInEngine;

/// <summary>
/// Adaptador SDL3 event-driven para gamepads.
/// SDL_UpdateGamepads sólo se usa una vez al iniciar para sincronizar
/// controles que ya estaban conectados; no existe loop de polling.
/// </summary>
public sealed class ExInSdl : IDisposable
{
    private const uint SdlInitGamepadVE = 0x00002000;

    private const uint SdlEventGamepadAxisMotionVE = 0x650;
    private const uint SdlEventGamepadButtonDownVE = 0x651;
    private const uint SdlEventGamepadButtonUpVE = 0x652;
    private const uint SdlEventGamepadAddedVE = 0x653;
    private const uint SdlEventGamepadRemovedVE = 0x654;
    private const uint SdlEventGamepadRemappedVE = 0x655;
    private const uint SdlEventGamepadTouchpadDownVE = 0x656;
    private const uint SdlEventGamepadTouchpadMotionVE = 0x657;
    private const uint SdlEventGamepadTouchpadUpVE = 0x658;
    private const uint SdlEventJoystickBatteryUpdatedVE = 0x607;

    private readonly NLServiceNovoraPaths _paths;
    private readonly Channel<ExInEvent> _eventsVE =
        Channel.CreateUnbounded<ExInEvent>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

    private IntPtr _libraryVE;
    private SdlInitSubSystemDelegate? _initVE;
    private SdlQuitSubSystemDelegate? _quitVE;
    private SdlGetGamepadsDelegate? _getGamepadsVE;
    private SdlOpenGamepadDelegate? _openVE;
    private SdlCloseGamepadDelegate? _closeVE;
    private SdlGetGamepadIdDelegate? _getIdVE;
    private SdlGetGamepadNameDelegate? _getNameVE;
    private SdlGetGamepadVendorDelegate? _getVendorVE;
    private SdlGetGamepadProductDelegate? _getProductVE;
    private SdlGetGamepadGuidForIdDelegate? _getGuidForIdVE;
    private SdlGetGamepadTypeDelegate? _getTypeVE;
    private SdlGetGamepadStringDelegate? _getPathVE;
    private SdlGetGamepadStringDelegate? _getSerialVE;
    private SdlGetGamepadMappingDelegate? _getMappingVE;
    private SdlGetGamepadPowerInfoDelegate? _getPowerInfoVE;
    private SdlGetGamepadAxisDelegate? _axisVE;
    private SdlGetGamepadButtonDelegate? _buttonVE;
    private SdlUpdateGamepadsDelegate? _updateVE;
    private SdlWaitEventTimeoutDelegate? _waitEventTimeoutVE;
    private SdlSetGamepadEventsEnabledDelegate? _setEventsEnabledVE;
    private SdlFreeDelegate? _freeVE;
    private bool _initializedVE;
    private bool _disposedVE;

    public ExInSdl(NLServiceNovoraPaths paths)
        => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public void InitializeVE()
    {
        ThrowIfDisposedVE();
        if (_initializedVE) return;

        string path = Path.Combine(_paths.ToolsDirectory, "SDL3.dll");
        if (!File.Exists(path))
            throw new FileNotFoundException("VisionEngine no encuentra SDL3.dll.", path);

        _libraryVE = NativeLibrary.Load(path);

        try
        {
            _initVE = LoadVE<SdlInitSubSystemDelegate>("SDL_InitSubSystem");
            _quitVE = LoadVE<SdlQuitSubSystemDelegate>("SDL_QuitSubSystem");
            _getGamepadsVE = LoadVE<SdlGetGamepadsDelegate>("SDL_GetGamepads");
            _openVE = LoadVE<SdlOpenGamepadDelegate>("SDL_OpenGamepad");
            _closeVE = LoadVE<SdlCloseGamepadDelegate>("SDL_CloseGamepad");
            _getIdVE = LoadVE<SdlGetGamepadIdDelegate>("SDL_GetGamepadID");
            _getNameVE = LoadVE<SdlGetGamepadNameDelegate>("SDL_GetGamepadName");
            _getVendorVE = TryLoadVE<SdlGetGamepadVendorDelegate>("SDL_GetGamepadVendor");
            _getProductVE = TryLoadVE<SdlGetGamepadProductDelegate>("SDL_GetGamepadProduct");
            _getGuidForIdVE = TryLoadVE<SdlGetGamepadGuidForIdDelegate>("SDL_GetGamepadGUIDForID");
            _getTypeVE = TryLoadVE<SdlGetGamepadTypeDelegate>("SDL_GetGamepadType");
            _getPathVE = TryLoadVE<SdlGetGamepadStringDelegate>("SDL_GetGamepadPath");
            _getSerialVE = TryLoadVE<SdlGetGamepadStringDelegate>("SDL_GetGamepadSerial");
            _getMappingVE = TryLoadVE<SdlGetGamepadMappingDelegate>("SDL_GetGamepadMapping");
            _getPowerInfoVE = TryLoadVE<SdlGetGamepadPowerInfoDelegate>("SDL_GetGamepadPowerInfo");
            _axisVE = LoadVE<SdlGetGamepadAxisDelegate>("SDL_GetGamepadAxis");
            _buttonVE = LoadVE<SdlGetGamepadButtonDelegate>("SDL_GetGamepadButton");
            _updateVE = LoadVE<SdlUpdateGamepadsDelegate>("SDL_UpdateGamepads");
            _waitEventTimeoutVE = LoadVE<SdlWaitEventTimeoutDelegate>("SDL_WaitEventTimeout");
            _setEventsEnabledVE = LoadVE<SdlSetGamepadEventsEnabledDelegate>("SDL_SetGamepadEventsEnabled");
            _freeVE = LoadVE<SdlFreeDelegate>("SDL_free");

            if (!_initVE(SdlInitGamepadVE))
                throw new InvalidOperationException("SDL3 no pudo inicializar GAMEPAD.");

            _setEventsEnabledVE(true);
            _initializedVE = true;

            // Sincronización única del estado inicial; no es polling.
            _updateVE();
        }
        catch
        {
            CleanupVE();
            throw;
        }
    }

    public IReadOnlyList<uint> GetDetectedIdsVE()
    {
        EnsureInitializedVE();
        _updateVE!();

        IntPtr ids = _getGamepadsVE!(out int count);
        if (ids == IntPtr.Zero || count <= 0) return Array.Empty<uint>();

        try
        {
            uint[] result = new uint[Math.Min(count, 8)];
            for (int i = 0; i < result.Length; i++)
                result[i] = unchecked((uint)Marshal.ReadInt32(ids, i * sizeof(uint)));
            return result;
        }
        finally
        {
            _freeVE!(ids);
        }
    }

    public async IAsyncEnumerable<ExInEvent> ReadEventsVE(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureInitializedVE();

        IntPtr eventBuffer =
            Marshal.AllocHGlobal(
                128);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool received =
                    _waitEventTimeoutVE!(
                        eventBuffer,
                        250);

                if (received &&
                    TryReadEventVE(
                        eventBuffer,
                        out ExInEvent? message) &&
                    message is not null)
                {
                    yield return message.Value;
                    continue;
                }

                while (_eventsVE.Reader.TryRead(out ExInEvent queued))
                {
                    yield return queued;
                }

                await Task.Yield();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(
                eventBuffer);
        }
    }

    public IntPtr OpenVE(uint instanceId)
    {
        EnsureInitializedVE();
        return _openVE!(instanceId);
    }

    public uint GetIdVE(IntPtr gamepad) => _getIdVE!(gamepad);

    public string GetNameVE(IntPtr gamepad)
    {
        IntPtr value = _getNameVE!(gamepad);
        return value == IntPtr.Zero ? "Gamepad" : Marshal.PtrToStringUTF8(value) ?? "Gamepad";
    }

    public ExInControllerIdentity GetIdentityVE(IntPtr gamepad)
    {
        EnsureInitializedVE();
        ArgumentOutOfRangeException.ThrowIfEqual(gamepad, IntPtr.Zero);

        uint instanceId = GetIdVE(gamepad);
        ushort vendorId = _getVendorVE?.Invoke(gamepad) ?? 0;
        ushort productId = _getProductVE?.Invoke(gamepad) ?? 0;
        Guid guid = _getGuidForIdVE is null ? Guid.Empty : _getGuidForIdVE(instanceId).ToGuidVE();

        return ExInControllerIdentity.CreateVE(
            instanceId,
            vendorId,
            productId,
            guid,
            GetNameVE(gamepad),
            ReadUtf8VE(_getSerialVE?.Invoke(gamepad) ?? IntPtr.Zero),
            ReadUtf8VE(_getPathVE?.Invoke(gamepad) ?? IntPtr.Zero));
    }

    public int? GetTypeVE(IntPtr gamepad)
    {
        EnsureInitializedVE();
        ArgumentOutOfRangeException.ThrowIfEqual(gamepad, IntPtr.Zero);
        return _getTypeVE?.Invoke(gamepad);
    }

    public string? GetMappingVE(IntPtr gamepad)
    {
        EnsureInitializedVE();
        ArgumentOutOfRangeException.ThrowIfEqual(gamepad, IntPtr.Zero);
        if (_getMappingVE is null) return null;

        IntPtr value = _getMappingVE(gamepad);
        if (value == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(value); }
        finally { _freeVE!(value); }
    }

    public ExInBatteryStatus GetBatteryStatusVE(IntPtr gamepad, ExInControllerIdentity identity)
    {
        EnsureInitializedVE();
        ArgumentOutOfRangeException.ThrowIfEqual(gamepad, IntPtr.Zero);
        ArgumentNullException.ThrowIfNull(identity);
        if (_getPowerInfoVE is null)
            return ExInBatteryStatus.CreateVE(identity.ProfileKey, identity.Name, -1, ExInBatteryState.Unknown);
        int powerState = _getPowerInfoVE(gamepad, out int percent);
        return ExInBatteryStatus.CreateVE(identity.ProfileKey, identity.Name, percent, MapPowerStateVE(powerState));
    }

    public ExInState ReadStateVE(IntPtr gamepad)
    {
        EnsureInitializedVE();
        short triggerL = Math.Max((short)0, _axisVE!(gamepad, 4));
        short triggerR = Math.Max((short)0, _axisVE!(gamepad, 5));
        ExInButtons buttons = ExInButtons.None;
        if (_buttonVE!(gamepad, 0)) buttons |= ExInButtons.South;
        if (_buttonVE!(gamepad, 1)) buttons |= ExInButtons.East;
        if (_buttonVE!(gamepad, 2)) buttons |= ExInButtons.West;
        if (_buttonVE!(gamepad, 3)) buttons |= ExInButtons.North;
        if (_buttonVE!(gamepad, 4)) buttons |= ExInButtons.Back;
        if (_buttonVE!(gamepad, 5)) buttons |= ExInButtons.Guide;
        if (_buttonVE!(gamepad, 6)) buttons |= ExInButtons.Start;
        if (_buttonVE!(gamepad, 7)) buttons |= ExInButtons.LeftStick;
        if (_buttonVE!(gamepad, 8)) buttons |= ExInButtons.RightStick;
        if (_buttonVE!(gamepad, 9)) buttons |= ExInButtons.LeftShoulder;
        if (_buttonVE!(gamepad, 10)) buttons |= ExInButtons.RightShoulder;
        if (_buttonVE!(gamepad, 11)) buttons |= ExInButtons.DPadUp;
        if (_buttonVE!(gamepad, 12)) buttons |= ExInButtons.DPadDown;
        if (_buttonVE!(gamepad, 13)) buttons |= ExInButtons.DPadLeft;
        if (_buttonVE!(gamepad, 14)) buttons |= ExInButtons.DPadRight;
        if (_buttonVE!(gamepad, 20)) buttons |= ExInButtons.Touchpad;

        return new ExInState(
            _axisVE!(gamepad, 0),
            _axisVE!(gamepad, 1),
            _axisVE!(gamepad, 2),
            _axisVE!(gamepad, 3),
            triggerL,
            triggerR,
            buttons);
    }

    public void CloseVE(IntPtr gamepad)
    {
        if (gamepad != IntPtr.Zero && _closeVE is not null)
            _closeVE(gamepad);
    }

    private bool EventWatchVE(IntPtr userdata, IntPtr eventPointer)
    {
        try
        {
            if (TryReadEventVE(
                    eventPointer,
                    out ExInEvent? message) &&
                message is not null)
            {
                _eventsVE.Writer.TryWrite(message.Value);
            }
        }
        catch
        {
            // Nunca propagar excepciones a SDL desde un callback nativo.
        }

        return true;
    }

    private static bool TryReadEventVE(
        IntPtr eventPointer,
        out ExInEvent? message)
    {
        uint type =
            unchecked(
                (uint)Marshal.ReadInt32(
                    eventPointer,
                    0));

        uint which =
            unchecked(
                (uint)Marshal.ReadInt32(
                    eventPointer,
                    16));

        message =
            type switch
            {
                SdlEventGamepadAddedVE =>
                    new(
                        ExInTypeEvent.Added,
                        which,
                        0,
                        0,
                        0,
                        false),

                SdlEventGamepadRemovedVE =>
                    new(
                        ExInTypeEvent.Removed,
                        which,
                        0,
                        0,
                        0,
                        false),

                SdlEventGamepadRemappedVE =>
                    new(
                        ExInTypeEvent.Remapped,
                        which,
                        0,
                        0,
                        0,
                        false),

                SdlEventGamepadAxisMotionVE =>
                    new(
                        ExInTypeEvent.Axis,
                        which,
                        Marshal.ReadByte(eventPointer, 20),
                        Marshal.ReadInt16(eventPointer, 24),
                        0,
                        false),

                SdlEventGamepadButtonDownVE =>
                    new(
                        ExInTypeEvent.Button,
                        which,
                        0,
                        0,
                        Marshal.ReadByte(eventPointer, 20),
                        true),

                SdlEventGamepadButtonUpVE =>
                    new(
                        ExInTypeEvent.Button,
                        which,
                        0,
                        0,
                        Marshal.ReadByte(eventPointer, 20),
                        false),

                SdlEventJoystickBatteryUpdatedVE =>
                    new(
                        ExInTypeEvent.Battery,
                        which,
                        0,
                        0,
                        0,
                        false,
                        MapPowerStateVE(Marshal.ReadInt32(eventPointer, 20)),
                        Marshal.ReadInt32(eventPointer, 24)),

                SdlEventGamepadTouchpadDownVE or SdlEventGamepadTouchpadMotionVE or SdlEventGamepadTouchpadUpVE =>
                    new(
                        type == SdlEventGamepadTouchpadDownVE ? ExInTypeEvent.TouchDown :
                        type == SdlEventGamepadTouchpadMotionVE ? ExInTypeEvent.TouchMotion : ExInTypeEvent.TouchUp,
                        which,
                        0,
                        0,
                        0,
                        false,
                        ExInBatteryState.Unknown,
                        -1,
                        Marshal.ReadInt32(eventPointer, 20),
                        Marshal.ReadInt32(eventPointer, 24),
                        BitConverter.Int32BitsToSingle(Marshal.ReadInt32(eventPointer, 28)),
                        BitConverter.Int32BitsToSingle(Marshal.ReadInt32(eventPointer, 32)),
                        BitConverter.Int32BitsToSingle(Marshal.ReadInt32(eventPointer, 36))),

                _ =>
                    null
            };

        return
            message is not null;
    }

    private T LoadVE<T>(string export) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_libraryVE, export));

    private T? TryLoadVE<T>(string export) where T : Delegate
        => NativeLibrary.TryGetExport(_libraryVE, export, out IntPtr address)
            ? Marshal.GetDelegateForFunctionPointer<T>(address)
            : null;

    private static string? ReadUtf8VE(IntPtr value)
        => value == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(value);

    private static ExInBatteryState MapPowerStateVE(int state) => state switch
    {
        1 => ExInBatteryState.OnBattery,
        2 => ExInBatteryState.NoBattery,
        3 => ExInBatteryState.Charging,
        4 => ExInBatteryState.Charged,
        _ => ExInBatteryState.Unknown
    };

    private void EnsureInitializedVE()
    {
        if (!_initializedVE) InitializeVE();
    }

    private void ThrowIfDisposedVE()
        => ObjectDisposedException.ThrowIf(_disposedVE, this);

    private void CleanupVE()
    {
        if (_initializedVE)
        {
            try { _quitVE?.Invoke(SdlInitGamepadVE); } catch { }
        }

        _initializedVE = false;

        if (_libraryVE != IntPtr.Zero)
        {
            try { NativeLibrary.Free(_libraryVE); } catch { }
            _libraryVE = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposedVE) return;
        _eventsVE.Writer.TryComplete();
        CleanupVE();
        _disposedVE = true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlInitSubSystemDelegate(uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlQuitSubSystemDelegate(uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlGetGamepadsDelegate(out int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlOpenGamepadDelegate(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlCloseGamepadDelegate(IntPtr gamepad);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint SdlGetGamepadIdDelegate(IntPtr gamepad);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlGetGamepadNameDelegate(IntPtr gamepad);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort SdlGetGamepadVendorDelegate(IntPtr gamepad);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort SdlGetGamepadProductDelegate(IntPtr gamepad);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate SdlGuidVE SdlGetGamepadGuidForIdDelegate(uint instanceId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SdlGetGamepadTypeDelegate(IntPtr gamepad);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlGetGamepadStringDelegate(IntPtr gamepad);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlGetGamepadMappingDelegate(IntPtr gamepad);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SdlGetGamepadPowerInfoDelegate(IntPtr gamepad, out int percent);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate short SdlGetGamepadAxisDelegate(IntPtr gamepad, int axis);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlGetGamepadButtonDelegate(IntPtr gamepad, int button);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlUpdateGamepadsDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlWaitEventTimeoutDelegate(
        IntPtr eventPointer,
        int timeoutMs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlSetGamepadEventsEnabledDelegate([MarshalAs(UnmanagedType.I1)] bool enabled);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlEventFilterDelegate(IntPtr userdata, IntPtr eventPointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlFreeDelegate(IntPtr memory);

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    private struct SdlGuidVE
    {
        private ulong _lowVE;
        private ulong _highVE;

        public readonly Guid ToGuidVE()
        {
            Span<byte> bytes = stackalloc byte[16];
            BitConverter.TryWriteBytes(bytes, _lowVE);
            BitConverter.TryWriteBytes(bytes[8..], _highVE);
            return new Guid(bytes);
        }
    }
}
