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
using NOVORA.Linux.VisionEngine;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NOVORA.Linux;

public sealed partial class MainWindow : Window
{
    private static readonly string[] Navigation = ["Inicio", "VisionEngine", "LinkEngine", "ExInEngine", "Integración", "Streaming", "Configuración"];
    private static readonly string[] VisionProfiles =
        ["Automático", "Competitivo", "Equilibrado", "Calidad", "Fluidez", "Software", "Móvil extremo", "Móvil ahorro"];
    private static readonly string[] CaptureResolutions =
        ["480", "720", "960", "1080", "1280", "1440", "1600", "1920", "2160"];
    private static readonly string[] CaptureFrameRates =
        NOVORA.Linux.VisionEngine.LinuxVisionSettings.AllFrameRates.Select(value => value.ToString()).ToArray();
    private static readonly string[] CaptureBitrates =
        NOVORA.Linux.VisionEngine.LinuxVisionSettings.AllBitrates;
    private readonly LinuxControlHost _host = new();
    private readonly ComboBox _addresses = new();
    private readonly ComboBox _adbDevices = new();
    private readonly Button _startButton = new();
    private readonly Button _usbButton = new();
    private readonly Button _adbRefreshButton = new() { Content = "Buscar teléfonos" };
    private readonly Button _renewButton = new();
    private readonly TextBlock _hostStatus = new();
    private readonly TextBlock _pairingCode = new();
<<<<<<< Updated upstream
=======
    // Línea visible en LinkEngine con el código de enlace LAN, para no depender de la ventana «Tipo de conexión».
    private readonly TextBlock _linkPairingHint = new();
>>>>>>> Stashed changes
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
<<<<<<< Updated upstream
    private readonly TextBlock _visionMetrics = new();
=======
>>>>>>> Stashed changes
    private readonly Button _visionStopButton = new() { Content = "Detener VisionEngine", IsEnabled = false };
    private readonly TextBlock _homeNextStep = new();
    private readonly Button _homeScreenButton = new() { Content = "VER PANTALLA" };
    private readonly CheckBox _performanceSession = new()
    {
        Content = "Sesión de rendimiento: cierra mediciones en vivo, desactiva red experimental y pausa actualizaciones automáticas",
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
        this.Bind(BackgroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("WindowBrush"));
        FontSize = 15;
        SystemDecorations = SystemDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        LinuxTheme.ApplyIcon(this);

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
        _host.ConnectionChanged += HostOnConnectionChanged;
        _host.ConnectionNotice += HostOnConnectionNotice;
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
        InitializeVisionExchangeVE();
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
            Background = Avalonia.Media.Brushes.Transparent
        };
        shell.Children.Add(BuildBrandBar());
        AvaloniaControl navigation = new Border
        {
            Background = Brush("TitleBarBrush"), BorderBrush=Brush("BorderBrush"),
            BorderThickness=new Thickness(0,0,0,1), Child=BuildNavigation()
        };
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
            Background = Avalonia.Media.Brushes.Transparent,
            BorderBrush = Brush("#26364d"),
            BorderThickness = new Thickness(1),
            Child = shell
        };
    }

    private AvaloniaControl BuildBrandBar()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("220,*,Auto"),
            Background = Brush("TitleBarBrush")
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
            Margin = new Thickness(1, 0, 7, 0)
        };
        _brandEmblem.Width = 58;
        _brandEmblem.Height = 56;
        _brandEmblem.Stretch = Stretch.Uniform;
        brand.Children.Add(_brandEmblem);
        _brandWordmark.Width = 156;
        _brandWordmark.Height = 42;
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
        windowButtons.Children.Add(WindowButton("M0.5,5.5 H9.5", () => WindowState = WindowState.Minimized));
        windowButtons.Children.Add(WindowButton("M0.5,0.5 H9.5 V9.5 H0.5 Z", () => WindowState =
            WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        windowButtons.Children.Add(WindowButton("M0.5,0.5 L9.5,9.5 M9.5,0.5 L0.5,9.5", Close));
        Grid.SetColumn(windowButtons, 2);
        row.Children.Add(windowButtons);
        return row;
    }

    private static Button WindowButton(string geometry, Action action)
    {
        var button = new Button
        {
            Content = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(geometry), Stroke = Brush("TextBrush"), StrokeThickness = 1.2,
                Width = 10, Height = 10, Stretch = Stretch.None
            }
        };
        button.Classes.Add("nv-window");
        button.Click += (_, _) => action();
        return button;
    }

    private AvaloniaControl BuildNavigation()
    {
        _navigation.Orientation = Orientation.Horizontal;
        _navigation.HorizontalAlignment = HorizontalAlignment.Center;
        _navigation.VerticalAlignment = VerticalAlignment.Center;
        _navigation.Spacing = 0;
        _navigation.Background = Brushes.Transparent;
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
                    "Integración" or "Streaming" => 150,
                    _ => 164
                },
                Height = 44,
                Padding = new Thickness(12, 0),
                Background = Brushes.Transparent,
                Foreground = Brush("TextBrush"),
                BorderThickness = new Thickness(0),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            button.Classes.Add("nv-nav");
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
            "Streaming" => "M16,12 A4,4 0 1 1 16,20 A4,4 0 1 1 16,12Z M9,5 A15,15 0 0 0 9,27 M23,5 A15,15 0 0 1 23,27",
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
                    Text = page,
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
            Background = Brush("PanelBrush2"),
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
        "Streaming" => BuildStreamingPage(),
        _ => BuildSettingsPage()
    };

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

    private AvaloniaControl BuildIntegrationPage()
    {
        var content = PageContent();
        AddPageHeading(content, "", "Integración Android ↔ Linux",
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

        var cards = new UniformGrid { Columns=2,Rows=3 };
        AddIntegrationCard(cards, "Clipboard", "Android ↔ Linux con Ctrl+C / Ctrl+V en la ventana de VisionEngine, sin historial sensible.", 0, 0);
        var filesCard = new StackPanel { Spacing = 8 };
        filesCard.Children.Add(SectionTitle("Archivos / Share"));
        filesCard.Children.Add(Caption("Suelta aquí archivos o carpetas para enviarlos al teléfono."));
        var receiveFolder = FlatButton("RECIBIR CARPETA NOVORA");
        var changeFolder = FlatButton("CAMBIAR CARPETA");
        var folderPath = Caption("Carpeta actual: " + _host.VisionSettings.ReceiveDirectory);
        changeFolder.Click += async (_, _) =>
        {
            await ChooseVisionDirectoryAsync(false);
            folderPath.Text = "Carpeta actual: " + _host.VisionSettings.ReceiveDirectory;
        };
        var folderButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        folderButtons.Children.Add(receiveFolder);
        folderButtons.Children.Add(changeFolder);
        filesCard.Children.Add(folderButtons);
        filesCard.Children.Add(folderPath);
        // Channel state and its real cause; restarting capture is the only way to reopen a closed FILES channel.
        _integrationFilesStatus = Caption("Archivos: inicia VisionEngine para enviar o recibir.");
        _integrationRestartButton = FlatButton("REINICIAR CAPTURA");
        _integrationRestartButton.Click += async (_, _) => await RestartCaptureVEAsync();
        filesCard.Children.Add(_integrationFilesStatus);
        filesCard.Children.Add(_integrationRestartButton);
        var filesBorder = Card(filesCard);
        ConfigureIntegrationExchangeVE(filesBorder, receiveFolder);
        PlaceIntegrationCard(cards, filesBorder, 2, 0);
        AddIntegrationCard(cards, "Apps y notificaciones", "Infraestructura de integración aún no disponible en Linux.", 0, 2);
        AddIntegrationCard(cards, "Display / ventanas", "Virtual display, resize y ventanas sólo cuando el backend las soporte.", 2, 2);
        var micStack = new StackPanel { Spacing = 6 };
        micStack.Children.Add(SectionTitle("Micrófono Android"));
        var micCaption = Caption(MicrophoneCardText(_host.VisionSettings));
        var micButton = FlatButton(MicrophoneButtonText(_host.VisionSettings));
        micButton.Click += (_, _) =>
        {
            var current = _host.VisionSettings;
            bool enable = current.AudioSource != LinuxAudioSource.Microphone;
            VeAction(() => _host.ConfigureVisionSettings(enable
                ? current with { AudioSource = LinuxAudioSource.Microphone, AudioEnabled = true }
                : current with { AudioSource = LinuxAudioSource.Output }));
            micCaption.Text = MicrophoneCardText(_host.VisionSettings);
            micButton.Content = MicrophoneButtonText(_host.VisionSettings);
        };
        micStack.Children.Add(micCaption);
        micStack.Children.Add(micButton);
        PlaceIntegrationCard(cards, Card(micStack), 0, 4);
        AddIntegrationCard(cards, "Cámara / PC → Android", "Se mostrará cuando exista soporte real; no se simula la función.", 2, 4);
        content.Children.Add(cards);
        return ScrollPage(content);
    }

    private static string MicrophoneCardText(LinuxVisionSettings settings) => settings.AudioSource == LinuxAudioSource.Microphone
        ? "Activo: el micrófono del teléfono se escucha en esta PC al iniciar VisionEngine. Android debe tener permiso de micrófono."
        : "Desactivado. Al activarlo, el micrófono del teléfono se envía a esta PC al iniciar VisionEngine.";

    private static string MicrophoneButtonText(LinuxVisionSettings settings) =>
        settings.AudioSource == LinuxAudioSource.Microphone ? "DESACTIVAR MICRÓFONO" : "ACTIVAR MICRÓFONO";

    private static void AddIntegrationCard(Panel grid, string title, string description, int column, int row)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(SectionTitle(title));
        stack.Children.Add(Caption(description));
        PlaceIntegrationCard(grid, Card(stack), column, row);
    }

    private static void PlaceIntegrationCard(Panel grid, AvaloniaControl card, int column, int row)
    {
        card.Margin=new Thickness(column==0?0:7,row==0?0:7,column==0?7:0,row==4?0:7);
        grid.Children.Add(card);
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
        if (!string.IsNullOrEmpty(eyebrow)) parent.Children.Add(Label(eyebrow, "BlueBrush", 12, true, new Thickness(0,0,0,5)));
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
        if (title.Split('·')[0].Trim() is "VisionEngine" or "LinkEngine" or "ExInEngine")
        {
            var icon=EngineIcon(title.Split('·')[0].Trim()); icon.Width=38; icon.Height=38; titleRow.Children.Add(icon);
        }
        titleRow.Children.Add(Label(title, "TextBrush", 27, true));
        parent.Children.Add(titleRow);
        parent.Children.Add(Caption(caption, new Thickness(0, 4, 0, 18)));
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

    private AvaloniaControl BuildNetworkControls()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };
        _addresses.MinWidth = 0;
        _addresses.HorizontalAlignment = HorizontalAlignment.Stretch;
        row.Children.Add(_addresses);
        var refresh = FlatButton("Actualizar");
        refresh.Margin = new Thickness(10,0,0,0);
        refresh.Click += (_, _) => RefreshAddresses();
        Grid.SetColumn(refresh,1);
        row.Children.Add(refresh);
        Grid.SetRow(_startButton,1); Grid.SetColumnSpan(_startButton,2);
        _startButton.Margin = new Thickness(0,10,0,0);
        _startButton.HorizontalAlignment = HorizontalAlignment.Left;
        row.Children.Add(_startButton);
        StylePrimaryButton(_startButton);
        return row;
    }

    private AvaloniaControl BuildAdbControls()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };
        _adbDevices.MinWidth = 0;
        _adbDevices.HorizontalAlignment = HorizontalAlignment.Stretch;
        row.Children.Add(_adbDevices);
        StyleFlatButton(_usbButton);
        Grid.SetColumn(_usbButton,1); _usbButton.Margin=new Thickness(10,0,0,0);
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
            button.Classes.Set("nv-selected", active);
            button.ClearValue(BackgroundProperty);
            button.ClearValue(ForegroundProperty);
        }
    }

    private static Border Card(AvaloniaControl child)
    {
        var card = new Border { Margin = new Thickness(0, 0, 0, 14), Child = child };
        card.Classes.Add("nv-card");
        return card;
    }

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
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(10, 5),
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

    private static TextBlock SectionTitle(string text) => new() { Text=text, Foreground=Brush("TextBrush"), FontSize=18, FontWeight=FontWeight.Bold, TextWrapping=TextWrapping.Wrap };

    private static TextBlock Caption(string text, Thickness? margin = null) =>
        new() { Text=text, Foreground=Brush("MutedBrush"), FontSize=15, LineHeight=22, TextWrapping=TextWrapping.Wrap, Margin=margin ?? new Thickness(0) };

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
        button.Classes.Remove("nv-flat");
        button.Classes.Add("nv-primary");
    }

    private static void StyleFlatButton(Button button)
    {
        button.Classes.Remove("nv-primary");
        button.Classes.Add("nv-flat");
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
        string emblemPath = LinuxTheme.GetAssetPath(LinuxTheme.IsDark ? "Emblema NOVORA blanco sobre fondo transparente.png" : "Emblema NOVORA blanco sobre fondo transparente - copia.png");
        string wordmarkPath = LinuxTheme.GetAssetPath(LinuxTheme.IsDark ? "NOVORA-LINK en blanco sobre transparencia.png" : "NOVORA-LINK.png");
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
            // A manual stop keeps LAN off until the user prepares it again.
            _host.AutoConnect?.PauseLan(true);
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
            _host.AutoConnect?.PauseLan(false);
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
            // A manual stop leaves this phone alone until it is unplugged or linked again.
            if (_host.UsbSerial is { } stoppedSerial) _host.AutoConnect?.PauseUsb(stoppedSerial);
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

        if (_adbDevices.SelectedItem is not AdbDevice { State: "device" } device)
        {
            SetMessage("Selecciona un teléfono autorizado en ADB (cable USB o depuración inalámbrica).", "#fbbf24");
            return;
        }

        _usbButton.IsEnabled = false;
        _adbRefreshButton.IsEnabled = false;
        SetMessage("Verificando LINK Control, preparando ADB reverse y abriendo el emparejamiento USB protegido…");
        try
        {
            await _host.StartUsbAsync(device, CancellationToken.None);
            _host.AutoConnect?.PreferUsb(device.Serial);
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
            await _host.StopLanAsync();
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
<<<<<<< Updated upstream
=======
            _linkPairingHint.Text = "";
>>>>>>> Stashed changes
            _fingerprint.Text = "";
            _countdown.Text = "Válido durante 2 minutos";
            _settingsHostStatus.Text = _hostStatus.Text;
            UpdateDeviceStatus();
            UpdateTrustedDevices();
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
<<<<<<< Updated upstream
=======
        _linkPairingHint.Text = seconds > 0
            ? $"Código de enlace LAN: {invitation.Code} · caduca en {seconds / 60:D2}:{seconds % 60:D2}"
            : "El código de enlace LAN caducó. Genera uno nuevo en «Preparar ruta».";
>>>>>>> Stashed changes
        _fingerprint.Text = string.Join(' ', Enumerable.Range(0, invitation.Fingerprint.Length / 4)
            .Select(index => invitation.Fingerprint.Substring(index * 4, 4)));
        UpdateDeviceStatus();
        UpdateTrustedDevices();
    }

    private void UpdateDeviceStatus()
    {
        _usbButton.Content = _host.IsUsbRunning ? "Detener control USB" : "Enlazar por USB";
        // The automatic connector can open or close LAN; keep the manual controls truthful.
        _startButton.Content = _host.IsRunning ? "Detener control LAN" : "Preparar control LAN";
        _renewButton.IsEnabled = _host.IsRunning;
        _hostStatus.Text = _host.LanAddress is { } lanAddress
            ? $"Escuchando en {lanAddress}:{ControlProtocol.Port}."
            : "El control LAN está detenido.";
        _settingsHostStatus.Text = _hostStatus.Text;
        _deviceStatus.Text = _host.SelectedTransport switch
        {
            "USB" when _host.IsUsbAuthorized => $"Android conectado por {(_host.UsbIsWireless ? "ADB Wi-Fi" : "USB")} · {_host.UsbSerial} · TLS confirmado.",
            "USB" when _host.IsUsbRunning => $"ADB reverse listo para {_host.UsbSerial}; esperando respuesta de LINK Control.",
            "USB" => "USB: sin conexión autorizada.",
            "LAN" when _host.IsLanAuthorized => "Android conectado por LAN.",
            "LAN" when _host.IsRunning => "LAN preparada; esperando autorización de LINK Control.",
            "LAN" => "LAN: sin conexión autorizada.",
            _ => _host.ConnectionSummary + " · conecta el cable USB o abre LINK Control en la misma Wi-Fi."
        };
        UpdateHomeConnection();
        UpdateLinkState();
    }

    private void UpdateLinkState()
    {
        // Windows LESessionPresentation texts (MainWindow.LinkSession.cs).
        var linkPresentation = LinkPresentationVE(_host.CurrentLinkEngine);
        _linkState.Text = linkPresentation.State;
        _linkMessage.Text = linkPresentation.Link;
        _linkPageState.Text = linkPresentation.State;
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
<<<<<<< Updated upstream
        NOVORA.Linux.VisionEngine.LinuxVisionTelemetry? telemetry = _host.VisionTelemetry;
        _visionMetrics.Text = telemetry is null
            ? "Renderer SDL: sin datos."
            : $"SDL {telemetry.RendererBackend} · {telemetry.FramesRendered} presentados · " +
              $"{telemetry.VideoPacketsReceived} paquetes · {telemetry.FramesDroppedEstimate} sin presentar aprox. · " +
              $"salida {telemetry.OutputWidth}×{telemetry.OutputHeight}";
        _visionMetrics.Foreground = Brush(
            telemetry?.RendererBackend == "software" ? "#fbbf24" : "#8fa3bd");
=======
>>>>>>> Stashed changes
        _visionStopButton.IsEnabled = _host.HasVisionSession;
        RefreshFunctionalVisionState();
        UpdateHomeConnection();
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
            string? selectedSerial = _host.UsbSerial ?? (_adbDevices.SelectedItem as AdbDevice)?.Serial;
            _adbDevices.ItemsSource = devices;
            _adbDevices.SelectedItem = devices.FirstOrDefault(device => device.Serial == selectedSerial)
                ?? devices.FirstOrDefault(device => device.IsUsb && device.State == "device");
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

    private void HostOnConnectionChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            UpdateDeviceStatus();
            UpdateTrustedDevices();
        });

    private void HostOnConnectionNotice(object? sender, (string Message, bool Warning) notice) =>
        Dispatcher.UIThread.Post(() =>
        {
            SetMessage(notice.Message, notice.Warning ? "#fbbf24" : "#dbe5f1");
            UpdateInvitationDisplay();
            UpdateDeviceStatus();
        });

    /// <summary>Starts dynamic USB/LAN connectivity (called by the application, not by UI tests).</summary>
    internal void StartAutomaticConnection()
    {
        try
        {
            _host.StartAutomaticConnection();
            SetMessage("Conexión automática activa: conecta el cable USB o abre LINK Control en la misma Wi-Fi.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or NetworkInformationException)
        {
            SetMessage($"No se pudo iniciar la conexión automática: {ex.Message}", "#f87171");
        }
        UpdateDeviceStatus();
    }

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
        _host.ConnectionChanged -= HostOnConnectionChanged;
        _host.ConnectionNotice -= HostOnConnectionNotice;
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
