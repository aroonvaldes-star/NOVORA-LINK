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
    private readonly LinuxVisionSettings _settings;
    private readonly LinuxVisionInput _input = new();
    private readonly SdlPollEvent _pollEvent;
<<<<<<< Updated upstream
=======
    private readonly SdlGetWindowProperties _getWindowProperties;
    private readonly SdlGetPointerProperty _getPointerProperty;
    private readonly SdlAddEventWatch _addEventWatch;
    private readonly SdlRemoveEventWatch _removeEventWatch;
>>>>>>> Stashed changes
    private readonly SdlGetWindowId _getWindowId;
    private readonly SdlGetWindowSize _getWindowSize;
    private readonly SdlStartTextInput _startTextInput;
    private readonly SdlStopTextInput _stopTextInput;
    private readonly SdlSetWindowFullscreen _setWindowFullscreen;
    private readonly SdlGetDisplayBounds _getDisplayBounds;
    private readonly SdlGetWindowPosition _getWindowPosition;
    private readonly SdlSetWindowTitle _setWindowTitle;
    private const string BaseWindowTitle = "NOVORA-LINK · VisionEngine";
    private readonly SdlSetRenderVSync _setRenderVSync;
    // Position is saved only after SDL reports a real move; Wayland never does, so 0,0 is never stored.
    private bool _positionKnown, _placementDirty;
    private long _lastMoveTicks;
<<<<<<< Updated upstream
    private DispatcherTimer? _eventTimer;
=======
    private DispatcherTimer? _eventTimer;   // fallback only when no display fd is exposed
    private LinuxDisplayWakeWatcher? _displayWake;
    private SdlEventFilter? _eventFilter;   // kept alive: SDL holds the native pointer
    private int _pumpQueued;
    private int _placementRecheckQueued;
    private bool _eventPumpStarted;
>>>>>>> Stashed changes
    private VideoSession? _session;
    private bool _closed;
    private float _wheelHorizontal, _wheelVertical, _wheelX, _wheelY;
    private long _nextWheelAt;
    public event Action<byte[]>? Input;
    public event Action? Closed;
    /// <summary>Ctrl+C (true) or Ctrl+V (false) in the video window; owned by ExchangeVE, never sent to Android as keys.</summary>
    public event Action<bool>? ClipboardShortcut;
    /// <summary>Files or folders dropped on the video window (SDL_EVENT_DROP_*), raised once per drop gesture.</summary>
    public event Action<string[]>? FilesDropped;
    public string? InputError { get; private set; }
<<<<<<< Updated upstream
    private sealed record PresentedFrame(byte[] Rgba, int Width, int Height);
    private PresentedFrame? _lastFrame;
=======
    // Copia propia del último fotograma para capturas: el decoder reutiliza su buffer entre fotogramas.
    private readonly object _snapshotGate = new();
    private byte[]? _snapshot;
    private int _snapshotWidth, _snapshotHeight;
