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

public sealed partial class MainWindow : Window
{
    private static readonly string[] Navigation = ["Inicio", "VisionEngine", "LinkEngine", "ExInEngine", "Integración", "Configuración"];
    private static readonly string[] VisionProfiles =
        ["Automático", "Competitivo", "Equilibrado", "Calidad", "Fluidez", "Software"];
    private static readonly string[] CaptureResolutions =
        ["480", "720", "960", "1080", "1280", "1440", "1600", "1920", "2160"];
    private static readonly string[] CaptureFrameRates = ["15", "24", "30", "45", "60"];
    private static readonly string[] CaptureBitrates =
        Enumerable.Range(2, 14).Select(value => $"{value}M").ToArray();
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
    private readonly TextBlock _homeNextStep = new();
    private readonly Button _homeScreenButton = new() { Content = "VER PANTALLA" };
    private readonly Button _homeConnectionButton = new() { Content = "TIPO DE CONEXIÓN ▾" };
    private readonly CheckBox _performanceSession = new()
    {
        Content = "Sesión de rendimiento: cierra mediciones en vivo, desactiva red experimental y pausa actualizaciones automáticas.",
        IsEnabled = false
    };
    private readonly ComboBox _visionProfileSelector = new();
    private readonly ComboBox _visionResolutionSelector = new();
    private readonly ComboBox _visionFpsSelector = new();
    private readonly ComboBox _visionBitrateSelector = new();
    private readonly TextBlock _visionSettingsStatus = new();
    private readonly TextBlock _linkTechnicalStatus = new();
    private readonly Button _linkTechnicalButton = new() { Content = "MOSTRAR DETALLES TÉCNICOS" };
    private readonly Button _linkPrepareButton = new() { Content = "PREPARAR ESTA RUTA", IsEnabled = false };
    private readonly Button _linkInternetTestButton = new() { Content = "MEDIR INTERNET", IsEnabled = false };
    private readonly Button _linkTunnelTestButton = new() { Content = "MEDIR ENLACE PC ↔ ANDROID", IsEnabled = false };
    private readonly Button _linkTrafficButton = new() { Content = "ACTUALIZAR TRÁFICO", IsEnabled = false };
    private readonly TextBlock _integrationSentSummary = new();
    private readonly TextBlock _integrationReceivedSummary = new();
    private readonly TextBlock _integrationSentNames = new();
    private readonly TextBlock _integrationReceivedNames = new();
    private readonly Button _openPerformanceButton = new() { Content = "ABRIR" };
    private readonly Button _checkPcUpdateButton = new() { Content = "Buscar actualización de PC", IsEnabled = false };
    private readonly Button _checkAndroidUpdateButton = new() { Content = "Buscar actualización de LINK Control", IsEnabled = false };
    private readonly Button _manualEsButton = new() { Content = "MANUAL ES", IsEnabled = false };
    private readonly Button _manualEnButton = new() { Content = "MANUAL EN", IsEnabled = false };
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
        _linkMessage.Text = "Conecta el teléfono por USB o LAN y habilita LinkEngine desde AppControl.";
        _linkPageState.Text = "Desconectado";
        _linkPageMessage.Text = "Conecta el teléfono por USB o LAN y habilita LinkEngine desde AppControl.";
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
        AvaloniaControl performancePage = BuildPerformancePage();
        _pages.Add("Rendimiento", performancePage);
        pageHost.Children.Add(performancePage);
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
                Content = NavigationContent(page),
                Tag = page,
                MinWidth = page switch
                {
                    "Inicio" => 112,
                    "VisionEngine" or "LinkEngine" or "ExInEngine" => 144,
                    "Integración" => 150,
                    _ => 164
                },
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

