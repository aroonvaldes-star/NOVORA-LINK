using NOVORA.VisionEngine.Exchange;
using System.Windows.Forms.Integration;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace NOVORA.VisionEngine.Renderer;

/// <summary>
/// Host HWND reutilizable de VisionEngine.
///
/// Aloja SDL3/Direct3D11 sobre una superficie WinForms real
/// y concentra:
///
/// - mouse
/// - teclado
/// - foco
/// - Drag & Drop PC -> Android
///
/// El Drag & Drop se conecta directamente al Panel WinForms,
/// evitando problemas de airspace con WindowsFormsHost.
/// </summary>
public sealed class VERendererHost : WindowsFormsHost
{
    private readonly Forms.Panel _panelVE;
    private readonly Forms.Panel _privacyPanelVE;
    private readonly Forms.Label _privacyLabelVE;
    private readonly VEExchangeDrop _dropExchangeVE;

    private bool _disposedVE;

    public VERendererHost()
    {
        _panelVE =
            new Forms.Panel
            {
                Dock =
                    Forms.DockStyle.Fill,

                TabStop =
                    true,

                TabIndex =
                    0,

                BackColor =
                    System.Drawing.Color.Black
            };

        // ========================================================
        // KEYBOARD
        // ========================================================

        _panelVE.PreviewKeyDown +=
            PanelVE_PreviewKeyDown;

        _panelVE.KeyDown +=
            PanelVE_KeyDown;

        _panelVE.KeyUp +=
            PanelVE_KeyUp;

        // ========================================================
        // MOUSE
        // ========================================================

        _panelVE.MouseDown +=
            PanelVE_MouseDown;

        _panelVE.MouseMove +=
            PanelVE_MouseMove;

        _panelVE.MouseUp +=
            PanelVE_MouseUp;

        _panelVE.MouseWheel +=
            PanelVE_MouseWheel;

        _panelVE.LostFocus +=
            PanelVE_LostFocus;

        _panelVE.GotFocus +=
            PanelVE_GotFocus;

        // ========================================================
        // DRAG & DROP
        // ========================================================

        _dropExchangeVE =
            new VEExchangeDrop(
                _panelVE);

        _dropExchangeVE.FilesDroppedVE +=
            DropExchangeVE_FilesDroppedVE;

        _dropExchangeVE.DragEnteredVE +=
            DropExchangeVE_DragEnteredVE;

        _dropExchangeVE.DragEndedVE +=
            DropExchangeVE_DragEndedVE;

        _dropExchangeVE.AttachVE();

        // ========================================================
        // HOST
        // ========================================================

        _privacyLabelVE =
            new Forms.Label
            {
                Dock =
                    Forms.DockStyle.Fill,

                TextAlign =
                    System.Drawing.ContentAlignment.MiddleCenter,

                ForeColor =
                    System.Drawing.Color.White,

                BackColor =
                    System.Drawing.Color.Black,

                Text =
                    "Contenido protegido.`nCompleta esta operación directamente en tu Android."
            };

        _privacyPanelVE =
            new Forms.Panel
            {
                Dock =
                    Forms.DockStyle.Fill,

                BackColor =
                    System.Drawing.Color.Black,

                Visible =
                    false,

                TabStop =
                    false
            };

        _privacyPanelVE.Controls.Add(
            _privacyLabelVE);

        _panelVE.Controls.Add(
            _privacyPanelVE);

        Child =
            _panelVE;
    }

    // ============================================================
    // INPUT EVENTS
    // ============================================================

    public event EventHandler<Forms.MouseEventArgs>?
        MouseDownVE;

    public event EventHandler<Forms.MouseEventArgs>?
        MouseMoveVE;

    public event EventHandler<Forms.MouseEventArgs>?
        MouseUpVE;

    public event EventHandler<Forms.MouseEventArgs>?
        MouseWheelVE;

    public event EventHandler<Forms.KeyEventArgs>?
        KeyDownVE;

    public event EventHandler<Forms.KeyEventArgs>?
        KeyUpVE;

    public event EventHandler?
        InputFocusLostVE;

    public event EventHandler?
        InputFocusGainedVE;

    // ============================================================
    // EXCHANGE EVENTS
    // ============================================================

    /// <summary>
    /// Archivos o carpetas soltados directamente
    /// sobre la ventana de VisionEngine.
    ///
    /// Este evento todavía NO transfiere nada.
    /// Sólo expone las rutas recibidas.
    /// </summary>
    public event EventHandler<VEExchangeFilesDroppedEventArgs>?
        FilesDroppedVE;

    /// <summary>
    /// El usuario comenzó a arrastrar un elemento válido
    /// sobre VisionEngine.
    /// </summary>
    public event EventHandler?
        DragEnteredVE;

    /// <summary>
    /// El Drag terminó o abandonó VisionEngine.
    /// </summary>
    public event EventHandler?
        DragEndedVE;

    // ============================================================
    // HANDLE
    // ============================================================

    public IntPtr HandleVE
    {
        get
        {
            if (!_panelVE.IsHandleCreated)
            {
                _ =
                    _panelVE.Handle;
            }

            return
                _panelVE.Handle;
        }
    }

    // ============================================================
    // SIZE
    // ============================================================

    public int ClientWidthVE =>
        Math.Max(
            1,
            _panelVE.ClientSize.Width);

    public int ClientHeightVE =>
        Math.Max(
            1,
            _panelVE.ClientSize.Height);

    // ============================================================
    // DISPATCHER
    // ============================================================

    public Dispatcher DispatcherVE =>
        Dispatcher;

    // ============================================================
    // PANEL ACCESS
    // ============================================================

