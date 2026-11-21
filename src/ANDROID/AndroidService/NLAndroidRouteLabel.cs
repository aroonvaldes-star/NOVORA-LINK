using Android.Content;
using Android.OS;

namespace NOVORA.AndroidService;

// A session started by ADB reaches the PC through the adb reverse tunnel, so its transport is "USB"
// even when ADB runs over Wi-Fi (wireless debugging). These helpers name the physical route for the UI.
internal static class NLAndroidRouteLabel
{
    private const string UsbStateAction = "android.hardware.usb.action.USB_STATE";

    // True when a USB cable to a host is attached. Unknown state keeps the session's own label.
    public static bool UsbCableConnected(Context? context = null)
    {
        context ??= Android.App.Application.Context;
        try
        {
            Intent? usb = context.RegisterReceiver(null, new IntentFilter(UsbStateAction));
            if (usb?.HasExtra("connected") == true) return usb.GetBooleanExtra("connected", true);
            Intent? battery = context.RegisterReceiver(null, new IntentFilter(Intent.ActionBatteryChanged));
            if (battery is not null)
                return battery.GetIntExtra(BatteryManager.ExtraPlugged, -1) == (int)BatteryPlugged.Usb;
        }
        catch (Exception) { }
        return true;
    }

    // "USB" for a cable, "ADB_WIFI" for ADB over Wi-Fi, "LAN" for the LAN route.
    public static string Physical(string? transport, Context? context = null) => transport switch
    {
        "USB" => UsbCableConnected(context) ? "USB" : "ADB_WIFI",
        "LAN" => "LAN",
        _ => transport ?? ""
    };

    public static bool IsWifi(string? transport, Context? context = null) =>
        Physical(transport, context) is "LAN" or "ADB_WIFI";

    public static string Display(string? transport, Context? context = null) => Physical(transport, context) switch
    {
        "USB" => "USB",
        "ADB_WIFI" => "ADB Wi-Fi",
        "LAN" => "Wi-Fi",
        var other => other
    };
}