    private static AvaloniaControl NavigationContent(string page)
    {
        string geometry = page switch
        {
            "Inicio" => "M2,15 L16,2 30,15 27,18 27,30 19,30 19,21 13,21 13,30 5,30 5,18Z",
            "VisionEngine" => "M2,3 L30,3 30,23 19,23 19,28 25,28 25,31 7,31 7,28 13,28 13,23 2,23Z M5,6 L5,20 27,20 27,6Z M12,9 L21,13 12,17Z",
            "LinkEngine" => "M25,47 A21,21 0 1 1 47,25 M5,26 L45,26 M10,13 L42,13 M10,39 L25,39 M26,5 C13,16 13,36 26,47 M26,5 C34,12 38,19 37,27 M36,43 L32,47 C24,55 14,45 22,37 L30,29 C38,21 48,31 40,39 M43,36 L47,32 C55,24 65,34 57,42 L49,50 C41,58 31,48 39,40 M32,44 L47,35",
            "ExInEngine" => "M10,8 C5,8 4,12 2,23 C1,29 5,30 9,25 L12,21 20,21 23,25 C27,30 31,29 30,23 L27,12 C26,8 23,8 20,8Z M10,12 L10,19 M7,15.5 L13,15.5 M22,12 L22,14 M25,15 L25,17",
            "Integración" => "M8,3 L24,3 24,12 8,12Z M3,18 L13,18 13,28 3,28Z M19,18 L29,18 29,28 19,28Z M12,12 L12,16 M24,12 L24,16 M8,16 L24,16",
            _ => "M16,2 A14,14 0 1 1 16,30 A14,14 0 1 1 16,2Z M16,9 L16,23 M9,16 L23,16"
        };
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(geometry),
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            Fill = Brushes.Transparent,
            Stroke = Brush("#93c5fd"),
            StrokeThickness = 2,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                path,
                new TextBlock
                {
                    Text = page switch
                    {
                        "VisionEngine" => "Screen",
                        "LinkEngine" => "Network",
                        "ExInEngine" => "Game Input",
                        _ => page
                    },
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
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
        _homeNextStep.Text = "Paso 1 de 4 · Conecta el teléfono por USB o Wi-Fi.";
        _homeNextStep.Foreground = Brush("#60a5fa");
        _homeNextStep.FontWeight = FontWeight.SemiBold;
        _homeNextStep.Margin = new Thickness(0, 0, 0, 8);
        _homeNextStep.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(_homeNextStep);
        _performanceSession.Foreground = Brush("#dbe5f1");
        _performanceSession.Margin = new Thickness(0, 0, 0, 16);
        content.Children.Add(_performanceSession);

        var connection = new StackPanel { Spacing = 12 };
        connection.Children.Add(Label("CONEXIÓN DEL TELÉFONO", "#60a5fa", 12, true));
        connection.Children.Add(SectionTitle("Conecta tu teléfono"));
        connection.Children.Add(Caption("Selecciona USB o Wi-Fi. Cuando esté listo, activa los motores desde AppControl en LINK Control."));
        connection.Children.Add(StatusPill(_deviceStatus));
        connection.Children.Add(BuildAdbControls());
        connection.Children.Add(_adbStatus);
        connection.Children.Add(_adbRefreshButton);
        var connectionActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 4, 0, 0)
        };
        StylePrimaryButton(_homeScreenButton);
        _homeScreenButton.Click += (_, _) =>
        {
            ShowPage("VisionEngine");
            SetMessage("Abre VisionEngine desde LINK Control para autorizar e iniciar la captura.");
        };
        StyleFlatButton(_homeConnectionButton);
        _homeConnectionButton.Click += (_, _) => ShowPage("Configuración");
        connectionActions.Children.Add(_homeScreenButton);
        connectionActions.Children.Add(_homeConnectionButton);
        connection.Children.Add(connectionActions);
        content.Children.Add(Card(connection));

