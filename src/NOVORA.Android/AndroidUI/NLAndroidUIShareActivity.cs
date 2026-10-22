using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Widget;
using Android.Graphics;
using NOVORA.AndroidService;
using NOVORA.Control;
using Uri = Android.Net.Uri;

namespace NOVORA.AndroidUI;

[Activity(Name = "com.linkcontrol.novora.ShareActivity", Label = "Enviar con LINK Control", Exported = true,
    ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation | Android.Content.PM.ConfigChanges.ScreenSize,
    Theme = "@android:style/Theme.Material.NoActionBar")]
[IntentFilter(new[] { Intent.ActionSend, Intent.ActionSendMultiple }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "*/*")]
public sealed class NLAndroidUIShareActivity : Activity
{
    private readonly List<Uri> _uris = new();
    private NLAndroidServiceControl? _service;
    private Connection? _connection;
    private bool _bound, _destroyed;
    private TextView _status = null!;
    private TextView _selection = null!;
    private Button _send = null!, _pick = null!, _stop = null!;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        NLAndroidUIPalette palette = NLAndroidUITheme.Current(this);
        int padding = NLAndroidUIVisual.Dp(this, palette.PagePadding);
        layout.SetPadding(padding, padding, padding, padding);
        layout.SetBackgroundColor(Color.ParseColor(palette.Background));
        _status = new TextView(this) { Text = "Conecta NOVORA con tu PC antes de enviar.", TextSize = 20 };
        _selection = new TextView(this) { TextSize = 16 };
        _pick = new Button(this) { Text = "Elegir archivos" };
        _send = new Button(this) { Text = "Enviar a la PC", Enabled = false };
        _stop = new Button(this) { Text = "Cancelar envío", Enabled = false };
        var open = new Button(this) { Text = "Abrir NOVORA para conectar" };
        _status.SetTextColor(Color.ParseColor(palette.Text));
        _selection.SetTextColor(Color.ParseColor(palette.Muted));
        NLAndroidUIVisual.Button(_pick);
        NLAndroidUIVisual.Button(_send, true);
        NLAndroidUIVisual.Button(_stop);
        NLAndroidUIVisual.Button(open);
        foreach (var view in new Android.Views.View[] { _status, _selection, _pick, _send, _stop, open }) layout.AddView(view);
        var scroll = new ScrollView(this); scroll.AddView(layout); NLAndroidUILayout.Prepare(scroll, this); SetContentView(scroll);
        _pick.Click += (_, _) =>
        {
            var picker = new Intent(Intent.ActionOpenDocument).SetType("*/*");
            picker.AddCategory(Intent.CategoryOpenable); picker.PutExtra(Intent.ExtraAllowMultiple, true);
            StartActivityForResult(picker, 1);
        };
        _send.Click += (_, _) => SendFilesInBackground();
        _stop.Click += (_, _) => _service?.CancelBackgroundFileTransfer();
        open.Click += (_, _) => StartActivity(new Intent(this, typeof(NLAndroidUIActivity)));
        if (savedInstanceState?.GetStringArray("uris") is { } restored)
            foreach (string uri in restored) AddUri(Uri.Parse(uri));
        else ReadUris(Intent);
        RenderSelection();
        _connection = new Connection(this);
        _bound = BindService(new Intent(this, typeof(NLAndroidServiceControl)), _connection, Bind.AutoCreate);
    }
    private void AddUri(Uri? uri)
    {
        if (uri?.Scheme == "content" && _uris.Count < 100 && !_uris.Any(x => x.ToString() == uri.ToString())) _uris.Add(uri);
    }
    private void ReadUris(Intent? intent)
    {
        if (intent?.ClipData is { } clip) for (int i = 0; i < clip.ItemCount; i++) AddUri(clip.GetItemAt(i)?.Uri);
#pragma warning disable CS0618, CA1422 // Compatibility accessor supports Android 8; validate content URIs below.
        if (intent?.Action == Intent.ActionSend) AddUri(intent.GetParcelableExtra(Intent.ExtraStream) as Uri);
        if (intent?.Action == Intent.ActionSendMultiple && intent.GetParcelableArrayListExtra(Intent.ExtraStream) is { } items)
            foreach (var item in items) AddUri(item as Uri);
#pragma warning restore CS0618, CA1422
        AddUri(intent?.Data);
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != 1 || resultCode != Result.Ok || data is null) return;
        _uris.Clear(); ReadUris(data); RenderSelection(); UpdateSession();
    }
    private void RenderSelection()
    {
        _selection.Text = _uris.Count == 0 ? "Selecciona uno o varios archivos (máximo 100)." : $"{_uris.Count} archivo(s) seleccionado(s). Máximo 2 GiB por archivo.";
    }
    private void Changed(object? sender, NLControlSessionState args) => RunOnUiThread(UpdateSession);
    private void ServiceChanged(object? sender, EventArgs args) => RunOnUiThread(UpdateSession);
    private void UpdateSession()
    {
        if (_destroyed) return;
        var state = _service?.Session.Current;
        bool connected = state?.Phase == NLControlSessionPhase.Connected;
        bool sending = _service?.TransferInProgress == true;
        _status.Text = sending || !string.IsNullOrWhiteSpace(_service?.FileTransferMessage)
            ? _service!.FileTransferMessage
            : connected ? $"Enviar a {state!.Snapshot?.PcName ?? "PC vinculada"} · {state.Transport}"
            : "Conecta NOVORA con tu PC antes de enviar.";
        _send.Enabled = connected && state?.Busy == false && !sending && _uris.Count > 0;
        _pick.Enabled = !sending;
        _stop.Enabled = sending;
    }
    private void SendFilesInBackground()
    {
        if (_service?.Session.Current.Phase != NLControlSessionPhase.Connected ||
            _service.TransferInProgress || _uris.Count == 0) return;
        try
        {
            var clip = ClipData.NewRawUri("Archivos LINK Control", _uris[0]);
            for (int i = 1; i < _uris.Count; i++) clip!.AddItem(new ClipData.Item(_uris[i]));
            var intent = new Intent(this, typeof(NLAndroidServiceControl))
                .SetAction(NLAndroidServiceControl.SendFilesAction)
                .AddFlags(ActivityFlags.GrantReadUriPermission);
            intent.ClipData = clip;
            StartService(intent);
            _status.Text = "Envío iniciado en segundo plano. Puedes salir de esta pantalla.";
            _send.Enabled = false;
        }
        catch (Exception ex) { _status.Text = "No se pudo iniciar el envío: " + ex.Message; }
    }
    protected override void OnSaveInstanceState(Bundle outState)
    {
        outState.PutStringArray("uris", _uris.Select(x => x.ToString()!).ToArray());
        base.OnSaveInstanceState(outState);
    }
    protected override void OnDestroy()
    {
        _destroyed = true;
        if (_service is { } service) { service.Session.Changed -= Changed; service.StatusChanged -= ServiceChanged; }
        if (_bound && _connection is { } connection) UnbindService(connection);
        _service = null; base.OnDestroy();
    }
    private sealed class Connection(NLAndroidUIShareActivity owner) : Java.Lang.Object, IServiceConnection
    {
        public void OnServiceConnected(ComponentName? name, IBinder? binder)
        {
            if (owner._destroyed || binder is not NLAndroidServiceControl.NLAndroidServiceBinder service) return;
            owner._service = service.Owner;
            owner._service.Session.Changed += owner.Changed;
            owner._service.StatusChanged += owner.ServiceChanged;
            owner.UpdateSession();
        }
        public void OnServiceDisconnected(ComponentName? name) { owner._service = null; owner.UpdateSession(); }
    }
}
