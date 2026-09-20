using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Media.Session;
using Android.Views;
using Android.Widget;
using NOVORA.AndroidService;
using NOVORA.Control;
using Resource = NOVORA.AndroidApp.Resource;

namespace NOVORA.AndroidUI;

/// <summary>One event-driven overlay; the existing service remains the sole connection owner.</summary>
public sealed class NLAndroidUIFloatingController : IDisposable
{
    private readonly Context _context;
    private readonly NLAndroidServiceControl _service;
    private readonly IWindowManager _manager;
    private readonly Handler _main = new(Looper.MainLooper!);
    private NLControlSessionState _state;
    private LinearLayout? _view;
    private LinearLayout? _panel;
    private WindowManagerLayoutParams? _layout;
    private bool _foreground = true, _hidden, _expanded, _disposed, _more;
    private long _generation = -1;
    private string? _renderKey;
    private string? _quickAction;
    private string _feedback = "";
    private NLControlOption[] _quickOptions = [];
    private readonly List<NLAndroidUIMediaCard> _mediaCards = [];
    private MediaSessionManager? _mediaManager;
    private SessionsListener? _sessionsListener;
    public NLAndroidUIFloatingController(Context context, NLAndroidServiceControl service)
    {
        _context = context; _service = service; _state = service.Session.Current;
        _manager = context.GetSystemService(Context.WindowService)!.JavaCast<IWindowManager>();
        NLAndroidUIFloatingPreferences.Changed += PreferencesChanged;
        NLAndroidUIMediaAccess.AccessChanged += MediaAccessChanged;
    }
    private int Dp(int value) => (int)(value * _context.Resources!.DisplayMetrics!.Density + .5f);
    public void SetForeground(bool foreground) { _foreground = foreground; Refresh(); }
    public void Update(NLControlSessionState state)
    {
        if (state.Generation != _generation) { _hidden = false; _expanded = false; _quickAction = null; _feedback = ""; _generation = state.Generation; }
        if (_state.Snapshot?.VideoRunning == true && state.Snapshot?.VideoRunning != true) _hidden = false;
        _state = state; Refresh();
    }
    public void RefreshPreferences() { _hidden = false; _renderKey = null; Remove(); Refresh(); }
    private void PreferencesChanged(object? sender, EventArgs e) => _main.Post(() => { if (!_disposed) RefreshPreferences(); });
    private void MediaAccessChanged(object? sender, EventArgs e) => _main.Post(() => {
        if (_disposed) return;
        StopMedia(); _renderKey = null; Refresh();
    });
    private sealed class SessionsListener(NLAndroidUIFloatingController owner) : Java.Lang.Object, MediaSessionManager.IOnActiveSessionsChangedListener
    {
        public void OnActiveSessionsChanged(IList<Android.Media.Session.MediaController>? controllers) {
            if (owner._disposed) return;
            owner._renderKey = null; owner.Refresh();
        }
    }
    private void ClearMediaCards() { foreach (var card in _mediaCards) card.Dispose(); _mediaCards.Clear(); }
    private void StopMedia() {
        ClearMediaCards();
        if (_sessionsListener is not null) {
            try { _mediaManager?.RemoveOnActiveSessionsChangedListener(_sessionsListener); } catch (Java.Lang.RuntimeException) { }
            _sessionsListener.Dispose(); _sessionsListener = null;
        }
        _mediaManager = null;
    }
    private void RenderMedia(string[] packages) {
        if (!_expanded || packages.Length == 0 || !NLAndroidUIMediaAccess.Enabled(_context)) { StopMedia(); return; }
        try {
            _mediaManager ??= _context.GetSystemService(Context.MediaSessionService)!.JavaCast<MediaSessionManager>();
            if (_sessionsListener is null) {
                _sessionsListener = new SessionsListener(this);
                _mediaManager.AddOnActiveSessionsChangedListener(_sessionsListener, NLAndroidUIMediaAccess.Component(_context), _main);
            }
            var sessions = _mediaManager.GetActiveSessions(NLAndroidUIMediaAccess.Component(_context));
            foreach (string package in packages) {
                var controller = sessions?.FirstOrDefault(x => x.PackageName == package);
                if (controller is null) continue;
                string title = package;
                try { title = _context.PackageManager!.GetApplicationInfo(package, 0)?.LoadLabel(_context.PackageManager!) ?? package; }
                catch (Android.Content.PM.PackageManager.NameNotFoundException) { }
                var card = new NLAndroidUIMediaCard(_context, controller, title);
                _mediaCards.Add(card); _panel!.AddView(card.View);
            }
        } catch (Java.Lang.RuntimeException) { StopMedia(); Label("Activa el acceso multimedia en los ajustes de la burbuja.", 12); }
    }
    private void Refresh()
    {
        if (_disposed) return;
        if (_foreground || _hidden || _state.Phase != NLControlSessionPhase.Connected || _state.Snapshot?.VideoRunning != true ||
            !NLAndroidUIFloatingPreferences.IsEnabled(_context) || !Settings.CanDrawOverlays(_context)) { Remove(); return; }
        try
        {
            if (_view is null) Create();
            var key = $"{_state.Generation}:{_state.Snapshot?.Revision}:{_state.Busy}:{_service.TransferInProgress}:{_expanded}:{_quickAction}";
            if (key != _renderKey) { RenderPanel(); _renderKey = key; }
            ClampPosition();
        }
        catch (Exception ex) when (ex is Java.Lang.RuntimeException or System.ArgumentException)
        { Remove(); Toast.MakeText(_context, "No se pudo mostrar el control flotante. Abre NOVORA para continuar.", ToastLength.Long)?.Show(); }
    }
    private void Create()
    {
        _view = new LinearLayout(_context) { Orientation = Orientation.Vertical };
        var bubble = NLAndroidUIVisual.Logo(_context, 56); bubble.Clickable = true;
        bubble.Alpha = NLAndroidUIFloatingPreferences.BubbleOpacity(_context) / 100f;
        bubble.ContentDescription = "NOVORA: abrir controles. Mantén y arrastra para mover.";
        bubble.SetPadding(Dp(8), Dp(8), Dp(8), Dp(8));
        bubble.ClipToOutline = true;
        var background = new GradientDrawable(); background.SetColor(Color.ParseColor("#E6202D35")); background.SetCornerRadius(Dp(28)); background.SetStroke(Dp(1), Color.Cyan); bubble.Background = background;
        _view.AddView(bubble, new LinearLayout.LayoutParams(Dp(56), Dp(56)));
        bubble.Click += (_, _) => { _expanded = !_expanded; _renderKey = null; Refresh(); };
        bubble.SetOnTouchListener(new DragListener(this));
        _panel = new LinearLayout(_context) { Orientation = Orientation.Vertical }; _panel.SetPadding(Dp(12), Dp(8), Dp(12), Dp(12));
        _panel.Background = NLAndroidUIVisual.Surface(_context, "#192329", "#40545F", 18);
        var scroll = new ScrollView(_context); scroll.AddView(_panel);
        _view.AddView(scroll, new LinearLayout.LayoutParams(Dp(300), -2));
        _layout = new WindowManagerLayoutParams(-2, -2, WindowManagerTypes.ApplicationOverlay,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal, Format.Translucent) { Gravity = GravityFlags.Top | GravityFlags.Left };
        var position = NLAndroidUIFloatingPreferences.Position(_context); var size = Size();
        _layout.X = (int)(position.X * Math.Max(0, size.Width - Dp(56)));
        _layout.Y = (int)(position.Y * Math.Max(0, size.Height - Dp(56)));
        _view.LayoutChange += (_, _) =>
        {
            if (_view is null) return;
            try { ClampPosition(); }
            catch (Java.Lang.RuntimeException) { Remove(); }
        };
        _manager.AddView(_view, _layout);
    }
    private (int Width, int Height) Size()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var metrics = _manager.CurrentWindowMetrics!;
            var inset = metrics.WindowInsets.GetInsetsIgnoringVisibility(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout())!;
            return (Math.Max(Dp(56), metrics.Bounds.Width() - inset.Left - inset.Right), Math.Max(Dp(56), metrics.Bounds.Height() - inset.Top - inset.Bottom));
        }
        var dm = _context.Resources!.DisplayMetrics!;
        return (dm.WidthPixels, Math.Max(Dp(56), dm.HeightPixels - Dp(72)));
    }
    private void ClampPosition()
    {
        if (_view is null || _layout is null) return;
        // Both the circle and panel must be capturable; Secure creates black regions in VE.
        bool secure = false;
        bool flagsChanged = ((_layout.Flags & WindowManagerFlags.Secure) != 0) != secure;
        if (secure) _layout.Flags |= WindowManagerFlags.Secure; else _layout.Flags &= ~WindowManagerFlags.Secure;
        var size = Size(); int w = _expanded ? Math.Min(Dp(300), size.Width) : Dp(56);
        var scroll = _view.GetChildAt(1)!; scroll.Visibility = _expanded ? ViewStates.Visible : ViewStates.Gone;
        int maxPanel = Math.Max(Dp(56), Math.Min(Dp(460), size.Height - Dp(64)));
        if (scroll.LayoutParameters!.Width != w || scroll.LayoutParameters.Height != maxPanel)
            scroll.LayoutParameters = new LinearLayout.LayoutParams(w, maxPanel);
        int h = _expanded ? Dp(56) + maxPanel : Dp(56);
        int x = Math.Clamp(_layout.X, 0, Math.Max(0, size.Width - w));
        int y = Math.Clamp(_layout.Y, 0, Math.Max(0, size.Height - h));
        if (flagsChanged || _layout.X != x || _layout.Y != y) { _layout.X = x; _layout.Y = y; UpdateWindow(); }
    }
    private void UpdateWindow()
    {
        if (_view is null || _layout is null) return;
        try { _manager.UpdateViewLayout(_view, _layout); }
        catch (Java.Lang.RuntimeException) { Remove(); }
    }
    private void RenderPanel()
    {
        if (_panel is null) return;
        ClearMediaCards();
        if (!_expanded) { StopMedia(); _panel.RemoveAllViews(); return; }
        _panel.RemoveAllViews(); var snapshot = _state.Snapshot!;
        var header = new LinearLayout(_context) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical); _panel.AddView(header);
        header.AddView(NLAndroidUIVisual.Logo(_context, 30));
        var name = new TextView(_context) { Text = "NOVORA", TextSize = 14 }; name.SetTextColor(Color.White); name.SetPadding(Dp(8),0,0,0);
        header.AddView(name, new LinearLayout.LayoutParams(0,-2,1));
        var close = new Button(_context) { Text = "Cerrar", ContentDescription = "Plegar herramientas" }; NLAndroidUIVisual.Button(close); close.TextSize = 11;
        close.Click += (_,_) => { _expanded = false; _renderKey = null; Refresh(); };
        header.AddView(close,new LinearLayout.LayoutParams(Dp(64),-2));
        Label(snapshot.PcName + " · Conectada por " + _state.Transport, 12);
        bool ready = !_state.Busy && !_service.TransferInProgress;
        if (!string.IsNullOrEmpty(_feedback)) Label(_feedback, 12);
        if (_quickAction is { } quick) {
            StopMedia();
            Label("Elegir ajuste · se aplica al tocar", 15);
            foreach (var option in _quickOptions) {
                string value = option.Value;
                Button(option.Label, () => ApplyQuickAsync(quick, value), ready && snapshot.Media is not ({ Recording: true } or { Starting: true }));
            }
            Button("Volver al panel", () => { _quickAction = null; return Task.CompletedTask; });
            return;
        }
        Label("HERRAMIENTAS", 11);
        var tools = new LinearLayout(_context) { Orientation = Orientation.Horizontal }; _panel.AddView(tools);
        var media = snapshot.Media;
        Tile(tools, media?.Recording == true ? "Detener\ngrabación" : "Grabar\npantalla", Android.Resource.Drawable.IcMenuSave,
            () => Send(media?.Recording == true ? "stopRecording" : "startRecording"), ready && (media?.Recording == true || media?.CanRecord == true));
        Tile(tools, "Captura", Android.Resource.Drawable.IcMenuCamera, () => Send("capture"), ready && media?.CanCapture == true);
        Tile(tools, "Archivos", Android.Resource.Drawable.IcMenuGallery, () => { Launch(new Intent(_context, typeof(NLAndroidUIFilesActivity))); return Task.CompletedTask; }, true);
        if (media?.Recording == true || media?.Starting == true) Label(media.Message, 12);
        var packages = NLAndroidUIFloatingPreferences.Favorites(_context);
        RenderMedia(packages);
        if (packages.Length > 0)
        {
            Label("APLICACIONES", 11);
            LinearLayout? row = null;
            for (int i = 0; i < packages.Length; i++)
            {
                if (i % 3 == 0) { row = new LinearLayout(_context) { Orientation = Orientation.Horizontal }; _panel.AddView(row); }
                string package = packages[i], title = package; Drawable? icon = null;
                try { var app = _context.PackageManager!.GetApplicationInfo(package, 0); title = app?.LoadLabel(_context.PackageManager!) ?? package; icon = app?.LoadIcon(_context.PackageManager!); }
                catch (Android.Content.PM.PackageManager.NameNotFoundException) { title = "No disponible"; }
                var button = Tile(row!, title, Android.Resource.Drawable.IcMenuMyPlaces, () => { OpenFavorite(package); return Task.CompletedTask; }, true);
                if (icon is not null) { icon.SetBounds(0,0,Dp(30),Dp(30)); button.SetCompoundDrawables(null,icon,null,null); }
            }
        }
        Label("CONTROL DE PC", 11);
        bool settingsReady = ready && snapshot.VideoSettings?.CanApplyTogether == true && snapshot.Media is not ({ Recording: true } or { Starting: true });
        QuickButton("Monitor", "monitor", snapshot.VideoSettings?.Monitor ?? "", snapshot.VideoSettings?.Monitors ?? [], settingsReady);
        QuickButton("Perfil", "profile", snapshot.Profile, snapshot.Profiles, settingsReady);
        QuickButton("Bitrate", "bitrate", snapshot.Bitrate, snapshot.Bitrates, settingsReady);
        QuickButton("Resolución", "resolution", snapshot.VideoSettings?.Resolution ?? "", snapshot.VideoSettings?.Resolutions ?? [], settingsReady);
        QuickButton("FPS", "fps", snapshot.VideoSettings?.Fps ?? "", snapshot.VideoSettings?.FrameRates ?? [], settingsReady);
        QuickButton("Audio PC", "audio", snapshot.AudioOutput, snapshot.AudioOutputs, settingsReady);
        Label("Los cambios se aplican aquí y reinician VE. Durante una grabación quedan bloqueados.", 12);
        Button("Configuración", () => { OpenMain("settings"); return Task.CompletedTask; });
        Button("Más opciones", () => { _more = !_more; return Task.CompletedTask; });
        if (_more) {
            Button("Detener VisionEngine", () => { OpenMain("stopVideo"); return Task.CompletedTask; }, snapshot.Engines?.VideoCanStop == true && ready);
            Button("Abrir NOVORA", () => { OpenMain(null); return Task.CompletedTask; });
            Button("Ocultar burbuja · mantener VE", () => { _hidden = true; Remove(); return Task.CompletedTask; });
            Label("La burbuja y el panel son visibles en VE y sus grabaciones.", 12);
        }
    }
    private Button Tile(LinearLayout row, string text, int iconResource, Func<Task> action, bool enabled)
    {
        var button = new Button(_context) { Text = text, Enabled = enabled }; NLAndroidUIVisual.Button(button);
        button.TextSize = 11; button.SetMinHeight(Dp(82)); button.SetMinimumHeight(Dp(82));
        button.SetMaxLines(2); button.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        button.Background = NLAndroidUIVisual.Surface(_context, "#232F37", "#32444E", 10);
        var icon = _context.GetDrawable(iconResource)!.Mutate(); icon.SetTint(Color.ParseColor("#DDECF4")); icon.SetBounds(0,0,Dp(27),Dp(27));
        button.SetCompoundDrawables(null,icon,null,null); button.CompoundDrawablePadding = Dp(5);
        var lp = new LinearLayout.LayoutParams(0,Dp(104),1); lp.SetMargins(Dp(4),Dp(6),Dp(4),Dp(6)); row.AddView(button,lp);
        button.Click += async (_,_) => { button.Enabled = false; try { await action(); } catch(Exception ex) { Toast.MakeText(_context,ex.Message,ToastLength.Long)?.Show(); } finally { if(!_disposed) { _renderKey = null; Refresh(); } } };
        return button;
    }
    private void QuickButton(string title, string action, string current, NLControlOption[] options, bool enabled)
    {
        if (options.Length == 0) return;
        string value = options.FirstOrDefault(x => x.Value == current)?.Label ?? current;
        Button(title + ": " + value, () =>
        {
            _quickAction = action; _quickOptions = options; _feedback = ""; return Task.CompletedTask;
        }, enabled);
    }
    private async Task ApplyQuickAsync(string action, string value) {
        var state = _service.Session.Current;
        if (state.Busy || _service.TransferInProgress || state.Phase != NLControlSessionPhase.Connected || state.Snapshot is not { VideoSettings: { CanApplyTogether: true } video } snapshot ||
            snapshot.Media is { Recording: true } or { Starting: true }) return;
        var changes = new NLControlVideoChanges(snapshot.Profile, snapshot.Bitrate, video.Resolution, video.Fps, snapshot.AudioOutput, video.Monitor);
        changes = action switch { "profile" => changes with { Profile = value }, "bitrate" => changes with { Bitrate = value },
            "resolution" => changes with { Resolution = value }, "fps" => changes with { Fps = value }, "audio" => changes with { Audio = value },
            "monitor" => changes with { Monitor = value }, _ => changes };
        _quickAction = null; _feedback = "Aplicando cambio en VE…";
        try {
            var reply = await _service.Session.SendAsync("applyVideoSettings", System.Text.Json.JsonSerializer.Serialize(changes));
            if (!_disposed && _service.Session.Current.Generation == state.Generation) _feedback = reply.Message;
        } catch (Exception) {
            if (!_disposed && _service.Session.Current.Generation == state.Generation) _feedback = "Cambio no confirmado. Revisa la conexión con PC.";
        }
    }
    private async Task Send(string action)
    {
        var before = _state;
        if (before.Busy || _service.TransferInProgress || before.Phase != NLControlSessionPhase.Connected) return;
        var reply = await _service.Session.SendAsync(action);
        if (!_disposed && _service.Session.Current.Generation == before.Generation)
            Toast.MakeText(_context, reply.Message, ToastLength.Long)?.Show();
    }
    private void OpenFavorite(string package)
    {
        try
        {
            var intent = _context.PackageManager!.GetLaunchIntentForPackage(package);
            if (intent is null) { Toast.MakeText(_context, "La aplicación no está disponible. Puedes quitar su acceso en Configuraciones.", ToastLength.Long)?.Show(); return; }
            Launch(intent); _expanded = false; _renderKey = null; Refresh();
        }
        catch (Exception ex) when (ex is ActivityNotFoundException or Java.Lang.SecurityException)
        { Toast.MakeText(_context, "Android no permitió abrir esta aplicación.", ToastLength.Long)?.Show(); }
    }
    private void OpenMain(string? page)
    {
        var intent = new Intent(_context, typeof(NLAndroidUIActivity)); intent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        if (page is not null) intent.PutExtra("novora.page", page);
        intent.PutExtra("novora.generation", _state.Generation);
        intent.PutExtra("novora.revision", _state.Snapshot?.Revision ?? -1);
        Launch(intent);
    }
    private void Launch(Intent intent) { intent.AddFlags(ActivityFlags.NewTask); _context.StartActivity(intent); _expanded = false; }
    private void Label(string text, int size) { var view = new TextView(_context) { Text = text, TextSize = size }; view.SetTextColor(Color.White); view.SetPadding(0, Dp(6), 0, Dp(6)); _panel!.AddView(view); }
    private Button Button(string text, Func<Task> action, bool enabled = true)
    {
        var button = new Button(_context) { Text = text, Enabled = enabled }; NLAndroidUIVisual.Button(button);
        button.Click += async (_, _) => { button.Enabled = false; try { await action(); } catch (Exception ex) { Toast.MakeText(_context, ex.Message, ToastLength.Long)?.Show(); } finally { if (!_disposed) { _renderKey = null; Refresh(); } } };
        var lp = new LinearLayout.LayoutParams(-1, -2); lp.SetMargins(0,Dp(3),0,Dp(3)); _panel!.AddView(button, lp); return button;
    }
    private void Remove()
    {
        StopMedia();
        var view = _view; _view = null; _panel = null; _layout = null; _renderKey = null;
        if (view is not null) try { _manager.RemoveView(view); } catch (Java.Lang.RuntimeException) { }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; NLAndroidUIFloatingPreferences.Changed -= PreferencesChanged; NLAndroidUIMediaAccess.AccessChanged -= MediaAccessChanged; Remove(); _main.RemoveCallbacksAndMessages(null); _main.Dispose(); }
    private sealed class DragListener(NLAndroidUIFloatingController owner) : Java.Lang.Object, View.IOnTouchListener
    {
        private float _x, _y; private int _startX, _startY; private bool _dragging;
        public bool OnTouch(View? view, MotionEvent? motion)
        {
            if (motion is null || owner._layout is null || owner._view is null) return false;
            switch (motion.ActionMasked)
            {
                case MotionEventActions.Down: _x = motion.RawX; _y = motion.RawY; _startX = owner._layout.X; _startY = owner._layout.Y; _dragging = false; return true;
                case MotionEventActions.Move:
                    float dx = motion.RawX - _x, dy = motion.RawY - _y;
                    _dragging |= Math.Abs(dx) + Math.Abs(dy) > owner.Dp(8);
                    if (_dragging)
                    {
                        owner._layout.X = _startX + (int)dx; owner._layout.Y = _startY + (int)dy;
                        try { owner.ClampPosition(); owner.UpdateWindow(); }
                        catch (Java.Lang.RuntimeException) { owner.Remove(); }
                    }
                    return true;
                case MotionEventActions.Up:
                    if (!_dragging) view?.PerformClick();
                    else { var size = owner.Size(); NLAndroidUIFloatingPreferences.SavePosition(owner._context, owner._layout.X / (float)Math.Max(1, size.Width - owner.Dp(56)), owner._layout.Y / (float)Math.Max(1, size.Height - owner.Dp(56))); }
                    return true;
                case MotionEventActions.Cancel: return true;
                default: return false;
            }
        }
    }
}
