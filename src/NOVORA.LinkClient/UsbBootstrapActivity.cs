using Android.App;
using Android.Content;
using Android.OS;

namespace NOVORA.LinkClient;

[Activity(Name = "com.novora.linkclient.UsbBootstrapActivity", Exported = true,
    Permission = "android.permission.DUMP", NoHistory = true, ExcludeFromRecents = true,
    Theme = "@android:style/Theme.Translucent.NoTitleBar")]
public sealed class UsbBootstrapActivity : Activity
{
    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        try
        {
            string text = Intent?.GetStringExtra("novora.usb") ?? string.Empty;
            Intent?.RemoveExtra("novora.usb");
            await NovoraConnection.ConnectTunnelAsync(NovoraUsbBootstrap.Parse(text));
            StartActivity(new Intent(this, typeof(HomeActivity))
                .AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop));
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("NOVORA-LINK-USB", "BOOTSTRAP_REJECTED " + ex.GetType().Name);
        }
        finally { Finish(); }
    }
}
