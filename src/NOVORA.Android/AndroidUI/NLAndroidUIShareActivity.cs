using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Widget;
using NOVORA.AndroidService;
using NOVORA.Control;
using Uri = Android.Net.Uri;

namespace NOVORA.AndroidUI;

[Activity(Name = "com.novora.appcontrol.ShareActivity", Label = "Enviar con NOVORA", Exported = true,
    ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation | Android.Content.PM.ConfigChanges.ScreenSize,
    Theme = "@android:style/Theme.Material.NoActionBar")]
[IntentFilter(new[] { Intent.ActionSend, Intent.ActionSendMultiple }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "*/*")]
public sealed class NLAndroidUIShareActivity : Activity
{
    private readonly List<Uri> _uris = new();
    private NLAndroidServiceControl? _service;
    private Connection? _connection;
    private bool _bound, _sending, _destroyed, _started, _uiRegistered;
    private CancellationTokenSource? _cancel;
    private TextView _status = null!;
    private TextView _selection = null!;
    private Button _send = null!, _pick = null!, _stop = null!;
    private long _generation = -1;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(24, 24, 24, 24);
        _status = new TextView(this) { Text = "Conecta NOVORA con tu PC antes de enviar.", TextSize = 20 };
        _selection = new TextView(this) { TextSize = 16 };
        _pick = new Button(this) { Text = "Elegir archivos" };
        _send = new Button(this) { Text = "Enviar a la PC", Enabled = false };
        _stop = new Button(this) { Text = "Cancelar envío", Enabled = false };
        var open = new Button(this) { Text = "Abrir NOVORA para conectar" };
        foreach (var view in new Android.Views.View[] { _status, _selection, _pick, _send, _stop, open }) layout.AddView(view);
        var scroll = new ScrollView(this); scroll.AddView(layout); NLAndroidUILayout.Prepare(scroll, this); SetContentView(scroll);
        _pick.Click += (_, _) =>
        {
            var picker = new Intent(Intent.ActionOpenDocument).SetType("*/*");
            picker.AddCategory(Intent.CategoryOpenable); picker.PutExtra(Intent.ExtraAllowMultiple, true);
            StartActivityForResult(picker, 1);
        };
        _send.Click += async (_, _) => await SendFilesAsync();
        _stop.Click += (_, _) => _cancel?.Cancel();
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
    private (string Name, long Length) Describe(Uri uri)
    {
        using var cursor = ContentResolver!.Query(uri, new[] { IOpenableColumns.DisplayName, IOpenableColumns.Size }, null, null, null);
        if (cursor?.MoveToFirst() != true || cursor.IsNull(0) || cursor.IsNull(1))
            throw new IOException("El proveedor no informa el nombre y tamaño. Guarda una copia local y vuelve a elegirla.");
        return (NLControlFileStorage.SafeName(cursor.GetString(0)!), cursor.GetLong(1));
    }
    private void RenderSelection()
    {
        _selection.Text = _uris.Count == 0 ? "Selecciona uno o varios archivos (máximo 100)." : $"{_uris.Count} archivo(s) seleccionado(s). Máximo 2 GiB por archivo.";
    }
    private void Changed(object? sender, NLControlSessionState args) => RunOnUiThread(UpdateSession);
    private void UpdateSession()
    {
        if (_destroyed) return;
        var state = _service?.Session.Current;
        bool connected = state?.Phase == NLControlSessionPhase.Connected;
        if (_sending)
        {
            if (!connected || state!.Generation != _generation) _cancel?.Cancel();
            return;
        }
        _status.Text = connected ? $"Enviar a {state!.Snapshot?.PcName ?? "PC vinculada"} · {state.Transport}" : "Conecta NOVORA con tu PC antes de enviar.";
        _send.Enabled = connected && state?.Busy == false && _uris.Count > 0;
    }
    private async Task SendFilesAsync()
    {
        var service = _service;
        if (_sending || service?.Session.Current.Phase != NLControlSessionPhase.Connected || !service.BeginFileTransfer()) return;
        _sending = true; _generation = service.Session.Current.Generation;
        _cancel = new CancellationTokenSource();
        _send.Enabled = _pick.Enabled = false; _stop.Enabled = true;
        int completed = 0;
        try
        {
            foreach (Uri uri in _uris)
            {
                _cancel.Token.ThrowIfCancellationRequested();
                var (name, length) = Describe(uri);
                using var stream = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("No se pudo abrir el archivo.");
                string result = await NLControlFileSender.SendAsync(stream, name, length, (action, value) =>
                {
                    var current = service.Session.Current;
                    if (current.Phase != NLControlSessionPhase.Connected || current.Generation != _generation)
                        throw new IOException("La PC o la sesión cambió; vuelve a iniciar el envío.");
                    return service.Session.SendAsync(action, value);
                }, new Progress<long>(bytes => { if (!_destroyed && _sending) _status.Text = $"{name}: {bytes:N0} / {length:N0} bytes"; }), _cancel.Token);
                completed++;
                _selection.Text = $"{completed}/{_uris.Count}: {result}";
            }
            _status.Text = $"Envío completo: {completed} archivo(s) verificado(s) en la PC.";
        }
        catch (System.OperationCanceledException) { if (!_destroyed) _status.Text = $"Envío cancelado. {completed} archivo(s) ya completado(s)."; }
        catch (Exception ex) { if (!_destroyed) _status.Text = $"{completed} archivo(s) completado(s). {ex.Message}"; }
        finally
        {
            service.EndFileTransfer();
            _sending = false; _cancel.Dispose(); _cancel = null;
            if (!_destroyed) { _pick.Enabled = true; _stop.Enabled = false; _send.Enabled = false; }
        }
    }
    protected override void OnStart()
    {
        base.OnStart(); _started = true;
        if (_service is { } service && !_uiRegistered) { service.SetUiForeground(true); _uiRegistered = true; }
    }
    protected override void OnStop()
    {
        _started = false;
        if (_service is { } service && _uiRegistered) { service.SetUiForeground(false); _uiRegistered = false; }
        base.OnStop();
    }
    protected override void OnSaveInstanceState(Bundle outState)
    {
        outState.PutStringArray("uris", _uris.Select(x => x.ToString()!).ToArray());
        base.OnSaveInstanceState(outState);
    }
    protected override void OnDestroy()
    {
        _destroyed = true; _cancel?.Cancel();
        if (_service is { } service) service.Session.Changed -= Changed;
        if (_bound && _connection is { } connection) UnbindService(connection);
        _service = null; base.OnDestroy();
    }
    private sealed class Connection(NLAndroidUIShareActivity owner) : Java.Lang.Object, IServiceConnection
    {
        public void OnServiceConnected(ComponentName? name, IBinder? binder)
        {
            if (owner._destroyed || binder is not NLAndroidServiceControl.NLAndroidServiceBinder service) return;
            owner._service = service.Owner; owner._service.Session.Changed += owner.Changed; owner.UpdateSession();
            if (owner._started && !owner._uiRegistered) { service.Owner.SetUiForeground(true); owner._uiRegistered = true; }
        }
        public void OnServiceDisconnected(ComponentName? name) { owner._cancel?.Cancel(); owner._service = null; owner.UpdateSession(); }
    }
}
