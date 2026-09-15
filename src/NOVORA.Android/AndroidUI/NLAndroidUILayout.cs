using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace NOVORA.AndroidUI;

public static class NLAndroidUILayout
{
    public static void Prepare(View root, Context context)
    {
        int padding = (int)(16 * context.Resources!.DisplayMetrics!.Density);
        root.SetBackgroundColor(Color.ParseColor("#10191E"));
        root.SetPadding(padding, padding, padding, padding);
        root.SetOnApplyWindowInsetsListener(new Insets(padding));
    }
    private sealed class Insets(int padding) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
        {
            if (insets is null) return null!;
            if (OperatingSystem.IsAndroidVersionAtLeast(30)) {
                var safe = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.Ime() | WindowInsets.Type.DisplayCutout());
                view?.SetPadding(safe.Left + padding, safe.Top + padding, safe.Right + padding, safe.Bottom + padding);
            }
            else {
#pragma warning disable CS0618, CA1422
                view?.SetPadding(insets.SystemWindowInsetLeft + padding, insets.SystemWindowInsetTop + padding,
                    insets.SystemWindowInsetRight + padding, insets.SystemWindowInsetBottom + padding);
#pragma warning restore CS0618, CA1422
            }
            return insets;
        }
    }
}