        var engines = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*,12,*"),
            Margin = new Thickness(0, 0, 0, 14)
        };
        AddEngineCard(engines, "VisionEngine", "Transmisión de pantalla y audio",
            "Captura AppControl USB/LAN, decodificación FFmpeg y presentación SDL.", "VisionEngine",
            "USB + LAN", 0);
        AddEngineCard(engines, "LinkEngine", "Internet y conexión de esta sesión",
            "Inicia LinkEngine desde AppControl del teléfono enlazado por USB o LAN/Wi-Fi.", "LinkEngine",
            "Bajo demanda", 2);
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

    private AvaloniaControl BuildVisionPage() => BuildFunctionalVisionPage();

    private static void StyleSelector(ComboBox selector)
    {
        selector.MinHeight = 38;
        selector.Foreground = Brush("#dbe5f1");
        selector.Background = Brush("#1b2a3e");
        selector.BorderBrush = Brush("#34475f");
        selector.BorderThickness = new Thickness(1);
        selector.Padding = new Thickness(10, 6);
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
        status.Children.Add(Caption("LinkEngine conectado por USB o Wi-Fi."));
        status.Children.Add(StatusGrid(
            ("Estado de LE", _linkPageState),
            ("Teléfono", _deviceStatus),
            ("Enlace activo", _linkMessage),
            ("Reconexión", Caption("Reconexión automática según el estado del relay."))));
        _linkTechnicalStatus.Text = "ADB y transporte: USB 27183/27184 · LAN TLS fijado a certificado";
        _linkTechnicalStatus.Foreground = Brush("#a8b7ca");
        _linkTechnicalStatus.TextWrapping = TextWrapping.Wrap;
        _linkTechnicalStatus.IsVisible = false;
        _linkTechnicalButton.Click += (_, _) =>
        {
            _linkTechnicalStatus.IsVisible = !_linkTechnicalStatus.IsVisible;
            _linkTechnicalButton.Content = _linkTechnicalStatus.IsVisible
                ? "OCULTAR DETALLES TÉCNICOS"
                : "MOSTRAR DETALLES TÉCNICOS";
        };
        StyleFlatButton(_linkTechnicalButton);
        status.Children.Add(_linkTechnicalButton);
        status.Children.Add(_linkTechnicalStatus);
        Border statusCard = Card(status);
        Grid.SetColumn(statusCard, 0);
        columns.Children.Add(statusCard);

        var preparation = new StackPanel { Spacing = 12 };
        preparation.Children.Add(SectionTitle("Preparar el canal de conexión"));
        preparation.Children.Add(Caption("Deja listo el transporte de LinkEngine: conexión USB por cable o ADB inalámbrico por Wi-Fi. Después prepara la ruta y autoriza Iniciar Internet desde AppControl."));
        preparation.Children.Add(Caption("Conecta el teléfono por cable y activa LinkEngine desde AppControl. Usa la guía si necesitas habilitar Internet USB en Android."));
        var wifi = new StackPanel { Spacing = 8 };
        wifi.Children.Add(SectionTitle("Por Wi-Fi"));
        wifi.Children.Add(Caption("Configura ADB inalámbrico cuando el teléfono y la PC estén en la misma red; esto no inicia LinkEngine por sí solo."));
        var wifiButton = FlatButton("CONFIGURAR ADB INALÁMBRICO");
        wifiButton.IsEnabled = false;
        wifi.Children.Add(wifiButton);
        preparation.Children.Add(Card(wifi));
        preparation.Children.Add(Caption("Para cambiar o actualizar el dispositivo, usa la tarjeta de conexión en Inicio."));
        preparation.Children.Add(_linkPrepareButton);
        var lanInfo = new StackPanel { Spacing = 6 };
        lanInfo.Children.Add(SectionTitle("Por LAN / Wi-Fi"));
        lanInfo.Children.Add(Caption("LinkEngine LAN usa el relay local y un gateway TLS en la IP privada autorizada. Android recibe host, puerto, huella y token de un solo uso; no requiere ADB reverse."));
        preparation.Children.Add(Card(lanInfo));
        Border preparationCard = Card(preparation);
        Grid.SetColumn(preparationCard, 2);
        columns.Children.Add(preparationCard);
        content.Children.Add(columns);
        content.Children.Add(Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                SectionTitle("Mediciones técnicas de esta sesión"),
                Caption("Valores en vivo y pruebas recibidas por este enlace; se reinician al cambiar o detener la sesión."),
                Caption("Tráfico actual: la telemetría de sesión aún no está disponible en Linux."),
                Caption("Internet: sin medir en esta sesión."),
                Caption("Enlace PC ↔ Android: sin medir en esta sesión."),
                new Expander
                {
                    Header = "Mediciones de esta sesión",
                    Foreground = Brush("#dbe5f1"),
                    Content = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            Caption("Tráfico y pruebas de esta sesión. El relay mide el tráfico; las pruebas se muestran por separado."),
                            _linkInternetTestButton,
                            _linkTunnelTestButton,
                            _linkTrafficButton,
                            Caption("Sin mediciones en esta sesión.")
                        }
                    }
                },
                new Expander
                {
                    Header = "Ajustes técnicos · red experimental (LE / VE / Archivos)",
                    Foreground = Brush("#dbe5f1"),
                    Content = DisabledSection("Control de capacidad y latencia",
                        "La adaptación experimental de red no está disponible en Linux.")
                }
            }
        }));
        return ScrollPage(content);
    }

    private AvaloniaControl BuildExInPage()
    {
        var content = PageContent();
        AddPageHeading(content, "EXINENGINE", "ExInEngine · Control",
            "Primero calibra el mando; después elige el teléfono, el modo y las asignaciones.");
        content.Children.Add(BuildExInCalibrationCard());
        content.Children.Add(BuildExInDeviceCards());
        content.Children.Add(BuildExInModeAndPaddles());
        content.Children.Add(BuildExInDiagnostics());
        return ScrollPage(content);
    }

    private AvaloniaControl BuildExInCalibrationCard()
    {
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("1.4*,24,*") };
        var controller = new Border
        {
            Height = 360,
            Background = Brush("#0b1220"),
            BorderBrush = Brush("#26364d"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new Viewbox
            {
                Child = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse("M25,15 C12,12 8,20 5,52 C4,64 12,66 21,53 L28,45 72,45 79,53 C88,66 96,64 95,52 C92,20 88,12 75,15 L62,21 38,21Z M33,29 L33,41 M27,35 L39,35 M65,28 L65,33 M76,35 L82,35"),
                    Fill = Brushes.Transparent,
                    Stroke = Brush("#35d7e9"),
                    StrokeThickness = 4,
                    Stretch = Stretch.Uniform
                }
            }
        };
        var controls = new StackPanel { Spacing = 10 };
        controls.Children.Add(SectionTitle("Calibración dinámica"));
        controls.Children.Add(Label("ExInEngine no está integrado en esta compilación Linux.", "#fbbf24", 14, true));
        controls.Children.Add(Caption("Deja el mando quieto y pulsa INICIAR. La imagen muestra el frontal, los dos laterales y los gatillos."));
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*"), RowDefinitions = new RowDefinitions("Auto,8,Auto") };
        foreach ((string text, int col, int row) in new[]
                 {
                     ("INICIAR", 0, 0), ("GUARDAR", 2, 0),
                     ("RESTABLECER", 0, 2), ("CONTROLES DEL SISTEMA", 2, 2)
                 })
        {
            var button = FlatButton(text);
            button.IsEnabled = false;
            Grid.SetColumn(button, col);
            Grid.SetRow(button, row);
            buttons.Children.Add(button);
        }
        controls.Children.Add(buttons);
        controls.Children.Add(Label("COBERTURA MÍNIMA                                        0%", "#a8b7ca", 12, true));
        controls.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 10 });
        AddExInProgressPair(controls, "Stick izquierdo", "Stick derecho");
        AddExInProgressPair(controls, "Gatillo izquierdo", "Gatillo derecho");
        Grid.SetColumn(controller, 0);
        Grid.SetColumn(controls, 2);
        layout.Children.Add(controller);
        layout.Children.Add(controls);
        return Card(layout);
    }

    private static void AddExInProgressPair(StackPanel parent, string left, string right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        foreach ((string label, int column) in new[] { (left, 0), (right, 2) })
        {
            var stack = new StackPanel { Spacing = 5 };
            stack.Children.Add(Caption($"{label} · 0%"));
            stack.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 10 });
            Grid.SetColumn(stack, column);
            grid.Children.Add(stack);
        }
        parent.Children.Add(grid);
    }

    private AvaloniaControl BuildExInDeviceCards()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("0.85*,14,1.15*") };
        var state = new StackPanel { Spacing = 8 };
        state.Children.Add(SectionTitle("Estado de ExInEngine"));
        state.Children.Add(Caption("ExInEngine detenido."));
        state.Children.Add(Caption("Mandos conectados: 0"));
        state.Children.Add(Caption("Reconexión automática"));
        var devices = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*") };
        var phone = new StackPanel { Spacing = 8 };
        phone.Children.Add(SectionTitle("Conexión al teléfono"));
        phone.Children.Add(DisabledSelector("Teléfono no detectado"));
        phone.Children.Add(Caption("Automático: USB primero, después ADB Wi-Fi."));
        var controller = new StackPanel { Spacing = 8 };
        controller.Children.Add(SectionTitle("Mando"));
        controller.Children.Add(DisabledSelector("Sin control físico"));
        controller.Children.Add(new CheckBox
        {
            Content = "Enviar al teléfono solo el mando seleccionado",
            IsEnabled = false,
            Foreground = Brush("#dbe5f1")
        });
        controller.Children.Add(Caption("La identidad, la calibración y los paddles siguen al mando seleccionado."));
        Grid.SetColumn(controller, 2);
        devices.Children.Add(phone);
        devices.Children.Add(controller);
        var deviceCard = Card(devices);
        Grid.SetColumn(deviceCard, 2);
        var stateCard = Card(state);
        Grid.SetColumn(stateCard, 0);
        grid.Children.Add(stateCard);
        grid.Children.Add(deviceCard);
        return grid;
    }

    private static ComboBox DisabledSelector(string value)
    {
        var selector = new ComboBox
        {
            ItemsSource = new[] { value },
            SelectedIndex = 0,
            IsEnabled = false
        };
        StyleSelector(selector);
        return selector;
    }

    private AvaloniaControl BuildExInModeAndPaddles()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*") };
        var mode = new StackPanel { Spacing = 8 };
        mode.Children.Add(SectionTitle("Modo de conexión y control"));
        mode.Children.Add(Caption("El teléfono elegido arriba recibe el modo seleccionado."));
        var modeButtons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
        foreach ((string text, int column) in new[] { ("MODO JUEGO", 0), ("MODO UI", 2) })
        {
            var button = FlatButton(text);
            button.IsEnabled = false;
            Grid.SetColumn(button, column);
            modeButtons.Children.Add(button);
        }
        mode.Children.Add(modeButtons);
        mode.Children.Add(Caption("Sin control físico"));
        mode.Children.Add(Caption("Sensibilidad del joystick para mouse virtual · solo modo UI"));
        mode.Children.Add(new Slider { Minimum = 5, Maximum = 40, Value = 20, IsEnabled = false });
        mode.Children.Add(Caption("20% · UI"));
        mode.Children.Add(Caption("Sensibilidad de los sticks para el mando · solo modo juego"));
        mode.Children.Add(new Slider { Minimum = 25, Maximum = 150, Value = 100, IsEnabled = false });
        mode.Children.Add(Caption("100% · Juego"));

        var paddles = new StackPanel { Spacing = 8 };
        paddles.Children.Add(SectionTitle("Paddles traseros"));
        paddles.Children.Add(Caption("Conecta un mando compatible para configurar P1–P4."));
        var mappings = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
            RowDefinitions = new RowDefinitions("Auto,10,Auto")
        };
        for (int index = 0; index < 4; index++)
        {
            var stack = new StackPanel { Spacing = 5 };
            stack.Children.Add(Caption($"P{index + 1}"));
            stack.Children.Add(DisabledSelector("Sin asignar"));
            Grid.SetColumn(stack, index % 2 == 0 ? 0 : 2);
            Grid.SetRow(stack, index / 2 == 0 ? 0 : 2);
            mappings.Children.Add(stack);
        }
        paddles.Children.Add(mappings);
        paddles.Children.Add(Caption("Cada paddle puede repetir el mismo botón. La asignación se guarda para este mando."));
        var modeCard = Card(mode);
        var paddleCard = Card(paddles);
        Grid.SetColumn(paddleCard, 2);
        grid.Children.Add(modeCard);
        grid.Children.Add(paddleCard);
        return grid;
    }

    private AvaloniaControl BuildExInDiagnostics()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*") };
        var reading = new StackPanel { Spacing = 7 };
        reading.Children.Add(SectionTitle("Lectura del mando"));
        reading.Children.Add(Caption("Sticks, gatillos y botones: sin dispositivo."));
        reading.Children.Add(Caption("Identidad: no disponible."));
        var health = new StackPanel { Spacing = 7 };
        health.Children.Add(SectionTitle("Salud y batería"));
        health.Children.Add(Caption("Diagnóstico no ejecutado"));
        health.Children.Add(Caption("Batería no reportada"));
        var readingCard = Card(reading);
        var healthCard = Card(health);
        Grid.SetColumn(healthCard, 2);
        grid.Children.Add(readingCard);
        grid.Children.Add(healthCard);
        return grid;
    }

    private AvaloniaControl BuildIntegrationPage()
    {
        var content = PageContent();
        AddPageHeading(content, "INTEGRACIÓN", "Integración Android ↔ Linux",
            "Funciones que hacen que Android se comporte como una extensión de NOVORA.");
        var files = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*") };
        var sent = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 16, 0) };
        sent.Children.Add(Caption("Enviados al teléfono"));
        _integrationSentSummary.Text = "0 archivos · 0 B";
        _integrationSentSummary.Foreground = Brush("#60a5fa");
        _integrationSentSummary.FontSize = 18;
        sent.Children.Add(_integrationSentSummary);
        _integrationSentNames.Text = "Aún no hay archivos enviados.";
        _integrationSentNames.Foreground = Brush("#a8b7ca");
        _integrationSentNames.TextWrapping = TextWrapping.Wrap;
        sent.Children.Add(_integrationSentNames);
        var divider = new Border { Background = Brush("#26364d") };
        Grid.SetColumn(divider, 1);
        var received = new StackPanel { Spacing = 6, Margin = new Thickness(16, 0, 0, 0) };
        received.Children.Add(Caption("Recibidos en esta PC"));
        _integrationReceivedSummary.Text = "0 archivos · 0 B";
        _integrationReceivedSummary.Foreground = Brush("#22c55e");
        _integrationReceivedSummary.FontSize = 18;
        received.Children.Add(_integrationReceivedSummary);
        _integrationReceivedNames.Text = "Aún no hay archivos recibidos.";
        _integrationReceivedNames.Foreground = Brush("#a8b7ca");
        _integrationReceivedNames.TextWrapping = TextWrapping.Wrap;
        received.Children.Add(_integrationReceivedNames);
        Grid.SetColumn(received, 2);
        files.Children.Add(sent);
        files.Children.Add(divider);
        files.Children.Add(received);
        content.Children.Add(Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                SectionTitle("Archivos de esta sesión"),
                Caption("El comparativo se actualiza cuando una transferencia termina correctamente."),
                files
            }
        }));

        var cards = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
            RowDefinitions = new RowDefinitions("Auto,12,Auto,12,Auto")
        };
        AddIntegrationCard(cards, "Clipboard", "Android ↔ Linux por eventos, sin historial sensible.", 0, 0);
        var filesCard = new StackPanel { Spacing = 8 };
        filesCard.Children.Add(SectionTitle("Archivos / Share"));
        filesCard.Children.Add(Caption("Suelta aquí archivos o carpetas para enviarlos al teléfono."));
        var receiveFolder = FlatButton("RECIBIR CARPETA NOVORA");
        receiveFolder.IsEnabled = false;
        filesCard.Children.Add(receiveFolder);
        PlaceIntegrationCard(cards, Card(filesCard), 2, 0);
        AddIntegrationCard(cards, "Apps y notificaciones", "Infraestructura de integración aún no disponible en Linux.", 0, 2);
        AddIntegrationCard(cards, "Display / ventanas", "Virtual display, resize y ventanas sólo cuando el backend las soporte.", 2, 2);
        AddIntegrationCard(cards, "Micrófono Android", "La capacidad Android → PC no está disponible en esta compilación.", 0, 4);
        AddIntegrationCard(cards, "Cámara / PC → Android", "Se mostrará cuando exista soporte real; no se simula la función.", 2, 4);
        content.Children.Add(cards);
        content.Children.Add(EngineDetailCard("LinkEngine", "Integración USB y LAN",
            "Relay nativo Linux, CONTROL USB y DATA LAN con TLS fijado al certificado de esta PC. Android inicia LinkEngine desde la sesión autorizada; telemetría y speedtest siguen pendientes."));
        content.Children.Add(EngineDetailCard("VisionEngine", "Integración USB · video",
            "AppControl USB/LAN negocia bitrate, tamaño y FPS; decodificación FFmpeg, presentación SDL y audio. Control remoto, grabación, transferencia de archivos, salida de audio configurable y optimización de perfiles pendientes."));
        content.Children.Add(EngineDetailCard("ExInEngine", "No integrado",
            "Dispositivos de juego, SDL/HID y calibración pendientes."));
        content.Children.Add(EngineDetailCard("Transferencia de archivos", "No integrada",
            "Transferencia y almacenamiento de archivos pendientes."));
        return ScrollPage(content);
    }

    private static void AddIntegrationCard(Grid grid, string title, string description, int column, int row)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(SectionTitle(title));
        stack.Children.Add(Caption(description));
        PlaceIntegrationCard(grid, Card(stack), column, row);
    }

    private static void PlaceIntegrationCard(Grid grid, AvaloniaControl card, int column, int row)
    {
        Grid.SetColumn(card, column);
        Grid.SetRow(card, row);
        grid.Children.Add(card);
    }

    private AvaloniaControl BuildSettingsPage()
    {
        var content = PageContent();
        AddPageHeading(content, "SISTEMA", "Configuración",
            "Sólo opciones globales. Pantalla, red, rendimiento y game input viven en sus propias secciones.");
        var performance = new Grid { ColumnDefinitions = new ColumnDefinitions("*,250") };
        var performanceText = new StackPanel { Spacing = 6 };
        performanceText.Children.Add(SectionTitle("Sistema Android"));
        performanceText.Children.Add(Caption("Rendimiento y salud del teléfono. Se consulta sólo al abrir esta sección."));
        Grid.SetColumn(performanceText, 0);
        performance.Children.Add(performanceText);
        StylePrimaryButton(_openPerformanceButton);
        _openPerformanceButton.Click += (_, _) => ShowPage("Rendimiento");
        Grid.SetColumn(_openPerformanceButton, 1);
        performance.Children.Add(_openPerformanceButton);
        content.Children.Add(Card(performance));

        var lan = new StackPanel { Spacing = 12 };
        lan.Children.Add(SectionTitle("Conexión LAN"));
        lan.Children.Add(Caption("Selecciona la interfaz privada de la PC para generar una invitación de un solo uso compatible con LINK Control 1.4.124."));
        lan.Children.Add(BuildNetworkControls());
        lan.Children.Add(_settingsHostStatus);
        lan.Children.Add(BuildPairingPanel());
        lan.Children.Add(Caption("El estado del teléfono y los controles ADB se muestran en Inicio."));
        content.Children.Add(Card(lan));
        var appearance = new StackPanel { Spacing = 10 };
        appearance.Children.Add(SectionTitle("Tema"));
        appearance.Children.Add(Caption("Inicio predeterminado: Light. Puedes activar Dark durante esta sesión."));
        _themeSelector.ItemsSource = new[] { "Claro · Light", "Oscuro · Dark" };
        _themeSelector.SelectedIndex = LinuxTheme.IsDark ? 1 : 0;
        _themeSelector.MinWidth = 220;
        _themeSelector.SelectionChanged += ThemeSelectionChanged;
        appearance.Children.Add(_themeSelector);
        content.Children.Add(Card(appearance));

        var updates = new StackPanel { Spacing = 8 };
        updates.Children.Add(SectionTitle("Actualizaciones"));
        updates.Children.Add(Caption("La versión Linux no ofrece actualmente descarga e instalación de actualizaciones desde la aplicación."));
        StylePrimaryButton(_checkPcUpdateButton);
        StyleFlatButton(_checkAndroidUpdateButton);
        updates.Children.Add(_checkPcUpdateButton);
        updates.Children.Add(_checkAndroidUpdateButton);
        content.Children.Add(Card(updates));

        var manuals = new StackPanel { Spacing = 8 };
        manuals.Children.Add(SectionTitle("Manual de usuario"));
        manuals.Children.Add(Caption("Español prioritario · English available."));
        StylePrimaryButton(_manualEsButton);
        StylePrimaryButton(_manualEnButton);
        manuals.Children.Add(_manualEsButton);
        manuals.Children.Add(_manualEnButton);
        content.Children.Add(Card(manuals));

        var showQr = FlatButton("Mostrar QR");
        showQr.IsEnabled = false;
        var copyLink = FlatButton("Copiar enlace directo");
        copyLink.IsEnabled = false;
        content.Children.Add(Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                SectionTitle("LINK Control · Android"),
                Caption("LinkControl.apk · Descarga en tu teléfono"),
                Caption("La descarga y el código QR todavía no están integrados en Linux."),
                showQr,
                copyLink
            }
        }));
        var trusted = new StackPanel { Spacing = 12 };
        trusted.Children.Add(SectionTitle("Teléfonos con acceso recordado"));
        trusted.Children.Add(Caption("Puedes revocar el acceso de un dispositivo guardado en esta PC."));
        trusted.Children.Add(BuildTrustedDevicesPanel());
        content.Children.Add(Card(trusted));
        return ScrollPage(content);
    }

    private AvaloniaControl BuildPerformancePage()
    {
        var content = PageContent();
        AddPageHeading(content, "SISTEMA ANDROID", "Sistema Android",
            "Rendimiento y salud del teléfono.");
        var batteryValue = Caption("-- %");
        batteryValue.FontSize = 20;
        batteryValue.Foreground = Brush("#dbe5f1");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("190,14,*,14,*") };
        var battery = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        battery.Children.Add(SectionTitle("Batería"));
        battery.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Width = 140, Height = 14 });
        battery.Children.Add(batteryValue);
        var system = new StackPanel { Spacing = 10 };
        system.Children.Add(SectionTitle("Sistema"));
        system.Children.Add(Caption("Dispositivo: sin datos"));
        system.Children.Add(Caption("Android: sin datos"));
        var metrics = new StackPanel { Spacing = 8 };
        foreach (string label in new[] { "CPU", "RAM", "Temperatura" })
        {
            metrics.Children.Add(SectionTitle(label));
            metrics.Children.Add(Caption("--"));
            metrics.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 10 });
        }
        var batteryCard = Card(battery);
        var systemCard = Card(system);
        var metricsCard = Card(metrics);
        Grid.SetColumn(systemCard, 2);
        Grid.SetColumn(metricsCard, 4);
        grid.Children.Add(batteryCard);
        grid.Children.Add(systemCard);
        grid.Children.Add(metricsCard);
        content.Children.Add(grid);
        var optimization = new Grid { ColumnDefinitions = new ColumnDefinitions("*,220") };
        var text = new StackPanel { Spacing = 6 };
        text.Children.Add(SectionTitle("Optimización segura del dispositivo"));
        text.Children.Add(Caption("Lista para analizar y optimizar cuando tú lo solicites."));
        Grid.SetColumn(text, 0);
        optimization.Children.Add(text);
        var optimize = FlatButton("OPTIMIZAR AHORA");
        optimize.IsEnabled = false;
        StylePrimaryButton(optimize);
        Grid.SetColumn(optimize, 1);
        optimization.Children.Add(optimize);
        content.Children.Add(Card(optimization));
        content.Children.Add(Card(DisabledSection("Métricas del dispositivo",
            "La consulta bajo demanda de batería, CPU, RAM y temperatura requiere la integración de métricas Android.")));
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
        content.Children.Add(Badge(name == "LinkEngine" ? "USB · LAN" : "MOTOR", "#172b45", "#93c5fd"));
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
        string selectedNavigation = page == "Rendimiento" ? "Configuración" : page;
        foreach (Button button in _navigation.Children.OfType<Button>())
        {
            bool active = Equals(button.Tag, selectedNavigation);
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

    private static Grid StatusGrid(params (string Label, TextBlock Value)[] items)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,14,*"),
            RowDefinitions = new RowDefinitions("Auto,12,Auto")
        };
        for (int index = 0; index < items.Length; index++)
        {
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(Caption(items[index].Label));
            items[index].Value.TextWrapping = TextWrapping.Wrap;
            var value=new TextBlock { TextWrapping=TextWrapping.Wrap, Foreground=items[index].Value.Foreground };
            value.Bind(TextBlock.TextProperty,new Avalonia.Data.Binding(nameof(TextBlock.Text)) { Source=items[index].Value });
            stack.Children.Add(value);
            Grid.SetColumn(stack, index % 2 == 0 ? 0 : 2);
            Grid.SetRow(stack, index / 2 == 0 ? 0 : 2);
            grid.Children.Add(stack);
        }
        return grid;
    }

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
        RefreshFunctionalVisionState();
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
