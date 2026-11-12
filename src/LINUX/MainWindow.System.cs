using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace NOVORA.Linux;

public sealed partial class MainWindow
{
    private Avalonia.Controls.Control BuildSettingsPage()
    {
        var content=PageContent();
        AddPageHeading(content,"","Configuración","Sólo opciones globales. Pantalla, red, rendimiento y game input viven en sus propias secciones.");
        var performance=new Grid { ColumnDefinitions=new ColumnDefinitions("*,250") };
        performance.Children.Add(new StackPanel { Spacing=5,Children={SectionTitle("Sistema Android"),Caption("Rendimiento y salud del teléfono. Se consulta sólo al abrir esta sección.")} });
        StylePrimaryButton(_openPerformanceButton); _openPerformanceButton.Height=42; _openPerformanceButton.MinWidth=150;
        _openPerformanceButton.VerticalAlignment=VerticalAlignment.Center;
        _openPerformanceButton.Click += (_,_)=>ShowPage("Rendimiento");
        Grid.SetColumn(_openPerformanceButton,1); performance.Children.Add(_openPerformanceButton); content.Children.Add(Card(performance));

        var appearance=new Grid { ColumnDefinitions=new ColumnDefinitions("*,250") };
        appearance.Children.Add(new StackPanel { Spacing=5,Children={SectionTitle("Tema"),Caption("Inicio predeterminado: Light. Puedes activar Dark durante esta sesión.")} });
        _themeSelector.ItemsSource=new[]{"Claro · Light","Oscuro · Dark"}; _themeSelector.SelectedIndex=LinuxTheme.IsDark?1:0;
        _themeSelector.SelectionChanged += ThemeSelectionChanged;
        _themeSelector.VerticalAlignment=VerticalAlignment.Center;
        Grid.SetColumn(_themeSelector,1); appearance.Children.Add(_themeSelector); content.Children.Add(Card(appearance));

        var updates=new StackPanel(); updates.Children.Add(SectionTitle("Actualizaciones"));
        updates.Children.Add(Caption("La descarga e instalación de actualizaciones todavía no está disponible en Linux.",new Thickness(0,7,0,0)));
        StylePrimaryButton(_checkPcUpdateButton); StyleFlatButton(_checkAndroidUpdateButton);
        updates.Children.Add(ActionRow(new Thickness(0,14,0,0),_checkPcUpdateButton,_checkAndroidUpdateButton));
        content.Children.Add(Card(updates));

        var manuals=new StackPanel(); manuals.Children.Add(SectionTitle("Manual de usuario"));
        manuals.Children.Add(Caption("Español prioritario · English available.",new Thickness(0,5,14,0)));
        StylePrimaryButton(_manualEsButton); StylePrimaryButton(_manualEnButton);
        manuals.Children.Add(ActionRow(new Thickness(0,12,0,0),_manualEsButton,_manualEnButton));
        var manualCard=Card(manuals); manualCard.Margin=new Thickness(0,14,0,0); content.Children.Add(manualCard);

        var android=new StackPanel(); android.Children.Add(SectionTitle("LINK Control · Android"));
        android.Children.Add(Caption("LinkControl.apk · Descarga en tu teléfono",new Thickness(0,8,0,0)));
        android.Children.Add(Caption("La descarga y el código QR todavía no están integrados en Linux.",new Thickness(0,8,0,12)));
        var qr=FlatButton("Mostrar QR"); qr.IsEnabled=false; StylePrimaryButton(qr);
        var link=FlatButton("Copiar enlace directo"); link.IsEnabled=false;
        android.Children.Add(ActionRow(new Thickness(0),qr,link)); content.Children.Add(Card(android));
        return ScrollPage(content);
    }

    private static WrapPanel ActionRow(Thickness margin,params Button[] buttons)
    {
        var row=new WrapPanel { Margin=margin };
        foreach(var button in buttons) { button.Margin=new Thickness(0,0,12,12); row.Children.Add(button); }
        return row;
    }

    private Avalonia.Controls.Control BuildPerformancePage()
    {
        var content=PageContent();
        AddPageHeading(content,"","Sistema Android","Rendimiento y salud del teléfono.");
        var grid=new Grid { ColumnDefinitions=new ColumnDefinitions("190,14,*") };
        var battery=new StackPanel { HorizontalAlignment=HorizontalAlignment.Center };
        var heading=SectionTitle("Batería"); heading.HorizontalAlignment=HorizontalAlignment.Center; battery.Children.Add(heading);
        battery.Children.Add(new ProgressBar
        {
            Minimum=0,Maximum=100,Value=0,Width=54,Height=120,Orientation=Orientation.Vertical,
            Margin=new Thickness(0,14,0,8),Foreground=Brush("GreenBrush"),Background=Brush("BatteryTrackBrush")
        });
        var percentage=Label("--","TextBrush",20,true); percentage.HorizontalAlignment=HorizontalAlignment.Center; battery.Children.Add(percentage);
        grid.Children.Add(Card(battery));
        var metrics=new StackPanel();
        foreach(string name in new[]{"CPU","RAM","Temperatura"})
        {
            metrics.Children.Add(SectionTitle(name));
            var value=Label("--",name=="RAM"?"TextBrush":name=="CPU"?"BlueBrush":"CyanBrush",name=="RAM"?16:18,true);
            value.HorizontalAlignment=HorizontalAlignment.Right; metrics.Children.Add(value);
            metrics.Children.Add(new ProgressBar { Minimum=0,Maximum=100,Value=0,Height=8,Margin=new Thickness(0,4,0,name=="Temperatura"?0:14),Foreground=Brush("BlueBrush"),Background=Brush("TrackBrush") });
        }
        var metricsCard=Card(metrics); Grid.SetColumn(metricsCard,2); grid.Children.Add(metricsCard); content.Children.Add(grid);
        var optimization=new Grid { ColumnDefinitions=new ColumnDefinitions("*,220") };
        optimization.Children.Add(new StackPanel { Spacing=6,Children={SectionTitle("Optimización segura del dispositivo"),Caption("La consulta y optimización del dispositivo todavía no están integradas en Linux.")} });
        var optimize=FlatButton("OPTIMIZAR AHORA"); StylePrimaryButton(optimize); optimize.IsEnabled=false;
        Grid.SetColumn(optimize,1); optimization.Children.Add(optimize);
        var card=Card(optimization); card.Margin=new Thickness(0,14,0,0); content.Children.Add(card);
        return ScrollPage(content);
    }
}
