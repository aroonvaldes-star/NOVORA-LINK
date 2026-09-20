using System.ComponentModel;
using System.IO;
using System.Windows;
using NOVORA.Service;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private CancellationTokenSource? _androidInstallCheck, _androidInstallOperation;
    private NLServiceAndroidPackageInfo? _androidInstallPackage;
    private NLServiceAndroidInstallState? _androidInstallState;
    private long _androidInstallGeneration;
    private string? _androidInstallDeviceKey;
    private bool _androidInstalling;
    private NLServiceAndroidInstaller CreateAndroidInstaller() => new(
        (arguments, token) => _adb.ExecuteAndroidInstallerCommandAsync(arguments, token));

    private void InitializeAndroidInstallation() => _viewModel.PropertyChanged += AndroidInstallDeviceChanged;
    private void CloseAndroidInstallation()
    {
        _androidInstallGeneration++;
        _viewModel.PropertyChanged -= AndroidInstallDeviceChanged;
        _androidInstallCheck?.Cancel();
        _androidInstallOperation?.Cancel();
    }
    private void AndroidInstallDeviceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(NOVORA.ViewModel.NLViewModelMain.Device) || _closing || !IsLoaded) return;
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.BeginInvoke(new Action(() => AndroidInstallDeviceChanged(sender, args))); return; }
        var device = _viewModel.Device;
        string key = $"{device.Serial}|{device.Connected}|{device.IsWifiConnection}";
        if (key == _androidInstallDeviceKey) return;
        _androidInstallDeviceKey = key;
        _androidInstallGeneration++;
        _androidInstallOperation?.Cancel();
        if (!_androidInstalling) _ = RefreshAndroidInstallationAsync();
    }
    private async void RefreshAndroidInstallation_Click(object sender, RoutedEventArgs e) => await RefreshAndroidInstallationAsync();
    private async Task RefreshAndroidInstallationAsync()
    {
        if (_closing || _androidInstalling) return;
        _androidInstallCheck?.Cancel();
        using var check = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _androidInstallCheck = check;
        long generation = ++_androidInstallGeneration;
        _androidInstallPackage = null; _androidInstallState = null;
        AndroidInstallButton.IsEnabled = false;
        AndroidInstallButton.Content = "INSTALAR NOVORA POR USB";
        var device = _viewModel.Device;
        try
        {
            string directory = Path.Combine(_paths.BaseDirectory, "Android");
            if (!File.Exists(Path.Combine(directory, "NLAndroidApp.apk")) || !File.Exists(Path.Combine(directory, "NLAndroidRelease.json")))
            {
                AndroidInstallStatus.Text = "El instalador Android todavía no está incluido en esta edición de NOVORA PC. El botón se habilitará cuando esté disponible el paquete de instalación validado.";
                return;
            }
            AndroidInstallStatus.Text = "Comprobando el paquete de NOVORA Android…";
            var package = await Task.Run(() => NLServiceAndroidPackage.Load(directory), check.Token);
            if (generation != _androidInstallGeneration || _closing) return;
            if (!device.Connected || device.IsWifiConnection || string.IsNullOrWhiteSpace(device.Serial))
            { AndroidInstallStatus.Text = $"NOVORA Android {package.VersionName} disponible. Selecciona un teléfono USB y autoriza la depuración USB en su pantalla."; return; }
            AndroidInstallStatus.Text = $"Comprobando NOVORA en {device.FriendlyName}…";
            var state = await CreateAndroidInstaller().InspectAsync(device.Serial, package, check.Token);
            if (generation != _androidInstallGeneration || _closing) return;
            _androidInstallPackage = package; _androidInstallState = state;
            AndroidInstallButton.Content = state.Action switch
            {
                NLServiceAndroidInstallAction.Install => "INSTALAR NOVORA POR USB",
                NLServiceAndroidInstallAction.Update => "ACTUALIZAR NOVORA ANDROID",
                _ => "NOVORA ANDROID ACTUALIZADO"
            };
            AndroidInstallButton.IsEnabled = state.Action != NLServiceAndroidInstallAction.Current;
            AndroidInstallStatus.Text = $"{device.FriendlyName} · {state.Description}";
        }
        catch (OperationCanceledException) { if (!_closing && generation == _androidInstallGeneration) AndroidInstallStatus.Text = "Comprobación cancelada o sin respuesta. Pulsa COMPROBAR APP para volver a intentar."; }
        catch (Exception ex) { if (!_closing && generation == _androidInstallGeneration) AndroidInstallStatus.Text = "No se puede preparar la instalación: " + ex.Message; }
        finally { if (ReferenceEquals(_androidInstallCheck, check)) _androidInstallCheck = null; }
    }
    private async void InstallAndroidApp_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || _androidInstalling || _androidControlPreparing || _androidInstallPackage is not { } package || _androidInstallState is not { } expected || expected.Action == NLServiceAndroidInstallAction.Current) return;
        long generation = _androidInstallGeneration;
        string serial = _viewModel.Device.Serial, name = _viewModel.Device.FriendlyName;
        _androidInstalling = true;
        AndroidInstallButton.IsEnabled = AndroidInstallRefreshButton.IsEnabled = false;
        using var operation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _androidInstallOperation = operation;
        try
        {
            string oldVersion = expected.InstalledVersionCode is null ? "No instalada" : $"{expected.InstalledVersionName} ({expected.InstalledVersionCode})";
            var answer = System.Windows.MessageBox.Show(this,
                $"Teléfono: {name}\nVersión actual: {oldVersion}\nVersión disponible: {package.VersionName} ({package.VersionCode})\n\nSe instalará la misma app de NOVORA. La actualización solicita conservar sus datos. El control y la VPN de NOVORA se desconectarán durante la instalación. Si Android rechaza la firma, no se desinstalará la app existente.\n\n¿Continuar?",
                "Instalar NOVORA Android", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            if (generation != _androidInstallGeneration || _viewModel.Device.Serial != serial || !_viewModel.Device.Connected || _viewModel.Device.IsWifiConnection)
                throw new InvalidOperationException("El teléfono cambió. Comprueba la app antes de instalar.");
            await StopAndroidControlAsync();
            operation.Token.ThrowIfCancellationRequested();
            if (generation != _androidInstallGeneration || _viewModel.Device.Serial != serial)
                throw new InvalidOperationException("El teléfono cambió durante la preparación.");
            AndroidInstallStatus.Text = $"Instalando en {name}. Mantén conectado el cable USB…";
            var result = await CreateAndroidInstaller().InstallAsync(serial, package, expected, operation.Token);
            if (!_closing && generation == _androidInstallGeneration)
            {
                _androidInstallState = result;
                AndroidInstallButton.Content = "NOVORA ANDROID ACTUALIZADO";
                AndroidInstallStatus.Text = $"Instalación verificada en {name}: {result.InstalledVersionName}. Abre NOVORA en el teléfono y prepara el control USB o LAN desde HOME.";
            }
        }
        catch (OperationCanceledException)
        { if (!_closing) AndroidInstallStatus.Text = "La operación se interrumpió. El resultado puede ser desconocido; pulsa COMPROBAR APP antes de volver a instalar."; }
        catch (Exception ex)
        { if (!_closing) AndroidInstallStatus.Text = "No se confirmó la instalación: " + ex.Message + " Pulsa COMPROBAR APP para revisar el teléfono."; }
        finally
        {
            _androidInstalling = false;
            if (ReferenceEquals(_androidInstallOperation, operation)) _androidInstallOperation = null;
            _androidInstallState = null; _androidInstallPackage = null;
            if (!_closing) AndroidInstallRefreshButton.IsEnabled = true;
        }
    }
}
