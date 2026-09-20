using Android.App;
using Android.Content;
using Android.Service.Notification;

namespace NOVORA.AndroidUI;

// Android owns binding and permission. Notification contents are not read or stored.
[Service(Name = "com.novora.appcontrol.MediaAccessService", Label = "NOVORA · controles multimedia",
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE", Exported = true)]
[IntentFilter(new[] { "android.service.notification.NotificationListenerService" })]
public sealed class NLAndroidUIMediaAccess : NotificationListenerService
{
    internal static event EventHandler? AccessChanged;
    public override void OnListenerConnected() { base.OnListenerConnected(); AccessChanged?.Invoke(null, EventArgs.Empty); }
    public override void OnListenerDisconnected() { AccessChanged?.Invoke(null, EventArgs.Empty); base.OnListenerDisconnected(); }
    internal static ComponentName Component(Context context) => new(context, Java.Lang.Class.FromType(typeof(NLAndroidUIMediaAccess)));
    internal static bool Enabled(Context context)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(27)) {
            var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
            return manager?.IsNotificationListenerAccessGranted(Component(context)) == true;
        }
        string? enabled = Android.Provider.Settings.Secure.GetString(context.ContentResolver, "enabled_notification_listeners");
        return enabled?.Split(':').Any(value => Component(context).Equals(ComponentName.UnflattenFromString(value))) == true;
    }
}
