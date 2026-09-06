using NOVORA.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace NOVORA.VisionEngine.Renderer;

/// <summary>
/// Ventana dedicada de presentación de VisionEngine.
///
/// Usa SetWindowPos con coordenadas físicas de Win32 para no mezclar
/// píxeles de Screen.Bounds con DIPs de WPF. Esto permite fullscreen real
/// en monitores secundarios y configuraciones con escalado DPI.
/// </summary>
public sealed class WindowRendererVE : System.Windows.Window
{
    private const uint SwpNoZOrderVE = 0x0004;
    private const uint SwpNoActivateVE = 0x0010;
    private const uint SwpFrameChangedVE = 0x0020;
    private const uint SwpShowWindowVE = 0x0040;
    private const uint SwpNoOwnerZOrderVE = 0x0200;

    private readonly MonitorInfo _monitorVE;
    private readonly bool _fullscreenVE;

    private int _targetLeftVE;
    private int _targetTopVE;
    private int _targetWidthVE;
    private int _targetHeightVE;
    private bool _allowCloseVE;

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    public WindowRendererVE(
        MonitorInfo monitor,
        bool fullscreen)
    {
        _monitorVE =
            monitor
            ?? throw new ArgumentNullException(
                nameof(monitor));

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

        HostVE.KeyDownVE +=
            HostVE_KeyDownVE;

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

    public event EventHandler? CloseRequestedVE;

    public HostRendererVE HostVE { get; }

    public bool IsFullscreenVE =>
        _fullscreenVE;

    public MonitorInfo MonitorVE =>
        _monitorVE;

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

    private void ConfigurePresentationVE()
    {
        if (_fullscreenVE)
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

            return;
        }

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

        width =
            Math.Clamp(
                width,
                Math.Min(
                    480,
                    _monitorVE.Width),
                Math.Max(
                    480,
                    _monitorVE.Width));

        height =
            Math.Clamp(
                height,
                Math.Min(
                    320,
                    _monitorVE.Height),
                Math.Max(
                    320,
                    _monitorVE.Height));

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

    private void WindowRendererVE_SourceInitialized(
        object? sender,
        EventArgs e)
    {
        ApplyBoundsVE();
    }

    private void WindowRendererVE_Loaded(
        object sender,
        System.Windows.RoutedEventArgs e)
    {
        ApplyBoundsVE();
        Activate();
        HostVE.FocusInputVE();
    }

    private void ApplyBoundsVE()
    {
        IntPtr handle =
            new System.Windows.Interop.WindowInteropHelper(
                this)
                .Handle;

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

        if (!SetWindowPos(
                handle,
                IntPtr.Zero,
                _targetLeftVE,
                _targetTopVE,
                _targetWidthVE,
                _targetHeightVE,
                flags))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "No se pudo posicionar la ventana VisionEngine.");
        }
    }

    private void HostVE_KeyDownVE(
        object? sender,
        Forms.KeyEventArgs e)
    {
        if (e.KeyCode !=
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

    private void WindowRendererVE_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key !=
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

    protected override void OnClosed(
        EventArgs e)
    {
        HostVE.KeyDownVE -=
            HostVE_KeyDownVE;

        PreviewKeyDown -=
            WindowRendererVE_PreviewKeyDown;

        Closing -=
            WindowRendererVE_Closing;

        SourceInitialized -=
            WindowRendererVE_SourceInitialized;

        Loaded -=
            WindowRendererVE_Loaded;

        base.OnClosed(e);
    }
}
