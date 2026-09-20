using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using Resource = NOVORA.AndroidApp.Resource;

namespace NOVORA.AndroidUI;

/// <summary>Shared native presentation for the NOVORA concept; no session ownership.</summary>
internal static class NLAndroidUIVisual
{
    internal static int Dp(Context c, int n) => (int)(n * c.Resources!.DisplayMetrics!.Density + .5f);
    internal static GradientDrawable Surface(Context c, string fill = "#182228", string border = "#35434B", int radius = 12)
    {
        var d = new GradientDrawable(); d.SetColor(Color.ParseColor(fill));
        d.SetCornerRadius(Dp(c, radius)); d.SetStroke(Dp(c, 1), Color.ParseColor(border)); return d;
    }
    internal static void Button(Button b, bool primary = false)
    {
        var c = b.Context!;
        b.SetAllCaps(false); b.TextSize = 14; b.Gravity = GravityFlags.Center;
        b.SetTypeface(Typeface.Create("sans-serif-medium", TypefaceStyle.Normal), TypefaceStyle.Normal);
        b.SetMinHeight(Dp(c, 48)); b.SetMinimumHeight(Dp(c, 48)); b.SetMinWidth(0); b.SetMinimumWidth(0);
        b.SetPadding(Dp(c, 12), Dp(c, 8), Dp(c, 12), Dp(c, 8));
        var states = new StateListDrawable();
        states.AddState([-Android.Resource.Attribute.StateEnabled], Surface(c, "#192329", "#28363E", 9));
        states.AddState([Android.Resource.Attribute.StatePressed], Surface(c, primary ? "#54D7F3" : "#2B3C47", "#00BDEA", 9));
        states.AddState([], Surface(c, primary ? "#00BDEA" : "#111B20", primary ? "#00BDEA" : "#4C606B", 9));
        b.Background = states; b.BackgroundTintList = null;
        b.SetTextColor(new ColorStateList([new[] { -Android.Resource.Attribute.StateEnabled }, Array.Empty<int>()],
            [Color.ParseColor("#81939D").ToArgb(), Color.ParseColor(primary ? "#071319" : "#ECF3F6").ToArgb()]));
    }
    internal static ImageView Icon(Context c, int resource, int size = 26)
    {
        var v = new ImageView(c); v.SetImageResource(resource); v.SetColorFilter(Color.ParseColor("#00BDEA"));
        v.SetScaleType(ImageView.ScaleType.FitCenter); v.ImportantForAccessibility = ImportantForAccessibility.No;
        v.LayoutParameters = new LinearLayout.LayoutParams(Dp(c, size), Dp(c, size)); return v;
    }
    internal static ImageView Logo(Context c, int size)
    {
        var v = new ImageView(c) { ContentDescription = "NOVORA" };
        v.SetImageResource(Resource.Drawable.novora_logo);
        v.SetScaleType(ImageView.ScaleType.Matrix);
        // The supplied brand image has transparent margins. Frame the emblem itself.
        v.LayoutChange += (_, _) => {
            if (v.Drawable is not { } d || v.Width <= 0 || v.Height <= 0) return;
            using var matrix = new Matrix();
            using var source = new RectF(d.IntrinsicWidth * .23f, d.IntrinsicHeight * .18f, d.IntrinsicWidth * .77f, d.IntrinsicHeight * .74f);
            using var target = new RectF(0, 0, Math.Max(1, v.Width - v.PaddingLeft - v.PaddingRight),
                Math.Max(1, v.Height - v.PaddingTop - v.PaddingBottom));
            matrix.SetRectToRect(source, target, Matrix.ScaleToFit.Center!); v.ImageMatrix = matrix;
        };
        v.LayoutParameters = new LinearLayout.LayoutParams(Dp(c, size), Dp(c, size)) { Gravity = GravityFlags.CenterHorizontal };
        return v;
    }
}
