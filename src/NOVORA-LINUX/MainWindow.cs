using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using Avalonia.Platform;
using AvaloniaControl = Avalonia.Controls.Control;
using NOVORA.Linux.Control;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NOVORA.Linux;

public sealed class MainWindow : Window
{
    private static readonly string[] Navigation = ["Inicio", "VisionEngine", "LinkEngine", "ExInEngine", "Integración", "Configuración"];
    private readonly LinuxControlHost _host = new();
    private readonly ComboBox _addresses = new();
    private readonly ComboBox _adbDevices = new();
    private readonly Button _startButton = new();
    private readonly Button _usbButton = new();
    private readonly Button _adbRefreshButton = new() { Content = "Buscar teléfonos" };
    private readonly Button _renewButton = new();
    private readonly TextBlock _hostStatus = new();
    private readonly TextBlock _pairingCode = new();
    private readonly TextBlock _countdown = new();
    private readonly TextBox _fingerprint = new();
    private readonly TextBlock _deviceStatus = new();
    private readonly StackPanel _trustedDevices = new();
    private readonly TextBlock _adbStatus = new();
    private readonly TextBlock _message = new();
    private readonly TextBlock _linkState = new();
    private readonly TextBlock _linkMessage = new();
    private readonly TextBlock _linkPageState = new();
    private readonly TextBlock _linkPageMessage = new();
    private readonly TextBlock _visionState = new();
    private readonly TextBlock _visionMessage = new();
    private readonly TextBlock _visionMetrics = new();
    private readonly Button _visionStopButton = new() { Content = "Detener VisionEngine", IsEnabled = false };
    private readonly TextBlock _settingsHostStatus = new();
    private readonly Border _messageDot = new();
    private readonly Image _brandEmblem = new();
    private readonly Image _brandWordmark = new();
    private readonly ComboBox _themeSelector = new();
    private readonly StackPanel _navigation = new();
    private readonly Dictionary<string, AvaloniaControl> _pages = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _currentPage = "Inicio";
    private Bitmap? _emblemBitmap;
    private Bitmap? _wordmarkBitmap;
    private string? _themeLoadError;
    private bool _shutdownStarted;

    public MainWindow()
    {
        try
        {
            LinuxTheme.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _themeLoadError = $"No se pudo leer el tema guardado: {ex.Message}";
        }
        Title = "NOVORA-LINK 1.4";
        FontFamily = new FontFamily("avares://NOVORA-LINK.Linux/Assets/Fonts#Montserrat");
        Width = 1180;
        Height = 800;
        MinWidth = 1000;
        MinHeight = 610;
        Background = Brush("#0b1220");
        SystemDecorations = SystemDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        using (var iconStream = File.OpenRead(LinuxTheme.GetAssetPath("novora.ico")))
            Icon = new WindowIcon(iconStream);

        _hostStatus.Text = "El control LAN está detenido.";
        _settingsHostStatus.Text = _hostStatus.Text;
        _deviceStatus.Text = "Sin dispositivos Android conectados.";
        _pairingCode.Text = "------";
        _countdown.Text = "Válido durante 2 minutos";
        _fingerprint.IsReadOnly = true;
        _fingerprint.AcceptsReturn = true;
        _fingerprint.TextWrapping = TextWrapping.Wrap;
        _fingerprint.MinHeight = 66;
        _fingerprint.Watermark = "La huella aparecerá cuando prepares el control LAN.";
        _adbStatus.Text = "Consulta ADB pendiente.";
        _linkState.Text = "Desconectado";
        _linkMessage.Text = "Conecta el teléfono por USB y habilita LinkEngine desde AppControl.";
        _linkPageState.Text = "Desconectado";
        _linkPageMessage.Text = "Conecta el teléfono por USB y habilita LinkEngine desde AppControl.";
        _settingsHostStatus.Text = "El control LAN está detenido.";

        _addresses.MinWidth = 250;
        _adbDevices.MinWidth = 260;
        _startButton.Content = "Preparar control LAN";
        _usbButton.Content = "Enlazar por USB";
        _renewButton.Content = "Generar código nuevo";
        _renewButton.IsEnabled = false;

        _host.StatusChanged += HostOnStatusChanged;
        _host.InvitationChanged += HostOnInvitationChanged;
        LinuxTheme.Changed += ThemeChanged;
        _startButton.Click += StartControl;
        _usbButton.Click += StartUsbControl;
        _renewButton.Click += RenewInvitation;
        _adbRefreshButton.Click += async (_, _) => await RefreshAdbDevicesAsync();
        _timer.Tick += (_, _) =>
        {
            UpdateInvitationDisplay();
            UpdateVisionState();
        };
        _timer.Start();
        Closed += (_, _) => _ = ShutdownAsync();
        RefreshAddresses();

        Content = CreateShell();
        UpdateVisionState();
        UpdateBrandAssets();
        if (_themeLoadError is not null)
            SetMessage(_themeLoadError, "#fbbf24");
        _ = RefreshAdbDevicesAsync();
    }

