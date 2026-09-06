using System.Windows.Forms.Integration;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace NOVORA.VisionEngine.Renderer;

/// <summary>
/// Host HWND reutilizable de VisionEngine.
/// Además de alojar SDL3/Direct3D11, expone la superficie WinForms real
/// para mouse y teclado. Esto evita perder input por el airspace de
/// WindowsFormsHost dentro de WPF.
/// </summary>
public sealed class HostRendererVE : WindowsFormsHost
{
    private readonly Forms.Panel _panelVE;

    public HostRendererVE()
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

        _panelVE.PreviewKeyDown +=
            (_, e) =>
                e.IsInputKey =
                    true;

        _panelVE.MouseDown +=
            (_, e) =>
            {
                FocusInputVE();
                _panelVE.Capture = true;
                MouseDownVE?.Invoke(this, e);
            };

        _panelVE.MouseMove +=
            (_, e) =>
                MouseMoveVE?.Invoke(this, e);

        _panelVE.MouseUp +=
            (_, e) =>
            {
                MouseUpVE?.Invoke(this, e);

                if (Forms.Control.MouseButtons ==
                    Forms.MouseButtons.None)
                {
                    _panelVE.Capture = false;
                }
            };

        _panelVE.MouseWheel +=
            (_, e) =>
                MouseWheelVE?.Invoke(this, e);

        _panelVE.KeyDown +=
            (_, e) =>
                KeyDownVE?.Invoke(this, e);

        _panelVE.KeyUp +=
            (_, e) =>
                KeyUpVE?.Invoke(this, e);

        _panelVE.LostFocus +=
            (_, _) =>
                InputFocusLostVE?.Invoke(
                    this,
                    EventArgs.Empty);

        Child =
            _panelVE;
    }

    public event EventHandler<Forms.MouseEventArgs>? MouseDownVE;
    public event EventHandler<Forms.MouseEventArgs>? MouseMoveVE;
    public event EventHandler<Forms.MouseEventArgs>? MouseUpVE;
    public event EventHandler<Forms.MouseEventArgs>? MouseWheelVE;
    public event EventHandler<Forms.KeyEventArgs>? KeyDownVE;
    public event EventHandler<Forms.KeyEventArgs>? KeyUpVE;
    public event EventHandler? InputFocusLostVE;

    public IntPtr HandleVE
    {
        get
        {
            if (!_panelVE.IsHandleCreated)
            {
                _ =
                    _panelVE.Handle;
            }

            return _panelVE.Handle;
        }
    }

    public int ClientWidthVE =>
        Math.Max(
            1,
            _panelVE.ClientSize.Width);

    public int ClientHeightVE =>
        Math.Max(
            1,
            _panelVE.ClientSize.Height);

    public Dispatcher DispatcherVE =>
        Dispatcher;

    public void FocusInputVE()
    {
        if (!_panelVE.IsDisposed &&
            _panelVE.CanFocus)
        {
            _panelVE.Focus();
        }
    }
}
