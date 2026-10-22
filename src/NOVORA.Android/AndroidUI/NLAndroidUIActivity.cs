using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Android.Graphics;
using Android.Graphics.Drawables;
using NOVORA.Control;
using NOVORA.AndroidService;
using Android.Content.PM;
using Android.Media.Projection;
using NOVORA.AndroidVideo;
using NOVORA.AndroidApp;

namespace NOVORA.AndroidUI;

[Activity(Name = "com.novora.appcontrol.MainActivity", Label = "APPCONTROL", MainLauncher = true,
    Exported = true, LaunchMode = LaunchMode.SingleTop, Theme = "@android:style/Theme.Material.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
        ConfigChanges.SmallestScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.KeyboardHidden)]
public sealed class NLAndroidUIActivity : Activity
{
    private LinearLayout _body = null!;
    private ScrollView _connectionPage = null!, _dashboardPage = null!, _enginesPage = null!, _exInPage = null!, _filesPage = null!, _settingsPage = null!;
    private ImageView _homeBackground = null!, _settingsBackground = null!;
    private TextView _status = null!;
    private TextView _current = null!;
    private TextView _engineState = null!;
    private Button _videoEngine = null!, _videoRestart = null!, _linkEngine = null!;
    private Button? _tabHome, _tabSettings;
    private readonly List<TextView> _headerStates = [];
    private TextView _homeLinkState = null!, _homeVideoState = null!, _homeExInState = null!;
    private TextView _homePcState = null!, _homePhoneState = null!, _homeTransportState = null!, _homeEngineCount = null!;
    private Button _homeConnectionChip = null!;
    private TextView _flightConnectionState = null!, _flightEngineState = null!, _flightControlState = null!, _flightSummary = null!;
    private Button _flightGame = null!, _flightVideo = null!, _flightInternet = null!, _flightLaunch = null!;
    private string _flightMode = "Game";
    private TextView _engineLinkState = null!, _engineVideoState = null!, _engineExInState = null!;
    private TextView _linkLatency = null!, _linkLoss = null!, _linkThroughput = null!;
    private TextView _videoResolution = null!, _videoFps = null!, _videoBitrate = null!;
    private TextView _engineExInIdentity = null!, _engineExInPolling = null!, _engineExInProcessing = null!, _engineExInJitter = null!;
    private TextView _usbState = null!, _filesStatus = null!;
    private TextView _exInDevice = null!, _exInFamily = null!, _exInConnection = null!;
    private TextView _exInIdentity = null!, _exInCapabilities = null!, _exInDiagnostic = null!, _exInBattery = null!, _exInLive = null!;
    private NLAndroidUIControllerLiveView _exInController = null!;
    private NLAndroidUITriggerCalibrationView _exInTriggerCalibration = null!;
    private Button _exInGameMode = null!, _exInUiMode = null!;
    private TextView _exInHeaderState = null!, _exInCalibrationState = null!;
    private LinearLayout _exInTestSection = null!, _exInCalibrationSection = null!;
    private Button _exInTestTab = null!, _exInCalibrationTab = null!;
    private TextView _phoneAudioState = null!, _internalAudioState = null!;
    private string? _pendingUsbBootstrap;
    private bool _automaticUsbResumed; // NOVORA_AUTOUSB_V1
    private NLControlSessionPhase _lastRenderedPhase = NLControlSessionPhase.Disconnected;
    private Spinner _bitrate = null!, _profile = null!, _audio = null!;
    private TextView _accelerationState = null!;
    private NLAndroidServiceControl? _service;
    private NLAndroidUIServiceConnection? _connection;
    private bool _bound;
    private Func<Task>? _pendingConnect;
    private long _pendingConnectGeneration;
    private const int NotificationPermissionRequest = 215;
    private NLControlSnapshot? _snapshot;
    private NLControlOption[] _bitrates = [], _profiles = [], _outputs = [];
    private readonly List<Button> _actions = [];
    private bool _busy;
    private bool _active;
    private bool _connecting;
    private Button _connect = null!, _search = null!, _scan = null!;
    private CancellationTokenSource? _discovery;
    private NLControlLanPeer? _selectedLanPeer;
    private EditText _lanCodeEntry = null!;
    private LinearLayout _homeActions = null!, _lanCodePanel = null!;
    private int _homeActionColumns;
    private Button _lanPair = null!, _lanSearch = null!;
    private CheckBox _lanRemember = null!, _lanAuto = null!;
    private TextView _lanPeerLabel = null!, _lanFingerprint = null!;
    private CheckBox _lanFingerprintConfirmed = null!;
    private const int VpnRequest = 216;
    private const int VideoProjectionRequest = 217;
    private const int AudioPermissionRequest = 218;
    private readonly NLAndroidVideoProjectionRequest<Intent> _videoProjectionRequest = new();
    private bool _videoProjectionAudioDecisionReady;
    private long? _pcVideoAuthorizationObservedId;
    private long _launchVideoProjectionPromptedGeneration = -1;
    private long _vpnGeneration = -1, _vpnRevision = -1;
    private bool _vpnApproved;
    private bool _startVideoAfterLink;
    private Spinner _resolution = null!, _fps = null!, _monitor = null!, _presentation = null!;
    private TextView _settingsStatus = null!;
    private TextView _settingsLinkState = null!, _settingsExInState = null!;
    private TextView _linkMonitorState = null!, _linkInternetTestState = null!, _linkTunnelTestState = null!;
    private Button _linkMonitorButton = null!;
    private NLControlOption[] _resolutions = [], _frameRates = [], _monitors = [], _presentationModes = [];
    private Button _remember = null!, _rememberSettings = null!, _savedPcsSettings = null!, _restart = null!, _cancel = null!, _capture = null!, _record = null!, _sendFiles = null!;
    private readonly Dictionary<Spinner, string> _drafts = new();
    private readonly Dictionary<Spinner, string> _confirmed = new();
    private bool _populating;
    private bool _returnToSettings;
    private long _uiGeneration = -1;
    private string? _requestedPage;
    private long _requestedGeneration = -1, _requestedRevision = -1;
    private string? _uiServiceId;
    private long _lastControllerMoveMs;
    private NLAndroidUIPalette Palette => NLAndroidUITheme.Current(this);

