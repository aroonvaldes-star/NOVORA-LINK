using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace NOVORA.AndroidUI;

/// <summary>Shared native presentation for the NOVORA concept; no session ownership.</summary>
internal static class NLAndroidUIVisual
{
    internal static int Dp(Context c, int n) => (int)(n * c.Resources!.DisplayMetrics!.Density + .5f);
    internal static GradientDrawable Surface(Context c, string? fill = null, string? border = null, int? radius = null)
    {
        var palette = NLAndroidUITheme.Current(c);
        fill ??= palette.Surface; border ??= palette.Border;
        var d = new GradientDrawable(); d.SetColor(Color.ParseColor(fill));
        d.SetCornerRadius(Dp(c, radius ?? palette.CardRadius)); d.SetStroke(Dp(c, 1), Color.ParseColor(border)); return d;
    }
    internal static void Button(Button b, bool primary = false)
    {
        var c = b.Context!;
        var palette = NLAndroidUITheme.Current(c);
        b.SetAllCaps(false); b.TextSize = 14; b.Gravity = GravityFlags.Center;
        b.SetTypeface(Typeface.Create("sans-serif-medium", TypefaceStyle.Normal), TypefaceStyle.Normal);
        b.SetMinHeight(Dp(c, palette.TouchTarget)); b.SetMinimumHeight(Dp(c, palette.TouchTarget)); b.SetMinWidth(0); b.SetMinimumWidth(0);
        b.SetPadding(Dp(c, 12), Dp(c, 8), Dp(c, 12), Dp(c, 8));
        var states = new StateListDrawable();
        states.AddState([-Android.Resource.Attribute.StateEnabled], Surface(c, palette.SurfaceRaised, palette.Border));
        states.AddState([Android.Resource.Attribute.StatePressed], Surface(c, primary ? palette.Accent : palette.SurfaceRaised, palette.Accent));
        states.AddState([], Surface(c, primary ? palette.Accent : palette.Surface, primary ? palette.Accent : palette.Border));
        b.Background = states; b.BackgroundTintList = null;
        b.SetTextColor(new ColorStateList([new[] { -Android.Resource.Attribute.StateEnabled }, Array.Empty<int>()],
            [Color.ParseColor(palette.Disabled).ToArgb(), Color.ParseColor(primary ? palette.AccentText : palette.Text).ToArgb()]));
    }
}
