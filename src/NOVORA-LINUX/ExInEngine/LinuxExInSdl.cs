using NOVORA.Service;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace NOVORA.ExInEngine;

/// <summary>
/// Adaptador SDL3 de mandos para Linux, con la misma API que el de Windows.
/// En Linux la ventana SDL de VisionEngine vacía la cola de eventos desde el hilo de UI
/// (SDL_PollEvent) y X11/Wayland no admiten bombear vídeo desde otro hilo. Por eso ExIn no
/// llama a SDL_WaitEventTimeout: un hilo propio actualiza sólo los mandos
/// (SDL_UpdateGamepads, sin vídeo), un event watch copia los eventos de mando en el instante
/// en que SDL los publica y después se retiran de la cola para que el renderer no los vea.
/// </summary>
public sealed class ExInSdl : IDisposable
{
    private const uint SdlInitGamepadVE = 0x00002000;
    internal const string SdlJoystickAllowBackgroundEventsHintVE = "SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS";

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
    private const uint SdlEventJoystickFirstVE = 0x600;
    private const uint SdlEventJoystickLastVE = 0x6FF;
    internal const string SdlAutoUpdateJoysticksHintVE = "SDL_AUTO_UPDATE_JOYSTICKS";
    internal const string SdlLibraryNameVE = "libSDL3.so.0";
    // 1 ms entre actualizaciones: SDL_UpdateGamepads lee evdev sin bloquear; el mando
    // típico informa a 125–1000 Hz, así que el retardo añadido queda por debajo de 1 ms.
    private static readonly TimeSpan UpdateIntervalVE = TimeSpan.FromMilliseconds(1);

    private readonly NLServiceNovoraPaths _paths;

    private IntPtr _libraryVE;
    private SdlSetHintDelegate? _setHintVE;
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
    private SdlGetGamepadPowerInfoDelegate? _getPowerInfoVE;
    private SdlGetGamepadAxisDelegate? _axisVE;
    private SdlGetGamepadButtonDelegate? _buttonVE;
    private SdlUpdateGamepadsDelegate? _updateVE;
    private SdlFlushEventsDelegate? _flushEventsVE;
    private SdlAddEventWatchDelegate? _addEventWatchVE;
    private SdlRemoveEventWatchDelegate? _removeEventWatchVE;
    private GCHandle _selfHandleVE;
    private bool _watchInstalledVE;
    private Channel<ExInEvent>? _eventsVE;
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

        // Misma biblioteca del sistema que usa VisionEngine Linux; SDL3 cuenta las
        // inicializaciones por subsistema, así que GAMEPAD no interfiere con VIDEO/AUDIO.
        // La biblioteca permanece cargada durante toda la vida del proceso.
        if (_libraryVE == IntPtr.Zero)
        {
            if (!NativeLibrary.TryLoad(SdlLibraryNameVE, out _libraryVE))
                throw new DllNotFoundException(
                    "ExInEngine necesita SDL3 (libSDL3.so.0). Instala el paquete SDL3 de tu distribución.");
        }

        try
        {
            _setHintVE = LoadVE<SdlSetHintDelegate>("SDL_SetHint");
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
            _getPowerInfoVE = TryLoadVE<SdlGetGamepadPowerInfoDelegate>("SDL_GetGamepadPowerInfo");
            _axisVE = LoadVE<SdlGetGamepadAxisDelegate>("SDL_GetGamepadAxis");
            _buttonVE = LoadVE<SdlGetGamepadButtonDelegate>("SDL_GetGamepadButton");
            _updateVE = LoadVE<SdlUpdateGamepadsDelegate>("SDL_UpdateGamepads");
            _flushEventsVE = LoadVE<SdlFlushEventsDelegate>("SDL_FlushEvents");
            _addEventWatchVE = LoadVE<SdlAddEventWatchDelegate>("SDL_AddEventWatch");
            _removeEventWatchVE = LoadVE<SdlRemoveEventWatchDelegate>("SDL_RemoveEventWatch");
            _setEventsEnabledVE = LoadVE<SdlSetGamepadEventsEnabledDelegate>("SDL_SetGamepadEventsEnabled");
            _freeVE = LoadVE<SdlFreeDelegate>("SDL_free");

            if (!EnableBackgroundEventsVE((name, value) => _setHintVE(name, value)))
                throw new InvalidOperationException("SDL3 no permitió habilitar eventos de control en segundo plano.");
            // Sólo el hilo de ExIn actualiza los mandos; el SDL_PumpEvents del renderer no.
            if (!_setHintVE(SdlAutoUpdateJoysticksHintVE, "0"))
                throw new InvalidOperationException("SDL3 no permitió reservar la actualización de mandos a ExIn.");

            if (!_initVE(SdlInitGamepadVE))
                throw new InvalidOperationException("SDL3 no pudo inicializar GAMEPAD.");

            _setEventsEnabledVE(true);
            _eventsVE = Channel.CreateUnbounded<ExInEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            if (!_selfHandleVE.IsAllocated) _selfHandleVE = GCHandle.Alloc(this);
            if (!_addEventWatchVE(EventWatchPointerVE, GCHandle.ToIntPtr(_selfHandleVE)))
                throw new InvalidOperationException("SDL3 no permitió observar los eventos de mando.");
            _watchInstalledVE = true;
            _initializedVE = true;

            // Sincronización inicial del estado de mandos ya conectados.
            lock (SdlSharedGate.Gate)
            {
                _updateVE();
                _flushEventsVE(SdlEventJoystickFirstVE, SdlEventJoystickLastVE);
            }
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
        lock (SdlSharedGate.Gate)
        {
            _updateVE!();
            _flushEventsVE!(SdlEventJoystickFirstVE, SdlEventJoystickLastVE);
        }

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
        Channel<ExInEvent> events = _eventsVE!;
        using var pump = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var thread = new Thread(() => PumpGamepadsVE(pump.Token))
        {
            IsBackground = true,
            Name = "NOVORA ExIn SDL",
            Priority = ThreadPriority.AboveNormal
        };
        thread.Start();
        try
        {
            while (!cancellationToken.IsCancellationRequested && !_disposedVE && _initializedVE)
            {
                ExInEvent message;
                try
                {
                    if (!await events.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) break;
                    if (!events.Reader.TryRead(out message)) continue;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                yield return message;
            }
        }
        finally
        {
            pump.Cancel();
            thread.Join(TimeSpan.FromSeconds(1));
        }
    }

    private void PumpGamepadsVE(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            lock (SdlSharedGate.Gate)
            {
                if (_disposedVE || !_initializedVE) return;
                // El event watch recibe los eventos durante esta llamada, en este hilo.
                _updateVE!();
                // Ya copiados: retirarlos impide que el renderer los consuma o que la cola crezca.
                _flushEventsVE!(SdlEventJoystickFirstVE, SdlEventJoystickLastVE);
            }
            if (cancellationToken.WaitHandle.WaitOne(UpdateIntervalVE)) return;
        }
    }

