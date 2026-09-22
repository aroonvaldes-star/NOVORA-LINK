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

    internal static TextView LivePanel(Context context)
    {
        NLAndroidUIPalette palette = NLAndroidUITheme.Current(context);
        var live = new TextView(context) { TextSize = 13, Typeface = Android.Graphics.Typeface.Monospace };
        live.SetTextColor(Android.Graphics.Color.ParseColor(palette.Text));
        live.SetPadding(Dp(context, 12), Dp(context, 12), Dp(context, 12), Dp(context, 12));
        live.SetMinHeight(Dp(context, 180));
        live.Gravity = GravityFlags.Top | GravityFlags.Left;
        live.Background = NLAndroidUIVisual.Surface(context, palette.Navigation, palette.Border);
        live.ContentDescription = "Valores físicos y corregidos del control";
        return live;
    }

    private static int Dp(Context context, int value) => NLAndroidUIVisual.Dp(context, value);
}
