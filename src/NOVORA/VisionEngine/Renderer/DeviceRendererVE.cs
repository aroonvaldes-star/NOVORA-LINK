using NOVORA.Services;
using System.Runtime.InteropServices;

namespace NOVORA.VisionEngine.Renderer;

/// <summary>
/// Binding mínimo a SDL3. SDL envuelve el HWND suministrado por NOVORA y se
/// obliga a crear el renderer "direct3d11".
/// </summary>
public sealed class DeviceRendererVE : IDisposable
{
    public const uint PixelFormatIyuvVE = 0x56555949;
    public const uint PixelFormatNv12VE = 0x3231564E;
    public const uint PixelFormatNv21VE = 0x3132564E;
    public const int TextureAccessStreamingVE = 1;

    private const uint SdlInitVideoVE = 0x00000020;
    private const string Win32HwndPropertyVE = "SDL.window.create.win32.hwnd";

    private readonly string _sdlPathVE;
    private IntPtr _libraryVE;
    private IntPtr _windowVE;
    private IntPtr _rendererVE;
    private bool _videoInitializedVE;
    private bool _disposedVE;

    private SdlInitSubSystemDelegate? _initSubSystemVE;
    private SdlQuitSubSystemDelegate? _quitSubSystemVE;
    private SdlCreatePropertiesDelegate? _createPropertiesVE;
    private SdlDestroyPropertiesDelegate? _destroyPropertiesVE;
    private SdlSetPointerPropertyDelegate? _setPointerPropertyVE;
    private SdlCreateWindowWithPropertiesDelegate? _createWindowWithPropertiesVE;
    private SdlDestroyWindowDelegate? _destroyWindowVE;
    private SdlCreateRendererDelegate? _createRendererVE;
    private SdlDestroyRendererDelegate? _destroyRendererVE;
    private SdlGetRendererNameDelegate? _getRendererNameVE;
    private SdlCreateTextureDelegate? _createTextureVE;
    private SdlDestroyTextureDelegate? _destroyTextureVE;
    private SdlUpdateYuvTextureDelegate? _updateYuvTextureVE;
    private SdlUpdateNvTextureDelegate? _updateNvTextureVE;
    private SdlGetRenderOutputSizeDelegate? _getRenderOutputSizeVE;
    private SdlSetRenderDrawColorDelegate? _setRenderDrawColorVE;
    private SdlRenderClearDelegate? _renderClearVE;
    private SdlRenderTextureDelegate? _renderTextureVE;
    private SdlRenderTextureRotatedDelegate? _renderTextureRotatedVE;
    private SdlRenderPresentDelegate? _renderPresentVE;
    private SdlPumpEventsDelegate? _pumpEventsVE;
    private SdlGetErrorDelegate? _getErrorVE;

    public DeviceRendererVE(NovoraPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _sdlPathVE = Path.Combine(paths.ToolsDirectory, "SDL3.dll");
    }

    public bool IsOpenVE => _rendererVE != IntPtr.Zero;

    public string? BackendVE { get; private set; }

