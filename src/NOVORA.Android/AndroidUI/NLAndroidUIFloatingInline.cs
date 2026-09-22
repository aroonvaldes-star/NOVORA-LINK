using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Provider;
using Android.Views;
using Android.Widget;

namespace NOVORA.AndroidUI;

internal static class NLAndroidUIFloatingInline
{
    internal static void Build(Activity host, LinearLayout body)
    {
        NLAndroidUIPalette palette = NLAndroidUITheme.Current(host);
        int Dp(int n) => NLAndroidUIVisual.Dp(host, n);
        void Label(string value) { var text = new TextView(host) { Text = value, TextSize = 14 }; text.SetTextColor(Color.ParseColor(palette.Text)); text.SetPadding(0,Dp(8),0,Dp(8)); body.AddView(text); }
        void Button(string label, Action action) {
            var button = new Button(host) { Text = label }; NLAndroidUIVisual.Button(button);
            button.Click += (_,_) => { try { action(); } catch (ActivityNotFoundException) { Toast.MakeText(host,"Abre este permiso desde Ajustes de Android.",ToastLength.Long)?.Show(); } };
            body.AddView(button,new LinearLayout.LayoutParams(-1,-2));
        }
        Label("BURBUJA Y APLICACIONES");
        var enabled = new Switch(host) { Text = "Usar burbuja con VE", Checked = NLAndroidUIFloatingPreferences.IsEnabled(host) }; enabled.SetTextColor(Color.ParseColor(palette.Text));
        body.AddView(enabled);
        int dependencyStart = body.ChildCount;
        enabled.CheckedChange += (_,e) => { NLAndroidUIFloatingPreferences.SetEnabled(host,e.IsChecked); SetDetailsVisible(e.IsChecked); };
        Label("La burbuja y el panel se muestran en VE y en sus grabaciones.");
        Button("Permiso para mostrar la burbuja", () => host.StartActivity(new Intent(Settings.ActionManageOverlayPermission, Android.Net.Uri.Parse("package:" + host.PackageName))));
        Label("Opacidad de la burbuja");
        var opacity = new SeekBar(host) { Max = 80, Progress = NLAndroidUIFloatingPreferences.BubbleOpacity(host)-20 };
        opacity.StopTrackingTouch += (_,_) => NLAndroidUIFloatingPreferences.SetBubbleOpacity(host,opacity.Progress+20); body.AddView(opacity);
        Label("Controles de música: requiere acceso a notificaciones. NOVORA no lee ni guarda su contenido.");
        Button("Acceso a controles multimedia", () => host.StartActivity(new Intent(Settings.ActionNotificationListenerSettings)));
        Label("Apps favoritas · puedes dejar la lista vacía");
        var favorites = new LinearLayout(host) { Orientation = Orientation.Vertical }; body.AddView(favorites);
        void RenderFavorites() {
            favorites.RemoveAllViews();
            foreach (string package in NLAndroidUIFloatingPreferences.Favorites(host)) {
                string title = package;
                try { title = host.PackageManager!.GetApplicationInfo(package,0)?.LoadLabel(host.PackageManager!) ?? package; }
                catch (Android.Content.PM.PackageManager.NameNotFoundException) { }
                var remove = new Button(host) { Text = "Quitar · " + title }; NLAndroidUIVisual.Button(remove);
                remove.Click += (_,_) => { NLAndroidUIFloatingPreferences.SaveFavorites(host, NLAndroidUIFloatingPreferences.Favorites(host).Where(p=>p!=package)); RenderFavorites(); };
                favorites.AddView(remove,new LinearLayout.LayoutParams(-1,-2));
            }
        }
        RenderFavorites();
        Button("Agregar aplicación", () => {
            var selected = NLAndroidUIFloatingPreferences.Favorites(host);
            var intent = new Intent(Intent.ActionMain); intent.AddCategory(Intent.CategoryLauncher);
            var apps = (host.PackageManager!.QueryIntentActivities(intent,0) ?? []).Where(x=>x.ActivityInfo?.PackageName is { } p && p!=host.PackageName && !selected.Contains(p))
                .GroupBy(x=>x.ActivityInfo!.PackageName!).Select(g=>g.First())
                .Select(x=>(Package:x.ActivityInfo!.PackageName!,Name:x.LoadLabel(host.PackageManager!) ?? x.ActivityInfo!.PackageName!)).OrderBy(x=>x.Name).ToArray();
            var content = new LinearLayout(host) { Orientation = Orientation.Vertical };
            var search = new EditText(host) { Hint="Buscar aplicación" }; content.AddView(search);
            var list = new ListView(host); content.AddView(list,new LinearLayout.LayoutParams(-1,Dp(300)));
            var filtered = apps;
            void Filter() { filtered=apps.Where(x=>x.Name.Contains(search.Text??"",StringComparison.CurrentCultureIgnoreCase)).ToArray(); list.Adapter=new ArrayAdapter<string>(host,Android.Resource.Layout.SimpleListItem1,filtered.Select(x=>x.Name).ToArray()); }
            search.TextChanged += (_,_)=>Filter(); Filter();
            var dialog = new AlertDialog.Builder(host)!.SetTitle("Agregar aplicación")!.SetView(content)!.SetNegativeButton("Cancelar",(_,_)=>{})!.Create()!;
            list.ItemClick += (_,e)=> { if(e.Position<0 || e.Position>=filtered.Length) return; NLAndroidUIFloatingPreferences.SaveFavorites(host,selected.Append(filtered[e.Position].Package)); RenderFavorites(); dialog.Dismiss(); };
            dialog.Show();
        });
        SetDetailsVisible(enabled.Checked);

        void SetDetailsVisible(bool visible)
        {
            for (int index = dependencyStart; index < body.ChildCount; index++)
                body.GetChildAt(index)!.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
        }
    }
}