>>>>>>> Stashed changes
    private readonly List<string> _drop = new();
    private bool _vsync;


    public LinuxSdlVideoRenderer(LinuxVisionSettings? settings = null)
    {
        _settings = settings ?? LinuxVisionSettings.Default;
        _vsync = _settings.VSync;
        _input.Frame += frame => Input?.Invoke(frame);
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

        _pollEvent = Load<SdlPollEvent>("SDL_PollEvent");
<<<<<<< Updated upstream
=======
        _getWindowProperties = Load<SdlGetWindowProperties>("SDL_GetWindowProperties");
        _getPointerProperty = Load<SdlGetPointerProperty>("SDL_GetPointerProperty");
        _addEventWatch = Load<SdlAddEventWatch>("SDL_AddEventWatch");
        _removeEventWatch = Load<SdlRemoveEventWatch>("SDL_RemoveEventWatch");
>>>>>>> Stashed changes
        _getWindowId = Load<SdlGetWindowId>("SDL_GetWindowID");
        _getWindowSize = Load<SdlGetWindowSize>("SDL_GetWindowSize");
        _startTextInput = Load<SdlStartTextInput>("SDL_StartTextInput");
        _stopTextInput = Load<SdlStopTextInput>("SDL_StopTextInput");
        _setWindowFullscreen = Load<SdlSetWindowFullscreen>("SDL_SetWindowFullscreen");
        _getDisplayBounds = Load<SdlGetDisplayBounds>("SDL_GetDisplayBounds");
        _getWindowPosition = Load<SdlGetWindowPosition>("SDL_GetWindowPosition");
        _setWindowTitle = Load<SdlSetWindowTitle>("SDL_SetWindowTitle");
        _setRenderVSync = Load<SdlSetRenderVSync>("SDL_SetRenderVSync");

        _destinationRect = Marshal.AllocHGlobal(Marshal.SizeOf<SdlFRect>());
        try
        {
            InvokeOnUi(() =>
            {
                if (!LinuxSdlVideoDriver.InitVideo(_library, () => init(SdlInitVideo)))
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

<<<<<<< Updated upstream
        // The decoder allocates one array per frame and never mutates it after Present.
        Volatile.Write(ref _lastFrame, new PresentedFrame(rgba, width, height));
=======
        // El decoder reutiliza su buffer: se guarda una copia para las capturas, sin asignar memoria por fotograma.
        lock (_snapshotGate)
        {
            if (_snapshot is null || _snapshot.Length != rgba.Length)
                _snapshot = new byte[rgba.Length];
            Buffer.BlockCopy(rgba, 0, _snapshot, 0, rgba.Length);
            _snapshotWidth = width;
            _snapshotHeight = height;
        }
>>>>>>> Stashed changes
        InvokeOnUi(() => PresentOnUi(rgba, width, height));
    }

    /// <summary>Last decoded frame (RGBA, visible orientation) for still captures.</summary>
    public bool TryGetLastFrame(out byte[] rgba, out int width, out int height)
    {
<<<<<<< Updated upstream
        PresentedFrame? frame = Volatile.Read(ref _lastFrame);
        (rgba, width, height) = frame is null ? (Array.Empty<byte>(), 0, 0) : (frame.Rgba, frame.Width, frame.Height);
        return frame is not null;
=======
        lock (_snapshotGate)
        {
            if (_snapshot is null)
            {
                (rgba, width, height) = (Array.Empty<byte>(), 0, 0);
                return false;
            }
            rgba = (byte[])_snapshot.Clone();
            width = _snapshotWidth;
            height = _snapshotHeight;
            return true;
        }
>>>>>>> Stashed changes
    }

    /// <summary>Dynamic presentation can change during the session, as on Windows.</summary>
    public void SetVSync(bool enabled) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed || _vsync == enabled) return;
        _vsync = enabled;
        if (_renderer != IntPtr.Zero && !_setRenderVSync(_renderer, enabled ? 1 : 0))
        {
            _vsync = !enabled;
            InputError = CreateException("SDL_SetRenderVSync").Message;
        }
    });

    public void UpdateSession(VideoSession session) => InvokeOnUi(() =>
    {
        _session = session;
        RefreshViewport();
    });

    private void RefreshViewport()
    {
        if (_window == IntPtr.Zero || _session is null) return;
        if (!_getWindowSize(_window, out int width, out int height) ||
            !_getRenderOutputSize(_renderer, out int pixelsWidth, out int pixelsHeight)) return;
        _input.UpdateViewport(_session, width, height, pixelsWidth, pixelsHeight);
    }

    /// <summary>
    /// Shows a short state line in the VisionEngine title (FILES state, refused drops). Safe from any thread:
    /// the change is posted to the UI thread that owns the SDL window.
    /// </summary>
    public void SetStatus(string text)
    {
        string line = string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
        if (line.Length > 160) line = line[..160] + "…";
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _closed || _window == IntPtr.Zero) return;
            _setWindowTitle(_window, line.Length == 0 ? BaseWindowTitle : BaseWindowTitle + " — " + line);
        });
    }

    /// <summary>Saves the position once the user stops dragging, so a drag does not write the file per event.</summary>
    private void FlushWindowPlacement()
    {
<<<<<<< Updated upstream
        if (!_placementDirty || Environment.TickCount64 - _lastMoveTicks < 500) return;
=======
        if (!_placementDirty) return;
        long quietFor = Environment.TickCount64 - _lastMoveTicks;
        if (quietFor < 500)
        {
            // Si el usuario deja de mover la ventana no llega otro evento que vuelva a bombear: se programa una comprobación al terminar la calma.
            if (Interlocked.Exchange(ref _placementRecheckQueued, 1) == 0)
                _ = Task.Delay((int)(500 - quietFor) + 20).ContinueWith(_ =>
                {
                    Volatile.Write(ref _placementRecheckQueued, 0);
                    RequestPump();
                }, TaskScheduler.Default);
            return;
        }
>>>>>>> Stashed changes
        SaveWindowPlacement();
    }

    private void SaveWindowPlacement()
    {
        _placementDirty = false;
        if (!_positionKnown || _window == IntPtr.Zero) return;
        if (_getWindowPosition(_window, out int x, out int y))
            LinuxVisionWindowPlacement.Save(x, y);
    }

    private void PresentOnUi(byte[] rgba, int width, int height)
    {
        if (_closed) throw new IOException("Se cerró la ventana de VisionEngine.");
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

        DrawCurrentFrame();
        Interlocked.Increment(ref _framesPresented);
    }

    /// <summary>
    /// Redraws the texture already uploaded. Window moves and scale changes alter the letterbox even
    /// when the phone sends no new frame, so the image must be fitted again without waiting for one.
    /// </summary>
    private void DrawCurrentFrame()
    {
        if (_renderer == IntPtr.Zero || _texture == IntPtr.Zero) return;
        if (!_getRenderOutputSize(_renderer, out int outputWidth, out int outputHeight))
            throw CreateException("SDL_GetRenderOutputSize");
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
        if (outputWidth <= 0 || outputHeight <= 0)
            return;

        float scale = Math.Min(outputWidth / (float)_width, outputHeight / (float)_height);
        float destinationWidth = _width * scale;
        float destinationHeight = _height * scale;
        Marshal.StructureToPtr(new SdlFRect(
            (outputWidth - destinationWidth) / 2,
            (outputHeight - destinationHeight) / 2,
            destinationWidth,
            destinationHeight), _destinationRect, false);

        if (!_renderClear(_renderer) || !_renderTexture(_renderer, _texture, IntPtr.Zero, _destinationRect))
            throw CreateException("SDL_RenderTexture");
        _renderPresent(_renderer);
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
            uint monitorId = ResolveMonitorOrDefault(_settings);
            // An explicit monitor wins; otherwise reopen where the window was last closed.
            LinuxVisionWindowPlacement? placement = monitorId == 0 ? LinuxVisionWindowPlacement.Load() : null;
            _window = monitorId != 0
                ? CreateWindowOnMonitor(width, height, monitorId)
                : placement is not null
                    ? CreateWindowAt(width, height, placement.X, placement.Y)
                    : _createWindow("NOVORA-LINK · VisionEngine", width, height, WindowResizable | WindowHighPixelDensity);
            if (_window == IntPtr.Zero)
                throw CreateException("SDL_CreateWindow");
            if (_settings.Fullscreen && !_setWindowFullscreen(_window, true))
                throw CreateException("SDL_SetWindowFullscreen");
            if (_settings.RemoteInput && !_startTextInput(_window))
                throw CreateException("SDL_StartTextInput");
<<<<<<< Updated upstream
            _eventTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Input,
                (_, _) =>
                {
                    PollEvents();
                    FlushWindowPlacement();
                });
            _eventTimer.Start();
