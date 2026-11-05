using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace NOVORA.Linux.VisionEngine;

internal interface ILinuxVideoRenderer
{
    void Present(byte[] rgba, int width, int height);
}

internal sealed class LinuxSdlVideoRenderer : ILinuxVideoRenderer, IDisposable
{
    private const uint SdlInitVideo = 0x00000020;
    private const ulong WindowResizable = 0x0000000000000020;
    private const ulong WindowHighPixelDensity = 0x0000000000002000;
    private const int TextureAccessStreaming = 1;
    private const int ScaleModeLinear = 1;
    private readonly IntPtr _library;
    private readonly SdlQuitSubSystem _quitSubSystem;
    private readonly SdlDestroyWindow _destroyWindow;
    private readonly SdlDestroyRenderer _destroyRenderer;
    private readonly SdlDestroyTexture _destroyTexture;
    private readonly SdlCreateTexture _createTexture;
    private readonly SdlUpdateTexture _updateTexture;
    private readonly SdlRenderClear _renderClear;
    private readonly SdlRenderTexture _renderTexture;
    private readonly SdlRenderPresent _renderPresent;
    private readonly SdlGetRendererName _getRendererName;
    private readonly SdlGetRenderOutputSize _getRenderOutputSize;
    private readonly SdlSetTextureScaleMode _setTextureScaleMode;
    private readonly SdlPumpEvents _pumpEvents;
    private readonly SdlGetError _getError;
    private readonly IntPtr _destinationRect;
    private IntPtr _window;
    private IntPtr _renderer;
    private IntPtr _texture;
    private int _width;
    private int _height;
    private bool _initialized;
    private bool _disposed;
    private long _framesPresented;

    public LinuxSdlVideoRenderer()
    {
        _library = NativeLibrary.Load("libSDL3.so.0");
        var init = Load<SdlInitSubSystem>("SDL_InitSubSystem");
        _quitSubSystem = Load<SdlQuitSubSystem>("SDL_QuitSubSystem");
        _destroyWindow = Load<SdlDestroyWindow>("SDL_DestroyWindow");
        _destroyRenderer = Load<SdlDestroyRenderer>("SDL_DestroyRenderer");
        _destroyTexture = Load<SdlDestroyTexture>("SDL_DestroyTexture");
        _createTexture = Load<SdlCreateTexture>("SDL_CreateTexture");
        _updateTexture = Load<SdlUpdateTexture>("SDL_UpdateTexture");
        _renderClear = Load<SdlRenderClear>("SDL_RenderClear");
        _renderTexture = Load<SdlRenderTexture>("SDL_RenderTexture");
        _renderPresent = Load<SdlRenderPresent>("SDL_RenderPresent");
        _getRendererName = Load<SdlGetRendererName>("SDL_GetRendererName");
        _getRenderOutputSize = Load<SdlGetRenderOutputSize>("SDL_GetRenderOutputSize");
        _setTextureScaleMode = Load<SdlSetTextureScaleMode>("SDL_SetTextureScaleMode");
        _pumpEvents = Load<SdlPumpEvents>("SDL_PumpEvents");
        _getError = Load<SdlGetError>("SDL_GetError");
        _createWindow = Load<SdlCreateWindow>("SDL_CreateWindow");
        _createRenderer = Load<SdlCreateRenderer>("SDL_CreateRenderer");
        _getPixelFormatForMasks = Load<SdlGetPixelFormatForMasks>("SDL_GetPixelFormatForMasks");

        _destinationRect = Marshal.AllocHGlobal(Marshal.SizeOf<SdlFRect>());
        try
        {
            InvokeOnUi(() =>
            {
                if (!init(SdlInitVideo))
                    throw CreateException("SDL_InitSubSystem(SDL_INIT_VIDEO)");
                _initialized = true;
            });
        }
        catch
        {
            Marshal.FreeHGlobal(_destinationRect);
            NativeLibrary.Free(_library);
            throw;
        }
    }

    private readonly SdlCreateWindow _createWindow;
    private readonly SdlCreateRenderer _createRenderer;
    private readonly SdlGetPixelFormatForMasks _getPixelFormatForMasks;

    public string Backend { get; private set; } = "Unavailable";
    public long FramesPresented => Interlocked.Read(ref _framesPresented);
    public int OutputWidth { get; private set; }
    public int OutputHeight { get; private set; }

