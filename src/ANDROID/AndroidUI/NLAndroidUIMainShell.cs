using Android.Content;
using Android.Views;
using Android.Widget;

namespace NOVORA.AndroidUI;

internal static class NLAndroidUIMainShell
{
    internal static ScrollView CreatePage(Context context, FrameLayout host, out LinearLayout body, int maximumWidthDp = 560)
    {
        var scroll = new ScrollView(context) { FillViewport = true };
        body = new LinearLayout(context) { Orientation = Orientation.Vertical };
        int padding = NLAndroidUIVisual.Dp(context, NLAndroidUITheme.Current(context).PagePadding);
        body.SetPadding(padding, NLAndroidUIVisual.Dp(context, 8), padding, NLAndroidUIVisual.Dp(context, 24));
        var lane = new FrameLayout(context);
        int maximumWidth = NLAndroidUIVisual.Dp(context, maximumWidthDp);
        int width = Math.Min(context.Resources!.DisplayMetrics!.WidthPixels, maximumWidth);
        lane.AddView(body, new FrameLayout.LayoutParams(width, -2, GravityFlags.Top | GravityFlags.CenterHorizontal));
        LinearLayout content = body;
        lane.LayoutChange += (_, args) =>
        {
            int available = args.Right - args.Left;
            if (available <= 0 || content.LayoutParameters is not FrameLayout.LayoutParams layout) return;
            int target = Math.Min(available, maximumWidth);
            if (layout.Width == target) return;
            layout.Width = target;
            content.LayoutParameters = layout;
        };
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
