using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Widget;
using Android.Graphics;
using NOVORA.AndroidService;
using Uri = Android.Net.Uri;

namespace NOVORA.AndroidUI;

/// <summary>User grants access to the visible NOVORA directory; no all-files permission.</summary>
[Activity(Name = "com.novora.appcontrol.FilesActivity", Label = "Archivos NOVORA", Exported = false,
    Theme = "@android:style/Theme.Material.NoActionBar")]
public sealed class NLAndroidUIFilesActivity : Activity
{
    private TextView _status = null!;
    private const string Key = "novora_public_tree";
    private FilesConnection? _connection;
    private NLAndroidServiceControl? _service;
    private bool _bound, _started;
    protected override void OnStart()
    {
        base.OnStart(); _started = true;
        _connection = new FilesConnection(this);
        _bound = BindService(new Intent(this, typeof(NLAndroidServiceControl)), _connection, Bind.AutoCreate);
    }
    protected override void OnStop()
    {
        _started = false; _service?.SetUiForeground(false); _service = null;
        if (_bound && _connection is not null) UnbindService(_connection);
        _bound = false; _connection = null; base.OnStop();
    }
    private sealed class FilesConnection(NLAndroidUIFilesActivity owner) : Java.Lang.Object, IServiceConnection
    {
        public void OnServiceConnected(ComponentName? name, IBinder? binder)
        {
            if (!owner._started || !ReferenceEquals(owner._connection, this) || binder is not NLAndroidServiceControl.NLAndroidServiceBinder control) return;
            owner._service = control.Owner; owner._service.SetUiForeground(true);
        }
        public void OnServiceDisconnected(ComponentName? name) { owner._service = null; }
    }
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var palette = NLAndroidUITheme.Current(this);
        int padding = NLAndroidUIVisual.Dp(this, palette.PagePadding);
        layout.SetPadding(padding, padding, padding, padding);
        layout.SetBackgroundColor(Color.ParseColor(palette.Background));
        _status = new TextView(this) { Text = "Elige la carpeta NOVORA del almacenamiento interno. Los archivos se organizan por tipo.", TextSize = 18 };
        _status.SetTextColor(Color.ParseColor(palette.Text));
        layout.AddView(_status);
        var choose = new Button(this) { Text = "Elegir o crear carpeta NOVORA" };
        NLAndroidUIVisual.Button(choose, true);
        choose.Click += (_, _) => StartActivityForResult(new Intent(Intent.ActionOpenDocumentTree)
            .AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission), 1);
        layout.AddView(choose);
        foreach (string category in new[] { "Imagenes", "Videos", "Audio", "Documentos", "Comprimidos", "Instaladores", "Otros" })
        {
            var button = new Button(this) { Text = category };
            NLAndroidUIVisual.Button(button);
            button.Click += (_, _) => OpenCategory(category);
            layout.AddView(button);
        }
        var send = new Button(this) { Text = "Enviar archivos a PC" };
        NLAndroidUIVisual.Button(send);
        send.Click += (_, _) => StartActivity(new Intent(this, typeof(NLAndroidUIShareActivity)));
        layout.AddView(send);
        var scroll = new ScrollView(this); scroll.AddView(layout); NLAndroidUILayout.Prepare(scroll, this); SetContentView(scroll);
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == 2 && resultCode == Result.Ok && data?.Data is { } file)
        {
            try
            {
                var open = new Intent(Intent.ActionView).SetDataAndType(file, ContentResolver!.GetType(file) ?? "application/octet-stream")
                    .AddFlags(ActivityFlags.GrantReadUriPermission);
                StartActivity(Intent.CreateChooser(open, "Abrir archivo con"));
            }
            catch (Exception ex) { _status.Text = "No se pudo abrir el archivo. " + ex.Message; }
            return;
        }
        if (requestCode != 1 || resultCode != Result.Ok || data?.Data is not { } tree) return;
        try
        {
            // Require the same shared root used by the existing ADB exchange.
            if (tree.Authority != "com.android.externalstorage.documents" || DocumentsContract.GetTreeDocumentId(tree) != "primary:NOVORA")
                throw new InvalidOperationException("Selecciona NOVORA directamente dentro del almacenamiento interno, no otra carpeta.");
            ContentResolver!.TakePersistableUriPermission(tree, data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission));
            GetSharedPreferences("novora_files", FileCreationMode.Private)!.Edit()!.PutString(Key, tree.ToString())!.Apply();
            _status.Text = "Carpeta NOVORA vinculada. Las categorías se crean al abrirlas o recibir contenido.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private void OpenCategory(string category)
    {
        try
        {
            string? saved = GetSharedPreferences("novora_files", FileCreationMode.Private)!.GetString(Key, null);
            if (saved is null) throw new InvalidOperationException("Primero elige la carpeta NOVORA.");
            var tree = Uri.Parse(saved)!;
            string parentId = DocumentsContract.GetTreeDocumentId(tree)!;
            var parent = DocumentsContract.BuildDocumentUriUsingTree(tree, parentId)!;
            var children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, parentId)!;
            Uri? folder = null;
            using (var cursor = ContentResolver!.Query(children, new[] { DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType }, null, null, null))
            {
                while (cursor?.MoveToNext() == true)
                    if (cursor.GetString(1) == category && cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir)
                    { folder = DocumentsContract.BuildDocumentUriUsingTree(tree, cursor.GetString(0)); break; }
            }
            folder ??= DocumentsContract.CreateDocument(ContentResolver!, parent, DocumentsContract.Document.MimeTypeDir, category);
            if (folder is null) throw new IOException("No se pudo crear la categoría.");
            var browse = new Intent(Intent.ActionOpenDocument).SetType("*/*").AddCategory(Intent.CategoryOpenable);
            browse.PutExtra(DocumentsContract.ExtraInitialUri, folder);
            StartActivityForResult(browse, 2);
        }
        catch (Exception ex) { _status.Text = "No se pudo acceder a la carpeta. " + ex.Message; }
    }
}
