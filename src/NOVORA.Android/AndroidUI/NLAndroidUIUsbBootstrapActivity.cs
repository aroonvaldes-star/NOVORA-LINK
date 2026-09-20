// NOVORA_AUTOUSB_V1
using Android.App;
using Android.Content;
using Android.OS;
using NOVORA.AndroidService;
using NOVORA.Control;

namespace NOVORA.AndroidUI;

// DUMP is held by Android shell/system, not ordinary third-party applications.
// No runtime permission is granted to NOVORA by this declaration.
[Activity(Name = "com.novora.appcontrol.UsbBootstrapActivity", Exported = true,
    Permission = "android.permission.DUMP", NoHistory = true, ExcludeFromRecents = true,
    Theme = "@android:style/Theme.Translucent.NoTitleBar")]
public sealed class NLAndroidUIUsbBootstrapActivity : Activity
{
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        try
        {
            string text = Intent?.GetStringExtra("novora.usb") ?? "";
            Intent?.RemoveExtra("novora.usb");
            _ = NLAndroidServiceUsbBootstrap.Parse(text);
            StartService(new Intent(this, typeof(NLAndroidServiceControl))
                .SetAction(NLAndroidServiceControl.UsbBootstrapAction)
                .PutExtra(NLAndroidServiceControl.UsbBootstrapExtra, text));
            StartActivity(new Intent(this, typeof(NLAndroidUIActivity))
                .AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop));
        }
        catch (Exception ex)
        {
            // No secret, invitation, PC name, serial, or exception message in logs.
            Android.Util.Log.Warn("NOVORA-USB", "BOOTSTRAP_REJECTED " + ex.GetType().Name);
        }
        finally { Finish(); }
    }
}
