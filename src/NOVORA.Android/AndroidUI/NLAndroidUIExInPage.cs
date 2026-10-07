using Android.Content;
using Android.Views;
using Android.Widget;

namespace NOVORA.AndroidUI;

internal static class NLAndroidUIExInPage
{
    internal static (LinearLayout View, Button Game, Button Ui) ModeSelector(
        Context context, Func<Task> game, Func<Task> ui)
    {
        LinearLayout control = NLAndroidUIComponents.SegmentedControl(context,
        [
            ("Juego", true, true, game),
            ("UI", false, true, ui)
        ]);
        return (control, (Button)control.GetChildAt(0)!, (Button)control.GetChildAt(1)!);
    }

    internal static LinearLayout LivePanel(Context context, out NLAndroidUIControllerLiveView controller, out TextView live)
    {
        NLAndroidUIPalette palette = NLAndroidUITheme.Current(context);
        var panel = new LinearLayout(context) { Orientation = Orientation.Vertical };
        controller = new NLAndroidUIControllerLiveView(context);
        panel.AddView(controller, new LinearLayout.LayoutParams(-1, Dp(context, 230)));
        live = new TextView(context) { TextSize = 13, Typeface = Android.Graphics.Typeface.Monospace };
        live.SetTextColor(Android.Graphics.Color.ParseColor(palette.Text));
        live.SetPadding(Dp(context, 12), Dp(context, 12), Dp(context, 12), Dp(context, 12));
        live.SetMinHeight(Dp(context, 180));
        live.Gravity = GravityFlags.Top | GravityFlags.Left;
        live.Background = NLAndroidUIVisual.Surface(context, palette.Navigation, palette.Border);
        live.ContentDescription = "Valores físicos y corregidos del control";
        panel.AddView(live, new LinearLayout.LayoutParams(-1, -2));
        return panel;
    }

    private static int Dp(Context context, int value) => NLAndroidUIVisual.Dp(context, value);
}