    protected override void AttachBaseContext(Context? newBase)
    {
        base.AttachBaseContext(newBase is null ? null : NLAndroidUITheme.ApplyTo(newBase));
    }

    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        _requestedPage = Intent?.GetStringExtra("novora.page");
        _requestedGeneration = Intent?.GetLongExtra("novora.generation", -1) ?? -1;
        _requestedRevision = Intent?.GetLongExtra("novora.revision", -1) ?? -1;
        Intent?.RemoveExtra("novora.page");
        _pendingUsbBootstrap = NLAndroidServiceUsbBootstrap.Take();
        Intent?.RemoveExtra("novora.usb"); // Never trust externally supplied launcher extras.
        Window?.SetSoftInputMode(SoftInput.AdjustResize);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Color.ParseColor(Palette.Background));
        root.SetOnApplyWindowInsetsListener(new NLAndroidUIInsets());
        SetContentView(root);
        var pages = new FrameLayout(this);
        root.AddView(pages, new LinearLayout.LayoutParams(-1, 0, 1));
        _status = new TextView(this) { Text = "Preparando conexión…", TextSize = 12 };
        _status.SetTextColor(Color.ParseColor(Palette.Muted));
        _status.SetPadding(Dp(20), Dp(8), Dp(20), Dp(10));
        _status.AccessibilityLiveRegion = AccessibilityLiveRegion.Polite;
        root.AddView(_status);

        _dashboardPage = CreateHomePage(pages);
        _connectionPage = _dashboardPage;

        _enginesPage = CreateEnginesPage(pages);
        _exInPage = CreatePage(pages);

        _filesPage = CreateFilesPage(pages);

        _settingsBackground = CreateBackground(pages,
            Resource.Drawable.settings_background_portrait,
            Resource.Drawable.settings_background_landscape);
        _settingsPage = NLAndroidUIMainShell.CreatePage(this, pages, out _body, 1200);
        PageHeader();
        _restart = Button("Guardar ajustes", ConfirmRestartAsync);
        _settingsStatus = Muted("Guarda los ajustes para aplicarlos en PC y volver a Inicio.");
        Label("Configuración general", 26);
        Muted("Control de NOVORA PC · valores confirmados por Windows");
        var settingsBody = _body;
        var settingsSections = new LinearLayout(this) { Orientation = Orientation.Vertical };
        settingsBody.AddView(settingsSections, new LinearLayout.LayoutParams(-1, -2));
        var linkSection = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var videoSection = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var audioSection = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var exInSection = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _body = linkSection;
        _body.AddView(NLAndroidUIComponents.SectionTitle(this, "LinkEngine"));
        Card(() => {
            Label("Internet y transporte", 15);
            Muted("LinkEngine usa la conexión USB o LAN activa. Android solicitará permiso VPN al iniciarlo; este motor no expone parámetros editables adicionales.");
            _settingsLinkState = StatusLine("Estado", "Sin conexión");
            Label("Medición de LinkEngine", 15);
            Muted("Tráfico real que cruza el VPN de LE. TX: teléfono → PC; RX: PC → teléfono. Se guarda solo durante una medición iniciada aquí.");
            _linkMonitorState = Muted("Monitoreo detenido · sin datos actuales.");
            _linkMonitorButton = Button("Iniciar monitoreo en vivo", ToggleLinkMonitorAsync);
            _linkInternetTestState = Muted("Internet por LE: sin prueba. Se mide contra Cloudflare desde el navegador del teléfono y se guarda en NOVORA PC si termina correctamente.");
            Button("Abrir SpeedTest de Internet", OpenLinkInternetTestAsync);
            _linkTunnelTestState = Muted("Túnel PC ↔ Android: sin prueba. Se mide contra un servidor local de NOVORA PC.");
            Button("Medir túnel PC ↔ Android", OpenLinkTunnelTestAsync);
            Muted("El monitor muestra tráfico usado; la prueba del túnel mide capacidad hacia la PC. Su latencia HTTP no equivale al ping de un juego.");
            _rememberSettings = Button("Recordar esta PC", RememberPcAsync);
            _rememberSettings.Visibility = ViewStates.Gone;
            _savedPcsSettings = Button("PC guardadas", SavedPcsAsync);
            _savedPcsSettings.Visibility = ViewStates.Gone;
        });
        _body = videoSection;
        _body.AddView(NLAndroidUIVisionSettingsPage.VideoSection(this));
        Card(() => {
            Label("VIDEO", 15);
            _monitor = SettingRow("Monitor del PC");
            _profile = SettingRow("Perfil de VisionEngine y NVIDIA");
            Muted("Los perfiles distintos de Software permiten a VisionEngine solicitar NVIDIA. El estado de aceleración confirma si NVDEC quedó activo.");
            _accelerationState = Muted("Aceleración: esperando estado de VisionEngine.");
            _bitrate = SettingRow("Bitrate");
            _resolution = SettingRow("Resolución");
            _fps = SettingRow("Límite de FPS");
            _presentation = SettingRow("Ventana de VisionEngine");
        });
        _body = audioSection;
        Card(() => {
            Label("AUDIO", 15);
            _audio = SettingRow("Salida de audio en PC");
            Muted("La salida se aplica junto con el resto de ajustes de VisionEngine.");
        });
        _body = exInSection;
        _body.AddView(NLAndroidUIComponents.SectionTitle(this, "ExInEngine"));
        Card(() => {
            _settingsExInState = StatusLine("Estado", "No detectado");
            Muted("La calibración y los controles de ExInEngine se manejan solo en NOVORA PC.");
        });
        _body = settingsBody;
        settingsBody.AddView(NLAndroidUIVisionSettingsPage.ApplyHint(this));
        bool? twoColumns = null;
        settingsSections.LayoutChange += (_, args) => {
            bool wide = args.Right - args.Left >= Dp(720);
            if (twoColumns == wide) return;
            twoColumns = wide;
            foreach (var section in new[] { linkSection, videoSection, audioSection, exInSection })
                (section.Parent as Android.Views.ViewGroup)?.RemoveView(section);
            settingsSections.RemoveAllViews();
            settingsSections.Orientation = wide ? Orientation.Horizontal : Orientation.Vertical;
            if (wide)
            {
                var left = new LinearLayout(this) { Orientation = Orientation.Vertical };
                var right = new LinearLayout(this) { Orientation = Orientation.Vertical };
                settingsSections.AddView(left, new LinearLayout.LayoutParams(0, -2, 1));
                settingsSections.AddView(right, new LinearLayout.LayoutParams(0, -2, 1));
                left.SetPadding(0, 0, Dp(8), 0);
                right.SetPadding(Dp(8), 0, 0, 0);
                left.AddView(linkSection);
                left.AddView(videoSection);
                right.AddView(audioSection);
                right.AddView(exInSection);
            }
            else
                foreach (var section in new[] { linkSection, videoSection, audioSection, exInSection })
                    settingsSections.AddView(section);
        };
        BuildTabs(root);
        ShowPage(_dashboardPage);
        EnableActions(false);
        _returnToSettings = state?.GetBoolean("settings") == true;
        foreach (var (spinner, key) in new[] { (_profile,"profile"), (_bitrate,"bitrate"), (_audio,"audio"), (_resolution,"resolution"), (_fps,"fps"), (_monitor,"monitor"), (_presentation,"presentation") })
            if (state?.GetString("draft." + key) is { } value) _drafts[spinner] = value;
        _uiGeneration = state?.GetLong("generation", -1) ?? -1;
        _uiServiceId = state?.GetString("serviceId");
        _vpnGeneration = state?.GetLong("vpnGeneration", -1) ?? -1;
        _vpnRevision = state?.GetLong("vpnRevision", -1) ?? -1;
    }

    private void PageHeader()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        _body.AddView(row, new LinearLayout.LayoutParams(-1, -2));
        var parent = _body;
        var labels = new LinearLayout(this) { Orientation = Orientation.Vertical };
        row.AddView(labels, new LinearLayout.LayoutParams(0, -2, 1));
        _body = labels;
        var title = Label("APPCONTROL", 20);
        title.SetTypeface(null, TypefaceStyle.Bold);
        title.SetPadding(0, Dp(2), 0, 0);
        string version = PackageManager?.GetPackageInfo(PackageName!, PackageInfoFlags.MatchAll)?.VersionName ?? "desconocida";
        var product = Muted($"ANDROID · APPCONTROL · v{version}");
        product.TextSize = 12;
        var state = Muted("●  USB OFF  ·  0/3 Engines activos");
        state.TextSize = 12;
        state.SetTextColor(Color.ParseColor(Palette.Error));
        _headerStates.Add(state);
        _body = parent;
        var theme = new Button(this) { Text = "Tema", TextSize = 12, ContentDescription = "Cambiar tema oscuro o claro" };
        theme.SetAllCaps(false); theme.SetPadding(0, 0, 0, 0);
        theme.Background = NLAndroidUIVisual.Surface(this, Palette.Surface, Palette.Border, 8);
        theme.SetTextColor(Color.ParseColor(Palette.Accent));
        theme.Click += (_, _) => { NLAndroidUITheme.Toggle(this); Recreate(); };
        row.AddView(theme, new LinearLayout.LayoutParams(Dp(56), Dp(48)));
    }
    private ScrollView CreateHomePage(FrameLayout host)
    {
        _homeBackground = CreateBackground(host,
            Resource.Drawable.home_background_portrait,
            Resource.Drawable.home_background_landscape);
        var page = (ScrollView)LayoutInflater.Inflate(Resource.Layout.nl_android_home, host, false)!;
        host.AddView(page, new FrameLayout.LayoutParams(-1, -1));
        _homeActionColumns = 0;
        page.LayoutChange += (_, _) => LayoutHomeActions(page);
        LayoutHomeActions(page);
        BindStandardHeader(page);
        page.FindViewById<Button>(Resource.Id.header_theme)!.Visibility = ViewStates.Gone;

        _homeConnectionChip = page.FindViewById<Button>(Resource.Id.home_connection_chip)!;
        _homePcState = page.FindViewById<TextView>(Resource.Id.home_pc_state)!;
        _homePhoneState = page.FindViewById<TextView>(Resource.Id.home_phone_state)!;
        _homeTransportState = page.FindViewById<TextView>(Resource.Id.home_transport_state)!;
        _homeEngineCount = page.FindViewById<TextView>(Resource.Id.home_engine_count)!;
        _homeLinkState = page.FindViewById<TextView>(Resource.Id.home_link_state)!;
        _homeVideoState = page.FindViewById<TextView>(Resource.Id.home_video_state)!;
        _homeExInState = page.FindViewById<TextView>(Resource.Id.home_exin_state)!;
        _flightGame = page.FindViewById<Button>(Resource.Id.home_mode_game)!;
        _flightVideo = page.FindViewById<Button>(Resource.Id.home_mode_video)!;
        _flightInternet = page.FindViewById<Button>(Resource.Id.home_mode_internet)!;
        _flightConnectionState = page.FindViewById<TextView>(Resource.Id.home_flight_connection)!;
        _flightEngineState = page.FindViewById<TextView>(Resource.Id.home_flight_engine)!;
        _flightControlState = page.FindViewById<TextView>(Resource.Id.home_flight_control)!;
        _flightSummary = page.FindViewById<TextView>(Resource.Id.home_flight_summary)!;
        _flightLaunch = page.FindViewById<Button>(Resource.Id.primary_start)!;

        _homePhoneState.Text = LocalDeviceName();
        _usbState = _homeTransportState;
        _current = page.FindViewById<TextView>(Resource.Id.connect_pc_state)!;
        _connect = page.FindViewById<Button>(Resource.Id.primary_connect_usb)!;
        _cancel = page.FindViewById<Button>(Resource.Id.connect_disconnect)!;
        _scan = page.FindViewById<Button>(Resource.Id.connect_pair)!;
        _search = page.FindViewById<Button>(Resource.Id.primary_connect_lan)!;
        _homeActions = page.FindViewById<LinearLayout>(Resource.Id.home_primary_actions)!;
        _lanCodePanel = page.FindViewById<LinearLayout>(Resource.Id.primary_lan_code_panel)!;
        _lanPair = page.FindViewById<Button>(Resource.Id.primary_lan_pair)!;
        _lanSearch = page.FindViewById<Button>(Resource.Id.primary_lan_search)!;
        _lanRemember = page.FindViewById<CheckBox>(Resource.Id.primary_lan_remember)!;
        _lanAuto = page.FindViewById<CheckBox>(Resource.Id.primary_lan_auto)!;
        _lanCodeEntry = page.FindViewById<EditText>(Resource.Id.primary_lan_code)!;
        _lanPeerLabel = page.FindViewById<TextView>(Resource.Id.primary_lan_peer)!;
        _lanFingerprint = page.FindViewById<TextView>(Resource.Id.primary_lan_fingerprint)!;
        _lanFingerprintConfirmed = page.FindViewById<CheckBox>(Resource.Id.primary_lan_confirm_fingerprint)!;
        _lanFingerprint.SetTextIsSelectable(true);
        _lanCodeEntry.TextChanged += (_, _) => UpdateLanStartButton();
        BindAsync(_connect, StartUsbAsync, true);
        BindAsync(_cancel, DisconnectAsync);
        BindAsync(_scan, PairWithCodeAsync, true);
        BindAsync(_search, ConnectLanAsync);
        page.FindViewById<Button>(Resource.Id.primary_enter_code)!.Click += (_, _) => ShowLanCodePage(true);
        BindAsync(_lanSearch, SearchAsync);
        BindAsync(_lanPair, PairFromLanPageAsync);
        page.FindViewById<Button>(Resource.Id.primary_lan_back)!.Click += (_, _) => ShowLanCodePage(false);
        _lanAuto.CheckedChange += (_, _) => { if (_lanAuto.Checked) _lanRemember.Checked = true; };
        _lanRemember.CheckedChange += (_, _) => { if (!_lanRemember.Checked) _lanAuto.Checked = false; };
        BindAsync(page.FindViewById<Button>(Resource.Id.connect_saved)!, SavedPcsAsync);
        _remember = page.FindViewById<Button>(Resource.Id.home_remember_pc)!;
        BindAsync(_remember, RememberPcAsync);
        BindAsync(page.FindViewById<Button>(Resource.Id.connect_detect_engines)!, () => SendAsync("get"));
        _homeConnectionChip.Click += (_, _) => ScrollToConnectionOptions(page);
        page.FindViewById<Button>(Resource.Id.home_open_engines)!.Click += (_, _) => ShowPage(_settingsPage);
        page.FindViewById<Button>(Resource.Id.home_engine_diagnostics)!.Click += (_, _) => ShowPage(_settingsPage);

        BindHomeMode(_flightGame, "Game");
        BindHomeMode(_flightVideo, "Video");
        BindHomeMode(_flightInternet, "Internet");
        _flightLaunch.Click += async (_, _) => {
            try { await LaunchMotorsAsync(); }
            catch (Exception) { _status.Text = "No se completó la operación. Revisa la sesión y su estado en PC."; }
        };
        NLAndroidUIVisual.Button(_flightLaunch, true);
        UpdateFlightMode();
        return page;
    }

    private ImageView CreateBackground(FrameLayout host, int portraitResource, int landscapeResource)
    {
        var image = new ImageView(this);
        image.SetScaleType(ImageView.ScaleType.Matrix);
        bool? landscape = null;
        image.LayoutChange += (_, _) => {
            bool wide = image.Width > image.Height;
            if (landscape != wide)
            {
                landscape = wide;
                image.SetImageResource(wide ? landscapeResource : portraitResource);
            }
            var drawable = image.Drawable;
            if (image.Width <= 0 || image.Height <= 0 || drawable is null ||
                drawable.IntrinsicWidth <= 0 || drawable.IntrinsicHeight <= 0) return;
            float scale = Math.Max((float)image.Width / drawable.IntrinsicWidth,
                (float)image.Height / drawable.IntrinsicHeight);
            var matrix = new Matrix();
            matrix.SetScale(scale, scale);
            matrix.PostTranslate((image.Width - drawable.IntrinsicWidth * scale) / 2f, 0);
            image.ImageMatrix = matrix;
        };
        host.AddView(image, new FrameLayout.LayoutParams(-1, -1));
        return image;
    }

    private void LayoutHomeActions(ScrollView page)
    {
        var content = page.FindViewById<LinearLayout>(Resource.Id.home_content);
        var actions = page.FindViewById<LinearLayout>(Resource.Id.home_primary_actions);
        if (content is null || actions is null) return;
        if (page.Width <= 0 || page.Height <= 0) return;

        int availableWidth = page.Width - content.PaddingLeft - content.PaddingRight;
        bool landscape = page.Width > page.Height;
        int columns = !landscape || availableWidth < Dp(480) ? 1
            : availableWidth < Dp(1040) ? 2 : 4;
        if (_homeActionColumns != columns)
        {
            var buttons = new[] { Resource.Id.primary_connect_usb, Resource.Id.primary_connect_lan,
                Resource.Id.primary_enter_code, Resource.Id.primary_start }
                .Select(id => page.FindViewById<Button>(id)!).ToArray();
            foreach (Button button in buttons)
                (button.Parent as Android.Views.ViewGroup)?.RemoveView(button);
            actions.RemoveAllViews();
            actions.Orientation = Orientation.Vertical;
            for (int start = 0; start < buttons.Length; start += columns)
            {
                var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
                for (int index = start; index < Math.Min(start + columns, buttons.Length); index++)
                {
                    Button button = buttons[index];
                    button.SetMinHeight(Dp(56));
                    var parameters = new LinearLayout.LayoutParams(0, -2, 1);
                    if (index > start) parameters.LeftMargin = Dp(4);
                    row.AddView(button, parameters);
                }
                var rowParameters = new LinearLayout.LayoutParams(-1, -2);
                if (start > 0) rowParameters.TopMargin = Dp(4);
                actions.AddView(row, rowParameters);
            }
            _homeActionColumns = columns;
        }
        int top = landscape
            ? Math.Min(page.Height / 8, Dp(48))
            : Math.Min(page.Height / 4, Dp(180));
        top = Math.Min(top, Math.Max(0, page.Height - Dp(160)));
        if (content.PaddingTop != top)
            content.SetPadding(content.PaddingLeft, top, content.PaddingRight, content.PaddingBottom);
    }

    private void ScrollToConnectionOptions(ScrollView page)
    {
        var target = page.FindViewById(Resource.Id.connect_usb);
        if (target is null) return;

        var targetLocation = new int[2];
        var pageLocation = new int[2];
        target.GetLocationOnScreen(targetLocation);
        page.GetLocationOnScreen(pageLocation);
        int scrollY = Math.Max(0, page.ScrollY + targetLocation[1] - pageLocation[1]);
        page.SmoothScrollTo(0, scrollY);
    }

    private void BindAsync(Button button, Func<Task> action, bool primary = false)
    {
        NLAndroidUIVisual.Button(button, primary);
        button.Click += async (_, _) => {
            try { await action(); }
            catch (Exception) { _status.Text = "No se completó la operación. Revisa la sesión y su estado en PC."; }
        };
    }

    private void BindHomeMode(Button button, string mode)
    {
        button.Click += (_, _) => {
            _flightMode = mode;
            UpdateFlightMode();
            UpdateVisualSummary(_snapshot);
        };
    }

    private ScrollView CreateFilesPage(FrameLayout host)
    {
        var page = InflatePage(Resource.Layout.nl_android_files, host);
        BindStandardHeader(page);
        _filesStatus = page.FindViewById<TextView>(Resource.Id.files_status)!;
        _sendFiles = page.FindViewById<Button>(Resource.Id.files_sent)!;
        _record = page.FindViewById<Button>(Resource.Id.files_record)!;
        _capture = page.FindViewById<Button>(Resource.Id.files_capture)!;
        _phoneAudioState = page.FindViewById<TextView>(Resource.Id.files_phone_audio)!;
        _internalAudioState = page.FindViewById<TextView>(Resource.Id.files_internal_audio)!;
        BindAsync(page.FindViewById<Button>(Resource.Id.files_received)!, OpenFilesAsync, true);
        BindAsync(_sendFiles, () => { StartActivity(new Intent(this, typeof(NLAndroidUIShareActivity))); return Task.CompletedTask; });
        BindAsync(_record, () => SendAsync(_snapshot?.Media?.Recording == true ? "stopRecording" : "startRecording"), true);
        BindAsync(_capture, () => SendAsync("capture"));
        return page;
    }

    private ScrollView CreateEnginesPage(FrameLayout host)
    {
        var page = InflatePage(Resource.Layout.nl_android_engines, host);
        BindStandardHeader(page);
        _engineVideoState = page.FindViewById<TextView>(Resource.Id.engines_video_state)!;
        _engineLinkState = page.FindViewById<TextView>(Resource.Id.engines_link_state)!;
        _engineExInState = page.FindViewById<TextView>(Resource.Id.engines_exin_state)!;
        _engineState = page.FindViewById<TextView>(Resource.Id.engines_summary)!;
        _linkLatency = page.FindViewById<TextView>(Resource.Id.engines_link_latency)!;
        _linkLoss = page.FindViewById<TextView>(Resource.Id.engines_link_loss)!;
        _linkThroughput = page.FindViewById<TextView>(Resource.Id.engines_link_throughput)!;
        _videoResolution = page.FindViewById<TextView>(Resource.Id.engines_video_resolution)!;
        _videoFps = page.FindViewById<TextView>(Resource.Id.engines_video_fps)!;
        _videoBitrate = page.FindViewById<TextView>(Resource.Id.engines_video_bitrate)!;
        _engineExInIdentity = page.FindViewById<TextView>(Resource.Id.engines_exin_identity)!;
        _engineExInPolling = page.FindViewById<TextView>(Resource.Id.engines_exin_polling)!;
        _engineExInProcessing = page.FindViewById<TextView>(Resource.Id.engines_exin_processing)!;
        _engineExInJitter = page.FindViewById<TextView>(Resource.Id.engines_exin_jitter)!;
        _videoEngine = page.FindViewById<Button>(Resource.Id.engines_toggle_video)!;
        _videoRestart = page.FindViewById<Button>(Resource.Id.engines_restart_video)!;
        _linkEngine = page.FindViewById<Button>(Resource.Id.engines_toggle_link)!;
        BindAsync(_videoEngine, ToggleVideoEngineAsync);
        BindAsync(_videoRestart, RestartVideoEngineAsync);
        BindAsync(_linkEngine, ToggleLinkEngineAsync);
        BindAsync(page.FindViewById<Button>(Resource.Id.engines_detect)!, () => SendAsync("get"));
        page.FindViewById<Button>(Resource.Id.engines_settings)!.Click += (_, _) => ShowPage(_settingsPage);
        page.FindViewById<Button>(Resource.Id.engines_exin)!.Visibility = ViewStates.Gone;
        return page;
    }

    private ScrollView InflatePage(int layout, FrameLayout host)
    {
        var page = (ScrollView)LayoutInflater.Inflate(layout, host, false)!;
        host.AddView(page, new FrameLayout.LayoutParams(-1, -1));
        return page;
    }

    private void BindStandardHeader(View page)
    {
        string version = PackageManager?.GetPackageInfo(PackageName!, PackageInfoFlags.MatchAll)?.VersionName ?? "desconocida";
        page.FindViewById<TextView>(Resource.Id.header_product)!.Text = $"ANDROID · APPCONTROL · v{version}";
        var state = page.FindViewById<TextView>(Resource.Id.header_state)!;
        _headerStates.Add(state);
        var theme = page.FindViewById<Button>(Resource.Id.header_theme)!;
        theme.Click += (_, _) => { NLAndroidUITheme.Toggle(this); Recreate(); };
    }

    private static string LocalDeviceName()
    {
        string manufacturer = Build.Manufacturer?.Trim() ?? string.Empty;
        string model = Build.Model?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(manufacturer)) return string.IsNullOrWhiteSpace(model) ? "Android" : model;
        if (string.IsNullOrWhiteSpace(model)) return manufacturer;
        if (model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase)) return model;

        return $"{char.ToUpperInvariant(manufacturer[0])}{manufacturer[1..]} {model}";
    }
    private void UpdateFlightMode()
    {
        foreach (var (button, mode) in new[] { (_flightGame, "Game"), (_flightVideo, "Video"), (_flightInternet, "Internet") })
        {
            button.Selected = _flightMode == mode;
            NLAndroidUIVisual.Button(button, _flightMode == mode);
        }
        if (_flightEngineState is null) return;
        _flightEngineState.ContentDescription = _flightMode == "Internet" ? "LinkEngine" : "VisionEngine";
        _flightControlState.Visibility = _flightMode == "Game" ? ViewStates.Visible : ViewStates.Gone;
    }
    private async Task LaunchMotorsAsync()
    {
        if (_service?.Session.Current.Phase != NLControlSessionPhase.Connected || _snapshot is null)
        {
            _status.Text = "Primero conecta este teléfono con NOVORA PC por USB o LAN.";
            return;
        }

        if (_snapshot.Engines?.LinkCanStart == true &&
            (_snapshot.Engines.LinkRunning != true || _snapshot.Engines.LinkCanTakeOver))
        {
            _startVideoAfterLink = _snapshot.VideoRunning != true && _snapshot.Engines.VideoCanStart;
            await RequestVpnAsync();
            return;
        }

        if (_snapshot.VideoRunning != true && _snapshot.Engines?.VideoCanStart == true)
        {
            await RequestVideoProjectionAsync(_service.Session.Current.UsesNativeVeLanVideo);
            return;
        }

        _status.Text = _snapshot.VideoRunning == true || _snapshot.Engines?.LinkRunning == true
            ? "Los motores disponibles ya están activos."
            : _snapshot.Engines?.VideoMessage ?? "Los motores todavía no están listos en NOVORA PC.";
    }
    private async Task StartUsbAsync()
    {
        if (_service is null || !_active) return;
        if (_service.Session.Current is { Phase: NLControlSessionPhase.Connected, Transport: "USB" })
        {
            await LaunchMotorsAsync();
            return;
        }
        var service = _service;
        await RequestConnectAsync(async () =>
        {
            await service.ConnectUsbAsync();
            if (!_active || !ReferenceEquals(service, _service) ||
                service.Session.Current is not { Phase: NLControlSessionPhase.Connected, Transport: "USB" }) return;
            RenderSession();
            await LaunchMotorsAsync();
        });
    }
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is not null && IsControllerEvent(e.Source))
        {
            if (e.Action != KeyEventActions.Down) return base.DispatchKeyEvent(e);
            bool handled = e.KeyCode switch
            {
                Keycode.ButtonA or Keycode.ButtonX or Keycode.Enter or Keycode.DpadCenter => ActivateFocusedControl(),
                Keycode.ButtonB or Keycode.Back => NavigateBackFromController(),
                Keycode.ButtonStart => ShowDashboardFromController(),
                Keycode.ButtonL1 => FocusDirection(FocusSearchDirection.Left),
                Keycode.ButtonR1 => FocusDirection(FocusSearchDirection.Right),
                Keycode.DpadUp => FocusDirection(FocusSearchDirection.Up),
                Keycode.DpadDown => FocusDirection(FocusSearchDirection.Down),
                Keycode.DpadLeft => FocusDirection(FocusSearchDirection.Left),
                Keycode.DpadRight => FocusDirection(FocusSearchDirection.Right),
                _ => false
            };
            if (handled) return true;
        }

        return base.DispatchKeyEvent(e);
    }

    public override bool DispatchGenericMotionEvent(MotionEvent? e)
    {
        if (e is not null &&
            e.Action == MotionEventActions.Move &&
            IsControllerEvent(e.Source) &&
            HandleControllerAxis(e))
        {
            return true;
        }

        return base.DispatchGenericMotionEvent(e);
    }

    private static bool IsControllerEvent(InputSourceType source)
        => (source & InputSourceType.Gamepad) == InputSourceType.Gamepad ||
           (source & InputSourceType.Joystick) == InputSourceType.Joystick ||
           (source & InputSourceType.Dpad) == InputSourceType.Dpad;

    private bool HandleControllerAxis(MotionEvent e)
    {
        long now = SystemClock.ElapsedRealtime();
        if (now - _lastControllerMoveMs < 180) return false;

        float x = ControllerAxis(e, Axis.X);
        float y = ControllerAxis(e, Axis.Y);
        if (Math.Abs(x) < 0.55f && Math.Abs(y) < 0.55f) return false;

        _lastControllerMoveMs = now;
        return Math.Abs(x) > Math.Abs(y)
            ? FocusDirection(x > 0 ? FocusSearchDirection.Right : FocusSearchDirection.Left)
            : FocusDirection(y > 0 ? FocusSearchDirection.Down : FocusSearchDirection.Up);
    }

    private static float ControllerAxis(MotionEvent e, Axis axis)
    {
        float value = e.GetAxisValue(axis);
        if (Math.Abs(value) >= 0.1f) return value;
        return axis == Axis.X ? e.GetAxisValue(Axis.HatX) : e.GetAxisValue(Axis.HatY);
    }

    private bool FocusDirection(FocusSearchDirection direction)
    {
        View? current = CurrentFocus ?? Window?.DecorView?.FindFocus();
        View? next = current?.FocusSearch(direction) ?? Window?.DecorView?.FocusSearch(direction);
        if (next is null || !next.Focusable || !next.Enabled) return false;
        next.RequestFocus();
        return true;
    }

    private bool ActivateFocusedControl()
    {
        View? focused = CurrentFocus ?? Window?.DecorView?.FindFocus();
        if (focused is null || !focused.Enabled) return false;
        return focused.PerformClick();
    }

    private bool ShowDashboardFromController()
    {
        ShowPage(_dashboardPage);
        return true;
    }

    private bool NavigateBackFromController()
    {
        if (ReferenceEquals(_exInPage, VisiblePage()) || ReferenceEquals(_settingsPage, VisiblePage()))
        {
            ShowPage(_dashboardPage);
            return true;
        }

        if (!ReferenceEquals(_dashboardPage, VisiblePage()))
        {
            ShowPage(_dashboardPage);
            return true;
        }

        return false;
    }

    private ScrollView? VisiblePage()
    {
        foreach (ScrollView page in new[] { _dashboardPage, _enginesPage, _exInPage, _filesPage, _settingsPage })
            if (page.Visibility == ViewStates.Visible) return page;
        return null;
    }
    private TextView EngineStatusRow(string title, string subtitle)
    {
        var parent = _body;
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetPadding(0, Dp(7), 0, Dp(7));
        parent.AddView(row);
        var labels = new LinearLayout(this) { Orientation = Orientation.Vertical };
        row.AddView(labels, new LinearLayout.LayoutParams(0, -2, 1));
        var name = new TextView(this) { Text = title, TextSize = 13 };
        name.SetTypeface(null, TypefaceStyle.Bold); name.SetTextColor(Color.ParseColor(Palette.Text)); labels.AddView(name);
        var detail = new TextView(this) { Text = subtitle, TextSize = 12 };
        detail.SetTextColor(Color.ParseColor(Palette.Muted)); labels.AddView(detail);
        var state = new TextView(this) { Text = "No detectado", TextSize = 13, Gravity = GravityFlags.Right | GravityFlags.CenterVertical };
        state.SetTypeface(null, TypefaceStyle.Bold); state.SetTextColor(Color.ParseColor(Palette.Error));
        row.AddView(state, new LinearLayout.LayoutParams(Dp(112), -1));
        return state;
    }
    private TextView StatusLine(string title, string value)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetPadding(0, Dp(7), 0, Dp(7)); _body.AddView(row);
        var label = new TextView(this) { Text = title, TextSize = 13 };
        label.SetTextColor(Color.ParseColor(Palette.Text)); row.AddView(label, new LinearLayout.LayoutParams(0, -2, 1));
        var state = new TextView(this) { Text = value, TextSize = 13, Gravity = GravityFlags.Right };
        state.SetTextColor(Color.ParseColor(Palette.Text)); row.AddView(state, new LinearLayout.LayoutParams(0, -2, 1));
        return state;
    }
    private Button DetachedButton(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button(this) { Text = text, TextSize = 12 };
        button.SetAllCaps(false); NLAndroidUIVisual.Button(button, primary);
        button.Click += async (_, _) => {
            try { await action(); }
            catch (Exception) { _status.Text = "No se completó la operación. Revisa la sesión y su estado en PC."; }
        };
        return button;
    }
    private void AddButtonRow(params Button[] buttons)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetPadding(0, Dp(4), 0, Dp(4)); _body.AddView(row);
        for (int i = 0; i < buttons.Length; i++)
        {
            var layout = new LinearLayout.LayoutParams(0, Dp(48), 1);
            layout.SetMargins(i == 0 ? 0 : Dp(4), 0, i == buttons.Length - 1 ? 0 : Dp(4), 0);
            row.AddView(buttons[i], layout);
        }
    }
    private Task OpenFilesAsync()
    {
        StartActivity(new Intent(this, typeof(NLAndroidUIFilesActivity)));
        return Task.CompletedTask;
    }
    private void ButtonIcon(Button button, int resource, bool primary = false)
    {
        var icon = GetDrawable(resource)!.Mutate();
        icon.SetTint(Color.ParseColor(primary ? Palette.AccentText : Palette.Text));
        icon.SetBounds(0, 0, Dp(22), Dp(22));
        button.SetCompoundDrawables(icon, null, null, null); button.CompoundDrawablePadding = Dp(10);
    }
    private Button RowLink(string text, int icon, Func<Task> action)
    {
        var button = OutlineButton(text, action);
        button.Gravity = GravityFlags.CenterVertical | GravityFlags.Left;
        button.Background = NLAndroidUIVisual.Surface(this, Palette.Surface, Palette.Border, 6);
        ButtonIcon(button, icon);
        var arrow = GetDrawable(Android.Resource.Drawable.IcMediaNext)!.Mutate(); arrow.SetTint(Color.ParseColor(Palette.Muted));
        arrow.SetBounds(0, 0, Dp(14), Dp(14));
        var drawables = button.GetCompoundDrawables(); button.SetCompoundDrawables(drawables[0], null, arrow, null);
        return button;
    }
    private void BackLink(string text, Action action)
    {
        var button = new Button(this) { Text = text, TextSize = 13, ContentDescription = text };
        button.SetAllCaps(false);
        button.Gravity = GravityFlags.CenterVertical | GravityFlags.Left;
        button.SetTextColor(Color.ParseColor(Palette.Accent));
        button.SetBackgroundColor(Color.Transparent);
        button.SetMinHeight(Dp(48));
        button.SetPadding(0, 0, 0, 0);
        var icon = GetDrawable(Android.Resource.Drawable.IcMediaPrevious)!.Mutate();
        icon.SetTint(Color.ParseColor(Palette.Accent));
        icon.SetBounds(0, 0, Dp(18), Dp(18));
        button.SetCompoundDrawables(icon, null, null, null);
        button.CompoundDrawablePadding = Dp(8);
        button.Click += (_, _) => action();
        _body.AddView(button, new LinearLayout.LayoutParams(-1, Dp(48)));
    }
    private Spinner SettingRow(string title)
    {
        var parent = _body;
        var row = new LinearLayout(this) { Orientation = Orientation.Vertical };
        row.SetPadding(0, Dp(5), 0, Dp(7)); parent.AddView(row);
        var name = new TextView(this) { Text = title, TextSize = 13 }; name.SetTextColor(Color.ParseColor(Palette.Text));
        row.AddView(name, new LinearLayout.LayoutParams(-1, -2));
        var value = new LinearLayout(this) { Orientation = Orientation.Vertical }; row.AddView(value, new LinearLayout.LayoutParams(-1, -2)); _body = value;
        var spinner = Selector(title); _body = parent;
        return spinner;
    }
    private TextView Muted(string text)
    {
        var label = Label(text, 13);
        label.SetTextColor(Color.ParseColor(Palette.Muted));
        return label;
    }
    private LinearLayout ExInSection(Action build)
    {
        var parent = _body;
        var section = new LinearLayout(this) { Orientation = Orientation.Vertical };
        parent.AddView(section, new LinearLayout.LayoutParams(-1, -2));
        _body = section;
        build();
        _body = parent;
        return section;
    }
    private Button ExInSectionButton(string text, Action action)
    {
        var button = new Button(this) { Text = text, ContentDescription = $"Mostrar {text} de ExInEngine" };
        NLAndroidUIVisual.Button(button);
        button.Click += (_, _) => action();
        return button;
    }
    private void ShowExInSection(LinearLayout selected)
    {
        if (_exInTestSection is null || _exInCalibrationSection is null) return;
        _exInTestSection.Visibility = ReferenceEquals(selected, _exInTestSection) ? ViewStates.Visible : ViewStates.Gone;
        _exInCalibrationSection.Visibility = ReferenceEquals(selected, _exInCalibrationSection) ? ViewStates.Visible : ViewStates.Gone;
        NLAndroidUIVisual.Button(_exInTestTab, ReferenceEquals(selected, _exInTestSection));
        NLAndroidUIVisual.Button(_exInCalibrationTab, ReferenceEquals(selected, _exInCalibrationSection));
        _exInTestTab.Selected = ReferenceEquals(selected, _exInTestSection);
        _exInCalibrationTab.Selected = ReferenceEquals(selected, _exInCalibrationSection);
    }
    private void Card(Action build)
    {
        var parent = _body;
        var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var background = new GradientDrawable();
        background.SetColor(Color.ParseColor(Palette.Surface));
        background.SetCornerRadius(Dp(Palette.CardRadius));
        background.SetStroke(Dp(1), Color.ParseColor(Palette.Border));
        card.Background = background;
        card.SetPadding(Dp(14), Dp(8), Dp(14), Dp(10));
        var layout = new LinearLayout.LayoutParams(-1, -2);
        layout.SetMargins(0, Dp(5), 0, Dp(5));
        parent.AddView(card, layout);
        _body = card;
        build();
        _body = parent;
    }
    private Button OutlineButton(string text, Func<Task> action)
    {
        var button = Button(text, action);
        NLAndroidUIVisual.Button(button);
        return button;
    }
    private Task InfoAsync(string title, string message)
    {
        new AlertDialog.Builder(this)!.SetTitle(title)!.SetMessage(message)!.SetPositiveButton("Cerrar", (_, _) => { })!.Show();
        return Task.CompletedTask;
    }


    protected override void OnResume()
    {
        base.OnResume();
        RenderLinkMonitor();
        _automaticUsbResumed = true;
        _pendingUsbBootstrap ??= NLAndroidServiceUsbBootstrap.Take();
        DeliverUsbBootstrap();
        ApplyLanCodeBootstrap();
    }
    protected override void OnPause()
    {
        _automaticUsbResumed = false;
        base.OnPause();
    }

    protected override void OnStart()
    {
        base.OnStart();
        _active = true;
        NOVORA.AndroidVpn.NLAndroidVpnService.MonitorChanged += LinkMonitorChanged;
        RenderLinkMonitor();
        _connect.Enabled = _scan.Enabled = false;
        _connection = new NLAndroidUIServiceConnection(this);
        _bound = BindService(new Intent(this, typeof(NLAndroidServiceControl)), _connection, Bind.AutoCreate);
        if (!_bound) _status.Text = "No se pudo preparar el servicio de control. Vuelve a abrir NOVORA.";
    }
    private sealed class NLAndroidUIServiceConnection(NLAndroidUIActivity owner) : Java.Lang.Object, IServiceConnection
    {
        public void OnServiceConnected(ComponentName? name, IBinder? binder)
        {
            if (!owner._active || !ReferenceEquals(owner._connection, this)) return;
            if (binder is not NLAndroidServiceControl.NLAndroidServiceBinder control) return;
            owner._service = control.Owner;
            if (owner._uiServiceId is not null && owner._uiServiceId != control.Owner.InstanceId) {
                owner._drafts.Clear(); owner._confirmed.Clear();
                owner._vpnGeneration = -1; owner._vpnApproved = false;
                owner._pcVideoAuthorizationObservedId = null;
                owner._launchVideoProjectionPromptedGeneration = -1;
            }
            owner._uiServiceId = control.Owner.InstanceId;
            owner._service.Session.Changed += owner.SessionChanged;
            owner._service.StatusChanged += owner.ServiceStatusChanged;
            owner.RenderSession();
            if (owner.AutomaticLanFingerprint() is { } fingerprint)
                _ = owner.AnnounceTrustedPcAsync(control.Owner, fingerprint);
            owner.DeliverUsbBootstrap();
            _ = owner.CompleteVpnConsentAsync();
            _ = owner.CompleteVideoProjectionAsync();
        }
        public void OnServiceDisconnected(ComponentName? name)
        {
            if (!ReferenceEquals(owner._connection, this)) return;
            owner.DetachSession();
            if (owner._active)
            {
                owner._status.Text = "El servicio se cerró. Vuelve a abrir NOVORA y prepara un nuevo enlace.";
                owner.EnableActions(false);
                owner.ShowPage(owner._connectionPage);
                owner._connect.Enabled = owner._scan.Enabled = false;
            }
        }
    }
    private void DetachSession()
    {
        if (_service is not null) { _service.Session.Changed -= SessionChanged; _service.StatusChanged -= ServiceStatusChanged; }
        _service = null;
        _snapshot = null;
    }
    private string? AutomaticLanFingerprint() =>
        GetSharedPreferences("novora.lan", FileCreationMode.Private)?.GetString("automaticFingerprint", null);

    private void SetAutomaticLanFingerprint(string? fingerprint)
    {
        using var editor = GetSharedPreferences("novora.lan", FileCreationMode.Private)!.Edit()!;
        if (fingerprint is null) editor.Remove("automaticFingerprint");
        else editor.PutString("automaticFingerprint", fingerprint);
        editor.Apply();
    }

    private async Task AnnounceTrustedPcAsync(NLAndroidServiceControl service, string fingerprint)
    {
        for (int attempt = 0; attempt < 3 && _active && ReferenceEquals(service, _service); attempt++)
        {
            try
            {
                if (await service.AnnounceTrustedLanPcOnLaunchAsync(fingerprint) ||
                    service.Session.Current.Phase == NLControlSessionPhase.Connected)
                {
                    if (_active && ReferenceEquals(service, _service)) RenderSession();
                    return;
                }
            }
            catch (Exception ex)
            {
                Android.Util.Log.Warn("NOVORA-CONTROL", "LAUNCH_PRESENCE_FAILED " + ex.GetType().Name);
            }
            if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(2));
        }
        if (_active && ReferenceEquals(service, _service)) RenderSession();
    }
    private void SessionChanged(object? sender, NLControlSessionState state) => RunOnUiThread(() =>
    {
        if (_active && _service is not null && ReferenceEquals(sender, _service.Session)) RenderSession();
    });
    private void ServiceStatusChanged(object? sender, EventArgs args) => RunOnUiThread(() =>
    { if (_active && ReferenceEquals(sender, _service)) RenderSession(); });
    private void RenderSession()
    {
        if (!_active || _service is null) return;
        var state = _service.Session.Current;
        var previousPhase = _lastRenderedPhase;
        _lastRenderedPhase = state.Phase;
        if (_uiGeneration >= 0 && state.Generation != _uiGeneration) {
            _drafts.Clear(); _confirmed.Clear(); _returnToSettings = false;
            _pcVideoAuthorizationObservedId = null;
        }
        _uiGeneration = state.Generation;
        _busy = state.Busy || _service.TransferInProgress;
        _connecting = state.Phase == NLControlSessionPhase.Connecting;
        _cancel.Visibility = _connecting || _service.RecoveryMessage is not null ? ViewStates.Visible : ViewStates.Gone;
        _connect.Enabled = _scan.Enabled = _search.Enabled = !_connecting;
        _status.Text = _service.RecoveryMessage ?? state.Message;
        if (NOVORA.AndroidVpn.NLAndroidVpnService.IsRunning) _status.Text += "\n" + NOVORA.AndroidVpn.NLAndroidVpnService.Status;
        if (state.Phase == NLControlSessionPhase.Connected && state.Snapshot is { } snapshot)
        {
            ApplyState(snapshot);
            if (previousPhase != NLControlSessionPhase.Connected && _connectionPage.Visibility == ViewStates.Visible) ShowPage(_returnToSettings ? _settingsPage : _dashboardPage);
            _returnToSettings = false;
            if (_requestedPage is { } requested) {
                _requestedPage = null;
                if (requested == "settings") ShowPage(_settingsPage);
                else if (requested == "stopVideo") {
                    if (_requestedGeneration == state.Generation && _requestedRevision == snapshot.Revision)
                        _ = ConfirmStopEngineAsync("stopVideo", "VisionEngine", "La transmisión se detendrá. LinkEngine conserva su estado.");
                    else _status.Text = "El estado cambió. Revisa el teléfono antes de detener VisionEngine.";
                }
            }
        }
        else
        {
            _snapshot = null;
            EnableActions(false);
            _current.Text = "Sin sesión confirmada. Prepara un nuevo enlace USB o LAN en HOME de PC.";
            UpdateVisualSummary(null);
            if (previousPhase == NLControlSessionPhase.Connected) ShowPage(_connectionPage);
        }
        if (_videoProjectionRequest.IsPending) _ = CompleteVideoProjectionAsync();
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == VpnRequest)
        {
            _vpnApproved = resultCode == Result.Ok;
            if (!_vpnApproved) { _vpnGeneration = -1; _startVideoAfterLink = false; _status.Text = "VPN cancelada. LinkEngine no se inició."; }
            else _ = CompleteVpnConsentAsync();
            return;
        }
        if (requestCode == VideoProjectionRequest)
        {
            if (resultCode == Result.Ok && data is not null)
            {
                _videoProjectionRequest.Approve(data);
                _ = CompleteVideoProjectionAsync();
            }
            else
            {
                _videoProjectionRequest.Cancel();
                _videoProjectionAudioDecisionReady = false;
                _status.Text = "Captura cancelada. VisionEngine no se inició.";
                RenderSession();
            }
            return;
        }
    }
    protected override void OnStop()
    {
        _active = false;
        NOVORA.AndroidVpn.NLAndroidVpnService.MonitorChanged -= LinkMonitorChanged;
        _ = _service?.SynchronizeExInAsync();
        _discovery?.Cancel();
        _pendingConnect = null;
        DetachSession();
        if (_bound && _connection is not null) UnbindService(_connection);
        _bound = false;
        _connection = null;
        base.OnStop();
    }
    private void LinkMonitorChanged(object? sender, EventArgs args) =>
        RunOnUiThread(() => { if (_active) RenderLinkMonitor(); });

    private void RenderLinkMonitor()
    {
        if (_linkMonitorState is null || _linkMonitorButton is null) return;
        bool monitoring = NOVORA.AndroidVpn.NLAndroidVpnService.IsMonitoring;
        var sample = NOVORA.AndroidVpn.NLAndroidVpnService.MonitorSample;
        _linkMonitorButton.Text = monitoring ? "Detener monitoreo en vivo" : "Iniciar monitoreo en vivo";
        _linkMonitorButton.Enabled = true;
        _linkMonitorState.Text = sample is null
            ? monitoring ? "Midiendo tráfico VPN de LE…" : "Monitoreo detenido · sin datos actuales."
            : $"{(monitoring ? "EN VIVO" : "ÚLTIMA MUESTRA")} · LE {sample.Transport} · {sample.SampledAtUtc.ToLocalTime():HH:mm:ss}\n" +
              $"TX {sample.TxMbps:F2} Mbps · RX {sample.RxMbps:F2} Mbps\n" +
              $"Paquetes TX {sample.TxPackets:N0} · RX {sample.RxPackets:N0}";
    }

    private Task ToggleLinkMonitorAsync()
    {
        if (NOVORA.AndroidVpn.NLAndroidVpnService.IsMonitoring)
            NOVORA.AndroidVpn.NLAndroidVpnService.StopMonitor();
        else if (!NOVORA.AndroidVpn.NLAndroidVpnService.StartMonitor(this))
        {
            RenderLinkMonitor();
            _linkMonitorState.Text = "Inicia LinkEngine y espera a que el VPN esté conectado.";
            return Task.CompletedTask;
        }
        RenderLinkMonitor();
        return Task.CompletedTask;
    }

    private async Task OpenLinkInternetTestAsync()
    {
        if (!NOVORA.AndroidVpn.NLAndroidVpnService.IsTunnelReady || _service is null)
        {
            _linkInternetTestState.Text = "Inicia LinkEngine y espera a que el VPN esté conectado.";
            return;
        }
        try
        {
            var reply = await _service.Session.SendAsync("link.speedtest");
            if (!ValidLinkTestUrl(reply, out var url))
            {
                _linkInternetTestState.Text = reply.Message;
                return;
            }
            NOVORA.AndroidVpn.NLAndroidVpnService.StartMonitor(this);
            StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(url + "?mode=internet"))
                .AddCategory(Intent.CategoryBrowsable));
            _linkInternetTestState.Text = "Prueba de Internet abierta en el navegador. Si termina, el resultado se guarda en NOVORA PC. Detén el monitor al finalizar.";
        }
        catch (Exception)
        {
            _linkInternetTestState.Text = "No se pudo abrir la prueba de Internet. Revisa LE y el navegador.";
        }
    }

    private async Task OpenLinkTunnelTestAsync()
    {
        if (!NOVORA.AndroidVpn.NLAndroidVpnService.IsTunnelReady || _service is null)
        {
            _linkTunnelTestState.Text = "Conecta LE y espera a que el VPN esté activo.";
            return;
        }
        try
        {
            var reply = await _service.Session.SendAsync("link.speedtest");
            if (!ValidLinkTestUrl(reply, out var url))
            {
                _linkTunnelTestState.Text = reply.Message;
                return;
            }
            NOVORA.AndroidVpn.NLAndroidVpnService.StartMonitor(this);
            StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(url + "?mode=tunnel"))
                .AddCategory(Intent.CategoryBrowsable));
            _linkTunnelTestState.Text = "Prueba del túnel abierta en el navegador. El resultado aparece allí y se guarda en NOVORA PC.";
        }
        catch (Exception)
        {
            _linkTunnelTestState.Text = "No se pudo abrir la prueba del túnel. Revisa LE y el navegador.";
        }
    }
    private static bool ValidLinkTestUrl(NLControlReply reply, out string url)
    {
        url = reply.Value ?? "";
        return reply.Success && System.Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
            parsed.Scheme == "http" && parsed.Host == "10.0.2.2" && parsed.Port == 28085 &&
            parsed.AbsolutePath.Length > 3;
    }
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        _requestedPage = intent?.GetStringExtra("novora.page");
        _requestedGeneration = intent?.GetLongExtra("novora.generation", -1) ?? -1;
        _requestedRevision = intent?.GetLongExtra("novora.revision", -1) ?? -1;
        intent?.RemoveExtra("novora.page");
        intent?.RemoveExtra("novora.usb");
        if (NLAndroidServiceUsbBootstrap.Take() is { } usb)
        {
            _pendingUsbBootstrap = usb;
            DeliverUsbBootstrap();
        }
        if (_active) ApplyLanCodeBootstrap();
        if (_active && _service is not null) RenderSession();
    }
    protected override void OnSaveInstanceState(Bundle state)
    {
        state.PutBoolean("settings", _settingsPage.Visibility == ViewStates.Visible);
        state.PutLong("generation", _uiGeneration);
        state.PutString("serviceId", _uiServiceId);
        state.PutLong("vpnGeneration", _vpnGeneration);
        state.PutLong("vpnRevision", _vpnRevision);
        foreach (var (spinner, key) in new[] { (_profile,"profile"), (_bitrate,"bitrate"), (_audio,"audio"), (_resolution,"resolution"), (_fps,"fps"), (_monitor,"monitor"), (_presentation,"presentation") })
            if (_drafts.TryGetValue(spinner, out var value)) state.PutString("draft." + key, value);
        base.OnSaveInstanceState(state);
    }
