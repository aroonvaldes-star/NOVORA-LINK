using Android.App;
using Android.Content;
using NOVORA.LinkEngine.Android.Discovery;

namespace NOVORA.LinkEngine.Android.Remote;

// DUMP restringe la entrega a herramientas de sistema, incluido adb shell.
[BroadcastReceiver(Name = "com.novora.linkengine.SessionReceiverRemoteNV",
    Enabled = true, Exported = true, Permission = "android.permission.DUMP")]
public sealed class SessionReceiverRemoteNV : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != "com.novora.linkengine.SESSION_READY") return;
        string? token = intent.GetStringExtra("novora_session_token");
        intent.RemoveExtra("novora_session_token");
        if (!SessionRemoteNV.SetTokenNV(token)) return;
        DiscoverySignalReceiverNV.StartDiscoveryServiceNV(context,
            DiscoveryServiceNV.ActionReconnectNV);
        ResultCode = Result.Ok;
    }
}
