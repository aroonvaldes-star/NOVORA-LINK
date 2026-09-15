using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Graphics;
using Android.Graphics.Drawables;
using NOVORA.Control;
using NOVORA.AndroidService;
using Android.Content.PM;
using Resource = NOVORA.AndroidApp.Resource;

namespace NOVORA.AndroidUI;

[Activity(Name = "com.novora.appcontrol.MainActivity", Label = "NOVORA", MainLauncher = true,
    Exported = true, LaunchMode = LaunchMode.SingleTop, Theme = "@android:style/Theme.Material.NoActionBar")]
public sealed class NLAndroidUIActivity : Activity
{
    private LinearLayout _body = null!;
    private ScrollView _connectionPage = null!, _dashboardPage = null!, _settingsPage = null!;
    private TextView _status = null!;
    private TextView _current = null!;
    private TextView _engineState = null!;
    private Button _startVideo = null!, _stopVideo = null!, _startLink = null!, _stopLink = null!;
    private EditText _code = null!;
    private Spinner _bitrate = null!, _profile = null!, _audio = null!;
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
    private string? _pendingInvitation;
    private string? _selectedDiscoveredHost;
    private const int ScanRequest = 214;
    private const int VpnRequest = 216;
    private long _vpnGeneration = -1, _vpnRevision = -1;
    private bool _vpnApproved;
    private Spinner _resolution = null!, _fps = null!;
    private NLControlOption[] _resolutions = [], _frameRates = [];
    private Button _remember = null!, _restart = null!, _cancel = null!, _capture = null!, _record = null!, _sendFiles = null!;
    private readonly Dictionary<Spinner, string> _drafts = new();
    private readonly Dictionary<Spinner, string> _confirmed = new();
    private bool _populating;
    private bool _returnToSettings;
    private bool _floatWhenStarted;
    private long _uiGeneration = -1;
    private string? _requestedPage;
    private long _requestedGeneration = -1, _requestedRevision = -1;
    private string? _uiServiceId;


    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        _requestedPage = Intent?.GetStringExtra("novora.page");
        _requestedGeneration = Intent?.GetLongExtra("novora.generation", -1) ?? -1;
        _requestedRevision = Intent?.GetLongExtra("novora.revision", -1) ?? -1;
        Intent?.RemoveExtra("novora.page");
        Window?.SetSoftInputMode(SoftInput.AdjustResize);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Color.ParseColor("#10191E"));
        root.SetOnApplyWindowInsetsListener(new NLAndroidUIInsets());
        SetContentView(root);
        var pages = new FrameLayout(this);
        root.AddView(pages, new LinearLayout.LayoutParams(-1, 0, 1));
        _status = new TextView(this) { Text = "Preparando conexión…", TextSize = 12 };
        _status.SetTextColor(Color.ParseColor("#A9BCC5"));
        _status.SetPadding(Dp(20), Dp(8), Dp(20), Dp(10));
        _status.AccessibilityLiveRegion = AccessibilityLiveRegion.Polite;
        root.AddView(_status);

        _connectionPage = CreatePage(pages);
        Brand(134);
        var welcome = new ImageView(this);
        welcome.SetImageResource(Resource.Drawable.novora_welcome);
        welcome.SetScaleType(ImageView.ScaleType.FitCenter);
        welcome.ContentDescription = "Bienvenido a NOVORA-LINK";
        _body.AddView(welcome, new LinearLayout.LayoutParams(-1, Dp(100)));
        var intro = Label("Conecta tu Android con tu PC", 18);
        intro.Gravity = GravityFlags.Center;
        intro.SetPadding(0, Dp(18), 0, Dp(20));
        _search = Button("Buscar NOVORA PC", SearchAsync);
        var connectionBody = _body;
        var usb = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        OutlineButton("Conectar por USB", () => { usb.Visibility = usb.Visibility == ViewStates.Visible ? ViewStates.Gone : ViewStates.Visible; return Task.CompletedTask; });
        _scan = OutlineButton("Escanear código QR", ScanAsync);
        connectionBody.AddView(usb);
        _body = usb;
        Label("Pulsa Preparar control USB en HOME de PC e introduce su código temporal.", 13);
        _code = new EditText(this) { Hint = "Código de 8 dígitos", InputType = Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberVariationPassword };
        _code.SetTextColor(Color.White);
        _code.SetHintTextColor(Color.ParseColor("#A8B1B9"));
        _code.SetFilters([new Android.Text.InputFilterLengthFilter(8)]);
        _body.AddView(_code, new LinearLayout.LayoutParams(-1, Dp(52)));
        _connect = Button("Vincular con la PC", ConnectAsync);
        _body = connectionBody;
        OutlineButton("Más opciones", () => {
            new AlertDialog.Builder(this)!.SetTitle("Conexión")!.SetItems(new[] { "PC guardadas", "Pegar invitación" }, async (_, e) => {
                if (e.Which == 0) await SavedPcsAsync(); else await PasteInvitationAsync();
            })!.SetNegativeButton("Cerrar", (_, _) => {})!.Show(); return Task.CompletedTask;
        });
        _cancel = OutlineButton("Cancelar conexión", DisconnectAsync);
        _cancel.Visibility = ViewStates.Gone;
        var footer = Label("NOVORA Android · Compatible con PC 1.4", 12);
        footer.Gravity = GravityFlags.Center;

        _dashboardPage = CreatePage(pages);
        Brand(66);
        var brand = Label("N O V O R A   ·   APP CONTROL", 12);
        brand.Gravity = GravityFlags.Center;
        Card(() => {
            _current = Label("Esperando confirmación de PC…", 17);
            OutlineButton("Cambiar PC", DisconnectAsync);
        });
        Card(() => {
            Label("VisionEngine", 23);
            Muted("Pantalla, audio y control");
            _startVideo = Button("Iniciar VisionEngine", () => SendEngineAsync("startVideo"));
            _stopVideo = OutlineButton("Detener VisionEngine", () => ConfirmStopEngineAsync("stopVideo", "VisionEngine", "La captura de video del teléfono seleccionado se detendrá."));
        });
        Card(() => {
            Label("LinkEngine", 23);
            Muted("Internet mediante NOVORA PC · USB");
            _startLink = Button("Iniciar LinkEngine", () => SendEngineAsync("startLink"));
            _stopLink = OutlineButton("Detener LinkEngine", () => ConfirmStopEngineAsync("stopLink", "LinkEngine", "Se interrumpirá Internet compartido por la PC."));
            _engineState = Muted("Conecta para consultar los motores.");
        });
        Card(() => {
            _sendFiles = OutlineButton("Enviar archivos a PC", () => { StartActivity(new Intent(this, typeof(NLAndroidUIShareActivity))); return Task.CompletedTask; });
            OutlineButton("Mis archivos NOVORA", () => { StartActivity(new Intent(this, typeof(NLAndroidUIFilesActivity))); return Task.CompletedTask; });
            _capture = OutlineButton("Guardar captura en PC", () => SendAsync("capture"));
            _record = OutlineButton("Grabar en PC", () => SendAsync(_snapshot?.Media?.Recording == true ? "stopRecording" : "startRecording"));
            OutlineButton("Configuraciones", () => { ShowPage(_settingsPage); return Task.CompletedTask; });
            OutlineButton("Manual de usuario", () => InfoAsync("Manual de usuario", "CONEXIÓN\nEn PC, prepara el control USB y escribe el código en Android. Para LAN, prepara el enlace LAN en PC y busca el equipo o escanea su QR.\n\nVISIONENGINE\nInicia o detén la captura desde Control.\n\nLINKENGINE\nRequiere control USB autorizado. Acepta el permiso VPN para compartir Internet de PC.\n\nAJUSTES\nPerfil y bitrate se guardan en PC. Reinicia la captura con Aplicar video para usar los nuevos valores. La salida de audio se aplica mediante Windows."));
        });
        _remember = OutlineButton("Recordar esta PC", RememberPcAsync);
        ActionButton("Actualizar estado", () => SendAsync("get"));

        _settingsPage = CreatePage(pages);
        OutlineButton("Volver a Control", () => { ShowPage(_dashboardPage); return Task.CompletedTask; });
        Label("Configuraciones", 26);
        Muted("Control de NOVORA PC · valores confirmados por Windows");
        Card(() => {
            Label("VIDEO", 15);
            _profile = Selector("Perfil");
            ActionButton("Guardar perfil", () => ChangeAsync("profile", _profiles, _profile));
            _bitrate = Selector("Bitrate");
            ActionButton("Guardar bitrate", () => ChangeAsync("bitrate", _bitrates, _bitrate));
            _resolution = Selector("Resolución · máximo del lado mayor");
            ActionButton("Guardar resolución", () => ChangeAsync("resolution", _resolutions, _resolution));
            _fps = Selector("Límite de FPS");
            ActionButton("Guardar FPS", () => ChangeAsync("fps", _frameRates, _fps));
            _restart = OutlineButton("Aplicar video…", ConfirmRestartAsync);
            Muted("Aplicar video reinicia la captura con el perfil y bitrate guardados.");
        });
        Card(() => {
            Label("AUDIO", 15);
            _audio = Selector("Salida de audio del PC");
            ActionButton("Aplicar salida de audio", () => ChangeAsync("audio", _outputs, _audio));
        });
        Card(() => {
            OutlineButton("Red y VPN", () => InfoAsync("Red y VPN", "Conexión: " + (_service?.Session.Current.Transport ?? "sin conexión") + "\n\n" + NOVORA.AndroidVpn.NLAndroidVpnService.Status));
            OutlineButton("Control flotante y aplicaciones favoritas", () => { StartActivity(new Intent(this, typeof(NLAndroidUIFloatingSettingsActivity))); return Task.CompletedTask; });
            OutlineButton("Integración y privacidad", () => InfoAsync("Integración y privacidad", "El control requiere autorización de PC. Puedes consultar o eliminar equipos recordados en PC guardadas. El permiso VPN permite compartir Internet por USB. Los demás ajustes de privacidad de PC todavía no están disponibles aquí."));
        });
        ShowPage(_connectionPage);
        EnableActions(false);
        _returnToSettings = state?.GetBoolean("settings") == true;
        foreach (var (spinner, key) in new[] { (_profile,"profile"), (_bitrate,"bitrate"), (_audio,"audio"), (_resolution,"resolution"), (_fps,"fps") })
            if (state?.GetString("draft." + key) is { } value) _drafts[spinner] = value;
        _uiGeneration = state?.GetLong("generation", -1) ?? -1;
        _uiServiceId = state?.GetString("serviceId");
        _floatWhenStarted = state?.GetBoolean("floatPending") == true;
        _vpnGeneration = state?.GetLong("vpnGeneration", -1) ?? -1;
        _vpnRevision = state?.GetLong("vpnRevision", -1) ?? -1;
    }

    private void Brand(int height)
    {
        var image = new ImageView(this) { ContentDescription = "NOVORA" };
        image.SetImageResource(Resource.Drawable.novora_logo);
        image.SetScaleType(ImageView.ScaleType.FitCenter);
        var layout = new LinearLayout.LayoutParams(-1, Dp(height));
        layout.SetMargins(0, Dp(12), 0, Dp(4));
        _body.AddView(image, layout);
    }
    private TextView Muted(string text)
    {
        var label = Label(text, 13);
        label.SetTextColor(Color.ParseColor("#ADBDC5"));
        return label;
    }
    private void Card(Action build)
    {
        var parent = _body;
        var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var background = new GradientDrawable();
        background.SetColor(Color.ParseColor("#192329"));
        background.SetCornerRadius(Dp(12));
        background.SetStroke(Dp(1), Color.ParseColor("#34434B"));
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
        var background = new GradientDrawable();
        background.SetColor(Color.ParseColor("#111B20"));
        background.SetCornerRadius(Dp(9));
        background.SetStroke(Dp(1), Color.ParseColor("#52636D"));
        button.Background = background;
        button.SetTextColor(new Android.Content.Res.ColorStateList(
            [new[] { -Android.Resource.Attribute.StateEnabled }, Array.Empty<int>()],
            [Color.ParseColor("#64747D").ToArgb(), Color.ParseColor("#E7EFF4").ToArgb()]));
        return button;
    }
    private Task InfoAsync(string title, string message)
    {
        new AlertDialog.Builder(this)!.SetTitle(title)!.SetMessage(message)!.SetPositiveButton("Cerrar", (_, _) => { })!.Show();
        return Task.CompletedTask;
    }


    protected override void OnStart()
    {
        base.OnStart();
        _active = true;
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
                owner._drafts.Clear(); owner._confirmed.Clear(); owner._floatWhenStarted = false;
                owner._vpnGeneration = -1; owner._vpnApproved = false;
            }
            owner._uiServiceId = control.Owner.InstanceId;
            owner._service.SetUiForeground(true);
            owner._service.Session.Changed += owner.SessionChanged;
            owner._service.StatusChanged += owner.ServiceStatusChanged;
            owner.RenderSession();
            _ = owner.CompleteVpnConsentAsync();
            if (owner._pendingInvitation is { } pending)
            {
                owner._pendingInvitation = null;
                owner.ConfirmInvitation(pending);
            }
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
        if (_uiGeneration >= 0 && state.Generation != _uiGeneration) { _drafts.Clear(); _confirmed.Clear(); _returnToSettings = false; }
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
            if (_connectionPage.Visibility == ViewStates.Visible) ShowPage(_returnToSettings ? _settingsPage : _dashboardPage);
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
            if (_floatWhenStarted && snapshot.VideoRunning) {
                _floatWhenStarted = false;
                if (NLAndroidUIFloatingPreferences.IsEnabled(this) && Android.Provider.Settings.CanDrawOverlays(this)) MoveTaskToBack(true);
            }
        }
        else
        {
            _snapshot = null;
            _floatWhenStarted = false;
            EnableActions(false);
            _current.Text = "Sin sesión confirmada. Prepara un nuevo enlace USB o LAN en HOME de PC.";
            ShowPage(_connectionPage);
        }
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == VpnRequest)
        {
            _vpnApproved = resultCode == Result.Ok;
            if (!_vpnApproved) { _vpnGeneration = -1; _status.Text = "VPN cancelada. LinkEngine no se inició."; }
            else _ = CompleteVpnConsentAsync();
            return;
        }
        if (requestCode != ScanRequest || resultCode != Result.Ok) return;
        string? text = data?.GetStringExtra("invitation");
        if (string.IsNullOrEmpty(text)) return;
        if (_active && _service is not null) ConfirmInvitation(text);
        else _pendingInvitation = text;
    }
    protected override void OnStop()
    {
        _active = false;
        _service?.SetUiForeground(false);
        _discovery?.Cancel();
        _pendingConnect = null;
        DetachSession();
        if (_bound && _connection is not null) UnbindService(_connection);
        _bound = false;
        _connection = null;
        base.OnStop();
    }
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        _requestedPage = intent?.GetStringExtra("novora.page");
        _requestedGeneration = intent?.GetLongExtra("novora.generation", -1) ?? -1;
        _requestedRevision = intent?.GetLongExtra("novora.revision", -1) ?? -1;
        intent?.RemoveExtra("novora.page");
        if (_active && _service is not null) RenderSession();
    }
    protected override void OnSaveInstanceState(Bundle state)
    {
        state.PutBoolean("settings", _settingsPage.Visibility == ViewStates.Visible);
        state.PutLong("generation", _uiGeneration);
        state.PutString("serviceId", _uiServiceId);
        state.PutBoolean("floatPending", _floatWhenStarted);
        state.PutLong("vpnGeneration", _vpnGeneration);
        state.PutLong("vpnRevision", _vpnRevision);
        foreach (var (spinner, key) in new[] { (_profile,"profile"), (_bitrate,"bitrate"), (_audio,"audio"), (_resolution,"resolution"), (_fps,"fps") })
            if (_drafts.TryGetValue(spinner, out var value)) state.PutString("draft." + key, value);
        base.OnSaveInstanceState(state);
    }
