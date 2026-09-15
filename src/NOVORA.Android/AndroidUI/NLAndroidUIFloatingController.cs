using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Runtime;
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
    private bool _foreground = true, _hidden, _expanded, _disposed;
    private long _generation = -1;
    private string? _renderKey;
    private string? _quickAction, _quickValue;
    private NLControlOption[] _quickOptions = [];
    private long _quickGeneration, _quickRevision;
    public NLAndroidUIFloatingController(Context context, NLAndroidServiceControl service)
    {
        _context = context; _service = service; _state = service.Session.Current;
        _manager = context.GetSystemService(Context.WindowService)!.JavaCast<IWindowManager>();
        NLAndroidUIFloatingPreferences.Changed += PreferencesChanged;
    }
    private int Dp(int value) => (int)(value * _context.Resources!.DisplayMetrics!.Density + .5f);
    public void SetForeground(bool foreground) { _foreground = foreground; Refresh(); }
    public void Update(NLControlSessionState state)
    {
        if (_quickAction is not null && (state.Generation != _quickGeneration || state.Snapshot?.Revision != _quickRevision))
        {
            ClearQuick();
            Toast.MakeText(_context, "La PC o sus ajustes cambiaron. Vuelve a elegir el ajuste rápido.", ToastLength.Long)?.Show();
        }
        if (state.Generation != _generation) { _hidden = false; _expanded = false; _generation = state.Generation; }
        if (_state.Snapshot?.VideoRunning == true && state.Snapshot?.VideoRunning != true) _hidden = false;
        _state = state; Refresh();
    }
    public void RefreshPreferences() { _hidden = false; _renderKey = null; Remove(); Refresh(); }
    private void PreferencesChanged(object? sender, EventArgs e) => _main.Post(() => { if (!_disposed) RefreshPreferences(); });
    private void Refresh()
    {
        if (_disposed) return;
        if (_foreground || _hidden || _state.Phase != NLControlSessionPhase.Connected || _state.Snapshot?.VideoRunning != true ||
            !NLAndroidUIFloatingPreferences.IsEnabled(_context) || !Settings.CanDrawOverlays(_context)) { Remove(); return; }
        try
        {
            if (_view is null) Create();
            var key = $"{_state.Generation}:{_state.Snapshot?.Revision}:{_state.Busy}:{_service.TransferInProgress}:{_expanded}:{_quickAction}:{_quickValue}";
            if (key != _renderKey) { RenderPanel(); _renderKey = key; }
            ClampPosition();
        }
        catch (Exception ex) when (ex is Java.Lang.RuntimeException or System.ArgumentException)
        { Remove(); Toast.MakeText(_context, "No se pudo mostrar el control flotante. Abre NOVORA para continuar.", ToastLength.Long)?.Show(); }
    }
    private void Create()
    {
        _view = new LinearLayout(_context) { Orientation = Orientation.Vertical };
        var bubble = new ImageButton(_context); bubble.SetImageResource(Resource.Drawable.novora_logo); bubble.SetScaleType(ImageView.ScaleType.FitCenter);
        bubble.Alpha = NLAndroidUIFloatingPreferences.BubbleOpacity(_context) / 100f;
        bubble.ContentDescription = "NOVORA: abrir controles. Mantén y arrastra para mover.";
        bubble.SetPadding(Dp(8), Dp(8), Dp(8), Dp(8));
        var background = new GradientDrawable(); background.SetColor(Color.ParseColor("#E6202D35")); background.SetCornerRadius(Dp(28)); background.SetStroke(Dp(1), Color.Cyan); bubble.Background = background;
        _view.AddView(bubble, new LinearLayout.LayoutParams(Dp(56), Dp(56)));
        bubble.Click += (_, _) => { _expanded = !_expanded; _renderKey = null; Refresh(); };
        bubble.SetOnTouchListener(new DragListener(this));
        _panel = new LinearLayout(_context) { Orientation = Orientation.Vertical }; _panel.SetPadding(Dp(12), Dp(8), Dp(12), Dp(12));
        _panel.SetBackgroundColor(Color.ParseColor("#F0202D35"));
        var scroll = new ScrollView(_context); scroll.AddView(_panel);
        _view.AddView(scroll, new LinearLayout.LayoutParams(Dp(300), -2));
        _layout = new WindowManagerLayoutParams(-2, -2, WindowManagerTypes.ApplicationOverlay,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal, Format.Translucent) { Gravity = GravityFlags.Top | GravityFlags.Left };
        if (_expanded || NLAndroidUIFloatingPreferences.ExcludeFromCapture(_context)) _layout.Flags |= WindowManagerFlags.Secure;
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
        bool secure = _expanded || NLAndroidUIFloatingPreferences.ExcludeFromCapture(_context);
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
        _panel.RemoveAllViews(); var snapshot = _state.Snapshot!;
        Label("NOVORA · " + snapshot.PcName, 18); Label("VisionEngine activo · " + _state.Transport, 13);
        if (!string.IsNullOrWhiteSpace(snapshot.Engines?.DeviceName)) Label(snapshot.Engines.DeviceName, 13);
        bool ready = !_state.Busy && !_service.TransferInProgress;
        if (_service.TransferInProgress) Label("Transferencia en curso. Los controles estarán disponibles al terminar.", 13);
        if (_quickAction is not null) { RenderQuick(ready); return; }
        if (snapshot.Media is { } media)
        {
            Button("Captura en PC", () => Send("capture"), media.CanCapture && ready);
            Button(media.Recording ? "Detener grabación en PC" : "Grabar en PC", () => Send(media.Recording ? "stopRecording" : "startRecording"), (media.Recording || media.CanRecord) && ready);
            if (!string.IsNullOrWhiteSpace(media.Message)) Label(media.Message, 12);
        }
        Button("Detener VisionEngine", () => { OpenMain("stopVideo"); return Task.CompletedTask; }, snapshot.Engines?.VideoCanStop == true && ready);
        Label("Ajustes rápidos", 16);
        QuickButton("Perfil", "profile", snapshot.Profile, snapshot.Profiles, ready);
        QuickButton("Calidad de imagen", "bitrate", snapshot.Bitrate, snapshot.Bitrates, ready);
        QuickButton("Audio de PC", "audio", snapshot.AudioOutput, snapshot.AudioOutputs, ready);
        var packages = NLAndroidUIFloatingPreferences.Favorites(_context);
        if (packages.Length > 0)
        {
            Label("Tus aplicaciones", 16);
            foreach (string package in packages)
            {
                string name = package; Android.Graphics.Drawables.Drawable? icon = null;
                try { var info = _context.PackageManager!.GetApplicationInfo(package, 0); name = info?.LoadLabel(_context.PackageManager!) ?? package; icon = info?.LoadIcon(_context.PackageManager!); }
                catch (Android.Content.PM.PackageManager.NameNotFoundException) { name += " (no disponible)"; }
                var button = Button(name, () => { OpenFavorite(package); return Task.CompletedTask; });
                if (icon is not null) { icon.SetBounds(0, 0, Dp(28), Dp(28)); button.SetCompoundDrawables(icon, null, null, null); button.CompoundDrawablePadding = Dp(8); }
            }
        }
        Button("Todos los ajustes", () => { OpenMain("settings"); return Task.CompletedTask; });
        Button("Accesos y control flotante", () => { Launch(new Intent(_context, typeof(NLAndroidUIFloatingSettingsActivity))); return Task.CompletedTask; });
        Button("Abrir NOVORA", () => { OpenMain(null); return Task.CompletedTask; });
        Button("Ocultar burbuja · mantener VE", () => { _hidden = true; Remove(); return Task.CompletedTask; });
        Label("Se solicita excluir el panel abierto de capturas. Comprueba el resultado en tu teléfono; no se garantiza una imagen limpia debajo del control.", 12);
    }
    private void QuickButton(string title, string action, string current, NLControlOption[] options, bool enabled)
    {
        if (options.Length == 0) return;
        string value = options.FirstOrDefault(x => x.Value == current)?.Label ?? current;
        Button(title + ": " + value, () =>
        {
            _quickGeneration = _state.Generation; _quickRevision = _state.Snapshot!.Revision;
            _quickAction = action; _quickOptions = options.ToArray(); _quickValue = current;
            _renderKey = null; return Task.CompletedTask;
        }, enabled);
    }
    private void RenderQuick(bool ready)
    {
        string title = _quickAction switch { "profile" => "Perfil", "bitrate" => "Calidad de imagen", _ => "Audio de PC" };
        Label(title, 19);
        Label(_quickAction == "audio" ? "Selecciona y confirma la salida de audio. NOVORA mostrará la respuesta de Windows."
            : "Se guardará en PC. La transmisión actual requiere reiniciar video desde Todos los ajustes para usar el cambio.", 13);
        foreach (var option in _quickOptions)
        {
            Button((_quickValue == option.Value ? "✓ " : "") + option.Label, () =>
            { _quickValue = option.Value; _renderKey = null; return Task.CompletedTask; }, ready);
        }
        var selected = _quickOptions.FirstOrDefault(x => x.Value == _quickValue);
        Button("Confirmar: " + (selected?.Label ?? "elige un valor"), ConfirmQuick, ready && selected is not null);
        Button("Cancelar y volver", () => { ClearQuick(); return Task.CompletedTask; });
    }
    private void ClearQuick() { _quickAction = _quickValue = null; _quickOptions = []; _renderKey = null; }
    private async Task ConfirmQuick()
    {
        var current = _service.Session.Current;
        if (_quickAction is null || _quickValue is null) return;
        if (current.Generation != _quickGeneration || current.Snapshot?.Revision != _quickRevision || current.Phase != NLControlSessionPhase.Connected || current.Busy || _service.TransferInProgress)
        { ClearQuick(); Toast.MakeText(_context, "El estado cambió. Vuelve a elegir el ajuste.", ToastLength.Long)?.Show(); return; }
        string action = _quickAction, value = _quickValue; long generation = _quickGeneration;
        ClearQuick();
        var reply = await _service.Session.SendAsync(action, value);
        if (!_disposed && _service.Session.Current.Generation == generation) Toast.MakeText(_context, reply.Message, ToastLength.Long)?.Show();
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
        var button = new Button(_context) { Text = text, Enabled = enabled }; button.SetAllCaps(false);
        button.Click += async (_, _) => { button.Enabled = false; try { await action(); } catch (Exception ex) { Toast.MakeText(_context, ex.Message, ToastLength.Long)?.Show(); } finally { if (!_disposed) { _renderKey = null; Refresh(); } } };
        _panel!.AddView(button, new LinearLayout.LayoutParams(-1, -2)); return button;
    }
    private void Remove()
    {
        ClearQuick();
        var view = _view; _view = null; _panel = null; _layout = null; _renderKey = null;
        if (view is not null) try { _manager.RemoveView(view); } catch (Java.Lang.RuntimeException) { }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; NLAndroidUIFloatingPreferences.Changed -= PreferencesChanged; Remove(); _main.RemoveCallbacksAndMessages(null); _main.Dispose(); }
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
