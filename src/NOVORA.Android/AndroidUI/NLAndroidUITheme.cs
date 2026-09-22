using Android.Content;

namespace NOVORA.AndroidUI;

internal sealed record NLAndroidUIPalette(
    string Background, string Surface, string SurfaceRaised, string Border,
    string Text, string Muted, string Accent, string AccentText,
    string Navigation, string Disabled, string Success, string Error);

internal static class NLAndroidUITheme
{
    private const string Preferences = "novora.android.ui";
    private const string ThemeKey = "theme";

    internal static readonly NLAndroidUIPalette Dark = new(
        "#0A0E18", "#171B26", "#1C1F2A", "#343947",
        "#DFE2F1", "#B9CACB", "#00F0FF", "#071319",
        "#0F131D", "#69747F", "#55D98A", "#FF5C65");

    internal static readonly NLAndroidUIPalette Light = new(
        "#FAF8FF", "#FFFFFF", "#F2F3FF", "#D7D9E8",
        "#171B26", "#596171", "#006970", "#FFFFFF",
        "#EEF0FF", "#989EAA", "#147D57", "#BA1A1A");

    internal static bool IsDark(Context context) =>
        context.GetSharedPreferences(Preferences, FileCreationMode.Private)?.GetString(ThemeKey, "dark") != "light";

    internal static NLAndroidUIPalette Current(Context context) => IsDark(context) ? Dark : Light;

    internal static void Toggle(Context context)
    {
        var value = IsDark(context) ? "light" : "dark";
        context.GetSharedPreferences(Preferences, FileCreationMode.Private)?.Edit()?.PutString(ThemeKey, value)?.Apply();
    }
}
