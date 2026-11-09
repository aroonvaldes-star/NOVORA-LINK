using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace NOVORA.Linux;

public sealed partial class MainWindow
{
    private readonly ComboBox _linkRouteSelector = new() { ItemsSource=new[]{"USB · cable","LAN · Wi-Fi"},SelectedIndex=0,MinWidth=190,Height=38 };

    private Avalonia.Controls.Control BuildLinkPage()
    {
        InitializeLinkSessionVE();
        var content=PageContent();
        AddPageHeading(content,"","LinkEngine","Conecta el teléfono a esta PC por USB o Wi-Fi.");
        var columns=new Grid { ColumnDefinitions=new ColumnDefinitions("1.15*,14,*") };
        var status=new StackPanel();
        status.Children.Add(SectionTitle("Conexión"));
        _linkPageMessage.Foreground=Brush("BlueBrush"); _linkPageMessage.FontWeight=Avalonia.Media.FontWeight.SemiBold;
        _linkPageMessage.TextWrapping=Avalonia.Media.TextWrapping.Wrap; _linkPageMessage.Margin=new Thickness(0,7,0,14);
        status.Children.Add(_linkPageMessage);
        status.Children.Add(StatusGrid(("Estado de LE",_linkPageState),("Teléfono",_deviceStatus),
            ("Enlace activo",_linkMessage),("Reconexión",_linkRecoveryStatus)));
        _linkTechnicalStatus.Text="USB CONTROL/DATA: 27183/27184 · LAN: DATA TLS y relay local.";
        _linkTechnicalStatus.Foreground=Brush("MutedBrush"); _linkTechnicalStatus.TextWrapping=Avalonia.Media.TextWrapping.Wrap;
        _linkTechnicalStatus.IsVisible=false; _linkTechnicalStatus.Margin=new Thickness(0,10,0,0);
        StyleFlatButton(_linkTechnicalButton); _linkTechnicalButton.Height=38; _linkTechnicalButton.Margin=new Thickness(0,12,0,0);
        _linkTechnicalButton.Click += (_,_)=>
        {
            _linkTechnicalStatus.IsVisible=!_linkTechnicalStatus.IsVisible;
            _linkTechnicalButton.Content=_linkTechnicalStatus.IsVisible?"OCULTAR DETALLES TÉCNICOS":"MOSTRAR DETALLES TÉCNICOS";
        };
        status.Children.Add(_linkTechnicalButton); status.Children.Add(_linkTechnicalStatus);
        columns.Children.Add(Card(status));
        var preparation=new StackPanel();
        preparation.Children.Add(Caption("Deja listo el transporte de LinkEngine: conexión USB por cable o ADB inalámbrico por Wi-Fi. Después prepara la ruta y autoriza Iniciar Internet desde AppControl.",new Thickness(0,0,0,12)));
        preparation.Children.Add(Card(new StackPanel { Children={SectionTitle("Por USB"),Caption("Conecta el teléfono por cable y activa LinkEngine desde AppControl. Usa la guía si necesitas habilitar Internet USB en Android.",new Thickness(0,5,0,10))} }));
        var wifi=_linkWifiAdbButton; wifi.Height=38;
        preparation.Children.Add(Card(new StackPanel { Children={SectionTitle("Por Wi-Fi"),Caption("Configura ADB inalámbrico cuando el teléfono y la PC estén en la misma red; esto no inicia LinkEngine por sí solo.",new Thickness(0,5,0,10)),wifi} }));
        var transport=new Expander { Header="Preparar el canal de conexión",IsExpanded=false,Content=preparation,VerticalAlignment=VerticalAlignment.Top };
        Grid.SetColumn(transport,2); columns.Children.Add(transport); content.Children.Add(columns);

        var measurements=new StackPanel(); measurements.Children.Add(SectionTitle("Mediciones técnicas de esta sesión"));
        measurements.Children.Add(Caption("Valores en vivo y pruebas recibidas por este enlace; se reinician al cambiar o detener la sesión.",new Thickness(0,5,0,8)));
        measurements.Children.Add(_linkTrafficStatus);
        measurements.Children.Add(_linkInternetStatus);
        measurements.Children.Add(_linkTunnelStatus);
        var measurementsCard=Card(measurements); measurementsCard.Margin=new Thickness(0,14,0,0); content.Children.Add(measurementsCard);

        var route=new StackPanel(); route.Children.Add(SectionTitle("Preparar ruta de LinkEngine"));
        route.Children.Add(Caption("Elige la ruta de esta sesión. Prepara la conexión y en LINK Control acepta los permisos de VPN.",new Thickness(0,5,0,12)));
        _linkPrepareButton.IsEnabled=true; _linkPrepareButton.Height=38; _linkPrepareButton.MinWidth=160;
        StylePrimaryButton(_linkPrepareButton);
        _linkPrepareButton.Click += async (_,_)=>await OpenConnectionSetupAsync(_linkRouteSelector.SelectedIndex==1?"LAN":"USB");
        var routeActions=new WrapPanel {  };
        _linkPrepareButton.Margin=new Thickness(10,0,0,0);
        routeActions.Children.Add(_linkRouteSelector); routeActions.Children.Add(_linkPrepareButton); route.Children.Add(routeActions);
        var routeCard=Card(route); routeCard.Margin=new Thickness(0,16,0,0); content.Children.Add(routeCard);

        var tests=new StackPanel { Spacing=6 };
        tests.Children.Add(Caption("Tráfico y pruebas de esta sesión. El relay mide el tráfico; las pruebas se muestran por separado."));
        foreach(var button in new[]{_linkInternetTestButton,_linkTunnelTestButton,_linkTrafficButton})
        { StyleFlatButton(button); button.Height=38; tests.Children.Add(button); }
        StylePrimaryButton(_linkInternetTestButton);
        tests.Children.Add(_linkExportButton);
        var testExpander=VeExpander("Mediciones de esta sesión",tests); testExpander.Margin=new Thickness(0,18,0,0); content.Children.Add(testExpander);
        var experimental=new StackPanel { Spacing=6 };
        experimental.Children.Add(Caption("Límites explícitos de esta sesión. Sin una configuración activa, NOVORA conserva su comportamiento normal."));
        experimental.Children.Add(new CheckBox { Content="Habilitar red experimental en esta sesión",IsEnabled=false });
        experimental.Children.Add(new CheckBox { Content="Adaptar límites con telemetría",IsEnabled=false });
        experimental.Children.Add(Caption("Experimental: no disponible en Linux."));
        var experimentalExpander=VeExpander("Ajustes técnicos · red experimental (LE / VE / Archivos)",experimental);
        experimentalExpander.Margin=new Thickness(0,18,0,0); content.Children.Add(experimentalExpander);
        return ScrollPage(content);
    }
}
