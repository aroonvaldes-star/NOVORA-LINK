using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Widget;
using Android.Views;
using NOVORA.AndroidService;

namespace NOVORA.AndroidUI;

[Activity(Name = "com.novora.appcontrol.FloatingSettingsActivity", Label = "Control flotante", Exported = false,
    Theme = "@android:style/Theme.Material.NoActionBar")]
public sealed class NLAndroidUIFloatingSettingsActivity : Activity
{
    private LinearLayout _body = null!;
    private SettingsConnection? _connection;
    private NLAndroidServiceControl? _service;
    private bool _bound, _started;
    protected override void OnCreate(Bundle? savedInstanceState)
    { base.OnCreate(savedInstanceState); Window?.SetSoftInputMode(SoftInput.AdjustResize); }
    protected override void OnResume() { base.OnResume(); Render(); }
    protected override void OnStart()
    {
        base.OnStart(); _started = true;
        _connection = new SettingsConnection(this);
        _bound = BindService(new Intent(this, typeof(NLAndroidServiceControl)), _connection, Bind.AutoCreate);
    }
    protected override void OnStop()
    {
        _started = false; _service?.SetUiForeground(false); _service = null;
        if (_bound && _connection is not null) UnbindService(_connection);
        _bound = false; _connection = null; base.OnStop();
    }
    private sealed class SettingsConnection(NLAndroidUIFloatingSettingsActivity owner) : Java.Lang.Object, IServiceConnection
    {
        public void OnServiceConnected(ComponentName? name, IBinder? binder)
        {
            if (!owner._started || !ReferenceEquals(owner._connection, this) || binder is not NLAndroidServiceControl.NLAndroidServiceBinder service) return;
            owner._service = service.Owner; owner._service.SetUiForeground(true);
        }
        public void OnServiceDisconnected(ComponentName? name) { owner._service = null; }
    }
    private int Dp(int n) => (int)(n * Resources!.DisplayMetrics!.Density + .5f);
    private void Render()
    {
        var scroll = new ScrollView(this);
        scroll.SetOnApplyWindowInsetsListener(new SettingsInsets());
        scroll.SetBackgroundColor(Color.ParseColor("#10191E"));
        _body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _body.SetPadding(Dp(20), Dp(28), Dp(20), Dp(24));
        scroll.AddView(_body); SetContentView(scroll);
        AddButton("← Volver a NOVORA", Finish);
        Label("Control flotante", 24);
        Label("Controles de música: muestra canción, progreso y botones de las apps que elijas cuando compartan su reproducción con Android. Requiere acceso a notificaciones; NOVORA no lee ni guarda el contenido de tus notificaciones.");
        AddButton(NLAndroidUIMediaAccess.Enabled(this) ? "Administrar acceso multimedia" : "Activar controles multimedia", () => {
            try { StartActivity(new Intent(Settings.ActionNotificationListenerSettings)); }
            catch (ActivityNotFoundException) { Toast.MakeText(this, "Abre Ajustes de Android y busca Acceso a notificaciones.", ToastLength.Long)?.Show(); }
        });
        Label("Aparece al salir de NOVORA mientras VisionEngine esté activo. Puedes moverlo, plegarlo u ocultarlo. La burbuja puede aparecer en la transmisión y grabación.");
        var enabled = new Switch(this) { Text = "Usar control flotante", Checked = NLAndroidUIFloatingPreferences.IsEnabled(this) };
        enabled.SetTextColor(Color.White); enabled.SetPadding(0, Dp(12), 0, Dp(12));
        enabled.CheckedChange += (_, e) => NLAndroidUIFloatingPreferences.SetEnabled(this, e.IsChecked);
        _body.AddView(enabled);
        Label(Settings.CanDrawOverlays(this) ? "Permiso para mostrar sobre otras aplicaciones: concedido." : "Falta el permiso para mostrar sobre otras aplicaciones.");
        AddButton("Configurar permiso de Android", () =>
        {
            try { StartActivity(new Intent(Settings.ActionManageOverlayPermission, Android.Net.Uri.Parse("package:" + PackageName))); }
            catch (ActivityNotFoundException) { Toast.MakeText(this, "Abre Ajustes de Android y busca Mostrar sobre otras aplicaciones.", ToastLength.Long)?.Show(); }
        });
        Label("La burbuja y su panel son visibles en VisionEngine y sus grabaciones.");
        int opacity = NLAndroidUIFloatingPreferences.BubbleOpacity(this);
        var opacityLabel = new TextView(this) { Text = $"Opacidad de la burbuja: {opacity} %", TextSize = 16 };
        opacityLabel.SetTextColor(Color.White); opacityLabel.SetPadding(0, Dp(10), 0, Dp(6)); _body.AddView(opacityLabel);
        var opacitySlider = new SeekBar(this) { Max = 80, Progress = opacity - 20, ContentDescription = "Opacidad de la burbuja, de 20 a 100 por ciento" };
        opacitySlider.SetOnSeekBarChangeListener(new OpacityListener(this, opacityLabel));
        _body.AddView(opacitySlider, new LinearLayout.LayoutParams(-1, -2));
        Label("Solo cambia la burbuja; el panel conserva su legibilidad. La opacidad mínima es 20 % para poder encontrar el control.");
        Label("Accesos del control flotante", 21);
        Label("Escoge las aplicaciones que utilizas. Puedes no agregar ninguna. Quitar un acceso no desinstala la aplicación.");
        AddButton("Agregar aplicación", ChooseApp);
        string[] packages = NLAndroidUIFloatingPreferences.Favorites(this);
        if (packages.Length == 0) Label("Sin accesos. Esta sección permanecerá oculta en el panel.");
        for (int i = 0; i < packages.Length; i++)
        {
            int index = i; string package = packages[i]; string name = package;
            try { var info = PackageManager!.GetApplicationInfo(package, 0); name = info?.LoadLabel(PackageManager!) ?? package; }
            catch (PackageManager.NameNotFoundException) { name += " (no disponible)"; }
            Label(name, 17);
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal }; _body.AddView(row);
            void Move(int delta) { var list = packages.ToList(); (list[index], list[index + delta]) = (list[index + delta], list[index]); NLAndroidUIFloatingPreferences.SaveFavorites(this, list); Render(); }
            var up = SmallButton(row, "Subir", () => Move(-1)); up.Enabled = i > 0;
            var down = SmallButton(row, "Bajar", () => Move(1)); down.Enabled = i < packages.Length - 1;
            SmallButton(row, "Quitar", () => { NLAndroidUIFloatingPreferences.SaveFavorites(this, packages.Where(x => x != package)); Render(); });
        }
    }
    private void ChooseApp()
    {
        var intent = new Intent(Intent.ActionMain); intent.AddCategory(Intent.CategoryLauncher);
        var selected = NLAndroidUIFloatingPreferences.Favorites(this).ToHashSet(StringComparer.Ordinal);
        var apps = (PackageManager!.QueryIntentActivities(intent, 0) ?? [])
            .Where(r => r.ActivityInfo?.PackageName is { } p && p != PackageName && !selected.Contains(p))
            .GroupBy(r => r.ActivityInfo!.PackageName!).Select(g => g.First())
            .Select(r => (Package: r.ActivityInfo!.PackageName!, Name: r.LoadLabel(PackageManager!) ?? r.ActivityInfo!.PackageName!))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (apps.Length == 0) { Toast.MakeText(this, "No hay otras aplicaciones disponibles para agregar.", ToastLength.Long)?.Show(); return; }
        var container = new LinearLayout(this) { Orientation = Orientation.Vertical }; container.SetPadding(Dp(12), 0, Dp(12), 0);
        var search = new EditText(this) { Hint = "Buscar aplicación" }; search.SetSingleLine(true); container.AddView(search);
        var list = new ListView(this); container.AddView(list, new LinearLayout.LayoutParams(-1, Dp(320)));
        var filtered = apps;
        void Filter() { filtered = apps.Where(x => x.Name.Contains(search.Text ?? "", StringComparison.CurrentCultureIgnoreCase)).ToArray(); list.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItem1, filtered.Select(x => x.Name).ToArray()); }
        search.TextChanged += (_, _) => Filter(); Filter();
        var dialog = new AlertDialog.Builder(this)!.SetTitle("Agregar un acceso")!.SetView(container)!.SetNegativeButton("Cancelar", (_, _) => { })!.Create()!;
        list.ItemClick += (_, e) => { if (e.Position < 0 || e.Position >= filtered.Length) return; NLAndroidUIFloatingPreferences.SaveFavorites(this, NLAndroidUIFloatingPreferences.Favorites(this).Append(filtered[e.Position].Package)); dialog.Dismiss(); Render(); };
        dialog.Show();
    }
    private void Label(string text, int size = 14) { var label = new TextView(this) { Text = text, TextSize = size }; label.SetTextColor(Color.White); label.SetPadding(0, Dp(10), 0, Dp(6)); _body.AddView(label); }
    private void AddButton(string text, Action action) { var button = new Button(this) { Text = text }; NLAndroidUIVisual.Button(button); button.Click += (_, _) => action(); _body.AddView(button, new LinearLayout.LayoutParams(-1, -2)); }
    private Button SmallButton(LinearLayout row, string text, Action action) { var button = new Button(this) { Text = text }; NLAndroidUIVisual.Button(button); button.Click += (_, _) => action(); row.AddView(button, new LinearLayout.LayoutParams(0, -2, 1)); return button; }
    private sealed class SettingsInsets : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
        {
            if (insets is null) return null!;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var safe = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.Ime() | WindowInsets.Type.DisplayCutout())!;
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
    private sealed class OpacityListener(Context context, TextView label) : Java.Lang.Object, SeekBar.IOnSeekBarChangeListener
    {
        private bool _tracking;
        public void OnProgressChanged(SeekBar? seekBar, int progress, bool fromUser)
        {
            label.Text = $"Opacidad de la burbuja: {progress + 20} %";
            // Keyboard/accessibility adjustments may not produce a touch-tracking pair.
            if (fromUser && !_tracking) NLAndroidUIFloatingPreferences.SetBubbleOpacity(context, progress + 20);
        }
        public void OnStartTrackingTouch(SeekBar? seekBar) => _tracking = true;
        public void OnStopTrackingTouch(SeekBar? seekBar)
        {
            _tracking = false;
            if (seekBar is not null) NLAndroidUIFloatingPreferences.SetBubbleOpacity(context, seekBar.Progress + 20);
        }
    }
}
