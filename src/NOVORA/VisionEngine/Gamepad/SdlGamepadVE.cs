using NOVORA.Services;
using System.Runtime.InteropServices;

namespace NOVORA.VisionEngine.Gamepad;

/// <summary>
/// Adaptador SDL3 para controles físicos de Windows. SDL normaliza Xbox,
/// DualShock/DualSense y otros gamepads a posiciones South/East/West/North.
/// </summary>
public sealed class SdlGamepadVE : IDisposable
{
    private const uint SdlInitGamepadVE = 0x00002000;
    private readonly NovoraPaths _paths;
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
    private SdlFreeDelegate? _freeVE;
    private bool _initializedVE;
    private bool _disposedVE;

    public SdlGamepadVE(NovoraPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public void InitializeVE()
    {
        ThrowIfDisposedVE();
        if (_initializedVE) return;
        string path = Path.Combine(_paths.ToolsDirectory, "SDL3.dll");
        if (!File.Exists(path)) throw new FileNotFoundException("VisionEngine no encuentra SDL3.dll.", path);
        _libraryVE = NativeLibrary.Load(path);
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
        _freeVE = LoadVE<SdlFreeDelegate>("SDL_free");
        if (!_initVE(SdlInitGamepadVE)) throw new InvalidOperationException("SDL3 no pudo inicializar GAMEPAD.");
        _initializedVE = true;
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
        finally { _freeVE!(ids); }
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
        _updateVE!();
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
            _axisVE!(gamepad, 0), _axisVE!(gamepad, 1),
            _axisVE!(gamepad, 2), _axisVE!(gamepad, 3),
            triggerL, triggerR, buttons);
    }

    public void CloseVE(IntPtr gamepad)
    {
        if (gamepad != IntPtr.Zero && _closeVE is not null) _closeVE(gamepad);
    }

    private T LoadVE<T>(string export) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_libraryVE, export));
    private void EnsureInitializedVE() { if (!_initializedVE) InitializeVE(); }
    private void ThrowIfDisposedVE() => ObjectDisposedException.ThrowIf(_disposedVE, this);

    public void Dispose()
    {
        if (_disposedVE) return;
        if (_initializedVE) _quitVE?.Invoke(SdlInitGamepadVE);
        if (_libraryVE != IntPtr.Zero) NativeLibrary.Free(_libraryVE);
        _libraryVE = IntPtr.Zero;
        _initializedVE = false;
        _disposedVE = true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlInitSubSystemDelegate(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SdlQuitSubSystemDelegate(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr SdlGetGamepadsDelegate(out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr SdlOpenGamepadDelegate(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SdlCloseGamepadDelegate(IntPtr gamepad);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint SdlGetGamepadIdDelegate(IntPtr gamepad);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr SdlGetGamepadNameDelegate(IntPtr gamepad);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate short SdlGetGamepadAxisDelegate(IntPtr gamepad, int axis);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlGetGamepadButtonDelegate(IntPtr gamepad, int button);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SdlUpdateGamepadsDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SdlFreeDelegate(IntPtr memory);
}