    // Delegado estático: SDL conserva el puntero mientras el watch está instalado.
    private static readonly SdlEventFilterDelegate EventWatchDelegateVE = EventWatchVE;
    private static readonly IntPtr EventWatchPointerVE = Marshal.GetFunctionPointerForDelegate(EventWatchDelegateVE);

    private static byte EventWatchVE(IntPtr userdata, IntPtr eventPointer)
    {
        try
        {
            if (userdata == IntPtr.Zero || eventPointer == IntPtr.Zero) return 1;
            if (GCHandle.FromIntPtr(userdata).Target is not ExInSdl owner || owner._disposedVE) return 1;
            uint type = unchecked((uint)Marshal.ReadInt32(eventPointer, 0));
            if (type is < SdlEventJoystickFirstVE or > SdlEventJoystickLastVE) return 1;
            if (TryReadEventVE(eventPointer, out ExInEvent? message) && message is not null)
                owner._eventsVE?.Writer.TryWrite(message.Value);
        }
        catch
        {
            // Una excepción no puede cruzar a código nativo de SDL.
        }
        return 1;
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
        // SDL3: right paddle 1=16, left paddle 1=17, right paddle 2=18, left paddle 2=19.
        if (_buttonVE!(gamepad, 17)) buttons |= ExInButtons.P1;
        if (_buttonVE!(gamepad, 19)) buttons |= ExInButtons.P2;
        if (_buttonVE!(gamepad, 16)) buttons |= ExInButtons.P3;
        if (_buttonVE!(gamepad, 18)) buttons |= ExInButtons.P4;
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

    internal static bool EnableBackgroundEventsVE(Func<string, string, bool> setHint)
    {
        ArgumentNullException.ThrowIfNull(setHint);
        return setHint(SdlJoystickAllowBackgroundEventsHintVE, "1");
    }

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
        // Tras Dispose no se reinicia SDL: una llamada tardía debe fallar, no reabrir la biblioteca.
        ThrowIfDisposedVE();
        if (!_initializedVE) InitializeVE();
    }

    private void ThrowIfDisposedVE()
        => ObjectDisposedException.ThrowIf(_disposedVE, this);

    private void CleanupVE()
    {
        lock (SdlSharedGate.Gate)
        {
            if (_watchInstalledVE && _selfHandleVE.IsAllocated)
            {
                try { _removeEventWatchVE?.Invoke(EventWatchPointerVE, GCHandle.ToIntPtr(_selfHandleVE)); }
                catch { }
            }
            _watchInstalledVE = false;
            if (_initializedVE)
            {
                try { _flushEventsVE?.Invoke(SdlEventJoystickFirstVE, SdlEventJoystickLastVE); } catch { }
                try { _quitVE?.Invoke(SdlInitGamepadVE); } catch { }
            }
            _initializedVE = false;
        }
        _eventsVE?.Writer.TryComplete();
        if (_selfHandleVE.IsAllocated) _selfHandleVE.Free();
    }

    public void Dispose()
    {
        if (_disposedVE) return;
        // Marcar antes de liberar SDL: el bucle de eventos no debe llamar a la biblioteca descargada.
        _disposedVE = true;
        CleanupVE();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlSetHintDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

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
    private delegate int SdlGetGamepadPowerInfoDelegate(IntPtr gamepad, out int percent);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate short SdlGetGamepadAxisDelegate(IntPtr gamepad, int axis);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlGetGamepadButtonDelegate(IntPtr gamepad, int button);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlUpdateGamepadsDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte SdlEventFilterDelegate(IntPtr userdata, IntPtr eventPointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlFlushEventsDelegate(uint minType, uint maxType);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlAddEventWatchDelegate(IntPtr filter, IntPtr userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlRemoveEventWatchDelegate(IntPtr filter, IntPtr userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlSetGamepadEventsEnabledDelegate([MarshalAs(UnmanagedType.I1)] bool enabled);

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
