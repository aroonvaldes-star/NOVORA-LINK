using NOVORA.Models;
using NOVORA.VisionEngine.Exchange;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace NOVORA.VisionEngine.Renderer;

/// <summary>
/// Ventana dedicada de presentación de VisionEngine.
///
/// Usa SetWindowPos con coordenadas físicas de Win32 para no mezclar
/// píxeles de Screen.Bounds con DIPs de WPF.
///
/// También propaga Drag & Drop desde HostRendererVE hacia
/// MainWindow.VisionEngineVE.
///
/// Importante:
/// SetWindowPos se mantiene mediante DllImport.
/// Esta llamada sólo participa al posicionar la ventana y
/// no forma parte del hot-path de video/render de VisionEngine.
/// </summary>
public sealed class WindowRendererVE : System.Windows.Window
{
    // ============================================================
    // WIN32 CONSTANTS
    // ============================================================

    private const uint SwpNoZOrderVE =
        0x0004;

    private const uint SwpNoActivateVE =
        0x0010;

    private const uint SwpFrameChangedVE =
        0x0020;

    private const uint SwpShowWindowVE =
        0x0040;

    private const uint SwpNoOwnerZOrderVE =
        0x0200;

    // ============================================================
    // STATE
    // ============================================================

    private readonly MonitorInfo _monitorVE;
    private readonly bool _fullscreenVE;

    private int _targetLeftVE;
    private int _targetTopVE;
    private int _targetWidthVE;
    private int _targetHeightVE;

    private bool _allowCloseVE;

    // ============================================================
    // WIN32
    // ============================================================

