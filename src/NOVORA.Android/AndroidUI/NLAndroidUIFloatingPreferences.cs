using Android.Content;
using System.Text.Json;

namespace NOVORA.AndroidUI;

/// <summary>Local UI preferences only. Never stores session credentials or clipboard contents.</summary>
public static class NLAndroidUIFloatingPreferences
{
    public const string StoreName = "novora_floating";
    public static event EventHandler? Changed;
    private static ISharedPreferences Store(Context context) => context.GetSharedPreferences(StoreName, FileCreationMode.Private)!;
    public static bool IsEnabled(Context context) => Store(context).GetBoolean("enabled", false);
    public static void SetEnabled(Context context, bool enabled)
    { Store(context).Edit()!.PutBoolean("enabled", enabled)!.Apply(); Changed?.Invoke(null, EventArgs.Empty); }
    public static bool ExcludeFromCapture(Context context) => Store(context).GetBoolean("exclude_capture", false);
    public static void SetExcludeFromCapture(Context context, bool exclude)
    { Store(context).Edit()!.PutBoolean("exclude_capture", exclude)!.Apply(); Changed?.Invoke(null, EventArgs.Empty); }
    public static int BubbleOpacity(Context context) => Math.Clamp(Store(context).GetInt("bubble_opacity", 100), 20, 100);
    public static void SetBubbleOpacity(Context context, int percent)
    { Store(context).Edit()!.PutInt("bubble_opacity", Math.Clamp(percent, 20, 100))!.Apply(); Changed?.Invoke(null, EventArgs.Empty); }
    public static string[] Favorites(Context context)
    {
        try { return (JsonSerializer.Deserialize<string[]>(Store(context).GetString("favorites", "[]")!) ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(100).ToArray(); }
        catch (JsonException) { return []; }
    }
    public static void SaveFavorites(Context context, IEnumerable<string> packages)
    { Store(context).Edit()!.PutString("favorites", JsonSerializer.Serialize(packages.Distinct().Take(100)))!.Apply(); Changed?.Invoke(null, EventArgs.Empty); }
    public static (float X, float Y) Position(Context context)
    { var store = Store(context); return (Math.Clamp(store.GetFloat("x", 1), 0, 1), Math.Clamp(store.GetFloat("y", .4f), 0, 1)); }
    public static void SavePosition(Context context, float x, float y) => Store(context).Edit()!.PutFloat("x", Math.Clamp(x, 0, 1))!.PutFloat("y", Math.Clamp(y, 0, 1))!.Apply();
}