    public void OpenVE(IntPtr hwnd)
    {
        ThrowIfDisposedVE();

        if (hwnd == IntPtr.Zero)
            throw new ArgumentException("HWND del renderer inválido.", nameof(hwnd));

        if (IsOpenVE)
            return;

        if (!Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("VisionEngine Renderer requiere proceso Windows x64.");

        if (!File.Exists(_sdlPathVE))
            throw new FileNotFoundException("VisionEngine no encuentra SDL3.dll.", _sdlPathVE);

        try
        {
            _libraryVE = NativeLibrary.Load(_sdlPathVE);
            LoadExportsVE();

            EnsureVE(_initSubSystemVE!(SdlInitVideoVE), "SDL_InitSubSystem(SDL_INIT_VIDEO)");
            _videoInitializedVE = true;

            uint properties = _createPropertiesVE!();
            if (properties == 0)
                throw CreateSdlExceptionVE("SDL_CreateProperties");

            try
            {
                EnsureVE(
                    _setPointerPropertyVE!(properties, Win32HwndPropertyVE, hwnd),
                    "SDL_SetPointerProperty(HWND)");

                _windowVE = _createWindowWithPropertiesVE!(properties);
            }
            finally
            {
                _destroyPropertiesVE!(properties);
            }

            if (_windowVE == IntPtr.Zero)
                throw CreateSdlExceptionVE("SDL_CreateWindowWithProperties");

            _rendererVE = _createRendererVE!(_windowVE, "direct3d11");
            if (_rendererVE == IntPtr.Zero)
                throw CreateSdlExceptionVE("SDL_CreateRenderer(direct3d11)");

            BackendVE = PtrToUtf8VE(_getRendererNameVE!(_rendererVE));
            if (!string.Equals(BackendVE, "direct3d11", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SDL creó '{BackendVE ?? "desconocido"}' en lugar de direct3d11.");

            EnsureVE(_setRenderDrawColorVE!(_rendererVE, 0, 0, 0, 255), "SDL_SetRenderDrawColor");
            EnsureVE(_renderClearVE!(_rendererVE), "SDL_RenderClear");
            EnsureVE(_renderPresentVE!(_rendererVE), "SDL_RenderPresent");
        }
        catch
        {
            DisposeNativeVE();
            throw;
        }
    }

    public IntPtr CreateTextureVE(uint pixelFormat, int width, int height)
    {
        EnsureOpenVE();
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        IntPtr texture = _createTextureVE!(
            _rendererVE,
            pixelFormat,
            TextureAccessStreamingVE,
            width,
            height);

        if (texture == IntPtr.Zero)
            throw CreateSdlExceptionVE("SDL_CreateTexture");

        return texture;
    }

    public void DestroyTextureVE(ref IntPtr texture)
    {
        if (texture == IntPtr.Zero)
            return;

        _destroyTextureVE?.Invoke(texture);
        texture = IntPtr.Zero;
    }

    public void UpdateYuvVE(
        IntPtr texture,
        IntPtr yPlane,
        int yPitch,
        IntPtr uPlane,
        int uPitch,
        IntPtr vPlane,
        int vPitch)
    {
        EnsureOpenVE();
        EnsureVE(
            _updateYuvTextureVE!(texture, IntPtr.Zero, yPlane, yPitch, uPlane, uPitch, vPlane, vPitch),
            "SDL_UpdateYUVTexture");
    }

    public void UpdateNvVE(
        IntPtr texture,
        IntPtr yPlane,
        int yPitch,
        IntPtr uvPlane,
        int uvPitch)
    {
        EnsureOpenVE();
        EnsureVE(
            _updateNvTextureVE!(texture, IntPtr.Zero, yPlane, yPitch, uvPlane, uvPitch),
            "SDL_UpdateNVTexture");
    }

    public (int Width, int Height) GetOutputSizeVE()
    {
        EnsureOpenVE();
        EnsureVE(_getRenderOutputSizeVE!(_rendererVE, out int width, out int height), "SDL_GetRenderOutputSize");
        return (width, height);
    }

    public void ClearVE()
    {
        EnsureOpenVE();
        EnsureVE(_setRenderDrawColorVE!(_rendererVE, 0, 0, 0, 255), "SDL_SetRenderDrawColor");
        EnsureVE(_renderClearVE!(_rendererVE), "SDL_RenderClear");
        EnsureVE(_renderPresentVE!(_rendererVE), "SDL_RenderPresent");
    }

    public void PumpEventsVE()
    {
        if (_libraryVE != IntPtr.Zero)
            _pumpEventsVE?.Invoke();
    }

    public void PresentVE(IntPtr texture, RectRendererVE destination, int rotationDegrees)
    {
        EnsureOpenVE();

        EnsureVE(_setRenderDrawColorVE!(_rendererVE, 0, 0, 0, 255), "SDL_SetRenderDrawColor");
        EnsureVE(_renderClearVE!(_rendererVE), "SDL_RenderClear");

        SdlFRectVE dst = new(destination.X, destination.Y, destination.Width, destination.Height);
        int rotation = RotationRendererVE.NormalizeVE(rotationDegrees);

        bool rendered = rotation == 0
            ? _renderTextureVE!(_rendererVE, texture, IntPtr.Zero, ref dst)
            : _renderTextureRotatedVE!(_rendererVE, texture, IntPtr.Zero, ref dst, rotation, IntPtr.Zero, 0);

        EnsureVE(rendered, rotation == 0 ? "SDL_RenderTexture" : "SDL_RenderTextureRotated");
        EnsureVE(_renderPresentVE!(_rendererVE), "SDL_RenderPresent");
    }

    private void LoadExportsVE()
    {
        _initSubSystemVE = LoadVE<SdlInitSubSystemDelegate>("SDL_InitSubSystem");
        _quitSubSystemVE = LoadVE<SdlQuitSubSystemDelegate>("SDL_QuitSubSystem");
        _createPropertiesVE = LoadVE<SdlCreatePropertiesDelegate>("SDL_CreateProperties");
        _destroyPropertiesVE = LoadVE<SdlDestroyPropertiesDelegate>("SDL_DestroyProperties");
        _setPointerPropertyVE = LoadVE<SdlSetPointerPropertyDelegate>("SDL_SetPointerProperty");
        _createWindowWithPropertiesVE = LoadVE<SdlCreateWindowWithPropertiesDelegate>("SDL_CreateWindowWithProperties");
        _destroyWindowVE = LoadVE<SdlDestroyWindowDelegate>("SDL_DestroyWindow");
        _createRendererVE = LoadVE<SdlCreateRendererDelegate>("SDL_CreateRenderer");
        _destroyRendererVE = LoadVE<SdlDestroyRendererDelegate>("SDL_DestroyRenderer");
        _getRendererNameVE = LoadVE<SdlGetRendererNameDelegate>("SDL_GetRendererName");
        _createTextureVE = LoadVE<SdlCreateTextureDelegate>("SDL_CreateTexture");
        _destroyTextureVE = LoadVE<SdlDestroyTextureDelegate>("SDL_DestroyTexture");
        _updateYuvTextureVE = LoadVE<SdlUpdateYuvTextureDelegate>("SDL_UpdateYUVTexture");
        _updateNvTextureVE = LoadVE<SdlUpdateNvTextureDelegate>("SDL_UpdateNVTexture");
        _getRenderOutputSizeVE = LoadVE<SdlGetRenderOutputSizeDelegate>("SDL_GetRenderOutputSize");
        _setRenderDrawColorVE = LoadVE<SdlSetRenderDrawColorDelegate>("SDL_SetRenderDrawColor");
        _renderClearVE = LoadVE<SdlRenderClearDelegate>("SDL_RenderClear");
        _renderTextureVE = LoadVE<SdlRenderTextureDelegate>("SDL_RenderTexture");
        _renderTextureRotatedVE = LoadVE<SdlRenderTextureRotatedDelegate>("SDL_RenderTextureRotated");
        _renderPresentVE = LoadVE<SdlRenderPresentDelegate>("SDL_RenderPresent");
        _pumpEventsVE = LoadVE<SdlPumpEventsDelegate>("SDL_PumpEvents");
        _getErrorVE = LoadVE<SdlGetErrorDelegate>("SDL_GetError");
    }

    private T LoadVE<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_libraryVE, name));

    private void EnsureVE(bool success, string operation)
    {
        if (!success)
            throw CreateSdlExceptionVE(operation);
    }

    private Exception CreateSdlExceptionVE(string operation)
    {
        string error = _getErrorVE is null ? "error SDL desconocido" : PtrToUtf8VE(_getErrorVE()) ?? "error SDL desconocido";
        return new InvalidOperationException($"{operation} falló: {error}");
    }

    private static string? PtrToUtf8VE(IntPtr pointer)
        => pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);