    [DllImport(
        "user32.dll",
        SetLastError = true,
        ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    // ============================================================
    // CONSTRUCTOR
    // ============================================================

    public WindowRendererVE(
        MonitorInfo monitor,
        bool fullscreen)
    {
        ArgumentNullException.ThrowIfNull(
            monitor);

        _monitorVE =
            monitor;

        _fullscreenVE =
            fullscreen;

        HostVE =
            new HostRendererVE
            {
                HorizontalAlignment =
                    System.Windows.HorizontalAlignment.Stretch,

                VerticalAlignment =
                    System.Windows.VerticalAlignment.Stretch
            };

        // ========================================================
        // WINDOW
        // ========================================================

        Title =
            "NOVORA-LINK — VisionEngine";

        Background =
            System.Windows.Media.Brushes.Black;

        Content =
            HostVE;

        ShowInTaskbar =
            true;

        WindowStartupLocation =
            System.Windows.WindowStartupLocation.Manual;

        SizeToContent =
            System.Windows.SizeToContent.Manual;

        WindowState =
            System.Windows.WindowState.Normal;

        // ========================================================
        // HOST INPUT
        // ========================================================

        HostVE.KeyDownVE +=
            HostVE_KeyDownVE;

        // ========================================================
        // HOST DRAG & DROP
        // ========================================================

        HostVE.FilesDroppedVE +=
            HostVE_FilesDroppedVE;

        HostVE.DragEnteredVE +=
            HostVE_DragEnteredVE;

        HostVE.DragEndedVE +=
            HostVE_DragEndedVE;

        // ========================================================
        // WINDOW EVENTS
        // ========================================================

        PreviewKeyDown +=
            WindowRendererVE_PreviewKeyDown;

        Closing +=
            WindowRendererVE_Closing;

        SourceInitialized +=
            WindowRendererVE_SourceInitialized;

        Loaded +=
            WindowRendererVE_Loaded;

        ConfigurePresentationVE();
    }

    // ============================================================
    // EVENTS
    // ============================================================

    public event EventHandler?
        CloseRequestedVE;

    /// <summary>
    /// Archivos/carpetas soltados directamente sobre
    /// la superficie de VisionEngine.
    /// </summary>
    public event EventHandler<FilesDroppedEventArgsVE>?
        FilesDroppedVE;

    /// <summary>
    /// Un Drag compatible entró a VisionEngine.
    /// </summary>
    public event EventHandler?
        DragEnteredVE;

    /// <summary>
    /// El Drag terminó o abandonó VisionEngine.
    /// </summary>
    public event EventHandler?
        DragEndedVE;

    // ============================================================
    // PROPERTIES
    // ============================================================

    public HostRendererVE HostVE
    {
        get;
    }

    public bool IsFullscreenVE =>
        _fullscreenVE;

    public MonitorInfo MonitorVE =>
        _monitorVE;

    // ============================================================
    // OWNER CLOSE
    // ============================================================

    public void CloseFromOwnerVE()
    {
        if (!IsVisible)
        {
            return;
        }

        _allowCloseVE =
            true;

        try
        {
            Close();
        }
        finally
        {
            _allowCloseVE =
                false;
        }
    }

    // ============================================================
    // PRESENTATION CONFIGURATION
    // ============================================================

    private void ConfigurePresentationVE()
    {
        if (_fullscreenVE)
        {
            ConfigureFullscreenVE();

            return;
        }

        ConfigureWindowedVE();
    }

    // ============================================================
    // FULLSCREEN
    // ============================================================

    private void ConfigureFullscreenVE()
    {
        WindowStyle =
            System.Windows.WindowStyle.None;

        ResizeMode =
            System.Windows.ResizeMode.NoResize;

        Topmost =
            false;

        _targetLeftVE =
            _monitorVE.Left;

        _targetTopVE =
            _monitorVE.Top;

        _targetWidthVE =
            Math.Max(
                1,
                _monitorVE.Width);

        _targetHeightVE =
            Math.Max(
                1,
                _monitorVE.Height);
    }

    // ============================================================
    // WINDOWED
    // ============================================================

    private void ConfigureWindowedVE()
    {
        WindowStyle =
            System.Windows.WindowStyle.SingleBorderWindow;

        ResizeMode =
            System.Windows.ResizeMode.CanResize;

        MinWidth =
            480;

        MinHeight =
            320;

        int width =
            (int)Math.Round(
                _monitorVE.Width * 0.78,
                MidpointRounding.AwayFromZero);

        int height =
            (int)Math.Round(
                _monitorVE.Height * 0.78,
                MidpointRounding.AwayFromZero);

        int minimumWidth =
            Math.Min(
                480,
                _monitorVE.Width);

        int maximumWidth =
            Math.Max(
                480,
                _monitorVE.Width);

        int minimumHeight =
            Math.Min(
                320,
                _monitorVE.Height);

        int maximumHeight =
            Math.Max(
                320,
                _monitorVE.Height);

        width =
            Math.Clamp(
                width,
                minimumWidth,
                maximumWidth);

        height =
            Math.Clamp(
                height,
                minimumHeight,
                maximumHeight);

        _targetWidthVE =
            width;

        _targetHeightVE =
            height;

        _targetLeftVE =
            _monitorVE.Left +
            Math.Max(
                0,
                (_monitorVE.Width - width) / 2);

        _targetTopVE =
            _monitorVE.Top +
            Math.Max(
                0,
                (_monitorVE.Height - height) / 2);
    }

    // ============================================================
    // SOURCE INITIALIZED
    // ============================================================

    private void WindowRendererVE_SourceInitialized(
        object? sender,
        EventArgs e)
    {
        ApplyBoundsVE();
    }

    // ============================================================
    // LOADED
    // ============================================================

    private void WindowRendererVE_Loaded(
        object sender,
        System.Windows.RoutedEventArgs e)
    {
        ApplyBoundsVE();

        Activate();

        HostVE.FocusInputVE();
    }

    // ============================================================
    // APPLY BOUNDS
    // ============================================================

    private void ApplyBoundsVE()
    {
        System.Windows.Interop.WindowInteropHelper helper =
            new(
                this);

        IntPtr handle =
            helper.Handle;

        if (handle == IntPtr.Zero)
        {
            return;
        }

        uint flags =
            SwpNoZOrderVE |
            SwpNoActivateVE |
            SwpFrameChangedVE |
            SwpShowWindowVE |
            SwpNoOwnerZOrderVE;

        bool success =
            SetWindowPos(
                handle,
                IntPtr.Zero,
                _targetLeftVE,
                _targetTopVE,
                _targetWidthVE,
                _targetHeightVE,
                flags);

        if (success)
        {
            return;
        }

        int error =
            Marshal.GetLastWin32Error();

        throw new Win32Exception(
            error,
            "No se pudo posicionar la ventana VisionEngine.");
    }

    // ============================================================
    // HOST KEYBOARD
    // ============================================================

    private void HostVE_KeyDownVE(
        object? sender,
        Forms.KeyEventArgs e)
    {
        if (
            e.KeyCode !=
            Forms.Keys.Escape)
        {
            return;
        }

        e.Handled =
            true;

        e.SuppressKeyPress =
            true;

        CloseRequestedVE?.Invoke(
            this,
            EventArgs.Empty);
    }

    // ============================================================
    // WPF KEYBOARD
    // ============================================================

    private void WindowRendererVE_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (
            e.Key !=
            System.Windows.Input.Key.Escape)
        {
            return;
        }

        e.Handled =
            true;

        CloseRequestedVE?.Invoke(
            this,
            EventArgs.Empty);
    }

    // ============================================================
    // DRAG & DROP BRIDGE
    // ============================================================

    private void HostVE_FilesDroppedVE(
        object? sender,
        FilesDroppedEventArgsVE e)
    {
        /*
         * WindowRendererVE sólo propaga el evento.
         *
         * Aquí NO:
         *
         * - hacemos adb push
         * - consultamos serial
         * - consultamos DeviceIdentityService
         * - iniciamos polling
         *
         * MainWindow.VisionEngineVE recibe el evento
         * y usa el serial almacenado de la sesión.
         */
        FilesDroppedVE?.Invoke(
            this,
            e);
    }

    private void HostVE_DragEnteredVE(
        object? sender,
        EventArgs e)
    {
        DragEnteredVE?.Invoke(
            this,
            EventArgs.Empty);
    }

    private void HostVE_DragEndedVE(
        object? sender,
        EventArgs e)
    {
        DragEndedVE?.Invoke(
            this,
            EventArgs.Empty);
    }

    // ============================================================
    // CLOSING
    // ============================================================

    private void WindowRendererVE_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_allowCloseVE)
        {
            return;
        }

        e.Cancel =
            true;

        CloseRequestedVE?.Invoke(
            this,
            EventArgs.Empty);
    }

    // ============================================================
    // CLOSED
    // ============================================================

    protected override void OnClosed(
        EventArgs e)
    {
        // ========================================================
        // INPUT
        // ========================================================

        HostVE.KeyDownVE -=
            HostVE_KeyDownVE;

        // ========================================================
        // DRAG & DROP
        // ========================================================

        HostVE.FilesDroppedVE -=
            HostVE_FilesDroppedVE;

        HostVE.DragEnteredVE -=
            HostVE_DragEnteredVE;

        HostVE.DragEndedVE -=
            HostVE_DragEndedVE;

        // ========================================================
        // WINDOW
        // ========================================================

        PreviewKeyDown -=
            WindowRendererVE_PreviewKeyDown;

        Closing -=
            WindowRendererVE_Closing;

        SourceInitialized -=
            WindowRendererVE_SourceInitialized;

        Loaded -=
            WindowRendererVE_Loaded;

        base.OnClosed(
            e);
    }
}