    /// <summary>
    /// Expone la superficie WinForms real de VisionEngine.
    ///
    /// Útil para componentes internos que necesitan acceder
    /// al HWND o eventos nativos sin atravesar WPF.
    /// </summary>
    internal Forms.Control SurfaceControlVE =>
        _panelVE;

    // ============================================================
    // FOCUS
    // ============================================================

    public bool PrivacyShieldEnabledVE =>
        _privacyPanelVE.Visible;

    public void SetPrivacyShieldVE(
        bool enabled,
        string? message = null)
    {
        void ApplyVE()
        {
            if (_privacyPanelVE.IsDisposed)
            {
                return;
            }

            if (enabled)
            {
                _privacyLabelVE.Text =
                    string.IsNullOrWhiteSpace(message)
                        ? "Contenido protegido.`nCompleta esta operación directamente en tu Android."
                        : message;

                _privacyPanelVE.Visible =
                    true;

                _privacyPanelVE.BringToFront();
            }
            else
            {
                _privacyPanelVE.Visible =
                    false;
            }
        }

        if (DispatcherVE.CheckAccess())
        {
            ApplyVE();
        }
        else
        {
            DispatcherVE.Invoke(
                ApplyVE);
        }
    }

    public void FocusInputVE()
    {
        if (
            !_panelVE.IsDisposed &&
            _panelVE.CanFocus)
        {
            _panelVE.Focus();
        }
    }

    // ============================================================
    // KEYBOARD
    // ============================================================

    private void PanelVE_PreviewKeyDown(
        object? sender,
        Forms.PreviewKeyDownEventArgs e)
    {
        e.IsInputKey =
            true;

        if (e.KeyCode is
            Forms.Keys.CapsLock or
            Forms.Keys.Tab or
            Forms.Keys.Up or
            Forms.Keys.Down or
            Forms.Keys.Left or
            Forms.Keys.Right or
            Forms.Keys.Home or
            Forms.Keys.End or
            Forms.Keys.PageUp or
            Forms.Keys.PageDown or
            Forms.Keys.Insert or
            Forms.Keys.Delete)
        {
            e.IsInputKey =
                true;
        }
    }

    private void PanelVE_KeyDown(
        object? sender,
        Forms.KeyEventArgs e)
    {
        KeyDownVE?.Invoke(
            this,
            e);
    }

    private void PanelVE_KeyUp(
        object? sender,
        Forms.KeyEventArgs e)
    {
        KeyUpVE?.Invoke(
            this,
            e);
    }

    // ============================================================
    // MOUSE
    // ============================================================

    private void PanelVE_MouseDown(
        object? sender,
        Forms.MouseEventArgs e)
    {
        FocusInputVE();

        _panelVE.Capture =
            true;

        MouseDownVE?.Invoke(
            this,
            e);
    }

    private void PanelVE_MouseMove(
        object? sender,
        Forms.MouseEventArgs e)
    {
        MouseMoveVE?.Invoke(
            this,
            e);
    }

    private void PanelVE_MouseUp(
        object? sender,
        Forms.MouseEventArgs e)
    {
        MouseUpVE?.Invoke(
            this,
            e);

        if (
            Forms.Control.MouseButtons ==
            Forms.MouseButtons.None)
        {
            _panelVE.Capture =
                false;
        }
    }

    private void PanelVE_MouseWheel(
        object? sender,
        Forms.MouseEventArgs e)
    {
        MouseWheelVE?.Invoke(
            this,
            e);
    }

    private void PanelVE_LostFocus(
        object? sender,
        EventArgs e)
    {
        InputFocusLostVE?.Invoke(
            this,
            EventArgs.Empty);
    }

    private void PanelVE_GotFocus(
        object? sender,
        EventArgs e)
    {
        InputFocusGainedVE?.Invoke(
            this,
            EventArgs.Empty);
    }

    // ============================================================
    // DRAG & DROP BRIDGE
    // ============================================================

    private void DropExchangeVE_FilesDroppedVE(
        object? sender,
        VEExchangeFilesDroppedEventArgs e)
    {
        FilesDroppedVE?.Invoke(
            this,
            e);

        FocusInputVE();
    }

    private void DropExchangeVE_DragEnteredVE(
        object? sender,
        EventArgs e)
    {
        DragEnteredVE?.Invoke(
            this,
            EventArgs.Empty);
    }

    private void DropExchangeVE_DragEndedVE(
        object? sender,
        EventArgs e)
    {
        DragEndedVE?.Invoke(
            this,
            EventArgs.Empty);
    }

    // ============================================================
    // DISPOSE
    // ============================================================

    protected override void Dispose(
        bool disposing)
    {
        if (
            disposing &&
            !_disposedVE)
        {
            _disposedVE =
                true;

            _dropExchangeVE.FilesDroppedVE -=
                DropExchangeVE_FilesDroppedVE;

            _dropExchangeVE.DragEnteredVE -=
                DropExchangeVE_DragEnteredVE;

            _dropExchangeVE.DragEndedVE -=
                DropExchangeVE_DragEndedVE;

            _dropExchangeVE.Dispose();

            _panelVE.PreviewKeyDown -=
                PanelVE_PreviewKeyDown;

            _panelVE.KeyDown -=
                PanelVE_KeyDown;

            _panelVE.KeyUp -=
                PanelVE_KeyUp;

            _panelVE.MouseDown -=
                PanelVE_MouseDown;

            _panelVE.MouseMove -=
                PanelVE_MouseMove;

            _panelVE.MouseUp -=
                PanelVE_MouseUp;

            _panelVE.MouseWheel -=
                PanelVE_MouseWheel;

            _panelVE.LostFocus -=
                PanelVE_LostFocus;

            _panelVE.GotFocus -=
                PanelVE_GotFocus;
        }

        base.Dispose(
            disposing);
    }
}
