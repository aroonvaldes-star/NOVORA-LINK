using Android.App;
using Android.Content;
using Android.OS;
using NOVORA.AndroidService;

namespace NOVORA.AndroidUI;

// DUMP is available to the ADB shell, not to ordinary Android applications.
[Activity(Name = "com.linkcontrol.novora.LanCodeActivity", Exported = true,
    Permission = "android.permission.DUMP", NoHistory = true, ExcludeFromRecents = true,
    Theme = "@android:style/Theme.Translucent.NoTitleBar")]
public sealed class NLAndroidUILanCodeActivity : Activity
{
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        try
        {
            string text = Intent?.GetStringExtra("novora.lan.code") ?? "";
            Intent?.RemoveExtra("novora.lan.code");
            NLAndroidServiceLanCodeBootstrap.Put(text);
            StartActivity(new Intent(this, typeof(NLAndroidUIActivity))
                .AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop));
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("NOVORA-LAN", "CODE_HANDOFF_REJECTED " + ex.GetType().Name);
        }
        finally { Finish(); }
    }
}