=======
>>>>>>> Stashed changes
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
        if (_vsync && !_setRenderVSync(_renderer, 1))
            throw CreateException("El renderer seleccionado no admite sincronización de presentación");

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
        _session ??= new VideoSession(width, height, 0, 16 * 1024 * 1024);
        RefreshViewport();
<<<<<<< Updated upstream
=======
        // La bomba arranca solo cuando el renderer ya existe: si la creación falla, no queda una bomba sin renderer.
        if (!_eventPumpStarted)
        {
            _eventPumpStarted = true;
            StartEventPump();
        }
>>>>>>> Stashed changes
    }

    /// <summary>
    /// A monitor saved under another SDL driver can have a different name or ID (Wayland "Pantalla integrada"
    /// is "eDP-1" under XWayland). Opening VisionEngine on the default monitor beats refusing to open at all;
    /// the user can choose the monitor again on the VisionEngine page.
    /// </summary>
    private static uint ResolveMonitorOrDefault(LinuxVisionSettings settings)
    {
        if (settings.MonitorId == 0 && string.IsNullOrWhiteSpace(settings.MonitorName)) return 0;
        try { return LinuxSdlDevices.ResolveMonitorId(settings.MonitorId, settings.MonitorName); }
        catch (InvalidOperationException) when (!string.IsNullOrWhiteSpace(settings.MonitorName)) { return 0; }
    }

    private IntPtr CreateWindowOnMonitor(int width, int height, uint monitorId)
    {
        if (!_getDisplayBounds(monitorId, out SdlRect bounds))
            throw CreateException("El monitor seleccionado ya no está disponible; selecciona el predeterminado");
        return CreateWindowAt(width, height,
            bounds.X + Math.Max(0, (bounds.W - width) / 2),
            bounds.Y + Math.Max(0, (bounds.H - height) / 2));
    }

    private IntPtr CreateWindowAt(int width, int height, int x, int y)
    {
        var createProperties = Load<SdlCreateProperties>("SDL_CreateProperties");
        var destroyProperties = Load<SdlDestroyProperties>("SDL_DestroyProperties");
        var setNumber = Load<SdlSetNumberProperty>("SDL_SetNumberProperty");
        var setString = Load<SdlSetStringProperty>("SDL_SetStringProperty");
        var setBoolean = Load<SdlSetBooleanProperty>("SDL_SetBooleanProperty");
        var createWindow = Load<SdlCreateWindowWithProperties>("SDL_CreateWindowWithProperties");
        uint properties = createProperties();
        if (properties == 0) throw CreateException("SDL_CreateProperties");
        try
        {
            // Initial placement works on Wayland; moving a normal window after creation does not.
            if (!setString(properties, "SDL.window.create.title", "NOVORA-LINK · VisionEngine") ||
                !setNumber(properties, "SDL.window.create.width", width) ||
                !setNumber(properties, "SDL.window.create.height", height) ||
                !setNumber(properties, "SDL.window.create.x", x) ||
                !setNumber(properties, "SDL.window.create.y", y) ||
                !setBoolean(properties, "SDL.window.create.resizable", true) ||
                !setBoolean(properties, "SDL.window.create.high_pixel_density", true))
                throw CreateException("No se pudo configurar la ventana del monitor seleccionado");
            return createWindow(properties);
        }
        finally { destroyProperties(properties); }
    }

