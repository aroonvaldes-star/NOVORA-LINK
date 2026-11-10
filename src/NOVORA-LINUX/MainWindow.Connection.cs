using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NOVORA.Linux.Control;

namespace NOVORA.Linux;

public sealed partial class MainWindow
{
    private Window? _connectionWindow;
    private StackPanel? _connectionPanel;
    private StackPanel? _usbSetupPanel;
    private StackPanel? _lanSetupPanel;
    private readonly MenuItem _usbRouteItem = new() { Header="USB (cable)" };
    private readonly MenuItem _lanRouteItem = new() { Header="LAN / Wi-Fi" };

    private void ConfigureConnectionMenu()
    {
        _usbRouteItem.Click += async (_,_)=>await OpenConnectionSetupAsync("USB");
        _lanRouteItem.Click += async (_,_)=>await OpenConnectionSetupAsync("LAN");
        _homeConnectionButton.ContextMenu = new ContextMenu { ItemsSource = new[]{_usbRouteItem,_lanRouteItem} };
    }

    private async Task OpenConnectionSetupAsync(string transport)
    {
        try
        {
            _host.SelectTransport(transport);
            if (_connectionPanel is null) BuildConnectionSetup();
            _usbSetupPanel!.IsVisible = transport=="USB";
            _lanSetupPanel!.IsVisible = transport=="LAN";
            if(_connectionWindow is null)
            {
                var dialog=new Window
                {
                    Title="Tipo de conexión · NOVORA-LINK",Width=700,Height=570,MinWidth=570,MinHeight=420,
                    Icon=LinuxTheme.AppIcon,
                    WindowStartupLocation=WindowStartupLocation.CenterOwner,
                    FontFamily=FontFamily,FontSize=15,
                    Content=new ScrollViewer
                    {
                        Content=_connectionPanel,Margin=new Thickness(22),
                        HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                    }
                };
                dialog.Bind(BackgroundProperty,new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("WindowBrush"));
                dialog.Closed += (_,_)=>
                {
                    // Detach the shared setup panel so reopening cannot give its controls two parents.
                    if(dialog.Content is ScrollViewer viewer) viewer.Content=null;
                    _connectionWindow=null;
                };
                _connectionWindow=dialog;
                dialog.Show(this);
            }
            else _connectionWindow.Activate();
            if(transport=="USB") await RefreshAdbDevicesAsync();
            else RefreshAddresses();
            UpdateDeviceStatus();
        }
        catch(Exception ex) when(ex is InvalidOperationException or IOException or ArgumentException)
        { SetMessage(ex.Message,"DangerForegroundBrush"); }
    }

    private void BuildConnectionSetup()
    {
        _connectionPanel=new StackPanel { Spacing=14 };
        _connectionPanel.Children.Add(SectionTitle("Tipo de conexión"));
        _usbSetupPanel=new StackPanel { Spacing=12 };
        _usbSetupPanel.Children.Add(SectionTitle("Por USB"));
        _usbSetupPanel.Children.Add(Caption("Conecta el teléfono por cable y autoriza la depuración USB. LINK Control confirmará el enlace protegido."));
        _usbSetupPanel.Children.Add(BuildAdbControls());
        _usbSetupPanel.Children.Add(_adbRefreshButton);
        _usbSetupPanel.Children.Add(_adbStatus);
        _lanSetupPanel=new StackPanel { Spacing=12 };
        _lanSetupPanel.Children.Add(SectionTitle("Por LAN / Wi-Fi"));
        _lanSetupPanel.Children.Add(Caption("Conecta ambos dispositivos a la misma red. Prepara la conexión y compara la huella en LINK Control antes de usar el código."));
        _lanSetupPanel.Children.Add(BuildNetworkControls());
        _lanSetupPanel.Children.Add(_settingsHostStatus);
        _lanSetupPanel.Children.Add(BuildPairingPanel());
        _connectionPanel.Children.Add(_usbSetupPanel);
        _connectionPanel.Children.Add(_lanSetupPanel);
        var status=Caption("");
        status.Bind(TextBlock.TextProperty,new Avalonia.Data.Binding(nameof(TextBlock.Text)) { Source=_deviceStatus });
        _connectionPanel.Children.Add(status);
        _connectionPanel.Children.Add(SectionTitle("Teléfonos con acceso recordado"));
        _connectionPanel.Children.Add(Caption("Puedes revocar el acceso de un dispositivo guardado en esta PC."));
        _connectionPanel.Children.Add(BuildTrustedDevicesPanel());
    }

    private void UpdateHomeConnection()
    {
        string? route=_host.SelectedTransport;
        bool authorized=route=="USB" ? _host.IsUsbAuthorized : route=="LAN" && _host.IsLanAuthorized;
        // Dynamic connectivity: show the phone state; USB/LAN is only a technical detail.
        _homeConnectionStatus.Text=_host.ConnectionSummary;
        _homeDeviceName.Text=authorized
            ? route=="USB" ? _adbDevices.ItemsSource?.OfType<AdbDevice>().FirstOrDefault(d=>d.Serial==_host.UsbSerial)?.Model ?? _host.UsbSerial ?? "Android" : "Android · LAN"
            : "Sin dispositivo";
        _homeConnectionButton.Content=route is null ? "TIPO DE CONEXIÓN ▾" : $"CONEXIÓN: {route} ▾";
        bool capturing=_host.HasVisionSession || _host.VisionRequested;
        _homeScreenButton.Content=capturing ? "DETENER PANTALLA" : "VER PANTALLA";
        _homeVisionButton.Content=_homeScreenButton.Content;
        _homeNextStep.Text=!authorized ? "Paso 1 de 4 · Conecta el cable USB o abre LINK Control en la misma Wi-Fi; NOVORA se conecta solo." :
            _host.VisionRequested ? "Paso 2 de 4 · Autoriza la captura de pantalla en el teléfono." :
            !_host.HasVisionSession ? "Paso 3 de 4 · Abre VisionEngine con VER PANTALLA." :
            _host.LinkState!="Connected" ? "Paso 4 de 4 · Inicia LinkEngine solo si necesitas Internet o datos." :
            "Sesión lista · VisionEngine y LinkEngine están activos.";
    }
}
