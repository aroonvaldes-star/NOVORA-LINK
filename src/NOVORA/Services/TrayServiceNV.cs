using System.Drawing;
using Forms = System.Windows.Forms;

namespace NOVORA.Services;

public sealed class TrayServiceNV :
    IDisposable
{
    private readonly Forms.NotifyIcon _notifyIconNV;
    private readonly Forms.ContextMenuStrip _menuNV;

    private bool _disposedNV;

    public event EventHandler? OpenRequestedNV;
    public event EventHandler? SettingsRequestedNV;
    public event EventHandler? ExitRequestedNV;

    public TrayServiceNV()
    {
        _menuNV =
            new Forms.ContextMenuStrip();

        var openItem =
            new Forms.ToolStripMenuItem(
                "Abrir NOVORA");

        var devicesItem =
            new Forms.ToolStripMenuItem(
                "Dispositivos");

        var settingsItem =
            new Forms.ToolStripMenuItem(
                "Ajustes");

        var exitItem =
            new Forms.ToolStripMenuItem(
                "Salir de NOVORA");

        openItem.Click +=
            (_, _) =>
                OpenRequestedNV?.Invoke(
                    this,
                    EventArgs.Empty);

        devicesItem.Click +=
            (_, _) =>
                OpenRequestedNV?.Invoke(
                    this,
                    EventArgs.Empty);

        settingsItem.Click +=
            (_, _) =>
                SettingsRequestedNV?.Invoke(
                    this,
                    EventArgs.Empty);

        exitItem.Click +=
            (_, _) =>
                ExitRequestedNV?.Invoke(
                    this,
                    EventArgs.Empty);

        _menuNV.Items.Add(
            openItem);

        _menuNV.Items.Add(
            devicesItem);

        _menuNV.Items.Add(
            settingsItem);

        _menuNV.Items.Add(
            new Forms.ToolStripSeparator());

        _menuNV.Items.Add(
            exitItem);

        string? executable =
            Environment.ProcessPath;

        Icon icon =
            !string.IsNullOrWhiteSpace(
                executable)
                ? Icon.ExtractAssociatedIcon(
                      executable)
                  ?? SystemIcons.Application
                : SystemIcons.Application;

        _notifyIconNV =
            new Forms.NotifyIcon
            {
                Text =
                    "NOVORA-LINK",
                Icon =
                    icon,
                ContextMenuStrip =
                    _menuNV,
                Visible =
                    false
            };

        _notifyIconNV.DoubleClick +=
            (_, _) =>
                OpenRequestedNV?.Invoke(
                    this,
                    EventArgs.Empty);
    }

    public void ShowNV()
    {
        ThrowIfDisposedNV();

        _notifyIconNV.Visible =
            true;
    }

    public void HideNV()
    {
        if (_disposedNV)
        {
            return;
        }

        _notifyIconNV.Visible =
            false;
    }

    private void ThrowIfDisposedNV()
    {
        ObjectDisposedException.ThrowIf(
            _disposedNV,
            this);
    }

    public void Dispose()
    {
        if (_disposedNV)
        {
            return;
        }

        _disposedNV =
            true;

        try
        {
            _notifyIconNV.Visible =
                false;
        }
        catch
        {
        }

        _notifyIconNV.Dispose();
        _menuNV.Dispose();
    }
}