<<<<<<< Updated upstream
=======
    /// <summary>
    /// Event-driven pump: the display fd wakes the UI thread when data arrives, and an SDL event
    /// watch wakes it when SDL queues an event read during another call (e.g. presentation).
    /// Without a display fd (neither Wayland nor X11 exposed) it falls back to the 8 ms timer.
    /// </summary>
    private void StartEventPump()
    {
        uint props = _getWindowProperties(_window);
        IntPtr wayland = props == 0 ? IntPtr.Zero : _getPointerProperty(props, "SDL.window.wayland.display", IntPtr.Zero);
        IntPtr x11 = props == 0 || wayland != IntPtr.Zero ? IntPtr.Zero : _getPointerProperty(props, "SDL.window.x11.display", IntPtr.Zero);
        _displayWake = LinuxDisplayWakeWatcher.TryCreate(wayland, x11, RequestPump, OnDisplayWakeFailed);
        _eventFilter = (_, _) => { RequestPump(); return true; };
        if (!_addEventWatch(_eventFilter, IntPtr.Zero))
            throw CreateException("SDL_AddEventWatch");
        if (_displayWake is not null)
        {
            _displayWake.Start();
            return;
        }
        EnableTimerFallback();
    }

    /// <summary>Respaldo de la bomba: un temporizador de 8 ms en el hilo de UI.</summary>
    private void EnableTimerFallback()
    {
        if (_disposed || _closed || _eventTimer is not null) return;
        _eventTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Input, (_, _) => OnPump());
        _eventTimer.Start();
    }

    // Llamado desde el hilo del watcher: el respaldo por temporizador se activa en el hilo de UI.
    private void OnDisplayWakeFailed()
    {
        try { Dispatcher.UIThread.Post(EnableTimerFallback, DispatcherPriority.Input); }
        catch (Exception) { /* el dispatcher ya se está apagando */ }
    }

    /// <summary>Coalesces wake-ups: at most one pump is queued on the UI thread at a time. Returns false if one was already queued.</summary>
    private bool RequestPump()
    {
        if (_closed || _disposed) return false;
        if (Interlocked.Exchange(ref _pumpQueued, 1) != 0) return false;
        try
        {
            Dispatcher.UIThread.Post(OnPump, DispatcherPriority.Input);
            return true;
        }
        catch (Exception)
        {
            // Si el dispatcher no acepta la bomba, se libera la bandera para que una petición posterior sí pueda encolarse.
            Volatile.Write(ref _pumpQueued, 0);
            return false;
        }
    }

    private void OnPump()
    {
        Volatile.Write(ref _pumpQueued, 0);
        PollEvents();
        FlushWindowPlacement();
    }

    private void StopEventPump()
    {
        _displayWake?.Dispose();
        _displayWake = null;
        _eventTimer?.Stop();
        _eventTimer = null;
        if (_eventFilter is not null)
        {
            _removeEventWatch(_eventFilter, IntPtr.Zero);
            _eventFilter = null;
        }
    }

