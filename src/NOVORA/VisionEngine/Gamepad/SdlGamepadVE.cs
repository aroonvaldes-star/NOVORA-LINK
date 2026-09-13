using NOVORA.Services;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace NOVORA.VisionEngine.Gamepad;

/// <summary>
/// Adaptador SDL3 event-driven para gamepads.
/// SDL_UpdateGamepads sólo se usa una vez al iniciar para sincronizar
/// controles que ya estaban conectados; no existe loop de polling.
/// </summary>
public sealed class SdlGamepadVE : IDisposable
{
    private const uint SdlInitGamepadVE = 0x00002000;

    private const uint SdlEventGamepadAxisMotionVE = 0x650;
    private const uint SdlEventGamepadButtonDownVE = 0x651;
    private const uint SdlEventGamepadButtonUpVE = 0x652;
    private const uint SdlEventGamepadAddedVE = 0x653;
    private const uint SdlEventGamepadRemovedVE = 0x654;
    private const uint SdlEventGamepadRemappedVE = 0x655;

    private readonly NovoraPaths _paths;
    private readonly Channel<EventGamepadVE> _eventsVE =
        Channel.CreateUnbounded<EventGamepadVE>(
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
    private SdlGetGamepadAxisDelegate? _axisVE;
    private SdlGetGamepadButtonDelegate? _buttonVE;
    private SdlUpdateGamepadsDelegate? _updateVE;
    private SdlWaitEventTimeoutDelegate? _waitEventTimeoutVE;
    private SdlSetGamepadEventsEnabledDelegate? _setEventsEnabledVE;
    private SdlFreeDelegate? _freeVE;
    private bool _initializedVE;
    private bool _disposedVE;

    public SdlGamepadVE(NovoraPaths paths)
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

    public async IAsyncEnumerable<EventGamepadVE> ReadEventsVE(
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
                        out EventGamepadVE? message) &&
                    message is not null)
                {
                    yield return message.Value;
                    continue;
                }

                while (_eventsVE.Reader.TryRead(out EventGamepadVE queued))
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

    public StateGamepadVE ReadStateVE(IntPtr gamepad)
    {
        EnsureInitializedVE();
        short triggerL = Math.Max((short)0, _axisVE!(gamepad, 4));
        short triggerR = Math.Max((short)0, _axisVE!(gamepad, 5));
        ButtonsGamepadVE buttons = ButtonsGamepadVE.None;
        if (_buttonVE!(gamepad, 0)) buttons |= ButtonsGamepadVE.South;
        if (_buttonVE!(gamepad, 1)) buttons |= ButtonsGamepadVE.East;
        if (_buttonVE!(gamepad, 2)) buttons |= ButtonsGamepadVE.West;
        if (_buttonVE!(gamepad, 3)) buttons |= ButtonsGamepadVE.North;
        if (_buttonVE!(gamepad, 4)) buttons |= ButtonsGamepadVE.Back;
        if (_buttonVE!(gamepad, 5)) buttons |= ButtonsGamepadVE.Guide;
        if (_buttonVE!(gamepad, 6)) buttons |= ButtonsGamepadVE.Start;
        if (_buttonVE!(gamepad, 7)) buttons |= ButtonsGamepadVE.LeftStick;
        if (_buttonVE!(gamepad, 8)) buttons |= ButtonsGamepadVE.RightStick;
        if (_buttonVE!(gamepad, 9)) buttons |= ButtonsGamepadVE.LeftShoulder;
        if (_buttonVE!(gamepad, 10)) buttons |= ButtonsGamepadVE.RightShoulder;
        if (_buttonVE!(gamepad, 11)) buttons |= ButtonsGamepadVE.DPadUp;
        if (_buttonVE!(gamepad, 12)) buttons |= ButtonsGamepadVE.DPadDown;
        if (_buttonVE!(gamepad, 13)) buttons |= ButtonsGamepadVE.DPadLeft;
        if (_buttonVE!(gamepad, 14)) buttons |= ButtonsGamepadVE.DPadRight;

        return new StateGamepadVE(
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
                    out EventGamepadVE? message) &&
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
        out EventGamepadVE? message)
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
                        TypeEventGamepadVE.Added,
                        which,
                        0,
                        0,
                        0,
                        false),

                SdlEventGamepadRemovedVE =>
                    new(
                        TypeEventGamepadVE.Removed,
                        which,
                        0,
                        0,
                        0,
                        false),

                SdlEventGamepadRemappedVE =>
                    new(
                        TypeEventGamepadVE.Remapped,
                        which,
                        0,
                        0,
                        0,
                        false),

                SdlEventGamepadAxisMotionVE =>
                    new(
                        TypeEventGamepadVE.Axis,
                        which,
                        Marshal.ReadByte(eventPointer, 20),
                        Marshal.ReadInt16(eventPointer, 24),
                        0,
                        false),

                SdlEventGamepadButtonDownVE =>
                    new(
                        TypeEventGamepadVE.Button,
                        which,
                        0,
                        0,
                        Marshal.ReadByte(eventPointer, 20),
                        true),

                SdlEventGamepadButtonUpVE =>
                    new(
                        TypeEventGamepadVE.Button,
                        which,
                        0,
                        0,
                        Marshal.ReadByte(eventPointer, 20),
                        false),

                _ =>
                    null
            };

        return
            message is not null;
    }

    private T LoadVE<T>(string export) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_libraryVE, export));

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
}
