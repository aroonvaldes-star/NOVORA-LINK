using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace NOVORA.Linux;

public sealed partial class MainWindow
{
    private readonly TextBlock _homeDeviceName = new() { Text = "Sin dispositivo" };
    private readonly TextBlock _homeConnectionStatus = new() { Text = "Desconectado" };
    private readonly Button _homeVisionButton = new() { Content = "VER PANTALLA" };

    private Avalonia.Controls.Control BuildHomePage()
    {
        var content = PageContent();
        content.Children.Add(Label("CENTRO DE SESIÓN", "BlueBrush", 12, true, new Thickness(0,0,0,5)));
        content.Children.Add(Label("Inicio", "TextBrush", 27, true));
        content.Children.Add(Caption("Prepara tu conexión y abre el motor que necesitas para esta sesión.",new Thickness(0,6,0,8)));
        _homeNextStep.Text = "Paso 1 de 4 · Conecta el cable USB o abre LINK Control en la misma Wi-Fi; NOVORA se conecta solo.";
        _homeNextStep.Foreground = Brush("BlueBrush");
        _homeNextStep.FontWeight = FontWeight.SemiBold;
        _homeNextStep.TextWrapping = TextWrapping.Wrap;
        _homeNextStep.Margin = new Thickness(0,0,0,8);
        content.Children.Add(_homeNextStep);
        _performanceSession.Margin = new Thickness(0,0,0,20);
        content.Children.Add(_performanceSession);

        var connection = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var information = new StackPanel { Margin = new Thickness(0,0,18,0) };
        information.Children.Add(Label("CONEXIÓN DEL TELÉFONO", "BlueBrush",12,true,new Thickness(0,0,0,6)));
        information.Children.Add(SectionTitle("Conecta tu teléfono"));
        information.Children.Add(Caption("La conexión es automática por USB o Wi-Fi. Cuando esté listo, abre la pantalla del teléfono desde esta PC.",new Thickness(0,6,0,12)));
        var pillContent = new StackPanel { Orientation=Orientation.Horizontal };
        pillContent.Children.Add(new Border { Width=8,Height=8,CornerRadius=new CornerRadius(4),Background=Brush("GreenBrush"),Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center });
        _homeConnectionStatus.FontSize=13; _homeConnectionStatus.Foreground=Brush("TextBrush");
        pillContent.Children.Add(_homeConnectionStatus);
        information.Children.Add(new Border
        {
            Background=Brush("PanelBrush2"),BorderBrush=Brush("BorderBrush"),BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(14),Padding=new Thickness(10,5),HorizontalAlignment=HorizontalAlignment.Left,Child=pillContent
        });
        var device = new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0) };
        device.Children.Add(Caption("Dispositivo:"));
        _homeDeviceName.Foreground=Brush("BlueBrush"); _homeDeviceName.FontWeight=FontWeight.SemiBold;
        _homeDeviceName.Margin=new Thickness(7,0,0,0);
        device.Children.Add(_homeDeviceName); information.Children.Add(device); connection.Children.Add(information);

        var actions = new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
        StylePrimaryButton(_homeScreenButton); _homeScreenButton.MinWidth=140; _homeScreenButton.Height=42;
        _homeScreenButton.Click += async (_,_) => await ToggleHomeVisionAsync();
        actions.Children.Add(_homeScreenButton);
        Grid.SetColumn(actions,1); connection.Children.Add(actions);
        var connectionCard=Card(connection); connectionCard.Margin=new Thickness(0,0,0,16); content.Children.Add(connectionCard);
        content.Children.Add(Label("MOTORES DISPONIBLES","BlueBrush",12,true,new Thickness(0,2,0,10)));

        var engines = new Grid { ColumnDefinitions=new ColumnDefinitions("*,*,*") };
        engines.Children.Add(HomeEngineCard("VisionEngine","Transmisión de pantalla y audio",_visionState,_homeVisionButton,0));
        _homeVisionButton.Click += async (_,_) => await ToggleHomeVisionAsync();
        var link = new Button { Content="Abrir LinkEngine" }; link.Click += (_,_)=>ShowPage("LinkEngine");
        engines.Children.Add(HomeEngineCard("LinkEngine","Internet y conexión de esta sesión",_linkPageMessage,link,1));
        var controls = new Button { Content="Abrir controles" }; controls.Click += (_,_)=>ShowPage("ExInEngine");
        engines.Children.Add(HomeEngineCard("ExInEngine","Mando, entrada y calibración",ExInStatusText,controls,2));
        content.Children.Add(engines);
        return ScrollPage(content);
    }

    private Border HomeEngineCard(string title,string caption,TextBlock state,Button action,int column)
    {
        var layout = new Grid { RowDefinitions=new RowDefinitions("Auto,Auto,Auto,*,Auto") };
        var icon=EngineIcon(title); icon.Width=52; icon.Height=52; icon.HorizontalAlignment=HorizontalAlignment.Left; icon.Margin=new Thickness(0,0,0,16);
        layout.Children.Add(icon);
        var heading=SectionTitle(title); Grid.SetRow(heading,1); layout.Children.Add(heading);
        var description=Caption(caption,new Thickness(0,8,0,12)); Grid.SetRow(description,2); layout.Children.Add(description);
        var status=Caption("",new Thickness(0,0,0,20)); status.Bind(TextBlock.TextProperty,new Binding(nameof(TextBlock.Text)) { Source=state });
        Grid.SetRow(status,3); layout.Children.Add(status);
        StylePrimaryButton(action); action.FontSize=14; action.HorizontalAlignment=HorizontalAlignment.Stretch;
        Grid.SetRow(action,4); layout.Children.Add(action);
        var card=Card(layout); card.Margin=new Thickness(0,0,column<2?12:0,0); Grid.SetColumn(card,column);
        return card;
    }

    private async Task ToggleHomeVisionAsync()
    {
        try
        {
            if (_host.HasVisionSession || _host.VisionRequested) await _host.StopVisionAsync();
            else _host.RequestVision();
        }
        catch (Exception ex)
        { SetMessage(ex.Message,"DangerForegroundBrush"); }
        finally
        {
            UpdateVisionState();
            UpdateDeviceStatus();
        }
    }
}