>>>>>>> Stashed changes
    private void PollEvents()
    {
        if (_disposed || _closed || _window == IntPtr.Zero) return;
        try
        {
            uint id = _getWindowId(_window);
            RefreshViewport();
            // Bound work per UI tick, preserving queued events for the next tick.
<<<<<<< Updated upstream
            for (int count = 0; count < 256 && _pollEvent(out SdlEvent e); count++)
=======
            int count = 0;
            for (; count < 256 && _pollEvent(out SdlEvent e); count++)
>>>>>>> Stashed changes
            {
                if (e.Type == 0x100 || e.WindowId == id && e.Type == 0x210)
                {
                    SaveWindowPlacement();
                    _input.ReleaseAll();
                    _closed = true;
<<<<<<< Updated upstream
                    _eventTimer?.Stop();
=======
                    StopEventPump();
>>>>>>> Stashed changes
                    Closed?.Invoke();
                    return;
                }
                if (e.WindowId != id) continue;
                // Exposed, moved, resized and pixel-size changes: refit the image and mapping now.
                if (e.Type is 0x204 or 0x205 or 0x206 or 0x207)
                {
                    if (e.Type == 0x205)
                    {
                        _positionKnown = true;
                        _placementDirty = true;
                        _lastMoveTicks = Environment.TickCount64;
                    }
                    RefreshViewport();
                    DrawCurrentFrame();
                    continue;
                }
                if (e.Type is 0x1000 or 0x1002 or 0x1003)
                {
                    HandleDrop(e);
                    continue;
                }
                if (e.Type == 0x300 && IsClipboardShortcut(e.Scancode, e.Modifiers, out bool copy))
                {
                    if (e.Repeat == 0) ClipboardShortcut?.Invoke(copy);
                    continue;
                }
                if (e.Type == 0x20f)
                {
                    _input.ReleaseAll();
                    _wheelHorizontal = _wheelVertical = 0;
                    continue;
                }
                if (!_settings.RemoteInput) continue;
                switch (e.Type)
                {
                    case 0x300: case 0x301:
                        _input.Key(e.Scancode, e.Modifiers, e.Type == 0x300, e.Repeat != 0);
                        break;
                    case 0x303:
                        _input.Text(Marshal.PtrToStringUTF8(e.Text) ?? string.Empty);
                        break;
                    case 0x400:
                        _input.Motion(e.MotionX, e.MotionY);
                        break;
                    case 0x401: case 0x402:
                        _input.Button(e.Button, e.Type == 0x401, e.ButtonX, e.ButtonY);
                        break;
                    case 0x403:
                        float direction = e.WheelDirection == 1 ? -1 : 1;
                        _wheelHorizontal += e.WheelX * direction;
                        _wheelVertical += e.WheelY * direction;
                        _wheelX = e.WheelMouseX;
                        _wheelY = e.WheelMouseY;
                        break;
                }
            }
<<<<<<< Updated upstream
=======
            // Si se llegó al límite puede quedar cola en SDL; sin otra vuelta esperaría al siguiente aviso.
            if (count == 256) RequestPump();
>>>>>>> Stashed changes
            // Accessibility consumes gestures, not raw HID deltas. Coalesce touchpad updates
            // into one gesture window without truncating the fractional delta to an integer.
            if ((_wheelHorizontal != 0 || _wheelVertical != 0) && Environment.TickCount64 >= _nextWheelAt)
            {
                _input.Scroll(_wheelX, _wheelY, _wheelHorizontal, _wheelVertical, false);
                _wheelHorizontal = _wheelVertical = 0;
                _nextWheelAt = Environment.TickCount64 + 80;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            InputError = ex.Message;
            _closed = true;
<<<<<<< Updated upstream
            _eventTimer?.Stop();
=======
            StopEventPump();
>>>>>>> Stashed changes
            Closed?.Invoke();
        }
    }

    // SDL scancodes 6/25 are the physical C/V keys; Ctrl without Alt/AltGr, as Windows ExchangeVE.
    internal static bool IsClipboardShortcut(int scancode, ushort modifiers, out bool copy)
    {
        copy = scancode == 6;
        return scancode is 6 or 25 && (modifiers & 0x00c0) != 0 && (modifiers & 0x0300) == 0;
    }

    private void HandleDrop(SdlEvent e)
    {
        switch (e.Type)
        {
            case 0x1002: _drop.Clear(); break;
            case 0x1000:
                // SDL owns the string only until the next poll; copy it now.
                if (Marshal.PtrToStringUTF8(e.DropData) is { Length: > 0 } path && _drop.Count < 4096) _drop.Add(path);
                break;
            case 0x1003:
                if (_drop.Count > 0) FilesDropped?.Invoke(_drop.ToArray());
                _drop.Clear();
                break;
        }
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
<<<<<<< Updated upstream
        InvokeOnUi(() =>
        {
            if (_disposed) return;
            _eventTimer?.Stop();
            _input.ReleaseAll();
            _disposed = true;
            if (_window != IntPtr.Zero && _settings.RemoteInput) _stopTextInput(_window);
            if (_texture != IntPtr.Zero) _destroyTexture(_texture);
            if (_renderer != IntPtr.Zero) _destroyRenderer(_renderer);
            if (_window != IntPtr.Zero) _destroyWindow(_window);
            if (_initialized) _quitSubSystem(SdlInitVideo);
        });
=======
        // Espera acotada: si la UI no responde en 2 s (apagándose o bloqueada), no se espera indefinidamente.
        // En ese caso NO se libera nada aquí: la biblioteca SDL y los recursos nativos pueden seguir en uso
        // por trabajo ya encolado en la UI. _closed impide nuevos presentados y sondeos; el hilo de watcher se
        // detiene desde aquí (no toca SDL ni el dispatcher). Si la UI termina después, ReleaseOnUiVE libera
        // ventana, renderer y textura en el hilo correcto. La biblioteca se deja cargada a propósito.
        if (!TryInvokeOnUi(ReleaseOnUiVE, TimeSpan.FromSeconds(2)))
        {
            _closed = true;
            _displayWake?.Dispose();
            return;
        }
>>>>>>> Stashed changes
        Marshal.FreeHGlobal(_destinationRect);
        NativeLibrary.Free(_library);
    }

<<<<<<< Updated upstream
=======
    private void ReleaseOnUiVE()
    {
        if (_disposed) return;
        StopEventPump();
        _input.ReleaseAll();
        _disposed = true;
        if (_window != IntPtr.Zero && _settings.RemoteInput) _stopTextInput(_window);
        if (_texture != IntPtr.Zero) _destroyTexture(_texture);
        if (_renderer != IntPtr.Zero) _destroyRenderer(_renderer);
        if (_window != IntPtr.Zero) _destroyWindow(_window);
        if (_initialized) _quitSubSystem(SdlInitVideo);
    }

    /// <summary>Ejecuta en el hilo de UI y espera como máximo <paramref name="timeout"/>; false si no terminó a tiempo o falló.</summary>
    private static bool TryInvokeOnUi(Action action, TimeSpan timeout)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return true;
        }
        // Sin using: si se agota el tiempo, la acción aún puede terminar y señalar este evento.
        var done = new ManualResetEventSlim(false);
        Dispatcher.UIThread.Post(() =>
        {
            try { action(); }
            finally { done.Set(); }
        }, DispatcherPriority.Send);
        return done.Wait(timeout);
    }