    private AvaloniaControl CreateShell()
    {
        var shell = new Grid
        {
            RowDefinitions = new RowDefinitions("54,56,46,*"),
            Background = Brush("#0b1220")
        };
        shell.Children.Add(BuildBrandBar());
        AvaloniaControl navigation = BuildNavigation();
        Grid.SetRow(navigation, 1);
        shell.Children.Add(navigation);
        AvaloniaControl messageBar = BuildMessageBar();
        Grid.SetRow(messageBar, 2);
        shell.Children.Add(messageBar);

        var pageHost = new Grid { Margin = new Thickness(22, 18, 22, 22), ClipToBounds = true };
        foreach (string page in Navigation)
        {
            AvaloniaControl content = BuildPage(page);
            _pages.Add(page, content);
            pageHost.Children.Add(content);
        }
        Grid.SetRow(pageHost, 3);
        shell.Children.Add(pageHost);
        ShowPage(_currentPage);
        return new Border
        {
            Background = Brush("#0b1220"),
            BorderBrush = Brush("#26364d"),
            BorderThickness = new Thickness(1),
            Child = shell
        };
    }

    private AvaloniaControl BuildBrandBar()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Background = Brush("#111c2c")
        };
        row.PointerPressed += (_, e) =>
        {
            if (e.Source is not Button && e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0)
        };
        _brandEmblem.Width = 38;
        _brandEmblem.Height = 38;
        _brandEmblem.Stretch = Stretch.Uniform;
        brand.Children.Add(_brandEmblem);
        _brandWordmark.Width = 170;
        _brandWordmark.Height = 36;
        _brandWordmark.Stretch = Stretch.Uniform;
        brand.Children.Add(_brandWordmark);
        row.Children.Add(brand);
        var windowButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        windowButtons.Children.Add(WindowButton("—", () => WindowState = WindowState.Minimized));
        windowButtons.Children.Add(WindowButton("□", () => WindowState =
            WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        windowButtons.Children.Add(WindowButton("×", Close));
        Grid.SetColumn(windowButtons, 2);
        row.Children.Add(windowButtons);
        var version = new TextBlock
        {
            Text = "LINUX · 1.4",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#8fa3bd"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 20, 0)
        };
        Grid.SetColumn(version, 1);
        version.HorizontalAlignment = HorizontalAlignment.Right;
        row.Children.Add(version);
        return row;
    }

    private static Button WindowButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Width = 42,
            Height = 38,
            Background = Brush("#111c2c"),
            Foreground = Brush("#cbd5e1"),
            BorderThickness = new Thickness(0),
            FontSize = 16
        };
        button.Click += (_, _) => action();
        return button;
    }

    private AvaloniaControl BuildNavigation()
    {
        _navigation.Orientation = Orientation.Horizontal;
        _navigation.HorizontalAlignment = HorizontalAlignment.Center;
        _navigation.VerticalAlignment = VerticalAlignment.Center;
        _navigation.Spacing = 4;
        _navigation.Background = Brush("#111c2c");
        _navigation.Margin = new Thickness(0, 0, 0, 1);
        foreach (string page in Navigation)
        {
            var button = new Button
            {
                Content = page,
                Tag = page,
                MinWidth = page == "Configuración" ? 140 : 132,
                Height = 44,
                Padding = new Thickness(14, 0),
                Background = Brush("#111c2c"),
                Foreground = Brush("#cbd5e1"),
                BorderThickness = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold
            };
            button.Click += (_, _) => ShowPage(page);
            _navigation.Children.Add(button);
        }
        return _navigation;
    }

    private AvaloniaControl BuildMessageBar()
    {
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Background = Brush("#131f31"),
            Margin = new Thickness(0, 0, 0, 1)
        };
        _messageDot.Width = 9;
        _messageDot.Height = 9;
        _messageDot.CornerRadius = new CornerRadius(6);
        _messageDot.Background = Brush("#22c55e");
        _messageDot.VerticalAlignment = VerticalAlignment.Center;
        _messageDot.Margin = new Thickness(18, 0, 10, 0);
        bar.Children.Add(_messageDot);
        _message.Text = "NOVORA listo.";
        _message.Foreground = Brush("#dbe5f1");
        _message.FontSize = 12;
        _message.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_message, 1);
        bar.Children.Add(_message);
        return bar;
    }

    private AvaloniaControl BuildPage(string page) => page switch
    {
        "Inicio" => BuildHomePage(),
        "VisionEngine" => BuildVisionPage(),
        "LinkEngine" => BuildLinkPage(),
        "ExInEngine" => BuildExInPage(),
        "Integración" => BuildIntegrationPage(),
        _ => BuildSettingsPage()
    };

    private AvaloniaControl BuildHomePage()
    {
        var content = PageContent();
        AddPageHeading(content, "CENTRO DE SESIÓN", "Inicio",
            "Prepara tu conexión y abre el motor que necesitas para esta sesión.");
        var connection = new StackPanel { Spacing = 12 };
        connection.Children.Add(Label("CONEXIÓN DEL TELÉFONO", "#60a5fa", 12, true));
        connection.Children.Add(SectionTitle("Conecta tu teléfono"));
        connection.Children.Add(Caption("Selecciona USB o Wi-Fi. Cuando esté listo, activa los motores desde AppControl en LINK Control."));
        connection.Children.Add(StatusPill(_deviceStatus));
        connection.Children.Add(BuildAdbControls());
        connection.Children.Add(_adbStatus);
        connection.Children.Add(_adbRefreshButton);
        content.Children.Add(Card(connection));

        var engines = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*,12,*"),
            Margin = new Thickness(0, 0, 0, 14)
        };
        AddEngineCard(engines, "VisionEngine", "Transmisión de pantalla y audio",
            "Captura AppControl USB, decodificación FFmpeg y presentación SDL.", "VisionEngine",
            "USB · video", 0);
        AddEngineCard(engines, "LinkEngine", "Internet y conexión de esta sesión",
            "Inicia LinkEngine desde AppControl del teléfono después de enlazar por USB.", "LinkEngine",
            "USB · preparando", 2);
        AddEngineCard(engines, "ExInEngine", "Mando, entrada y calibración",
            "El backend SDL/HID Linux todavía no está integrado.", "ExInEngine",
            "No integrado", 4);
        content.Children.Add(Label("MOTORES DISPONIBLES", "#8fa3bd", 12, true));
        content.Children.Add(engines);
        content.Children.Add(Card(new StackPanel
        {
            Spacing = 9,
            Children =
            {
                SectionTitle("Estado de la sesión"),
                _hostStatus,
                _linkState,
                _linkMessage
            }
        }));
        return ScrollPage(content);
    }

    private AvaloniaControl BuildVisionPage()
    {
        var content = PageContent();
        AddPageHeading(content, "VISIONENGINE", "VisionEngine · Pantalla y audio",
            "Recibe la pantalla del teléfono por AppControl y preséntala con FFmpeg y SDL.");
        var status = new StackPanel { Spacing = 10 };
        status.Children.Add(SectionTitle("Estado de la transmisión"));
        _visionState.Text = "Detenido";
        _visionState.Foreground = Brush("#dbe5f1");
        _visionMessage.Text = "Enlaza LINK Control por USB y solicita la captura desde AppControl.";
        _visionMessage.Foreground = Brush("#a8b7ca");
        _visionMessage.TextWrapping = TextWrapping.Wrap;
        status.Children.Add(StatusPill(_visionState));
        status.Children.Add(_visionMessage);
        _visionMetrics.Foreground = Brush("#8fa3bd");
        _visionMetrics.TextWrapping = TextWrapping.Wrap;
        status.Children.Add(_visionMetrics);
        StyleFlatButton(_visionStopButton);
        _visionStopButton.Click += async (_, _) => await StopVisionAsync();
        status.Children.Add(_visionStopButton);
        content.Children.Add(Card(status));
        content.Children.Add(Card(StatusSection("Captura",
            "AppControl USB entrega video H.264 y audio PCM estéreo por canales autenticados con token. El flujo LAN aún no está integrado.",
            Badge("USB · VIDEO + AUDIO", "#172b45", "#93c5fd"))));
        content.Children.Add(Card(StatusSection("Decodificación y presentación",
            "FFmpeg decodifica el video. SDL selecciona el renderer disponible; la aceleración de video y los perfiles aún están pendientes.",
            Badge("FFMPEG + SDL", "#172b45", "#93c5fd"))));
        content.Children.Add(Card(DisabledSection("Validación y funciones pendientes",
            "La captura y salida de audio están integradas, pero aún no se validan en un teléfono físico. Interacción teclado/ratón, grabación y transporte VisionEngine LAN siguen pendientes.")));
        return ScrollPage(content);
    }

    private AvaloniaControl BuildLinkPage()
    {
        var content = PageContent();
        AddPageHeading(content, "LINKENGINE", "LinkEngine",
            "Conecta el teléfono a esta PC por USB o Wi-Fi.");
        var columns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.15*,14,*")
        };
        var status = new StackPanel { Spacing = 12 };
        status.Children.Add(SectionTitle("Conexión"));
        status.Children.Add(_linkPageMessage);
        status.Children.Add(StatusPill(_linkPageState));
        status.Children.Add(LabeledValue("Transporte", "ADB reverse · USB"));
        status.Children.Add(LabeledValue("CONTROL", "tcp:27183"));
        status.Children.Add(LabeledValue("DATA / relay", "tcp:27184"));
        status.Children.Add(Caption("Activa LinkEngine desde AppControl en el teléfono enlazado. El relay Linux y el canal de control se preparan bajo demanda."));
        Border statusCard = Card(status);
        Grid.SetColumn(statusCard, 0);
        columns.Children.Add(statusCard);

        var preparation = new StackPanel { Spacing = 12 };
        preparation.Children.Add(SectionTitle("Preparar el canal de conexión"));
        preparation.Children.Add(Caption("Conecta el teléfono por cable y enlázalo mediante ADB. Después, inicia LinkEngine desde AppControl. El servidor Linux no reemplaza rutas ADB existentes."));
        preparation.Children.Add(Caption("Para cambiar o actualizar el dispositivo USB, usa la tarjeta de conexión en Inicio."));
        var homeButton = FlatButton("Abrir conexión");
        homeButton.Click += (_, _) => ShowPage("Inicio");
        preparation.Children.Add(homeButton);
        preparation.Children.Add(Card(DisabledSection("Por LAN / Wi-Fi",
            "El transporte de datos LinkEngine por LAN aún no está integrado en Linux.")));
        Border preparationCard = Card(preparation);
        Grid.SetColumn(preparationCard, 2);
        columns.Children.Add(preparationCard);
        content.Children.Add(columns);
        content.Children.Add(Card(DisabledSection("Diagnóstico y velocidad",
            "La telemetría y la prueba de velocidad de LinkEngine todavía no están integradas en la interfaz Linux.")));
        return ScrollPage(content);
    }

    private AvaloniaControl BuildExInPage()
    {
        var content = PageContent();
        AddPageHeading(content, "EXINENGINE", "ExInEngine · Control",
            "Primero calibra el mando; después elige el teléfono, el modo y las asignaciones.");
        content.Children.Add(Card(DisabledSection("Calibración dinámica",
            "La vista del mando, calibración de sticks y gatillos, mapeo de botones y telemetría requieren integrar ExInEngine con SDL/HID en Linux.")));
        content.Children.Add(Card(DisabledSection("Dispositivos y asignaciones",
            "Detección y configuración de mandos todavía no disponibles.")));
        return ScrollPage(content);
    }

    private AvaloniaControl BuildIntegrationPage()
    {
        var content = PageContent();
        AddPageHeading(content, "INTEGRACIÓN", "Integración",
            "Estado de los componentes disponibles en esta versión Linux.");
        content.Children.Add(EngineDetailCard("LinkEngine", "Integración parcial · USB",
            "Relay nativo Linux y CONTROL USB disponibles. El canal Android completo requiere iniciar LinkEngine desde AppControl."));
        content.Children.Add(EngineDetailCard("VisionEngine", "Integración USB · video",
            "AppControl USB, sockets autenticados, decodificación FFmpeg y presentación SDL. Audio, LAN, control y grabación pendientes."));
        content.Children.Add(EngineDetailCard("ExInEngine", "No integrado",
            "Dispositivos de juego, SDL/HID y calibración pendientes."));
        content.Children.Add(EngineDetailCard("Transferencia de archivos", "No integrada",
            "Transferencia y almacenamiento de archivos pendientes."));
        return ScrollPage(content);
    }

    private AvaloniaControl BuildSettingsPage()
    {
        var content = PageContent();
        AddPageHeading(content, "SISTEMA", "Configuración",
            "Preferencias de conexión y confianza de los dispositivos.");
        var lan = new StackPanel { Spacing = 12 };
        lan.Children.Add(SectionTitle("Conexión LAN"));
        lan.Children.Add(Caption("Selecciona la interfaz privada de la PC para generar una invitación de un solo uso compatible con LINK Control 1.4.124."));
        lan.Children.Add(BuildNetworkControls());
        lan.Children.Add(_settingsHostStatus);
        lan.Children.Add(BuildPairingPanel());
        lan.Children.Add(Caption("El estado del teléfono y los controles ADB se muestran en Inicio."));
        content.Children.Add(Card(lan));
        var trusted = new StackPanel { Spacing = 12 };
        trusted.Children.Add(SectionTitle("Teléfonos con acceso recordado"));
        trusted.Children.Add(Caption("Puedes revocar el acceso de un dispositivo guardado en esta PC."));
        trusted.Children.Add(BuildTrustedDevicesPanel());
        content.Children.Add(Card(trusted));
        var appearance = new StackPanel { Spacing = 10 };
        appearance.Children.Add(SectionTitle("Apariencia"));
        appearance.Children.Add(Caption("Selecciona el tema de color de NOVORA-LINK."));
        _themeSelector.ItemsSource = new[] { "Claro · Light", "Oscuro · Dark" };
        _themeSelector.SelectedIndex = LinuxTheme.IsDark ? 1 : 0;
        _themeSelector.MinWidth = 220;
        _themeSelector.SelectionChanged += ThemeSelectionChanged;
        appearance.Children.Add(_themeSelector);
        content.Children.Add(Card(appearance));
        content.Children.Add(Card(DisabledSection("Otras preferencias",
            "Métricas de rendimiento y actualizaciones automáticas aún no están disponibles en Linux.")));
        return ScrollPage(content);
    }

    private static StackPanel PageContent() => new()
    {
        Spacing = 0,
        Margin = new Thickness(0, 0, 4, 8)
    };

    private static ScrollViewer ScrollPage(StackPanel content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private void AddPageHeading(StackPanel parent, string eyebrow, string title, string caption)
    {
        parent.Children.Add(Label(eyebrow, "#8fa3bd", 12, true));
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
        if (Navigation.Contains(title.Split('·')[0].Trim(), StringComparer.Ordinal))
            titleRow.Children.Add(EngineIcon(title.Split('·')[0].Trim()));
        titleRow.Children.Add(Label(title, "#f1f5f9", 28, true));
        parent.Children.Add(titleRow);
        parent.Children.Add(Caption(caption, new Thickness(0, 6, 0, 18)));
    }

    private static AvaloniaControl EngineIcon(string name)
    {
        string geometry = name switch
        {
            "VisionEngine" => "M2,3 L30,3 30,23 19,23 19,28 25,28 25,31 7,31 7,28 13,28 13,23 2,23Z M5,6 L5,20 27,20 27,6Z M12,9 L21,13 12,17Z",
            "LinkEngine" => "M25,47 A21,21 0 1 1 47,25 M5,26 L45,26 M10,13 L42,13 M10,39 L25,39 M26,5 C13,16 13,36 26,47 M26,5 C34,12 38,19 37,27 M26,5 L26,40 M36,43 L32,47 C24,55 14,45 22,37 L30,29 C38,21 48,31 40,39 M43,36 L47,32 C55,24 65,34 57,42 L49,50 C41,58 31,48 39,40 M32,44 L47,35",
            "ExInEngine" => "M10,8 C5,8 4,12 2,23 C1,29 5,30 9,25 L12,21 20,21 23,25 C27,30 31,29 30,23 L27,12 C26,8 23,8 20,8Z M10,12 L10,19 M7,15.5 L13,15.5 M22,12 L22,14 M25,15 L25,17",
            _ => ""
        };
        bool outlined = name != "VisionEngine";
        var shape = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(geometry),
            Width = 48,
            Height = 48,
            Stretch = Stretch.Uniform,
            Fill = outlined ? Brushes.Transparent : Brush("#35d7e9"),
            Stroke = outlined ? Brush("#35d7e9") : null,
            StrokeThickness = name == "LinkEngine" ? 2.4 : 1.8
        };
        return new Viewbox { Width = 44, Height = 44, Child = shape, HorizontalAlignment = HorizontalAlignment.Left };
    }

    private void AddEngineCard(Grid grid, string name, string subtitle, string detail,
        string destination, string state, int column)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(EngineIcon(name));
        content.Children.Add(Badge(name == "LinkEngine" ? "USB" : "MOTOR", "#172b45", "#93c5fd"));
        content.Children.Add(SectionTitle(name));
        content.Children.Add(Caption(subtitle));
        content.Children.Add(Caption(detail));
        content.Children.Add(Badge(state, "#26364d", "#dbe5f1"));
        var button = FlatButton(name == "LinkEngine" ? "Abrir LinkEngine" : "Ver estado");
        button.Click += (_, _) => ShowPage(destination);
        content.Children.Add(button);
        var card = Card(content);
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }

    private AvaloniaControl BuildNetworkControls()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        _addresses.HorizontalAlignment = HorizontalAlignment.Left;
        row.Children.Add(_addresses);
        var refresh = FlatButton("Actualizar");
        refresh.Click += (_, _) => RefreshAddresses();
        row.Children.Add(refresh);
        row.Children.Add(_startButton);
        StylePrimaryButton(_startButton);
        return row;
    }

    private AvaloniaControl BuildAdbControls()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(_adbDevices);
        StyleFlatButton(_usbButton);
        row.Children.Add(_usbButton);
        return row;
    }

    private AvaloniaControl BuildPairingPanel()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Caption("Código de emparejamiento · no compartir por canales inseguros"));
        var codeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        _pairingCode.FontSize = 26;
        _pairingCode.FontWeight = FontWeight.Bold;
        _pairingCode.Foreground = Brush("#f1f5f9");
        codeRow.Children.Add(_pairingCode);
        codeRow.Children.Add(_countdown);
        StyleFlatButton(_renewButton);
        codeRow.Children.Add(_renewButton);
        panel.Children.Add(codeRow);
        panel.Children.Add(Caption("Verifica la huella completa en NOVORA-LINK para Linux y en el teléfono antes de enviar el código."));
        panel.Children.Add(_fingerprint);
        return panel;
    }

    private AvaloniaControl BuildTrustedDevicesPanel()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(_trustedDevices);
        return panel;
    }

    private void ShowPage(string page)
    {
        if (!_pages.TryGetValue(page, out AvaloniaControl? selected))
        {
            _currentPage = page;
            return;
        }
        _currentPage = page;
        foreach ((string name, AvaloniaControl content) in _pages)
            content.IsVisible = name == page;
        foreach (Button button in _navigation.Children.OfType<Button>())
        {
            bool active = Equals(button.Tag, page);
            button.Background = Brush(active ? "#1d3150" : "#111c2c");
            button.Foreground = Brush(active ? "#93c5fd" : "#cbd5e1");
        }
    }

    private static Border Card(AvaloniaControl child) => new()
    {
        Background = Brush("#131f31"),
        BorderBrush = Brush("#26364d"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(18),
        Margin = new Thickness(0, 0, 0, 14),
        Child = child
    };

    private static StackPanel StatusSection(string heading, string message, Border badge) => new()
    {
        Spacing = 10,
        Children = { SectionTitle(heading), Caption(message), badge }
    };

    private static StackPanel DisabledSection(string heading, string message) => new()
    {
        Spacing = 10,
        Children =
        {
            SectionTitle(heading),
            Caption(message),
            Badge("PENDIENTE EN LINUX", "#3a2a19", "#fcd34d")
        }
    };

    private static Border EngineDetailCard(string name, string state, string description)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(SectionTitle(name));
        stack.Children.Add(Caption(description));
        stack.Children.Add(Badge(state, "#26364d", "#dbe5f1"));
        return Card(stack);
    }

    private static StackPanel LabeledValue(string title, string value) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children = { Caption(title), Label(value, "#dbe5f1", 13, true) }
    };

    private static Border StatusPill(TextBlock status) => new()
    {
        Background = Brush("#18283d"),
        BorderBrush = Brush("#314761"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(18),
        Padding = new Thickness(12, 8),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = status
    };

    private static Border Badge(string text, string background, string foreground) => new()
    {
        Background = Brush(background),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(9, 4),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = Label(text, foreground, 11, true)
    };

    private static TextBlock SectionTitle(string text) => Label(text, "#f1f5f9", 18, true);

    private static TextBlock Caption(string text, Thickness? margin = null) =>
        Label(text, "#a8b7ca", 13, false, margin);

    private static TextBlock Label(string text, string color, double size, bool bold, Thickness? margin = null) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = Brush(color),
        TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0)
    };

    private static Button FlatButton(string text)
    {
        var button = new Button { Content = text };
        StyleFlatButton(button);
        return button;
    }

    private static void StylePrimaryButton(Button button)
    {
        button.Height = 44;
        button.Padding = new Thickness(16, 0);
        button.Background = Brush("#2563eb");
        button.Foreground = Brush("#ffffff");
        button.BorderThickness = new Thickness(0);
        button.FontSize = 14;
        button.FontWeight = FontWeight.SemiBold;
    }

    private static void StyleFlatButton(Button button)
    {
        button.Height = 40;
        button.Padding = new Thickness(14, 0);
        button.Background = Brush("#1b2a3e");
        button.Foreground = Brush("#dbe5f1");
        button.BorderBrush = Brush("#34475f");
        button.BorderThickness = new Thickness(1);
        button.FontSize = 13;
    }

    private static SolidColorBrush Brush(string color) => LinuxTheme.CreateBrush(color);

    private void ThemeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        string? theme = _themeSelector.SelectedIndex switch
        {
            0 => LinuxTheme.Light,
            1 => LinuxTheme.Dark,
            _ => null
        };
        if (theme is null || theme == LinuxTheme.Current)
            return;

        LinuxTheme.Apply(theme);
        try
        {
            LinuxTheme.Save();
            SetMessage($"Tema {theme} aplicado y guardado.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetMessage($"Tema aplicado, pero no se pudo guardar la preferencia: {ex.Message}", "#fbbf24");
        }
    }

    private void ThemeChanged(object? sender, EventArgs e) => UpdateBrandAssets();

    private void UpdateBrandAssets()
    {
        string suffix = LinuxTheme.IsDark ? "dark" : "light";
        string emblemPath = LinuxTheme.GetAssetPath($"novora-emblem-{suffix}.png");
        string wordmarkPath = LinuxTheme.GetAssetPath($"novora-wordmark-{suffix}.png");
        if (!File.Exists(emblemPath) || !File.Exists(wordmarkPath) ||
            !File.Exists(LinuxTheme.GetAssetPath("novora.ico")))
            throw new FileNotFoundException("Faltan recursos de marca requeridos por el tema.",
                !File.Exists(emblemPath) ? emblemPath : wordmarkPath);

        var emblem = new Bitmap(emblemPath);
        var wordmark = new Bitmap(wordmarkPath);
        _brandEmblem.Source = emblem;
        _brandWordmark.Source = wordmark;
        _emblemBitmap?.Dispose();
        _wordmarkBitmap?.Dispose();
        _emblemBitmap = emblem;
        _wordmarkBitmap = wordmark;
    }

    private void RefreshAddresses()
    {
        try
        {
            IReadOnlyList<LanAddress> addresses = LinuxControlHost.GetLanAddresses();
            _addresses.ItemsSource = addresses;
            _addresses.SelectedItem = addresses.FirstOrDefault();
            if (addresses.Count == 0)
                SetMessage("No se detectó una dirección IPv4 privada. Conéctate a una LAN y actualiza la lista.");
        }
        catch (NetworkInformationException ex)
        {
            SetMessage($"No se pudieron consultar las interfaces de red: {ex.Message}");
        }
    }

    private void StartControl(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_host.IsRunning)
        {
            _ = StopControlAsync();
            return;
        }
        if (_addresses.SelectedItem is not LanAddress selected)
        {
            SetMessage("Selecciona una dirección IPv4 privada de la LAN.");
            return;
        }

        try
        {
            _host.Start(selected.Address);
            _hostStatus.Text = $"Escuchando en {selected.Address}:{ControlProtocol.Port}.";
            _settingsHostStatus.Text = _hostStatus.Text;
            _startButton.Content = "Detener control LAN";
            _renewButton.IsEnabled = true;
            UpdateTrustedDevices();
            UpdateInvitationDisplay();
            SetMessage($"Control LAN preparado en {selected.Address}:{ControlProtocol.Port}.");
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or
                                   UnauthorizedAccessException or System.Text.Json.JsonException or
                                   System.Security.Cryptography.CryptographicException)
        {
            SetMessage($"No se pudo preparar el control LAN: {ex.Message}", "#f87171");
        }
    }

    private async void StartUsbControl(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_host.IsUsbRunning)
        {
            _usbButton.IsEnabled = false;
            try
            {
                await _host.StopUsbAsync();
                _usbButton.Content = "Enlazar por USB";
                SetMessage("Control USB detenido.");
            }
            catch (Exception ex)
            {
                SetMessage($"No se pudo cerrar el control USB: {ex.Message}", "#f87171");
            }
            finally
            {
                _usbButton.IsEnabled = true;
                UpdateDeviceStatus();
            }
            return;
        }

        if (_adbDevices.SelectedItem is not AdbDevice { State: "device", IsUsb: true } device)
        {
            SetMessage("Selecciona un teléfono autorizado y conectado por USB en ADB.", "#fbbf24");
            return;
        }

        _usbButton.IsEnabled = false;
        _adbRefreshButton.IsEnabled = false;
        SetMessage("Verificando LINK Control, preparando ADB reverse y abriendo el emparejamiento USB protegido…");
        try
        {
            await _host.StartUsbAsync(device, CancellationToken.None);
            _usbButton.Content = "Detener control USB";
            SetMessage($"Enlace USB preparado para {device.Model}. Esperando confirmación TLS de LINK Control.");
        }
        catch (Exception ex)
        {
            SetMessage($"No se pudo preparar el enlace USB: {ex.Message}", "#f87171");
        }
        finally
        {
            _usbButton.IsEnabled = true;
            _adbRefreshButton.IsEnabled = true;
            UpdateDeviceStatus();
        }
    }

    private void RenewInvitation(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            _host.RenewInvitation();
            UpdateInvitationDisplay();
            SetMessage("Código renovado. El anterior dejó de ser válido.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            SetMessage($"No se pudo renovar el código: {ex.Message}", "#f87171");
        }
    }

    private async Task StopControlAsync()
    {
        try
        {
            await _host.DisposeAsync();
            SetMessage("Control LAN detenido.");
        }
        catch (Exception ex)
        {
            SetMessage($"El control se detuvo con un error: {ex.Message}", "#f87171");
        }
        finally
        {
            _hostStatus.Text = "El control LAN está detenido.";
            _startButton.Content = "Preparar control LAN";
            _renewButton.IsEnabled = false;
            _pairingCode.Text = "------";
            _fingerprint.Text = "";
            _countdown.Text = "Válido durante 2 minutos";
            _deviceStatus.Text = "Sin dispositivos Android conectados.";
            _trustedDevices.Children.Clear();
        }
    }

    private void UpdateInvitationDisplay()
    {
        LanInvitation? invitation = _host.Invitation;
        if (invitation is null)
            return;
        long seconds = Math.Max(0, invitation.ExpiresUnix - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _pairingCode.Text = invitation.Code;
        _countdown.Text = seconds > 0 ? $"Caduca en {seconds / 60:D2}:{seconds % 60:D2}" : "Código vencido · genera uno nuevo";
        _fingerprint.Text = string.Join(' ', Enumerable.Range(0, invitation.Fingerprint.Length / 4)
            .Select(index => invitation.Fingerprint.Substring(index * 4, 4)));
        UpdateDeviceStatus();
        UpdateTrustedDevices();
    }

    private void UpdateDeviceStatus()
    {
        if (_host.IsUsbRunning)
        {
            _usbButton.Content = "Detener control USB";
            _deviceStatus.Text = _host.IsAuthorized
                ? $"Android conectado por USB · {_host.UsbSerial} · TLS confirmado."
                : $"ADB reverse listo para {_host.UsbSerial}; esperando respuesta de LINK Control.";
        }
        else
        {
            _usbButton.Content = "Enlazar por USB";
            _deviceStatus.Text = _host.IsAuthorized
                ? "Android conectado por LAN."
                : "Sin dispositivos Android conectados.";
        }
        UpdateLinkState();
    }

    private void UpdateLinkState()
    {
        _linkState.Text = _host.LinkState;
        _linkMessage.Text = _host.LinkMessage;
        _linkPageState.Text = _host.LinkState;
        _linkPageMessage.Text = _host.LinkMessage;
        UpdateVisionState();
    }

    private void UpdateVisionState()
    {
        _visionState.Text = _host.VisionState switch
        {
            "Waiting" => "Esperando captura",
            "Streaming" => "Transmitiendo",
            "Failed" => "Error",
            "Unavailable" => "No disponible por esta ruta",
            _ => "Detenido"
        };
        _visionMessage.Text = _host.VisionMessage;
        NOVORA.Linux.VisionEngine.LinuxVisionTelemetry? telemetry = _host.VisionTelemetry;
        _visionMetrics.Text = telemetry is null
            ? "Renderer SDL: sin datos."
            : $"SDL {telemetry.RendererBackend} · {telemetry.FramesRendered} presentados · " +
              $"{telemetry.VideoPacketsReceived} paquetes · {telemetry.FramesDroppedEstimate} sin presentar aprox. · " +
              $"salida {telemetry.OutputWidth}×{telemetry.OutputHeight}";
        _visionMetrics.Foreground = Brush(
            telemetry?.RendererBackend == "software" ? "#fbbf24" : "#8fa3bd");
        _visionStopButton.IsEnabled = _host.HasVisionSession;
    }

    private async Task StopVisionAsync()
    {
        _visionStopButton.IsEnabled = false;
        try
        {
            await _host.StopVisionAsync();
            SetMessage("VisionEngine detenido.");
        }
        catch (Exception ex)
        {
            SetMessage($"No se pudo detener VisionEngine: {ex.Message}", "#f87171");
        }
        finally
        {
            UpdateVisionState();
        }
    }

    private void UpdateTrustedDevices()
    {
        _trustedDevices.Children.Clear();
        foreach (TrustedDevice device in _host.TrustedDevices)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(Label(device.Name, "#dbe5f1", 13, false));
            var revoke = FlatButton("Revocar");
            revoke.Click += async (_, _) =>
            {
                try
                {
                    await _host.RevokeAsync(device.DeviceId);
                }
                catch (AggregateException ex)
                {
                    SetMessage($"Acceso revocado, pero falló la limpieza de la sesión: {ex.Message}", "#f87171");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
                {
                    SetMessage($"No se pudo revocar el dispositivo: {ex.Message}", "#f87171");
                }
                finally
                {
                    UpdateTrustedDevices();
                    UpdateVisionState();
                }
            };
            row.Children.Add(revoke);
            _trustedDevices.Children.Add(row);
        }
        if (_trustedDevices.Children.Count == 0)
            _trustedDevices.Children.Add(Caption("Todavía no hay teléfonos guardados."));
    }

    private async Task RefreshAdbDevicesAsync()
    {
        _adbStatus.Text = "Buscando teléfonos USB con ADB…";
        _adbRefreshButton.IsEnabled = false;
        try
        {
            IReadOnlyList<AdbDevice> devices = await _host.GetAdbDevicesAsync(CancellationToken.None);
            _adbDevices.ItemsSource = devices;
            _adbDevices.SelectedItem = devices.FirstOrDefault(device => device.IsUsb && device.State == "device");
            int usbDevices = devices.Count(device => device.IsUsb && device.State == "device");
            int unauthorized = devices.Count(device => device.IsUsb && device.State == "unauthorized");
            _adbStatus.Text = usbDevices > 0
                ? $"ADB activo · {usbDevices} teléfono(s) USB autorizado(s)" +
                    (unauthorized > 0 ? $" · {unauthorized} esperando autorización" : "")
                : unauthorized > 0
                    ? $"ADB detectó {unauthorized} teléfono(s) USB; autoriza la depuración en cada teléfono."
                    : "ADB activo, pero no detecta teléfonos por USB. Revisa cable, modo USB y depuración.";
            _adbStatus.Foreground = Brush("#dbe5f1");
        }
        catch (Exception ex)
        {
            _adbDevices.ItemsSource = Array.Empty<AdbDevice>();
            _adbStatus.Text = $"No se pudo consultar ADB: {ex.Message}";
            _adbStatus.Foreground = Brush("#fbbf24");
        }
        finally
        {
            _adbRefreshButton.IsEnabled = true;
        }
    }

    private void HostOnStatusChanged(object? sender, string message) =>
        Dispatcher.UIThread.Post(() =>
        {
            SetMessage(message);
            UpdateInvitationDisplay();
            UpdateDeviceStatus();
            UpdateTrustedDevices();
        });

    private void HostOnInvitationChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(UpdateInvitationDisplay);

    private void SetMessage(string text, string color = "#dbe5f1")
    {
        _message.Text = text;
        _message.Foreground = Brush(color);
        _messageDot.Background = Brush(color == "#dbe5f1" ? "#22c55e" : color);
    }

    private async Task ShutdownAsync()
    {
        if (_shutdownStarted)
            return;
        _shutdownStarted = true;
        _timer.Stop();
        try
        {
            await _host.DisposeAsync();
        }
        catch (Exception ex)
        {
            SetMessage($"Error al cerrar los servicios Linux: {ex.Message}", "#f87171");
        }
    }
}
