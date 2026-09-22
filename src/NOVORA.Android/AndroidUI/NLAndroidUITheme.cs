using Android.Content;

namespace NOVORA.AndroidUI;

internal sealed record NLAndroidUIPalette(
    string Background, string Surface, string SurfaceRaised, string Border,
    string Text, string Muted, string Accent, string AccentText,
    string Navigation, string Disabled, string Success, string Warning, string Error,
    int CardRadius, int PagePadding, int TouchTarget);

internal static class NLAndroidUITheme
{
    private const string Preferences = "novora.android.ui";
    private const string ThemeKey = "theme";

    internal static readonly NLAndroidUIPalette Dark = new(
        "#0A0F11", "#171C1E", "#20282C", "#2B3A40",
        "#FFFFFF", "#91A2AA", "#00DCE8", "#071319",
        "#0F1416", "#607178", "#20D982", "#F2B84B", "#FF6670",
        8, 16, 48);

    internal static readonly NLAndroidUIPalette Light = new(
        "#F6FAFA", "#FFFFFF", "#EDF3F4", "#D2DEE1",
        "#191C1D", "#53646C", "#087F91", "#FFFFFF",
        "#FFFFFF", "#839095", "#16845B", "#976C00", "#B3262E",
        8, 16, 48);

    internal static bool IsDark(Context context) =>
        context.GetSharedPreferences(Preferences, FileCreationMode.Private)?.GetString(ThemeKey, "dark") != "light";

    internal static NLAndroidUIPalette Current(Context context) => IsDark(context) ? Dark : Light;

    internal static void Toggle(Context context)
    {
        var value = IsDark(context) ? "light" : "dark";
        context.GetSharedPreferences(Preferences, FileCreationMode.Private)?.Edit()?.PutString(ThemeKey, value)?.Apply();
    }
}