>>>>>>> Stashed changes
    // SDL 3.2 ABI: fixed-size event union; text pointer is copied before the next poll.
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct SdlEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(24)] public int Scancode;
        [FieldOffset(32)] public ushort Modifiers;
        [FieldOffset(37)] public byte Repeat;
        [FieldOffset(24)] public IntPtr Text;
        [FieldOffset(28)] public float MotionX;
        [FieldOffset(32)] public float MotionY;
        [FieldOffset(24)] public byte Button;
        [FieldOffset(28)] public float ButtonX;
        [FieldOffset(32)] public float ButtonY;
        [FieldOffset(24)] public float WheelX;
        [FieldOffset(28)] public float WheelY;
        [FieldOffset(32)] public uint WheelDirection;
        [FieldOffset(36)] public float WheelMouseX;
        [FieldOffset(40)] public float WheelMouseY;
        [FieldOffset(40)] public IntPtr DropData;
    }
    [StructLayout(LayoutKind.Sequential)] private struct SdlRect { public int X, Y, W, H; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlPollEvent(out SdlEvent e);
<<<<<<< Updated upstream
=======
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint SdlGetWindowProperties(IntPtr window);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr SdlGetPointerProperty(uint props, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr defaultValue);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlAddEventWatch(SdlEventFilter filter, IntPtr userdata);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlEventFilter(IntPtr userdata, IntPtr evt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SdlRemoveEventWatch(SdlEventFilter filter, IntPtr userdata);
>>>>>>> Stashed changes
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint SdlGetWindowId(IntPtr window);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlGetWindowSize(IntPtr window, out int w, out int h);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlStartTextInput(IntPtr window);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlStopTextInput(IntPtr window);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint SdlCreateProperties();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SdlDestroyProperties(uint properties);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr SdlCreateWindowWithProperties(uint properties);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlSetNumberProperty(uint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, long value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlSetStringProperty(uint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlSetBooleanProperty(uint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.I1)] bool value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlSetWindowFullscreen(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool fullscreen);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlGetDisplayBounds(uint display, out SdlRect bounds);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlSetRenderVSync(IntPtr renderer, int vsync);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlGetWindowPosition(IntPtr window, out int x, out int y);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SdlSetWindowTitle(IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

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