    public void Present(byte[] rgba, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4))
            throw new InvalidDataException("El frame RGBA de VisionEngine tiene dimensiones inválidas.");

        InvokeOnUi(() => PresentOnUi(rgba, width, height));
    }

    private void PresentOnUi(byte[] rgba, int width, int height)
    {
        EnsureSurface(width, height);
        GCHandle pinned = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            if (!_updateTexture(_texture, IntPtr.Zero, pinned.AddrOfPinnedObject(), checked(width * 4)))
                throw CreateException("SDL_UpdateTexture");
        }
        finally
        {
            pinned.Free();
        }

        if (!_getRenderOutputSize(_renderer, out int outputWidth, out int outputHeight))
            throw CreateException("SDL_GetRenderOutputSize");
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
        if (outputWidth <= 0 || outputHeight <= 0)
            return;

        float scale = Math.Min(outputWidth / (float)width, outputHeight / (float)height);
        float destinationWidth = width * scale;
        float destinationHeight = height * scale;
        Marshal.StructureToPtr(new SdlFRect(
            (outputWidth - destinationWidth) / 2,
            (outputHeight - destinationHeight) / 2,
            destinationWidth,
            destinationHeight), _destinationRect, false);

        if (!_renderClear(_renderer) || !_renderTexture(_renderer, _texture, IntPtr.Zero, _destinationRect))
            throw CreateException("SDL_RenderTexture");
        _renderPresent(_renderer);
        _pumpEvents();
        Interlocked.Increment(ref _framesPresented);
    }

    private void EnsureSurface(int width, int height)
    {
        if (_renderer != IntPtr.Zero && _width == width && _height == height)
            return;

        if (_texture != IntPtr.Zero)
        {
            _destroyTexture(_texture);
            _texture = IntPtr.Zero;
        }
        if (_renderer != IntPtr.Zero)
        {
            _destroyRenderer(_renderer);
            _renderer = IntPtr.Zero;
        }

        if (_window == IntPtr.Zero)
        {
            _window = _createWindow("NOVORA-LINK · VisionEngine", width, height,
                WindowResizable | WindowHighPixelDensity);
            if (_window == IntPtr.Zero)
                throw CreateException("SDL_CreateWindow");
        }
        string? rendererName = Environment.GetEnvironmentVariable("NOVORA_SDL_RENDERER");
        if (string.IsNullOrWhiteSpace(rendererName))
            rendererName = null;
        else
            rendererName = rendererName.Trim();
        _renderer = _createRenderer(_window, rendererName);
        if (_renderer == IntPtr.Zero)
            throw CreateException("SDL_CreateRenderer");
        Backend = Marshal.PtrToStringUTF8(_getRendererName(_renderer)) ?? "Unknown";

        uint pixelFormat = _getPixelFormatForMasks(32,
            0x000000ff, 0x0000ff00, 0x00ff0000, 0xff000000);
        if (pixelFormat == 0)
            throw CreateException("SDL_GetPixelFormatForMasks");
        _texture = _createTexture(_renderer, pixelFormat, TextureAccessStreaming, width, height);
        if (_texture == IntPtr.Zero)
            throw CreateException("SDL_CreateTexture");
        if (!_setTextureScaleMode(_texture, ScaleModeLinear))
            throw CreateException("SDL_SetTextureScaleMode");
        _width = width;
        _height = height;
    }

    private static void InvokeOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();
    }

    private T Load<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    private InvalidOperationException CreateException(string operation)
    {
        IntPtr error = _getError();
        return new InvalidOperationException(
            $"{operation} falló: {(error == IntPtr.Zero ? "error SDL desconocido" : Marshal.PtrToStringUTF8(error))}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        InvokeOnUi(() =>
        {
            if (_texture != IntPtr.Zero) _destroyTexture(_texture);
            if (_renderer != IntPtr.Zero) _destroyRenderer(_renderer);
            if (_window != IntPtr.Zero) _destroyWindow(_window);
            if (_initialized) _quitSubSystem(SdlInitVideo);
        });
        Marshal.FreeHGlobal(_destinationRect);
        NativeLibrary.Free(_library);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct SdlFRect(float X, float Y, float W, float H);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlInitSubSystem(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlQuitSubSystem(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlCreateWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int width, int height, ulong flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlDestroyWindow(IntPtr window);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlCreateRenderer(
        IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string? name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlDestroyRenderer(IntPtr renderer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlDestroyTexture(IntPtr texture);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint SdlGetPixelFormatForMasks(int bitsPerPixel, uint redMask, uint greenMask, uint blueMask, uint alphaMask);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlCreateTexture(IntPtr renderer, uint format, int access, int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlUpdateTexture(IntPtr texture, IntPtr rect, IntPtr pixels, int pitch);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlRenderClear(IntPtr renderer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlRenderTexture(IntPtr renderer, IntPtr texture, IntPtr sourceRect, IntPtr destinationRect);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlRenderPresent(IntPtr renderer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlGetRendererName(IntPtr renderer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlGetRenderOutputSize(IntPtr renderer, out int width, out int height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlSetTextureScaleMode(IntPtr texture, int scaleMode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlPumpEvents();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SdlGetError();
}
