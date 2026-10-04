using Android.Content;

namespace NOVORA.LinkClient;

internal static class NovoraVeTransportPreference
{
    private const string PreferencesName = "novora_linkclient";
    private const string SelectionKey = "ve_transport";

    internal static NovoraVeTransportSelection Load(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string? value = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
            ?.GetString(SelectionKey, null);
        return NovoraVeTransportPolicy.Resolve(value);
    }

    internal static void Save(Context context, NovoraVeTransportSelection selection)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
            ?.Edit()?.PutString(SelectionKey, selection == NovoraVeTransportSelection.Lan ? "LAN" : "USB")
            ?.Apply();
    }
}