#pragma warning disable CS0672, CA1422
    public override void OnBackPressed()
    {
        if (_settingsPage.Visibility == ViewStates.Visible) ShowPage(_dashboardPage);
        else MoveTaskToBack(true);
    }
#pragma warning restore CS0672, CA1422

    private ScrollView CreatePage(FrameLayout host)
    {
        var scroll = new ScrollView(this) { FillViewport = true };
        _body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _body.SetPadding(Dp(18), Dp(8), Dp(18), Dp(20));
        scroll.AddView(_body, new ScrollView.LayoutParams(-1, -2));
        host.AddView(scroll, new FrameLayout.LayoutParams(-1, -1));
        return scroll;
    }
    private void ShowPage(ScrollView page)
    {
        _connectionPage.Visibility = ReferenceEquals(page, _connectionPage) ? ViewStates.Visible : ViewStates.Gone;
        _dashboardPage.Visibility = ReferenceEquals(page, _dashboardPage) ? ViewStates.Visible : ViewStates.Gone;
        _settingsPage.Visibility = ReferenceEquals(page, _settingsPage) ? ViewStates.Visible : ViewStates.Gone;
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
        label.SetTextColor(Color.ParseColor("#F2F5F7"));
        label.SetPadding(0, Dp(10), 0, Dp(10));
        _body.AddView(label);
        return label;
    }
    private Button Button(string text, Func<Task> action)
    {
        var button = new Button(this) { Text = text };
        button.SetTextColor(new Android.Content.Res.ColorStateList(
            [new[] { -Android.Resource.Attribute.StateEnabled }, Array.Empty<int>()],
            [Color.ParseColor("#A8B1B9").ToArgb(), Color.ParseColor("#101923").ToArgb()]));
        button.SetAllCaps(false);
        button.TextSize = 15;
        var background = new GradientDrawable();
        background.SetColor(Color.ParseColor("#00BDEB"));
        background.SetCornerRadius(Dp(10));
        var disabled = new GradientDrawable();
        disabled.SetColor(Color.ParseColor("#303B45"));
        disabled.SetCornerRadius(Dp(10));
        var states = new StateListDrawable();
        states.AddState([-Android.Resource.Attribute.StateEnabled], disabled);
        states.AddState(Array.Empty<int>(), background);
        button.Background = states;
        button.SetMinHeight(Dp(52));
        var layout = new LinearLayout.LayoutParams(-1, -2);
        layout.SetMargins(0, Dp(6), 0, Dp(6));
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
        Label(title, 16);
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
        spinner.SetBackgroundColor(Color.ParseColor("#303B45"));
        _body.AddView(spinner, new LinearLayout.LayoutParams(-1, Dp(48)));
        return spinner;
    }
    private void EnableActions(bool enabled)
    {
        foreach (var button in _actions) button.Enabled = enabled;
        _bitrate.Enabled = _profile.Enabled = _audio.Enabled = enabled;
        _resolution.Enabled = _fps.Enabled = enabled && _snapshot?.VideoSettings is not null;
        _remember.Enabled = enabled && _service?.CanRememberCurrentPc == true;
        _restart.Enabled = enabled && _snapshot?.VideoRunning == true;
        _capture.Enabled = enabled && _snapshot?.Media?.CanCapture == true;
        _record.Enabled = enabled && (_snapshot?.Media is { Recording: true } or { CanRecord: true });
        _record.Text = _snapshot?.Media?.Starting == true ? "Cancelar inicio de grabación" : _snapshot?.Media?.Recording == true ? "Detener y guardar grabación" : "Grabar en PC";
        _sendFiles.Enabled = enabled && _snapshot?.FileSharing == true;
        var engines = _snapshot?.Engines;
        _startVideo.Enabled = enabled && engines?.VideoCanStart == true;
        _stopVideo.Enabled = enabled && engines?.VideoCanStop == true;
        _startLink.Enabled = enabled && engines?.LinkCanStart == true && _service?.Session.Current.Transport == "USB";
        _stopLink.Enabled = enabled && engines?.LinkCanStop == true;
        _startVideo.Visibility = engines?.VideoCanStop == true ? ViewStates.Gone : ViewStates.Visible;
        _stopVideo.Visibility = engines?.VideoCanStop == true ? ViewStates.Visible : ViewStates.Gone;
        _startLink.Visibility = engines?.LinkCanStop == true ? ViewStates.Gone : ViewStates.Visible;
        _stopLink.Visibility = engines?.LinkCanStop == true ? ViewStates.Visible : ViewStates.Gone;
        if (!enabled && _snapshot is null)
            _engineState.Text = "Sin estado confirmado de los motores. Conecta para consultar NOVORA PC.";
    }

    private Task ConnectAsync()
    {
        if (_connecting || _service is null || !_active) return Task.CompletedTask;
        string code = _code.Text?.Trim() ?? "";
        if (code.Length != 8 || !code.All(char.IsAsciiDigit))
        { _status.Text = "Introduce los 8 dígitos del código mostrado en PC."; return Task.CompletedTask; }
        _code.Text = "";
        return RequestConnectAsync(() => _service!.ConnectUsbAsync(code));
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
                .SetMessage("Permite las notificaciones para ver tu conexión y desconectar sin abrir la app. Si no las permites, Android puede ocultar ese panel; podrás desconectar dentro de NOVORA.")!
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
        if (requestCode != NotificationPermissionRequest) return;
        var pending = _pendingConnect;
        _pendingConnect = null;
        if (_active && _service is not null && pending is not null &&
            _service.Session.Current.Generation == _pendingConnectGeneration) await RunConnectAsync(pending);
    }
    private async Task RunConnectAsync(Func<Task> connect)
    {
        if (!_active || _service is null) return;
        try { await connect(); }
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
        using var cancellation = new CancellationTokenSource();
        _discovery = cancellation;
        _search.Enabled = false;
        _selectedDiscoveredHost = null;
        _status.Text = "Buscando NOVORA PC durante 3 segundos…";
        try
        {
            var peers = await NLControlLanDiscovery.SearchAsync(cancellation.Token);
            if (!_active || cancellation.IsCancellationRequested) return;
            if (peers.Count == 0)
            {
                _status.Text = "No se encontraron equipos. Revisa la misma red y el enlace LAN en HOME. Puedes escanear el QR aunque la red bloquee la detección.";
                return;
            }
            // Discovery replies are untrusted hints. No code, identity or certificate is trusted here.
            new AlertDialog.Builder(this)!.SetTitle("Equipos anunciados en la red")!
                .SetItems(peers.Select(p => $"{SafePeerName(p.Name)} · {System.Net.IPAddress.Parse(p.Host)}").ToArray(), (_, args) =>
                {
                    _selectedDiscoveredHost = System.Net.IPAddress.Parse(peers[args.Which].Host).ToString();
                    _status.Text = $"Equipo seleccionado: {_selectedDiscoveredHost}. Escanea el QR que muestra esa PC para verificar el enlace.";
                })!
                .SetNegativeButton("Cerrar", (_, _) => { })!.Show();
        }
        catch (System.OperationCanceledException) { }
        catch (Exception) { if (_active) _status.Text = "No se pudo buscar en esta red. Puedes usar el QR de PC o USB."; }
        finally
        {
            if (ReferenceEquals(_discovery, cancellation)) _discovery = null;
            _search.Enabled = true;
        }
    }

    private Task ScanAsync()
    {
        if (!_connecting && _active) StartActivityForResult(new Intent(this, typeof(NLAndroidUIQrActivity)), ScanRequest);
        return Task.CompletedTask;
    }

    private Task PasteInvitationAsync()
    {
        var entry = new EditText(this) { Hint = "Contenido NOVORA del QR", InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationVisiblePassword };
        entry.SetFilters([new Android.Text.InputFilterLengthFilter(4096)]);
        new AlertDialog.Builder(this)!.SetTitle("Pegar invitación LAN")!
            .SetMessage("Copia el contenido del QR desde tu NOVORA PC. Es una clave temporal; no lo compartas.")!
            .SetView(entry)!
            .SetNegativeButton("Cancelar", (_, _) => { entry.Text = ""; })!
            .SetPositiveButton("Continuar", (_, _) =>
            {
                string text = entry.Text?.Trim() ?? "";
                entry.Text = "";
                ConfirmInvitation(text);
            })!.Show();
        return Task.CompletedTask;
    }

    private void ConfirmInvitation(string text)
    {
        if (!_active || _connecting || _service is null) return;
        long generation = _service.Session.Current.Generation;
        NLControlLanInvitation invitation;
        try { invitation = NLControlLanInvitation.Parse(text); }
        catch (Exception) { _status.Text = "QR inválido o vencido. Prepara una nueva invitación en NOVORA PC."; return; }
        if (_selectedDiscoveredHost is { } host && !System.Net.IPAddress.Parse(host).Equals(System.Net.IPAddress.Parse(invitation.Host)))
        {
            _status.Text = "El QR no corresponde a la dirección seleccionada. Vuelve a buscar o selecciona la PC correcta.";
            return;
        }
        new AlertDialog.Builder(this)!.SetTitle("Conectar con NOVORA PC")!
            .SetMessage($"PC: {System.Net.IPAddress.Parse(invitation.Host)}:{invitation.Port}\n\nEscanea únicamente el QR mostrado en tu NOVORA PC. Al conectar autorizas esta sesión de control. La conexión verifica la identidad incluida en el QR.")!
            .SetNegativeButton("Cancelar", (_, _) => { })!
            .SetPositiveButton("Conectar", async (_, _) =>
            {
                if (_active && _service is not null && _service.Session.Current.Generation == generation)
                    await ConnectLanAsync(invitation);
            })!.Show();
    }

    private Task ConnectLanAsync(NLControlLanInvitation invitation) =>
        RequestConnectAsync(() => _service!.ConnectLanAsync(invitation));

    private async Task DisconnectAsync()
    {
        _selectedDiscoveredHost = null;
        _drafts.Clear();
        _pendingConnect = null;
        _vpnGeneration = -1;
        _vpnApproved = false;
        _pendingInvitation = null;
        if (_service is not null) await _service.DisconnectAsync();
    }

    private Task RememberPcAsync()
    {
        var service = _service;
        if (!_active || service is null) return Task.CompletedTask;
        long generation = service.Session.Current.Generation;
        new AlertDialog.Builder(this)!.SetTitle("Recordar esta PC")!
            .SetMessage("Esta PC podrá reconocer este teléfono en próximas conexiones LAN sin pedir otro QR. NOVORA guardará la autorización cifrada en el teléfono. Puedes olvidar la PC aquí y revocar el teléfono desde HOME de PC.")!
            .SetNegativeButton("Cancelar", (_, _) => { })!
            .SetPositiveButton("Recordar", async (_, _) =>
            {
                if (!_active || !ReferenceEquals(service, _service) || generation != service.Session.Current.Generation) return;
                try { await service.RememberCurrentPcAsync(); if (_active) _status.Text = "PC guardada. Ya puedes volver a conectar desde PC guardadas."; }
                catch (Exception ex) when (ex is InvalidOperationException or System.IO.InvalidDataException)
                { if (_active) _status.Text = ex.Message; }
                catch (Exception) { if (_active) _status.Text = "No se guardó la autorización. Revisa la sesión; si PC registró el teléfono, puedes revocarlo desde HOME."; }
            })!.Show();
        return Task.CompletedTask;
    }
    private Task SavedPcsAsync()
    {
        if (!_active || _service is null) return Task.CompletedTask;
        IReadOnlyList<NLControlTrustedPc> peers;
        try { peers = _service.GetSavedPcs(); }
        catch (Exception) { _status.Text = "No se pueden leer las PC guardadas. No se reemplazaron. Para recuperar el almacén, borra los datos de NOVORA en Android y vuelve a enlazar."; return Task.CompletedTask; }
        if (peers.Count == 0) { _status.Text = "Aún no hay PC guardadas. Conecta mediante un QR nuevo y pulsa Recordar esta PC."; return Task.CompletedTask; }
        new AlertDialog.Builder(this)!.SetTitle("PC guardadas")!
            .SetItems(peers.Select(p => $"{SafePeerName(p.PcName)} · {p.Host}").ToArray(), (_, args) => ShowSavedPc(peers[args.Which]))!
            .SetNegativeButton("Cerrar", (_, _) => { })!.Show();
        return Task.CompletedTask;
    }
    private void ShowSavedPc(NLControlTrustedPc peer)
    {
        if (!_active || _service is null) return;
        new AlertDialog.Builder(this)!.SetTitle(SafePeerName(peer.PcName))!
            .SetMessage($"Dirección guardada: {peer.Host}. PC debe tener habilitado el control LAN. Si cambió de dirección, prepara otro QR.\n\nOlvidar elimina la autorización de este teléfono y desconecta esta PC. No revoca la autorización almacenada en PC; revócala desde HOME de esa computadora.")!
            .SetPositiveButton("Conectar", async (_, _) =>
            { if (_active && _service is not null) await RequestConnectAsync(() => _service!.ConnectTrustedAsync(peer)); })!
            .SetNeutralButton("Olvidar", async (_, _) =>
            {
                if (!_active || _service is null) return;
                _pendingConnect = null;
                try { await _service.ForgetPcAsync(peer.Fingerprint); if (_active) _status.Text = "PC olvidada en este teléfono. Puedes revocar también el teléfono desde HOME de PC."; }
                catch (Exception) { if (_active) _status.Text = "No se pudo eliminar la PC guardada. Revisa el almacenamiento de NOVORA."; }
            })!
            .SetNegativeButton("Cerrar", (_, _) => { })!.Show();
    }

    private void ApplyState(NLControlSnapshot snapshot)
    {
        if (_snapshot is not null && snapshot.Revision < _snapshot.Revision) return;
        _snapshot = snapshot;
        _current.Text = $"{snapshot.PcName}\nNOVORA-LINK {snapshot.PcVersion}\nConectado por {_service?.Session.Current.Transport}";
        if (snapshot.Engines is { } target) _current.Text += "\nTeléfono: " + target.DeviceName;
        _engineState.Text = snapshot.Engines is { } engines
            ? $"Teléfono seleccionado en PC: {(string.IsNullOrWhiteSpace(engines.DeviceName) ? "ninguno" : engines.DeviceName)}\nVisionEngine: {(snapshot.VideoRunning ? "activo" : "detenido")}\nLinkEngine: {engines.LinkState} · {(engines.LinkRunning ? "servicio activo" : "servicio no activo")}\n{engines.LinkMessage}"
            : "Esta PC no informa control de motores. Comprueba que NOVORA PC esté actualizado.";
        Populate(_bitrate, snapshot.Bitrates, snapshot.Bitrate, ref _bitrates);
        Populate(_profile, snapshot.Profiles, snapshot.Profile, ref _profiles);
        Populate(_audio, snapshot.AudioOutputs, snapshot.AudioOutput, ref _outputs);
        Populate(_resolution, snapshot.VideoSettings?.Resolutions ?? [], snapshot.VideoSettings?.Resolution ?? "", ref _resolutions);
        Populate(_fps, snapshot.VideoSettings?.FrameRates ?? [], snapshot.VideoSettings?.Fps ?? "", ref _frameRates);
        _engineState.Text += "\n" + snapshot.Engines?.VideoMessage + "\nAudio activo: " + snapshot.ActiveAudioOutput;
        if (snapshot.Media is { } media) _engineState.Text += "\n" + media.Message;
        EnableActions(!_busy);
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
        var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, options.Select(o => o.Label).ToArray());
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        spinner.Adapter = adapter;
        }
        int index = Array.FindIndex(options, o => o.Value == wanted);
        if (index >= 0) spinner.SetSelection(index);
        _populating = false;
    }
    private async Task ChangeAsync(string action, NLControlOption[] values, Spinner spinner)
    {
        int index = spinner.SelectedItemPosition;
        if (index < 0 || index >= values.Length || _service is null || _busy) return;
        var service = _service;
        long generation = service.Session.Current.Generation;
        string value = values[index].Value;
        try {
            var reply = await service.Session.SendAsync(action, value);
            if (!_active || !ReferenceEquals(service, _service) || generation != service.Session.Current.Generation) return;
            if (reply.Success) _drafts.Remove(spinner);
            RenderSession();
            _status.Text = reply.Message;
        } catch { if (_active) _status.Text = "Cambio sin confirmar. Revisa la conexión antes de repetir."; }
    }
    private bool CanSendEngine(string action) => _active && !_busy && _snapshot?.Engines is { } engines && (action switch
    {
        "startVideo" => engines.VideoCanStart,
        "stopVideo" => engines.VideoCanStop,
        "startLink" => engines.LinkCanStart,
        "stopLink" => engines.LinkCanStop,
        _ => false
    });
    private Task SendEngineAsync(string action)
    {
        if (!CanSendEngine(action)) return Task.CompletedTask;
        if (action == "startVideo") _floatWhenStarted = NLAndroidUIFloatingPreferences.IsEnabled(this) && Android.Provider.Settings.CanDrawOverlays(this);
        return action == "startLink" ? RequestVpnAsync() : SendAsync(action);
    }
    private Task RequestVpnAsync()
    {
        if (_service is null || _service.Session.Current.Transport != "USB" || _vpnGeneration >= 0) return Task.CompletedTask;
        _vpnGeneration = _service.Session.Current.Generation;
        _vpnRevision = _snapshot!.Revision;
        new AlertDialog.Builder(this)!.SetTitle("Internet de PC por USB")!
            .SetMessage("NOVORA enviará el tráfico IPv4 del teléfono por USB hacia la conexión de esta PC. Mantén el cable conectado. Android pedirá permiso para crear una VPN; otra VPN activa puede ser sustituida. Detener LinkEngine cerrará este túnel.")!
            .SetNegativeButton("Cancelar", (_, _) => { _vpnGeneration = -1; })!
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
                catch (Exception) { _vpnGeneration = -1; _vpnApproved = false; _status.Text = "No se pudo preparar el permiso VPN."; }
            })!.Show();
        return Task.CompletedTask;
    }
    private sealed class NLAndroidUICancelVpn(NLAndroidUIActivity owner) : Java.Lang.Object, IDialogInterfaceOnCancelListener
    { public void OnCancel(IDialogInterface? dialog) => owner._vpnGeneration = -1; }
    private async Task CompleteVpnConsentAsync()
    {
        if (!_vpnApproved || !_active || _service is null) return;
        _vpnApproved = false;
        long generation = _vpnGeneration, revision = _vpnRevision;
        _vpnGeneration = -1;
        var service = _service;
        if (service.Session.Current.Generation != generation || _snapshot?.Revision != revision || !CanSendEngine("startLink"))
        { _status.Text = "La sesión o el dispositivo cambió. Revisa el estado antes de iniciar Internet."; return; }
        try
        {
            var reply = await service.StartInternetAsync(generation);
            if (_active && ReferenceEquals(service, _service)) _status.Text = reply.Message;
        }
        catch (Exception) { if (_active && ReferenceEquals(service, _service)) _status.Text = "No se pudo iniciar Internet por USB. Revisa LinkEngine en PC."; }
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
    private Task ConfirmRestartAsync()
    {
        var service = _service;
        if (service is null || _snapshot?.VideoRunning != true) return Task.CompletedTask;
        long generation = service.Session.Current.Generation, revision = _snapshot.Revision;
        new AlertDialog.Builder(this)!.SetTitle("Reiniciar video")!
            .SetMessage("La captura se detendrá y volverá a iniciar con los ajustes guardados. El control de NOVORA permanecerá conectado.")!
            .SetNegativeButton("Cancelar", (_, _) => { })!
            .SetPositiveButton("Reiniciar", async (_, _) =>
            {
                if (!_active || !ReferenceEquals(service, _service) || service.Session.Current.Generation != generation || _snapshot?.Revision != revision) {
                    if (_active) _status.Text = "El estado cambió. Revisa los ajustes y confirma de nuevo.";
                    return;
                }
                try { await SendAsync("restartVideo"); }
                catch (Exception) { _status.Text = "Reinicio no confirmado. Comprueba el estado en PC."; }
            })!.Show();
        return Task.CompletedTask;
    }
    private async Task SendAsync(string action, string? value = null)
    {
        var service = _service;
        if (_busy || service is null || _snapshot is null) return;
        try
        {
            var reply = action == "stopLink" ? await service.StopInternetAsync() : await service.Session.SendAsync(action, value);
            if (action == "startVideo" && !reply.Success) _floatWhenStarted = false;
            if (_active && ReferenceEquals(_service, service)) _status.Text = reply.Message;
        }
        catch (Exception)
        {
            if (action == "startVideo") _floatWhenStarted = false;
            if (_active && ReferenceEquals(_service, service)) RenderSession();
        }
    }
}