    private void EnsureOpenVE()
    {
        ThrowIfDisposedVE();
        if (!IsOpenVE)
            throw new InvalidOperationException("Renderer SDL/Direct3D11 no inicializado.");
    }

    private void ThrowIfDisposedVE() => ObjectDisposedException.ThrowIf(_disposedVE, this);

    private void DisposeNativeVE()
    {
        if (_rendererVE != IntPtr.Zero)
        {
            _destroyRendererVE?.Invoke(_rendererVE);
            _rendererVE = IntPtr.Zero;
        }

        if (_windowVE != IntPtr.Zero)
        {
            _destroyWindowVE?.Invoke(_windowVE);
            _windowVE = IntPtr.Zero;
        }

        if (_videoInitializedVE)
        {
            _quitSubSystemVE?.Invoke(SdlInitVideoVE);
            _videoInitializedVE = false;
        }

        if (_libraryVE != IntPtr.Zero)
        {
            NativeLibrary.Free(_libraryVE);
            _libraryVE = IntPtr.Zero;
        }

        BackendVE = null;
    }

    public void Dispose()
    {
        if (_disposedVE)
            return;
        DisposeNativeVE();
        _disposedVE = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlFRectVE
    {
        public SdlFRectVE(float x, float y, float w, float h)
        {
            X = x; Y = y; W = w; H = h;
        }
        public float X;
        public float Y;
        public float W;
        public float H;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlInitSubSystemDelegate(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlQuitSubSystemDelegate(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint SdlCreatePropertiesDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlDestroyPropertiesDelegate(uint properties);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlSetPointerPropertyDelegate(uint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlCreateWindowWithPropertiesDelegate(uint properties);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlDestroyWindowDelegate(IntPtr window);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlCreateRendererDelegate(IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlDestroyRendererDelegate(IntPtr renderer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlGetRendererNameDelegate(IntPtr renderer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlCreateTextureDelegate(IntPtr renderer, uint format, int access, int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlDestroyTextureDelegate(IntPtr texture);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlUpdateYuvTextureDelegate(IntPtr texture, IntPtr rect, IntPtr yPlane, int yPitch, IntPtr uPlane, int uPitch, IntPtr vPlane, int vPitch);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlUpdateNvTextureDelegate(IntPtr texture, IntPtr rect, IntPtr yPlane, int yPitch, IntPtr uvPlane, int uvPitch);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlGetRenderOutputSizeDelegate(IntPtr renderer, out int width, out int height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlSetRenderDrawColorDelegate(IntPtr renderer, byte r, byte g, byte b, byte a);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlRenderClearDelegate(IntPtr renderer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlRenderTextureDelegate(IntPtr renderer, IntPtr texture, IntPtr source, ref SdlFRectVE destination);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlRenderTextureRotatedDelegate(IntPtr renderer, IntPtr texture, IntPtr source, ref SdlFRectVE destination, double angle, IntPtr center, uint flip);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlRenderPresentDelegate(IntPtr renderer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlPumpEventsDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlGetErrorDelegate();
}
