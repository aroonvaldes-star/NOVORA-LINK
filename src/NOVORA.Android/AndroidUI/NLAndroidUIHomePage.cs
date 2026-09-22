using Android.Content;
using Android.Widget;

namespace NOVORA.AndroidUI;

internal static class NLAndroidUIHomePage
{
    internal static Button Action(Context context, string title, string subtitle, Action action)
    {
        int icon = title switch {
            "Conectar" => Android.Resource.Drawable.IcMenuSearch,
            "Engines" => Android.Resource.Drawable.IcMenuManage,
            "Archivos" => Android.Resource.Drawable.IcMenuGallery,
            _ => Android.Resource.Drawable.IcMenuSlideshow
        };
        return NLAndroidUIComponents.ActionTile(context, title, subtitle, icon, action);
    }
}
