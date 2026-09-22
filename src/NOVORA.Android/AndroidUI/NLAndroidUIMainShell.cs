using Android.Content;
using Android.Views;
using Android.Widget;

namespace NOVORA.AndroidUI;

internal static class NLAndroidUIMainShell
{
    internal static ScrollView CreatePage(Context context, FrameLayout host, out LinearLayout body)
    {
        var scroll = new ScrollView(context) { FillViewport = true };
        body = new LinearLayout(context) { Orientation = Orientation.Vertical };
        int padding = NLAndroidUIVisual.Dp(context, NLAndroidUITheme.Current(context).PagePadding);
        body.SetPadding(padding, NLAndroidUIVisual.Dp(context, 8), padding, NLAndroidUIVisual.Dp(context, 24));
        var lane = new FrameLayout(context);
        int width = Math.Min(context.Resources!.DisplayMetrics!.WidthPixels, NLAndroidUIVisual.Dp(context, 560));
        lane.AddView(body, new FrameLayout.LayoutParams(width, -2, GravityFlags.Top | GravityFlags.CenterHorizontal));
        scroll.AddView(lane, new ScrollView.LayoutParams(-1, -2));
        host.AddView(scroll, new FrameLayout.LayoutParams(-1, -1));
        return scroll;
    }

    internal static LinearLayout BottomNavigation(Context context) => new(context) {
        Orientation = Orientation.Horizontal,
        Background = NLAndroidUIVisual.Surface(context,
            NLAndroidUITheme.Current(context).Navigation,
            NLAndroidUITheme.Current(context).Border,
            0)
    };
}