#pragma warning disable CS0672, CA1422
    public override void OnBackPressed()
    {
        if (_settingsPage.Visibility == ViewStates.Visible) ShowPage(_dashboardPage);
        else if (_exInPage.Visibility == ViewStates.Visible) ShowPage(_dashboardPage);
        else if (!ReferenceEquals(_dashboardPage, VisiblePage())) ShowPage(_dashboardPage);
        else MoveTaskToBack(true);
    }
#pragma warning restore CS0672, CA1422

    private ScrollView CreatePage(FrameLayout host)
    {
        return NLAndroidUIMainShell.CreatePage(this, host, out _body);
    }
    private void ShowPage(ScrollView page)
    {
        _homeBackground.Visibility = ReferenceEquals(page, _dashboardPage) ? ViewStates.Visible : ViewStates.Gone;
        _settingsBackground.Visibility = ReferenceEquals(page, _settingsPage) ? ViewStates.Visible : ViewStates.Gone;
        _connectionPage.Visibility = ReferenceEquals(page, _connectionPage) ? ViewStates.Visible : ViewStates.Gone;
        _dashboardPage.Visibility = ReferenceEquals(page, _dashboardPage) ? ViewStates.Visible : ViewStates.Gone;
        _enginesPage.Visibility = ReferenceEquals(page, _enginesPage) ? ViewStates.Visible : ViewStates.Gone;
        _exInPage.Visibility = ReferenceEquals(page, _exInPage) ? ViewStates.Visible : ViewStates.Gone;
        _filesPage.Visibility = ReferenceEquals(page, _filesPage) ? ViewStates.Visible : ViewStates.Gone;
        _settingsPage.Visibility = ReferenceEquals(page, _settingsPage) ? ViewStates.Visible : ViewStates.Gone;
        UpdateTabs(page);
    }
    private void BuildTabs(LinearLayout root)
    {
        var tabs = NLAndroidUIMainShell.BottomNavigation(this);
        tabs.SetPadding(Dp(10), Dp(6), Dp(10), Dp(10));
        tabs.Background = NLAndroidUIVisual.Surface(this, Palette.Navigation, Palette.Border, 0);
        root.AddView(tabs, new LinearLayout.LayoutParams(-1, -2));
        _tabHome = TabButton("Principal", Android.Resource.Drawable.IcMenuView, () => ShowPage(_dashboardPage));
        _tabSettings = TabButton("Configuración", Android.Resource.Drawable.IcMenuManage, () => ShowPage(_settingsPage));
        foreach (var tab in new[] { _tabHome, _tabSettings })
            tabs.AddView(tab, new LinearLayout.LayoutParams(0, Dp(56), 1));
    }
    private Button TabButton(string text, int icon, Action action)
    {
        return NLAndroidUIComponents.BottomTab(this, text, icon, false, action);
    }
    private void UpdateTabs(ScrollView page)
    {
        if (_tabHome is null || _tabSettings is null) return;
        SetTabState(_tabHome, ReferenceEquals(page, _dashboardPage) || ReferenceEquals(page, _connectionPage));
        SetTabState(_tabSettings, ReferenceEquals(page, _settingsPage));
    }
    private void SetTabState(Button tab, bool selected)
    {
        tab.Selected = selected;
        tab.SetTextColor(Color.ParseColor(selected ? Palette.Accent : Palette.Muted));
        tab.SetBackgroundColor(Color.Transparent);
        var tint = selected ? Palette.Accent : Palette.Muted;
        var drawables = tab.GetCompoundDrawables();
        foreach (var drawable in drawables)
            drawable?.SetTint(Color.ParseColor(tint));
    }
    private sealed class NLAndroidUIInsets : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
        {
            if (insets is null) return null!;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var safe = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.Ime() | WindowInsets.Type.DisplayCutout());
                view?.SetPadding(safe.Left, safe.Top, safe.Right, safe.Bottom);
            }
            else
            {
#pragma warning disable CS0618, CA1422
                view?.SetPadding(insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop, insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
#pragma warning restore CS0618, CA1422
            }
            return insets;
        }
    }
    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density);
    private TextView Label(string text, int size)
    {
        var label = new TextView(this) { Text = text, TextSize = size };
        label.SetTextColor(Color.ParseColor(Palette.Text));
        label.SetPadding(0, Dp(6), 0, Dp(6));
        _body.AddView(label);
        return label;
    }
    private Button Button(string text, Func<Task> action)
    {
        var button = new Button(this) { Text = text };
        NLAndroidUIVisual.Button(button, true);
        var layout = new LinearLayout.LayoutParams(-1, -2);
        layout.SetMargins(0, Dp(4), 0, Dp(4));
        _body.AddView(button, layout);
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception) { _status.Text = "No se completó la operación. Revisa la sesión y su estado en PC."; }
        };
        return button;
    }
    private void ActionButton(string text, Func<Task> action) => _actions.Add(Button(text, action));
    private Spinner Selector(string title)
    {
        var spinner = new Spinner(this);
        spinner.ContentDescription = title;
        spinner.ItemSelected += (_, args) => {
            if (_populating || spinner.Tag is not Java.Lang.String encoded) return;
            var values = encoded.ToString().Split('\n');
            if (args.Position >= 0 && args.Position < values.Length) {
                string chosen = values[args.Position];
                if (_confirmed.TryGetValue(spinner, out var current) && chosen == current) _drafts.Remove(spinner);
                else _drafts[spinner] = chosen;
            }
        };
        spinner.Background = NLAndroidUIVisual.Surface(this, Palette.Surface, Palette.Border, 7);
        _body.AddView(spinner, new LinearLayout.LayoutParams(-1, Dp(48)));
        return spinner;
    }
    private void EnableActions(bool enabled)
    {
        foreach (var button in _actions) button.Enabled = enabled;
        _bitrate.Enabled = _profile.Enabled = _audio.Enabled = enabled;
        _resolution.Enabled = _fps.Enabled = _monitor.Enabled = _presentation.Enabled = enabled && _snapshot?.VideoSettings is not null;
        _remember.Enabled = enabled && _service?.CanRememberCurrentPc == true;
        _rememberSettings.Enabled = enabled && _service?.CanRememberCurrentPc == true;
        _rememberSettings.Visibility = _rememberSettings.Enabled ? ViewStates.Visible : ViewStates.Gone;
        bool hasSavedPcs = false;
        try { hasSavedPcs = _service?.GetSavedPcs().Count > 0; } catch (Exception) { }
        _savedPcsSettings.Visibility = hasSavedPcs ? ViewStates.Visible : ViewStates.Gone;
        _savedPcsSettings.Enabled = hasSavedPcs && !_connecting;
        _restart.Text = "Guardar ajustes";
        _restart.Enabled = enabled && _snapshot?.VideoSettings?.CanApplyTogether == true &&
            _snapshot.Media is not ({ Recording: true } or { Starting: true });
        _capture.Enabled = enabled && _snapshot?.Media?.CanCapture == true;
        _record.Enabled = enabled && (_snapshot?.Media is { Recording: true } or { CanRecord: true });
        _record.Text = _snapshot?.Media?.Starting == true ? "Cancelar inicio de grabación" : _snapshot?.Media?.Recording == true ? "Detener y guardar grabación" : "Grabar en PC";
        _sendFiles.Enabled = enabled && _snapshot?.FileSharing == true;
        var engines = _snapshot?.Engines;
        UpdateEngineButton(_videoEngine, "VisionEngine", enabled, engines?.VideoCanStart == true, engines?.VideoCanStop == true, _snapshot?.VideoRunning == true, true);
        _videoRestart.Enabled = enabled && _snapshot?.VideoRunning == true && engines?.VideoCanStop == true;
        UpdateEngineButton(_linkEngine, "LinkEngine", enabled, engines?.LinkCanStart == true &&
            _service?.Session.Current.Transport is "USB" or "LAN", engines?.LinkCanStop == true,
            engines?.LinkRunning == true, false, engines?.LinkCanTakeOver == true);
        if (!enabled && _snapshot is null)
        {
            _engineState.Text = "Sin estado confirmado de los motores. Conecta para consultar NOVORA PC.";
            _settingsStatus.Text = "Conecta USB o LAN para consultar y guardar ajustes del PC.";
        }
        else if (_snapshot is not null && _settingsStatus.Text == "Conecta USB o LAN para consultar y guardar ajustes del PC.")
            _settingsStatus.Text = "Guarda los ajustes para aplicarlos en PC y volver a Inicio.";
    }
    private void UpdateEngineButton(Button button, string name, bool enabled, bool canStart, bool canStop, bool running, bool primaryStart, bool canTakeOver = false)
    {
        bool stopping = !canTakeOver && (canStop || running);
        button.Text = canTakeOver ? $"Usar {name} aquí" : stopping ? $"Detener {name}" : $"Iniciar {name}";
        button.Enabled = enabled && (canTakeOver ? canStart : stopping ? canStop : canStart);
        NLAndroidUIVisual.Button(button, !stopping && primaryStart);
        ButtonIcon(button, stopping ? Android.Resource.Drawable.IcMediaPause : Android.Resource.Drawable.IcMediaPlay, !stopping && primaryStart);
    }

    private Task ConnectAsync()
    {
        if (_connecting || _service is null || !_active) return Task.CompletedTask;
        return RequestConnectAsync(() => _service!.ConnectUsbAsync());
    }
    private void DeliverUsbBootstrap()
    {
        if (!_active || !_automaticUsbResumed || _service is null || _pendingUsbBootstrap is not { } text) return;
        var service = _service;
        _pendingUsbBootstrap = null;
        _ = RunAutomaticUsbAsync(service, text);
    }
    private async Task RunAutomaticUsbAsync(NLAndroidServiceControl service, string text)
    {
        try
        {
            // USB starts from a protected ADB invitation. POST_NOTIFICATIONS is not an FGS startup prerequisite.
            // Keep the foreground notification and native VPN consent; do not fabricate Connected or capabilities.
            await service.ConnectAutomaticUsbAsync(text);
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("NOVORA-USB", "AUTO_CONNECT_FAILED " + ex.GetType().Name);
            if (_active && ReferenceEquals(service, _service))
                _status.Text = "USB no confirmado (" + ex.GetType().Name + "). Reintenta USB en PC.";
        }
        finally
        {
            if (_active && ReferenceEquals(service, _service)) RenderSession();
        }
    }

    private async Task RequestConnectAsync(Func<Task> connect)
    {
        if (!_active || _service is null || _connecting || _pendingConnect is not null) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            _pendingConnect = connect;
            _pendingConnectGeneration = _service.Session.Current.Generation;
            new AlertDialog.Builder(this)!.SetTitle("Control desde las notificaciones")!
                .SetMessage("Permite las notificaciones para ver la conexión, el estado de grabación y desconectar sin abrir la app. Si no las permites, Android puede ocultar ese panel; podrás consultar el estado dentro de APPCONTROL.")!
                .SetPositiveButton("Continuar", (_, _) =>
                {
                    if (_active && _pendingConnect is not null && OperatingSystem.IsAndroidVersionAtLeast(33))
                        RequestPermissions([Android.Manifest.Permission.PostNotifications], NotificationPermissionRequest);
                })!
                .SetNegativeButton("Cancelar", (_, _) => { _pendingConnect = null; })!
                .SetOnCancelListener(new NLAndroidUICancelConnect(this))!.Show();
            return;
        }
        await RunConnectAsync(connect);
    }
    private sealed class NLAndroidUICancelConnect(NLAndroidUIActivity owner) : Java.Lang.Object, IDialogInterfaceOnCancelListener
    { public void OnCancel(IDialogInterface? dialog) => owner._pendingConnect = null; }
    public override async void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == AudioPermissionRequest)
        {
            bool audioGranted = grantResults.Length > 0 && grantResults[0] == Permission.Granted;
            if (!audioGranted)
                _status.Text = "Sin permiso de audio; VisionEngine iniciará video y control sin sonido.";
            _videoProjectionAudioDecisionReady = true;
            _ = CompleteVideoProjectionAsync();
            return;
        }
        if (requestCode != NotificationPermissionRequest) return;
        var pending = _pendingConnect;
        _pendingConnect = null;
        if (_active && _service is not null && pending is not null &&
            _service.Session.Current.Generation == _pendingConnectGeneration) await RunConnectAsync(pending);
    }
    private async Task RunConnectAsync(Func<Task> connect)
    {
        if (!_active || _service is null) return;
        try { await connect(); if (_active) RenderSession(); }
        catch (Exception)
        {
            if (_active) _status.Text = _service?.Session.Current.Message ?? "No se pudo iniciar la conexión. Prepara un nuevo enlace en HOME de PC.";
        }
    }

    private static string SafePeerName(string name)
    {
        // Discovery text is untrusted. Bound its visual footprint and discard invisible controls.
        string safe = new(name.Take(64).Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "NOVORA PC" : safe.Trim();
    }

    private async Task SearchAsync()
    {
        if (_discovery is not null || _connecting) return;
        _selectedLanPeer = null;
        _lanPeerLabel.Text = "Buscando NOVORA PC...";
        _lanFingerprint.Visibility = ViewStates.Gone;
        _lanFingerprintConfirmed.Checked = false;
        _lanFingerprintConfirmed.Visibility = ViewStates.Gone;
        UpdateLanStartButton();
        using var cancellation = new CancellationTokenSource();
        _discovery = cancellation;
        _lanSearch.Enabled = false;
        _status.Text = "Buscando NOVORA PC…";
        try
        {
            var peers = await NLControlLanDiscovery.SearchAsync(cancellation.Token);
            if (!_active || cancellation.IsCancellationRequested) return;
            if (peers.Count == 0)
            {
                _status.Text = "No se encontraron equipos. Revisa la misma red y prepara un código LAN en NOVORA PC.";
                return;
            }
            // Discovery replies are untrusted hints; users compare the full certificate fingerprint out of band.
            new AlertDialog.Builder(this)!.SetTitle("Equipos anunciados en la red")!
                .SetItems(peers.Select(p => $"{SafePeerName(p.Name)} · {System.Net.IPAddress.Parse(p.Host)}").ToArray(), (_, args) =>
                {
                    _selectedLanPeer = peers[args.Which];
                    string host = System.Net.IPAddress.Parse(_selectedLanPeer.Host).ToString();
                    _lanPeerLabel.Text = $"{SafePeerName(_selectedLanPeer.Name)} · {host}";
                    _lanFingerprint.Text = "Compara esta huella completa con NOVORA PC antes de iniciar LAN:\n" +
                        _selectedLanPeer.Fingerprint;
                    _lanFingerprint.Visibility = ViewStates.Visible;
                    _lanFingerprintConfirmed.Visibility = ViewStates.Visible;
                    _lanCodeEntry.RequestFocus();
                    _status.Text = "Introduce el código LAN de seis dígitos que muestra NOVORA PC.";
                })!
                .SetNegativeButton("Cerrar", (_, _) => { })!.Show();
        }
        catch (System.OperationCanceledException) { }
        catch (Exception) { if (_active) _status.Text = "No se pudo buscar en esta red. Revisa Wi-Fi y prepara un código LAN en PC, o usa USB."; }
        finally
        {
            if (ReferenceEquals(_discovery, cancellation)) _discovery = null;
            _lanSearch.Enabled = true;
        }
    }

    private Task ConnectLanAsync()
    {
        if (!_active || _connecting || _service is null) return Task.CompletedTask;
        if (_service.Session.Current is { Phase: NLControlSessionPhase.Connected, Transport: "LAN" })
            return LaunchMotorsAsync();
        IReadOnlyList<NLControlTrustedPc> saved;
        try { saved = _service.GetSavedPcs().Where(p => p.Transport == "LAN").ToArray(); }
        catch (Exception) { _status.Text = "No se pueden leer las PC guardadas. No se inició LAN."; return Task.CompletedTask; }
        if (saved.Count == 1) return ConnectRememberedLanAsync(saved[0]);
        if (saved.Count > 1)
        {
            new AlertDialog.Builder(this)!.SetTitle("Selecciona una PC recordada")!
                .SetItems(saved.Select(p => SafePeerName(p.PcName)).ToArray(), (_, args) =>
                    _ = ConnectRememberedLanAsync(saved[args.Which]))!
                .SetNeutralButton("Usar código nuevo", (_, _) => ShowLanCodePage(true))!
                .SetNegativeButton("Cancelar", (_, _) => { })!.Show();
            return Task.CompletedTask;
        }
        ShowLanCodePage(true);
        return Task.CompletedTask;
    }

    private async Task ConnectRememberedLanAsync(NLControlTrustedPc peer)
    {
        var service = _service;
        if (!_active || _connecting || service is null) return;
        await RequestConnectAsync(async () =>
        {
            await service.ConnectTrustedAsync(peer);
            if (!_active || !ReferenceEquals(service, _service) ||
                service.Session.Current is not { Phase: NLControlSessionPhase.Connected, Transport: "LAN" }) return;
            RenderSession();
            await LaunchMotorsAsync();
        });
    }

    private void ShowLanCodePage(bool show, bool search = true)
    {
        _homeActions.Visibility = show ? ViewStates.Gone : ViewStates.Visible;
        _lanCodePanel.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
        if (show)
        {
            _lanCodeEntry.RequestFocus();
            _lanCodeEntry.Post(() =>
            {
                if (_lanCodePanel.Visibility == ViewStates.Visible)
                    (GetSystemService(InputMethodService) as InputMethodManager)?
                        .ShowSoftInput(_lanCodeEntry, ShowFlags.Implicit);
            });
            if (search) _ = SearchAsync();
        }
        else
        {
            _discovery?.Cancel();
            _selectedLanPeer = null;
            _lanCodeEntry.Text = "";
            _lanFingerprintConfirmed.Checked = false;
            _lanFingerprintConfirmed.Visibility = ViewStates.Gone;
            _lanFingerprint.Visibility = ViewStates.Gone;
            _lanRemember.Checked = false;
            _lanAuto.Checked = false;
            UpdateLanStartButton();
        }
    }

    private void ApplyLanCodeBootstrap()
    {
        if (!_active || NLAndroidServiceLanCodeBootstrap.Take() is not { } handoff) return;
        try { handoff.Validate(); }
        catch (InvalidDataException) { return; }
        ShowPage(_connectionPage);
        ShowLanCodePage(true, search: false);
        _selectedLanPeer = new NLControlLanPeer(
            handoff.PcName, handoff.Host, handoff.Port, handoff.Fingerprint);
        _lanPeerLabel.Text = $"{SafePeerName(handoff.PcName)} · {handoff.Host}";
        _lanFingerprint.Text = "Compara esta huella completa con NOVORA PC antes de iniciar LAN:\n" +
            handoff.Fingerprint;
        _lanFingerprint.Visibility = ViewStates.Visible;
        _lanFingerprintConfirmed.Checked = false;
        _lanFingerprintConfirmed.Visibility = ViewStates.Visible;
        _lanCodeEntry.Text = handoff.Code;
        _status.Text = "Código LAN recibido desde NOVORA PC por depuración autorizada. Confirma la huella para enlazar.";
        UpdateLanStartButton();
    }

    private Task PairFromLanPageAsync()
    {
        if (_selectedLanPeer is not { } peer) { _status.Text = "Busca y selecciona NOVORA PC."; return Task.CompletedTask; }
        string code = _lanCodeEntry.Text?.Trim() ?? "";
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        { _status.Text = "El código LAN debe contener exactamente 6 dígitos."; return Task.CompletedTask; }
        if (!_lanFingerprintConfirmed.Checked)
        { _status.Text = "Compara y confirma la huella completa antes de iniciar LAN."; return Task.CompletedTask; }
        bool remember = _lanRemember.Checked;
        bool automatic = _lanAuto.Checked;
        _lanCodeEntry.Text = "";
        return PairWithCodeAsync(peer, code, remember, automatic);
    }
    private void UpdateLanStartButton()
    {
        string code = _lanCodeEntry.Text?.Trim() ?? "";
        _lanPair.Enabled = _selectedLanPeer is not null && code.Length == 6 && code.All(char.IsAsciiDigit);
    }

    private Task PairWithCodeAsync()
    {
        if (!_active || _connecting || _service is null) return Task.CompletedTask;
        NLControlLanPeer? peer = _selectedLanPeer;
        if (peer is null)
        {
            _status.Text = "Primero busca y selecciona NOVORA PC en la red.";
            return Task.CompletedTask;
        }
        var entry = new EditText(this)
        {
            Hint = "000000",
            InputType = Android.Text.InputTypes.ClassNumber
        };
        entry.SetFilters([new Android.Text.InputFilterLengthFilter(6)]);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(Dp(20), Dp(8), Dp(20), Dp(8));
        var fingerprint = new TextView(this)
        {
            Text = $"Antes de enviar el código, compara en esta pantalla y en NOVORA PC la huella completa.\n\n{peer.Fingerprint}",
            TextSize = 13
        };
        fingerprint.SetTextIsSelectable(true);
        content.AddView(fingerprint, new LinearLayout.LayoutParams(-1, -2));
        content.AddView(entry, new LinearLayout.LayoutParams(-1, -2));
        var confirmFingerprint = new CheckBox(this)
        {
            Text = "Comparé la huella completa con la que muestra NOVORA PC"
        };
        content.AddView(confirmFingerprint, new LinearLayout.LayoutParams(-1, -2));
        var dialog = new AlertDialog.Builder(this)!.SetTitle("Código LAN de NOVORA PC")!
            .SetView(content)!
            .SetNegativeButton("Cancelar", (_, _) => entry.Text = "")!
            .SetPositiveButton("Conectar", async (_, _) =>
            {
                string code = entry.Text?.Trim() ?? "";
                entry.Text = "";
                if (code.Length != 6 || !code.All(char.IsAsciiDigit))
                {
                    _status.Text = "El código LAN debe contener exactamente 6 dígitos.";
                    return;
                }
                if (!confirmFingerprint.Checked)
                {
                    _status.Text = "Compara la huella completa antes de conectar.";
                    return;
                }
                await PairWithCodeAsync(peer, code);
            })!.Create()!;
        dialog.Show();
        dialog.GetButton(-1)!.Enabled = false;
        void UpdateCanConnect()
        {
            string code = entry.Text?.Trim() ?? "";
            dialog.GetButton(-1)!.Enabled = confirmFingerprint.Checked &&
                code.Length == 6 && code.All(char.IsAsciiDigit);
        }
        entry.TextChanged += (_, _) => UpdateCanConnect();
        confirmFingerprint.CheckedChange += (_, _) => UpdateCanConnect();
        return Task.CompletedTask;
    }

    private async Task PairWithCodeAsync(NLControlLanPeer peer, string code, bool remember = false, bool automatic = false)
    {
        NLAndroidServiceControl? service = _service;
        if (!_active || _connecting || service is null) return;
        _connecting = true;
        _status.Text = "Verificando huella y código por TLS…";
        try
        {
            await service.ConnectLanAsync(peer, code);
            if (_active && ReferenceEquals(service, _service))
            {
                RenderSession();
                bool rememberFailed = false;
                if (remember)
                {
                    try
                    {
                        await service.RememberCurrentPcAsync();
                        if (automatic) SetAutomaticLanFingerprint(peer.Fingerprint);
                        else if (AutomaticLanFingerprint() == peer.Fingerprint) SetAutomaticLanFingerprint(null);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        rememberFailed = true;
                        if (AutomaticLanFingerprint() == peer.Fingerprint) SetAutomaticLanFingerprint(null);
                    }
                }
                ShowLanCodePage(false);
                _status.Text = $"{SafePeerName(peer.Name)} conectada por LAN. Preparando motores.";
                await LaunchMotorsAsync();
                if (rememberFailed) Toast.MakeText(this, "LAN conectada, pero no se pudo recordar la PC. El ingreso automático quedó desactivado.", ToastLength.Long)?.Show();
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or
                                   System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException or InvalidOperationException)
        {
            if (_active) _status.Text = "No se completó el enlace LAN. Comprueba la huella y genera un código nuevo en NOVORA PC.";
        }
        finally
        {
            _connecting = false;
            _scan.Enabled = true;
            _search.Enabled = true;
            if (_active) EnableActions(_snapshot is not null && !_busy);
        }
    }

    private async Task DisconnectAsync()
    {
        _drafts.Clear();
        _pendingConnect = null;
        _vpnGeneration = -1;
        _vpnApproved = false;
        if (_service is not null) await _service.DisconnectAsync();
    }

    private Task RememberPcAsync()
    {
        var service = _service;
        if (!_active || service is null) return Task.CompletedTask;
        long generation = service.Session.Current.Generation;
        new AlertDialog.Builder(this)!.SetTitle("Recordar esta PC")!
            .SetMessage("Guarda la autorización de esta conexión USB o LAN en ambos equipos. Después puedes gestionarla en Configuración · LinkEngine y revocar el teléfono desde NOVORA PC. USB sigue necesitando el cable y el enlace preparado en PC.")!
            .SetNegativeButton("Cancelar", (_, _) => { })!
            .SetPositiveButton("Recordar", async (_, _) =>
            {
                if (!_active || !ReferenceEquals(service, _service) || generation != service.Session.Current.Generation) return;
                try { await service.RememberCurrentPcAsync(); if (_active) { RenderSession(); _status.Text = "PC guardada. Para LAN, usa Conectar PC guardada; para USB, prepara el enlace en NOVORA PC."; } }
                catch (Exception ex) when (ex is InvalidOperationException or System.IO.InvalidDataException)
                { if (_active) _status.Text = ex.Message; }
                catch (Exception) { if (_active) _status.Text = "No se guardó la autorización. Revisa la sesión; si PC registró el teléfono, puedes revocarlo desde HOME."; }
            })!.Show();
        return Task.CompletedTask;
    }
    private Task SavedPcsAsync() => ShowSavedPcsAsync(lanOnly: false);

    private Task SavedLanPcsAsync() => ShowSavedPcsAsync(lanOnly: true);

    private Task ForgetLanPcAsync()
    {
        if (!_active || _service is null) return Task.CompletedTask;
        IReadOnlyList<NLControlTrustedPc> peers;
        try { peers = _service.GetSavedPcs().Where(p => p.Transport == "LAN").ToArray(); }
        catch (Exception) { _status.Text = "No se pueden leer las PC guardadas. No se eliminó ninguna."; return Task.CompletedTask; }
        if (peers.Count == 0) { _status.Text = "No hay PC LAN guardadas para olvidar."; return Task.CompletedTask; }
        new AlertDialog.Builder(this)!.SetTitle("Olvidar PC LAN")!
            .SetItems(peers.Select(p => SafePeerName(p.PcName)).ToArray(), (_, args) =>
                ConfirmForgetPc(peers[args.Which]))!
            .SetNegativeButton("Cancelar", (_, _) => { })!.Show();
        return Task.CompletedTask;
    }

    private Task ShowSavedPcsAsync(bool lanOnly)
    {
        if (!_active || _service is null) return Task.CompletedTask;
        IReadOnlyList<NLControlTrustedPc> peers;
        try { peers = _service.GetSavedPcs(); }
        catch (Exception) { _status.Text = "No se pueden leer las PC guardadas. Revisa el almacenamiento de APPCONTROL antes de volver a enlazar."; return Task.CompletedTask; }
        if (lanOnly) peers = peers.Where(p => p.Transport == "LAN").ToArray();
        if (peers.Count == 0) { _status.Text = lanOnly ? "No hay PC LAN guardadas. Busca una PC en la red y recuérdala en Configuración · LinkEngine." : "Conecta por USB o LAN y usa Recordar esta PC en Configuración · LinkEngine."; return Task.CompletedTask; }
        new AlertDialog.Builder(this)!.SetTitle("PC guardadas")!
            .SetItems(peers.Select(p => $"{SafePeerName(p.PcName)} · {p.Transport}").ToArray(), (_, args) => ShowSavedPc(peers[args.Which]))!
            .SetNegativeButton("Cerrar", (_, _) => { })!.Show();
        return Task.CompletedTask;
    }
    private void ShowSavedPc(NLControlTrustedPc peer)
    {
        if (!_active || _service is null) return;
        new AlertDialog.Builder(this)!.SetTitle(SafePeerName(peer.PcName))!
            .SetMessage((peer.Transport == "USB" ? "Conecta el cable al teléfono seleccionado y prepara el control USB en PC. No necesitas código." : $"Dirección guardada: {peer.Host}. Activa el control LAN en PC; si cambió la dirección, prepara otro código LAN.") + "\n\nOlvidar elimina la autorización del teléfono. Puedes revocarla también desde HOME de PC.")!
            .SetPositiveButton("Conectar", async (_, _) =>
            { if (_active && _service is not null) await RequestConnectAsync(() => _service!.ConnectTrustedAsync(peer)); })!
            .SetNeutralButton("Olvidar", (_, _) => ConfirmForgetPc(peer))!
            .SetNegativeButton("Cerrar", (_, _) => { })!.Show();
    }

    private void ConfirmForgetPc(NLControlTrustedPc peer)
    {
        var service = _service;
        if (!_active || service is null) return;
        string name = SafePeerName(peer.PcName);
        new AlertDialog.Builder(this)!.SetTitle($"Olvidar {name}")!
            .SetMessage("Se eliminará la autorización guardada en este teléfono. Para volver a conectar tendrás que enlazar esta PC otra vez. También puedes revocar el teléfono desde NOVORA PC.")!
            .SetNegativeButton("Cancelar", (_, _) => { })!
            .SetPositiveButton("Olvidar", async (_, _) =>
            {
                if (!_active || !ReferenceEquals(service, _service)) return;
                _pendingConnect = null;
                try { await service.ForgetPcAsync(peer.Fingerprint); if (AutomaticLanFingerprint() == peer.Fingerprint) SetAutomaticLanFingerprint(null); if (_active) { RenderSession(); _status.Text = $"{name} olvidada en este teléfono."; } }
                catch (Exception) { if (_active) _status.Text = "No se pudo olvidar la PC. Revisa el almacenamiento de APPCONTROL."; }
            })!.Show();
    }

    private void ApplyState(NLControlSnapshot snapshot)
    {
        if (_snapshot is not null && snapshot.Revision < _snapshot.Revision) return;
        _snapshot = snapshot;
        _current.Text = $"{snapshot.PcName}\nNOVORA-LINK {snapshot.PcVersion}\nConectado por {_service?.Session.Current.Transport}";
        if (snapshot.Engines is { } target) _current.Text += "\nTeléfono: " + target.DeviceName;
        _engineState.Text = snapshot.Engines is { } engines
            ? engines.LinkMessage
            : "Esta PC no informa control de motores. Comprueba que NOVORA PC esté actualizado.";
        Populate(_bitrate, snapshot.Bitrates, snapshot.Bitrate, ref _bitrates);
        Populate(_profile, snapshot.Profiles, snapshot.Profile, ref _profiles);
        if (snapshot.Acceleration is { } acceleration)
            _accelerationState.Text = acceleration.Confirmed
                ? $"Aceleración activa: {acceleration.Active} · {acceleration.Evidence}"
                : $"Aceleración solicitada: {acceleration.Requested} · {acceleration.Evidence}" +
                  (string.IsNullOrWhiteSpace(acceleration.FallbackReason) ? "" : $" · Fallback: {acceleration.FallbackReason}");
        else _accelerationState.Text = "Aceleración: esta PC no publica estado detallado.";
        Populate(_audio, snapshot.AudioOutputs, snapshot.AudioOutput, ref _outputs);
        Populate(_resolution, snapshot.VideoSettings?.Resolutions ?? [], snapshot.VideoSettings?.Resolution ?? "", ref _resolutions);
        Populate(_fps, snapshot.VideoSettings?.FrameRates ?? [], snapshot.VideoSettings?.Fps ?? "", ref _frameRates);
        Populate(_monitor, snapshot.VideoSettings?.Monitors ?? [], snapshot.VideoSettings?.Monitor ?? "", ref _monitors);
        Populate(_presentation, snapshot.VideoSettings?.PresentationModes ?? [], snapshot.VideoSettings?.PresentationMode ?? "", ref _presentationModes);
        if (snapshot.VideoSettings?.CanApplyTogether != true) _settingsStatus.Text = "Actualiza NOVORA PC para aplicar todos los ajustes juntos.";
        else if (snapshot.Media is { Recording: true } or { Starting: true }) _settingsStatus.Text = "Termina la grabación antes de guardar cambios.";
        else if (_settingsStatus.Text is "Termina la grabación antes de guardar cambios." or "Actualiza NOVORA PC para aplicar todos los ajustes juntos.")
            _settingsStatus.Text = "Elige todos los ajustes y guárdalos.";
        UpdateVisualSummary(snapshot);
        EnableActions(!_busy);
        bool pcRequestedVideo = snapshot.Engines?.VideoAuthorizationRequested == true;
        long pcRequestId = snapshot.Engines?.VideoAuthorizationRequestId ?? 0;
        bool lanVideo = _service?.Session.Current.UsesNativeVeLanVideo == true;
        bool canStartRequestedVideo = CanSendEngine(lanVideo ? "startVideoLan" : "startAppVideo");
        if (pcRequestedVideo && _pcVideoAuthorizationObservedId != pcRequestId &&
            canStartRequestedVideo && !_videoProjectionRequest.IsPending)
        {
            _status.Text = "La PC solicita autorización para iniciar VisionEngine.";
            _ = RequestVideoProjectionAsync(lanVideo, requestedByPc: true);
        }
        else if (pcRequestedVideo && _pcVideoAuthorizationObservedId != pcRequestId)
            _status.Text = "La PC está liberando la sesión anterior. VisionEngine se reanudará cuando esté listo.";
        else if (!pcRequestedVideo)
        {
            _pcVideoAuthorizationObservedId = null;
            RequestVideoProjectionOnTrustedLanLaunch();
        }
    }

    private void RequestVideoProjectionOnTrustedLanLaunch()
    {
        NLControlSessionState? state = _service?.Session.Current;
        if (!_active || state is not { Phase: NLControlSessionPhase.Connected } ||
            !state.UsesNativeVeLanVideo || _snapshot?.VideoRunning == true ||
            _snapshot?.Engines?.VideoCanStart != true || _videoProjectionRequest.IsPending ||
            _launchVideoProjectionPromptedGeneration == state.Generation)
            return;

        _launchVideoProjectionPromptedGeneration = state.Generation;
        _status.Text = "PC enlazada. Preparando la autorización de Android para iniciar VisionEngine.";
        _ = RequestVideoProjectionAsync(lan: true);
    }
    private void UpdateVisualSummary(NLControlSnapshot? snapshot)
    {
        var engines = snapshot?.Engines;
        bool connected = _service?.Session.Current.Phase == NLControlSessionPhase.Connected;
        bool usb = connected && _service?.Session.Current.Transport == "USB";
        bool lan = connected && _service?.Session.Current.Transport == "LAN";
        string link = FriendlyState(engines?.LinkState, engines?.LinkRunning == true, usb || lan ? "No detectado" : "Bloqueado");
        string video = FriendlyState(engines?.VideoState, snapshot?.VideoRunning == true, "No detectado");
        string exin = FriendlyState(engines?.ExInState, false, "No detectado");
        SetState(_homeLinkState, link); SetState(_engineLinkState, link);
        SetState(_settingsLinkState, link);
        SetState(_homeVideoState, video); SetState(_engineVideoState, video);
        SetState(_homeExInState, exin); SetState(_engineExInState, exin);
        SetState(_settingsExInState, exin);
        int active = new[] { link, video, exin }.Count(value => value == "Activo");
        _homePcState.Text = connected && snapshot is not null ? snapshot.PcName : "Sin conexión";
        _homePcState.SetTextColor(Color.ParseColor(connected ? Palette.Success : Palette.Muted));
        _homePhoneState.Text = LocalDeviceName();
        _homeTransportState.Text = connected
            ? usb ? "USB físico · activo" : lan ? "LAN cifrada · activa" : "Conectado"
            : "Sin conexión";
        _homeTransportState.SetTextColor(Color.ParseColor(connected ? Palette.Success : Palette.Muted));
        _homeConnectionChip.Text = connected ? usb ? "USB · ACTIVO" : lan ? "LAN · ACTIVA" : "CONECTADO" : "USB / LAN";
        _homeEngineCount.Text = $"{active}/3 activos";
        _homeEngineCount.SetTextColor(Color.ParseColor(active > 0 ? Palette.Success : Palette.Muted));
        foreach (var header in _headerStates)
        {
            header.Text = $"●  USB {(usb ? "ON" : "OFF")}  ·  {active}/3 Engines activos";
            header.SetTextColor(Color.ParseColor(usb ? "#55D98A" : "#FF5C65"));
        }
        _usbState.Text = usb ? "Detectado" : "No detectado";
        _usbState.SetTextColor(Color.ParseColor(usb ? "#55D98A" : "#FF5C65"));
        _phoneAudioState.Text = snapshot?.Media?.AudioIncluded == true ? "Incluido" : "No incluido";
        _phoneAudioState.SetTextColor(Color.ParseColor(snapshot?.Media?.AudioIncluded == true ? Palette.Success : Palette.Muted));
        bool hasAudioRoute = snapshot?.VideoRunning == true && !string.IsNullOrWhiteSpace(snapshot.ActiveAudioOutput);
        _internalAudioState.Visibility = hasAudioRoute ? ViewStates.Visible : ViewStates.Gone;
        if (hasAudioRoute)
        {
            _internalAudioState.Text = snapshot!.ActiveAudioOutput;
            _internalAudioState.SetTextColor(Color.ParseColor(Palette.Success));
        }
        _filesStatus.Text = snapshot?.FileSharing == true
            ? "Transferencia disponible. Abre Recibidos o Enviados para consultar contenido real."
            : "Transferencia no disponible hasta conectar con NOVORA PC.";
        UpdateEngineTelemetry(snapshot);
        UpdateFlightSummary(snapshot, usb, video, link, exin);
        UpdateExIn(snapshot?.ExIn);
    }
    private void UpdateEngineTelemetry(NLControlSnapshot? snapshot)
    {
        NLControlLinkTelemetry? link = snapshot?.Engines?.LinkTelemetry;
        bool linkFresh = link is not null && DateTimeOffset.UtcNow - link.SampledAtUtc <= TimeSpan.FromSeconds(2);
        _linkLatency.Text = linkFresh ? $"LATENCIA DE ENLACE\n{link!.RoundTripMs:F1} ms" : "LinkEngine detenido";
        SetMetric(_linkLoss, linkFresh, linkFresh ? $"PÉRDIDA\n{link!.PacketLossPercent:F1}%" : "");
        SetMetric(_linkThroughput, linkFresh, linkFresh ? $"TX / RX\n{link!.TxMbps:F1} / {link.RxMbps:F1} Mbps" : "");

        NLControlVisionTelemetry? vision = snapshot?.Engines?.VisionTelemetry;
        bool visionFresh = vision is not null && DateTimeOffset.UtcNow - vision.SampledAtUtc <= TimeSpan.FromSeconds(2);
        string observation = vision?.ObservationCompleted == true
            ? "MONITOR VE COMPLETADO"
            : vision?.ObservationActive == true
                ? $"MONITOR VE {TimeSpan.FromSeconds(vision.ObservationSecondsRemaining):mm\\:ss}"
                : "MONITOR VE PENDIENTE";
        _videoResolution.Text = visionFresh
            ? $"RESOLUCIÓN ACTIVA\n{vision!.Width} × {vision.Height} · {observation}"
            : "VisionEngine detenido";
        SetMetric(_videoFps, visionFresh,
            visionFresh ? $"FPS ACTIVOS\n{vision!.FramesPerSecond:F1} · DROP {vision.FramesDropped:N0}" : "");
        SetMetric(_videoBitrate, visionFresh,
            visionFresh ? $"BITRATE ACTIVO\n{vision!.MegabitsPerSecond:F1} Mbps · ERR {vision.DecodeErrors + vision.RenderErrors:N0}" : "");

        NLControlExIn? exIn = snapshot?.ExIn;
        NLControlExInTelemetry? input = exIn?.Telemetry;
        bool inputFresh = exIn?.Detected == true && input is not null &&
            DateTimeOffset.UtcNow - input.SampledAtUtc <= TimeSpan.FromSeconds(2);
        _engineExInIdentity.Text = exIn?.Detected == true
            ? $"{exIn.DeviceName} · {exIn.VidPid}" + (exIn.BatteryPercent >= 0 ? $" · {exIn.BatteryPercent}%" : "")
            : "Control desconectado";
        _engineExInPolling.Text = inputFresh ? $"REPORTES\n{input!.PollingHz:F1} Hz" : "Control desconectado";
        SetMetric(_engineExInProcessing, inputFresh, inputFresh ? $"PROCESAMIENTO EN PC\n{input!.PcProcessingMs:F2} ms" : "");
        SetMetric(_engineExInJitter, inputFresh, inputFresh ? $"JITTER\n{input!.JitterMs:F2} ms" : "");
    }

    private static void SetMetric(TextView view, bool visible, string text)
    {
        view.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
        if (visible) view.Text = text;
    }
    private void UpdateFlightSummary(NLControlSnapshot? snapshot, bool usb, string video, string link, string exin)
    {
        bool connected = _service?.Session.Current.Phase == NLControlSessionPhase.Connected && snapshot is not null;
        SetState(_flightConnectionState, connected ? (usb ? "USB listo" : "Conectado") : "Pendiente");

        bool internet = _flightMode == "Internet";
        string engine = internet ? link : video;
        SetState(_flightEngineState, engine);
        if (_flightMode == "Game") SetState(_flightControlState, exin);

        if (!connected)
        {
            _flightSummary.Text = "Primero conecta este teléfono con NOVORA PC.";
            _flightLaunch.Text = "INICIAR";
            _flightLaunch.Enabled = true;
            return;
        }

        if (internet)
        {
            bool running = snapshot?.Engines?.LinkRunning == true;
            bool transportReady = connected && snapshot?.Engines?.LinkCanStart == true;
            string transport = usb ? "USB" : "LAN cifrada";
            _flightSummary.Text = running
                ? "Internet por PC está activo en este teléfono."
                : transportReady
                    ? $"LinkEngine usará {transport} y solicitará permiso VPN."
                    : snapshot?.Engines?.LinkMessage ?? "Conecta USB o LAN y autoriza el control antes de iniciar Internet.";
            _flightLaunch.Text = "INICIAR";
            _flightLaunch.Enabled = true;
            return;
        }

        bool videoRunning = snapshot?.VideoRunning == true;
        bool controllerReady = snapshot?.ExIn?.Detected == true;
        _flightSummary.Text = _flightMode == "Game"
            ? videoRunning && controllerReady ? "Todo listo. El control está disponible para la sesión de juego."
                : controllerReady ? "El control está listo. Falta iniciar VisionEngine." : "Puedes iniciar video; ExIn aparecerá cuando PC detecte el control."
            : videoRunning ? "La sesión de video está activa." : "VisionEngine iniciará pantalla y audio con los ajustes actuales.";
        _flightLaunch.Text = "INICIAR";
        _flightLaunch.Enabled = true;
    }
    private void UpdateExIn(NLControlExIn? exIn)
    {
        if (_exInDevice is null) return;
        bool detected = exIn?.Detected == true;
        _exInHeaderState.Text = detected ? "Detectado" : "Sin detectar";
        _exInHeaderState.SetTextColor(Color.ParseColor(detected ? Palette.Success : Palette.Warning));
        _exInDevice.Text = exIn?.DeviceName ?? "Sin detectar";
        _exInFamily.Text = exIn?.Family switch { "DualShock4" => "DualShock 4", "Xbox" => "Xbox", null or "Unknown" => "Desconocida", var family => family };
        _exInConnection.Text = detected ? exIn!.ConnectionType : "Sin datos";
        _exInIdentity.Text = detected && !string.IsNullOrWhiteSpace(exIn!.Identity)
            ? "Perfil: " + exIn.Identity
            : "Identidad de perfil no disponible.";
        _exInCapabilities.Text = detected
            ? "Capacidades: " + string.Join(" · ", new[]
            {
                exIn!.SupportsGamepad ? "juego" : null,
                exIn.SupportsNavigation ? "navegación" : null,
                exIn.SupportsPointer ? "puntero" : null,
                exIn.SupportsTouchpad ? "touchpad" : null
            }.Where(value => value is not null))
            : "Capacidades no reportadas.";
        _exInDiagnostic.Text = detected
            ? $"Estado: {FriendlyHealth(exIn!.Health)}" +
              (string.IsNullOrWhiteSpace(exIn.DiagnosticMessage) ? "" : $"\n{exIn.DiagnosticMessage}")
            : "ExInEngine espera un control físico detectado por NOVORA PC.";
        _exInBattery.Text = detected
            ? exIn!.BatteryPercent >= 0
                ? $"Batería: {exIn.BatteryPercent}% · {FriendlyBattery(exIn.BatteryState)}"
                : $"Batería: {FriendlyBattery(exIn.BatteryState)}"
            : "Batería no reportada.";
        _exInCalibrationState.Text = exIn?.Calibrating == true
            ? "Calibrando desde PC · entradas bloqueadas"
            : exIn?.CanCalibrate == true ? "Disponible desde PC" : "No disponible";
        _exInCalibrationState.SetTextColor(Color.ParseColor(
            exIn?.Calibrating == true ? Palette.Warning : exIn?.CanCalibrate == true ? Palette.Success : Palette.Muted));
        _exInLive.Text = detected
            ? $"CRUDO       L {exIn!.LeftX,4},{exIn.LeftY,4}  R {exIn.RightX,4},{exIn.RightY,4}\n" +
              $"CORREGIDO   L {exIn.CorrectedLeftX,4},{exIn.CorrectedLeftY,4}  R {exIn.CorrectedRightX,4},{exIn.CorrectedRightY,4}\n\n" +
              $"GATILLOS    {exIn.LeftTrigger,3}/{exIn.RightTrigger,3}%  →  {exIn.CorrectedLeftTrigger,3}/{exIn.CorrectedRightTrigger,3}%\n\n" +
              $"BOTONES     {(exIn.Buttons.Length == 0 ? "Ninguno" : string.Join(" · ", exIn.Buttons))}"
            : "STICK L       --\nSTICK R       --\n\nGATILLO LT    --\nGATILLO RT    --\n\nBOTONES       --";
        _exInController.UpdateVE(exIn);
        _exInTriggerCalibration.UpdateVE(exIn);
        bool ready = detected && !_busy && exIn?.Transitioning != true && exIn?.Calibrating != true;
        _exInGameMode.Text = exIn?.Mode == "Game" ? "Juego · activo" : "Juego";
        _exInUiMode.Text = exIn?.Mode == "Ui" ? "Interfaz · activa" : "Interfaz";
        NLAndroidUIVisual.Button(_exInGameMode, exIn?.Mode == "Game");
        NLAndroidUIVisual.Button(_exInUiMode, exIn?.Mode == "Ui");
        _exInGameMode.Enabled = ready && exIn?.CanSetMode == true && exIn.Mode != "Game";
        _exInUiMode.Enabled = ready && exIn?.CanSetMode == true && exIn.Mode != "Ui";
    }

    private static string FriendlyHealth(string value) => value switch
    {
        "Healthy" => "Correcto",
        "Correctable" => "Requiere ajuste en PC",
        "Faulty" => "Posible falla física",
        _ => "Sin diagnóstico"
    };

    private static string FriendlyBattery(string value) => value switch
    {
        "OnBattery" => "en uso",
        "Charging" => "cargando",
        "Charged" => "cargado",
        "NoBattery" => "sin batería",
        _ => "no reportada"
    };
    private static string FriendlyState(string? state, bool active, string fallback)
    {
        if (active || string.Equals(state, "Active", StringComparison.OrdinalIgnoreCase)) return "Activo";
        if (string.Equals(state, "Detected", StringComparison.OrdinalIgnoreCase) || string.Equals(state, "Ready", StringComparison.OrdinalIgnoreCase)) return "Detectado";
        if (string.Equals(state, "Degraded", StringComparison.OrdinalIgnoreCase)) return "Degradado";
        if (string.Equals(state, "Critical", StringComparison.OrdinalIgnoreCase)) return "Crítico";
        if (string.Equals(state, "Blocked", StringComparison.OrdinalIgnoreCase)) return "Bloqueado";
        if (string.Equals(state, "Error", StringComparison.OrdinalIgnoreCase)) return "Error";
        return fallback;
    }
    private static void SetState(TextView view, string value)
    {
        view.Text = value;
        string color = value is "Activo" or "Detectado" or "USB listo" or "Conectado" ? "#55D98A" : value == "Degradado" ? "#E9A23B" : "#FF5C65";
        view.SetTextColor(Color.ParseColor(color));
    }
    private void Populate(Spinner spinner, NLControlOption[] options, string selected, ref NLControlOption[] stored)
    {
        _confirmed[spinner] = selected;
        string wanted = _drafts.GetValueOrDefault(spinner, selected);
        if (!options.Any(o => o.Value == wanted)) { _drafts.Remove(spinner); wanted = selected; }
        bool unchanged = stored.SequenceEqual(options);
        stored = options;
        _populating = true;
        spinner.Tag = new Java.Lang.String(string.Join("\n", options.Select(o => o.Value)));
        if (!unchanged || spinner.Adapter is null) {
        var adapter = new NLAndroidUIOptionAdapter(this, options.Select(o => o.Label).ToArray());
        spinner.Adapter = adapter;
        }
        int index = Array.FindIndex(options, o => o.Value == wanted);
        if (index >= 0) spinner.SetSelection(index);
        _populating = false;
    }
    private bool CanSendEngine(string action) => _active && !_busy && _snapshot?.Engines is { } engines && (action switch
    {
        "startAppVideo" => engines.VideoCanStart,
        "startVideoLan" => engines.VideoCanStart,
        "stopVideo" => engines.VideoCanStop,
        "restartVideo" => _snapshot?.VideoRunning == true && engines.VideoCanStop,
        "startLink" => engines.LinkCanStart,
        "stopLink" => engines.LinkCanStop,
        _ => false
    });
    private Task ToggleVideoEngineAsync()
    {
        if (_snapshot?.Engines?.VideoCanStop == true)
            return ConfirmStopEngineAsync("stopVideo", "VisionEngine", "La captura de video del teléfono seleccionado se detendrá.");
        return RequestVideoProjectionAsync(
            _service?.Session.Current.UsesNativeVeLanVideo == true);
    }
    private Task ToggleLinkEngineAsync()
    {
        if (_snapshot?.Engines?.LinkCanStop == true)
            return ConfirmStopEngineAsync("stopLink", "LinkEngine", "Se interrumpirá Internet compartido por la PC.");
        return SendEngineAsync("startLink");
    }
    private Task RestartVideoEngineAsync()
    {
        var service = _service;
        if (service is null || !CanSendEngine("restartVideo")) return Task.CompletedTask;
        long generation = service.Session.Current.Generation;
        new AlertDialog.Builder(this)!.SetTitle("Reiniciar VisionEngine")!
            .SetMessage("VisionEngine se detendrá brevemente. Android pedirá de nuevo permiso para compartir la pantalla antes de iniciarlo.")!
            .SetNegativeButton("Cancelar", (_, _) => { })!
            .SetPositiveButton("Continuar", async (_, _) =>
            {
                try
                {
                    var reply = await service.Session.SendAsync("restartVideo");
                    if (!_active || !ReferenceEquals(service, _service) || service.Session.Current.Generation != generation) return;
                    if (!reply.Success)
                    {
                        _status.Text = reply.Message;
                        return;
                    }
                    if (reply.Snapshot is { } snapshot) ApplyState(snapshot);
                    _status.Text = "Autoriza la captura de pantalla para reiniciar VisionEngine.";
                    await RequestVideoProjectionAsync(service.Session.Current.UsesNativeVeLanVideo);
                }
                catch (Exception)
                {
                    if (_active && ReferenceEquals(service, _service))
                        _status.Text = "No se pudo reiniciar VisionEngine. Revisa la conexión con la PC.";
                }
            })!.Show();
        return Task.CompletedTask;
    }
    private Task SendEngineAsync(string action)
    {
        if (!CanSendEngine(action)) return Task.CompletedTask;
        return action == "startLink" ? RequestVpnAsync() : SendAsync(action);
    }
    private Task RequestVideoProjectionAsync(bool lan = false, bool requestedByPc = false)
    {
        if (_videoProjectionRequest.IsPending) return Task.CompletedTask;
        NLControlSessionState? state = _service?.Session.Current;
        string action = lan ? "startVideoLan" : "startAppVideo";
        bool validTransport = lan
            ? state?.UsesNativeVeLanVideo == true
            : state?.Phase == NLControlSessionPhase.Connected &&
                (state.Transport == "USB" || state.Tunnel);
        if (_service is null || !validTransport || !CanSendEngine(action))
        {
            if (_active) _status.Text = lan
                ? "VisionEngine LAN requiere una conexión LAN autorizada y disponible."
                : "VisionEngine desde AppControl requiere una conexión USB autorizada.";
            return Task.CompletedTask;
        }
        _videoProjectionRequest.Begin(_service.InstanceId, state!.Generation, lan,
            requestedByPc ? _snapshot!.Engines!.VideoAuthorizationRequestId : null);
        if (requestedByPc)
        {
            _pcVideoAuthorizationObservedId = _snapshot!.Engines!.VideoAuthorizationRequestId;
            _launchVideoProjectionPromptedGeneration = state.Generation;
        }
        _videoProjectionAudioDecisionReady = false;
        if (OperatingSystem.IsAndroidVersionAtLeast(29) &&
            CheckSelfPermission(Android.Manifest.Permission.RecordAudio) != Permission.Granted)
        {
            _status.Text = "Autoriza audio para compartir el sonido del teléfono con VisionEngine.";
            try { RequestPermissions([Android.Manifest.Permission.RecordAudio], AudioPermissionRequest); }
            catch (Exception ex)
            {
                _videoProjectionRequest.Cancel();
                _status.Text = "Android no pudo abrir el permiso de audio. Intenta iniciar VisionEngine de nuevo.";
                Android.Util.Log.Warn("NOVORA-VE", "No se pudo solicitar audio: " + ex.GetType().Name);
            }
            return Task.CompletedTask;
        }
        OpenVideoProjectionPermission();
        return Task.CompletedTask;
    }

    private void OpenVideoProjectionPermission()
    {
        try
        {
            var manager = (MediaProjectionManager)GetSystemService(MediaProjectionService)!;
            Intent captureIntent = OperatingSystem.IsAndroidVersionAtLeast(34)
                ? manager.CreateScreenCaptureIntent(MediaProjectionConfig.CreateConfigForDefaultDisplay())
                : manager.CreateScreenCaptureIntent();
            StartActivityForResult(captureIntent, VideoProjectionRequest);
        }
        catch (Exception ex)
        {
            _videoProjectionRequest.Cancel();
            _status.Text = "Android no pudo abrir el permiso de captura. Intenta iniciar VisionEngine de nuevo.";
            Android.Util.Log.Warn("NOVORA-VE", "No se pudo solicitar captura: " + ex.GetType().Name);
        }
    }

    private async Task CompleteVideoProjectionAsync()
    {
        var service = _service;
        if (!_active || _busy || service is null) return;
        if (_videoProjectionAudioDecisionReady)
        {
            NLAndroidVideoProjectionResult readiness = _videoProjectionRequest.ValidateSession(
                service.InstanceId, service.Session.Current);
            if (readiness == NLAndroidVideoProjectionResult.Waiting) return;
            _videoProjectionAudioDecisionReady = false;
            if (readiness == NLAndroidVideoProjectionResult.Invalidated)
            {
                _status.Text = "La sesión cambió antes de iniciar la captura.";
                RenderSession();
                return;
            }
            OpenVideoProjectionPermission();
            return;
        }
        NLAndroidVideoProjectionResult result = _videoProjectionRequest.TryTake(
            service.InstanceId, service.Session.Current, out NLAndroidVideoProjectionGrant<Intent>? grant);
        if (result == NLAndroidVideoProjectionResult.Waiting) return;
        if (result == NLAndroidVideoProjectionResult.Invalidated)
        {
            _status.Text = "La sesión cambió antes de iniciar la captura.";
            RenderSession();
            return;
        }
        long generation = grant!.Generation;
        bool lan = grant.Lan;
        Intent projectionData = grant.Data;
        string action = lan ? "startVideoLan" : "startAppVideo";
        bool lanPrepared = false;
        try
        {
            bool playbackAudioAvailable = OperatingSystem.IsAndroidVersionAtLeast(29) &&
                CheckSelfPermission(Android.Manifest.Permission.RecordAudio) == Permission.Granted;
            NLControlReply reply = await service.Session.SendAsync(action,
                playbackAudioAvailable ? null : NLControlProtocol.VideoOnlyCapture);
            if (!reply.Success || string.IsNullOrWhiteSpace(reply.Value))
            {
                _status.Text = reply.Message;
                return;
            }
            lanPrepared = lan;
            if (!ReferenceEquals(service, _service) ||
                service.Session.Current.Generation != generation ||
                service.Session.Current.Phase != NLControlSessionPhase.Connected)
                throw new System.OperationCanceledException("La sesión cambió durante el inicio de captura.");
            if (lan)
            {
                var offer = System.Text.Json.JsonSerializer.Deserialize<NLControlVeLanOffer>(reply.Value)
                    ?? throw new InvalidDataException("La PC no entregó una oferta VE LAN válida.");
                NLAndroidServiceVeLanCapture.ConfigureControlHandling(
                    NLAndroidControlAccessibilityService.ExecuteWithResponseAsync,
                    NLAndroidControlAccessibilityService.ResetPointersAsync,
                    async () => { await service.Session.SendAsync("stopVideo").ConfigureAwait(false); },
                    Resource.Drawable.novora_notification);
                NLAndroidServiceVeLanCapture.Start(this, Result.Ok, projectionData, offer);
            }
            else
            {
                var offer = System.Text.Json.JsonSerializer.Deserialize<NLControlVideoSourceOffer>(reply.Value)
                    ?? throw new InvalidDataException("La PC no entregó una oferta de video válida.");
                NLAndroidVideoService.Start(this, Result.Ok, projectionData, offer);
            }
            _status.Text = playbackAudioAvailable
                ? "Captura autorizada. VisionEngine está iniciando."
                : "Captura autorizada sin audio interno. VisionEngine está iniciando.";
        }
        catch (Exception)
        {
            if (lanPrepared && service.Session.Current.Generation == generation &&
                service.Session.Current.Phase == NLControlSessionPhase.Connected)
            {
                try { await service.Session.SendAsync("stopVideo"); }
                catch { }
            }
            _status.Text = lan
                ? "No se pudo iniciar VisionEngine LAN. Revisa la red y VisionEngine en PC."
                : "No se pudo iniciar la captura de pantalla. Revisa USB y VisionEngine en PC.";
        }
    }
    private Task RequestVpnAsync()
    {
        if (_service is null || _service.Session.Current.Transport is not ("USB" or "LAN") || _vpnGeneration >= 0)
            return Task.CompletedTask;
        _vpnGeneration = _service.Session.Current.Generation;
        _vpnRevision = _snapshot!.Revision;
        string transport = _service.Session.Current.Transport;
        new AlertDialog.Builder(this)!.SetTitle($"Internet de PC por {transport}")!
            .SetMessage($"NOVORA enviará el tráfico IPv4 del teléfono por {transport} hacia la conexión de esta PC. Android pedirá permiso para crear una VPN; otra VPN activa puede ser sustituida. Detener LinkEngine cerrará este túnel.")!
            .SetNegativeButton("Cancelar", (_, _) => { _vpnGeneration = -1; _startVideoAfterLink = false; })!
            .SetOnCancelListener(new NLAndroidUICancelVpn(this))!
            .SetPositiveButton("Continuar", async (_, _) =>
            {
                try
                {
                    if (!_active || _service is null || _service.Session.Current.Generation != _vpnGeneration)
                    { _vpnGeneration = -1; return; }
                    var permission = Android.Net.VpnService.Prepare(this);
                    if (permission is not null) StartActivityForResult(permission, VpnRequest);
                    else { _vpnApproved = true; await CompleteVpnConsentAsync(); }
                }
                catch (Exception) { _vpnGeneration = -1; _vpnApproved = false; _startVideoAfterLink = false; _status.Text = "No se pudo preparar el permiso VPN."; }
            })!.Show();
        return Task.CompletedTask;
    }
    private sealed class NLAndroidUICancelVpn(NLAndroidUIActivity owner) : Java.Lang.Object, IDialogInterfaceOnCancelListener
    { public void OnCancel(IDialogInterface? dialog) { owner._vpnGeneration = -1; owner._startVideoAfterLink = false; } }
    private async Task CompleteVpnConsentAsync()
    {
        if (!_vpnApproved || !_active || _service is null) return;
        _vpnApproved = false;
        long generation = _vpnGeneration, revision = _vpnRevision;
        _vpnGeneration = -1;
        var service = _service;
        bool startVideo = _startVideoAfterLink;
        _startVideoAfterLink = false;
        if (service.Session.Current.Generation != generation || _snapshot?.Revision != revision || !CanSendEngine("startLink"))
        { _status.Text = "La sesión o el dispositivo cambió. Revisa el estado antes de iniciar Internet."; return; }
        try
        {
            var reply = await service.StartInternetAsync(generation);
            if (_active && ReferenceEquals(service, _service)) _status.Text = reply.Message;
            if (startVideo && _active && ReferenceEquals(service, _service) &&
                service.Session.Current.Generation == generation && _snapshot?.VideoRunning != true &&
                _snapshot?.Engines?.VideoCanStart == true)
                await RequestVideoProjectionAsync(service.Session.Current.UsesNativeVeLanVideo);
        }
        catch (Exception) { if (_active && ReferenceEquals(service, _service)) _status.Text = "No se pudo iniciar Internet por USB o LAN. Revisa LinkEngine en PC."; }
    }
    private Task ConfirmStopEngineAsync(string action, string name, string explanation)
    {
        var service = _service;
        if (service is null || !CanSendEngine(action)) return Task.CompletedTask;
        long generation = service.Session.Current.Generation;
        long revision = _snapshot!.Revision;
        string device = _snapshot.Engines!.DeviceName;
        new AlertDialog.Builder(this)!.SetTitle($"Detener {name}")!
            .SetMessage($"Teléfono seleccionado en PC: {(string.IsNullOrWhiteSpace(device) ? "ninguno" : device)}\n\n{explanation}")!
            .SetNegativeButton("Cancelar", (_, _) => { })!
            .SetPositiveButton("Detener", async (_, _) =>
            {
                if (!_active || !ReferenceEquals(service, _service) || generation != service.Session.Current.Generation) return;
                if (_snapshot?.Revision != revision)
                {
                    _status.Text = "El estado de PC cambió. Revisa el teléfono y el motor antes de confirmar de nuevo.";
                    return;
                }
                await SendEngineAsync(action);
            })!.Show();
        return Task.CompletedTask;
    }
    private async Task ConfirmRestartAsync()
    {
        var service = _service;
        if (service is null || _busy || !_restart.Enabled) return;
        string Selected(Spinner spinner, NLControlOption[] options) => spinner.SelectedItemPosition is var i && i >= 0 && i < options.Length
            ? options[i].Value : throw new InvalidOperationException("Selecciona todos los valores antes de guardar.");
        long generation = service.Session.Current.Generation;
        try {
            var changes = new NLControlVideoChanges(Selected(_profile, _profiles), Selected(_bitrate, _bitrates),
                Selected(_resolution, _resolutions), Selected(_fps, _frameRates), Selected(_audio, _outputs),
                Selected(_monitor, _monitors), Selected(_presentation, _presentationModes));
            _settingsStatus.Text = "Guardando ajustes…";
            var reply = await service.Session.SendAsync("applyVideoSettings", System.Text.Json.JsonSerializer.Serialize(changes));
            if (!_active || !ReferenceEquals(service, _service) || service.Session.Current.Generation != generation)
                return;
            if (reply.Success) _drafts.Clear();
            if (reply.Snapshot is { } snapshot) ApplyState(snapshot);
            _settingsStatus.Text = reply.Message;
            if (reply.Success)
            {
                _status.Text = "Cambios guardados. Inicia VisionEngine cuando estés listo.";
                ShowPage(_dashboardPage);
            }
        } catch (Exception)
        {
            if (_active) _settingsStatus.Text = "No se guardaron los cambios. Revisa la conexión y el estado de VE en PC.";
        }
    }
    private async Task SendAsync(string action, string? value = null)
    {
        var service = _service;
        if (_busy || service is null || _snapshot is null) return;
        try
        {
            var reply = action == "stopLink" ? await service.StopInternetAsync() : await service.Session.SendAsync(action, value);
            if (reply.Success && action == "stopVideo")
            {
                NLAndroidVideoService.Stop(this);
                if (service.Session.Current.Transport == "LAN") NLAndroidServiceVeLanCapture.Stop(this);
            }
            if (_active && ReferenceEquals(_service, service)) _status.Text = reply.Message;
        }
        catch (Exception)
        {
            if (_active && ReferenceEquals(_service, service)) RenderSession();
        }
    }
}
