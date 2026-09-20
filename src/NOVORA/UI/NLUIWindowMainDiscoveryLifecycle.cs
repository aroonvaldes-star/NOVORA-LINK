using NOVORA.Discovery;
using System.Threading;
using System.Windows;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private NLDiscoveryADBTrackDevices?
        _adbDiscoveryNV;

    private readonly SemaphoreSlim
        _adbDiscoveryRefreshGateNV =
            new(1, 1);

    internal async Task StartDiscoveryLifecycleNVAsync()
    {
        if (_closing ||
            _adbDiscoveryNV is not null)
        {
            return;
        }

        var discoveryNV =
            new NLDiscoveryADBTrackDevices(
                _paths.Adb);

        discoveryNV.DevicesChangedNV +=
            AdbDiscovery_DevicesChangedNV;
        discoveryNV.DevicesChangedNV += AutomaticUsbDevicesChanged; // NOVORA_AUTOUSB_V1

        discoveryNV.RecoveryNV +=
            AdbDiscovery_RecoveryNV;

        _adbDiscoveryNV =
            discoveryNV;

        await discoveryNV
            .StartAsync()
            .ConfigureAwait(true);
        QueueAutomaticUsb();
    }

    private void AdbDiscovery_DevicesChangedNV(
        object? sender,
        string snapshotNV)
    {
        _ =
            RefreshFromAdbDiscoveryNVAsync();
    }

    private void AdbDiscovery_RecoveryNV(
        object? sender,
        string messageNV)
    {
        if (_closing)
        {
            return;
        }

        _ =
            Dispatcher.InvokeAsync(
                () =>
                {
                    if (!_closing)
                    {
                        _viewModel.ConnectionStatus =
                            $"DiscoveryEngine: recuperando ADB ({messageNV})";
                    }
                });
    }

    private async Task RefreshFromAdbDiscoveryNVAsync()
    {
        if (_closing)
        {
            return;
        }

        bool enteredNV =
            await _adbDiscoveryRefreshGateNV
                .WaitAsync(
                    0)
                .ConfigureAwait(false);

        if (!enteredNV)
        {
            return;
        }

        try
        {
            await Dispatcher
                .InvokeAsync(
                    () =>
                        RefreshDevicesAsync(
                            force: true))
                .Task
                .Unwrap()
                .ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            _adbDiscoveryRefreshGateNV
                .Release();
        }
    }

    internal async Task StopDiscoveryLifecycleNVAsync()
    {
        NLDiscoveryADBTrackDevices? discoveryNV =
            _adbDiscoveryNV;

        _adbDiscoveryNV =
            null;

        if (discoveryNV is null)
        {
            return;
        }

        discoveryNV.DevicesChangedNV -=
            AdbDiscovery_DevicesChangedNV;
        discoveryNV.DevicesChangedNV -= AutomaticUsbDevicesChanged;

        discoveryNV.RecoveryNV -=
            AdbDiscovery_RecoveryNV;

        try
        {
            await discoveryNV
                .StopAsync()
                .ConfigureAwait(true);
        }
        catch
        {
        }

        await discoveryNV
            .DisposeAsync();
    }

    internal void ShowFromTrayNV()
    {
        if (_closing)
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        ShowInTaskbar =
            true;

        if (WindowState ==
            WindowState.Minimized)
        {
            WindowState =
                WindowState.Normal;
        }

        Activate();
    }

    internal void OpenSettingsFromTrayNV()
    {
        if (_closing)
        {
            return;
        }

        ShowFromTrayNV();

        Configuration_Click(
            this,
            new RoutedEventArgs());
    }
}
