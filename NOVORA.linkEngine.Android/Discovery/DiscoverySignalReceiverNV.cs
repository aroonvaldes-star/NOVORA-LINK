using Android.App;
using Android.Content;
using Android.OS;

namespace NOVORA.LinkEngine.Android.Discovery;

[BroadcastReceiver(
    Name = "com.novora.linkengine.DiscoverySignalReceiverNV",
    Enabled = true,
    Exported = true)]
[IntentFilter(
    new[]
    {
        Intent.ActionBootCompleted,
        Intent.ActionMyPackageReplaced,
        "com.novora.linkengine.DISCOVERY_READY"
    })]
public sealed class DiscoverySignalReceiverNV :
    BroadcastReceiver
{
    public const string ActionDiscoveryReadyNV =
        "com.novora.linkengine.DISCOVERY_READY";

    public override void OnReceive(
        Context? context,
        Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        string? actionNV =
            intent?.Action;

        bool reconnectNV =
            string.Equals(
                actionNV,
                ActionDiscoveryReadyNV,
                StringComparison.Ordinal);

        StartDiscoveryServiceNV(
            context,
            reconnectNV
                ? DiscoveryServiceNV.ActionReconnectNV
                : DiscoveryServiceNV.ActionStartNV);
    }

    public static void StartDiscoveryServiceNV(
        Context context,
        string actionNV)
    {
        ArgumentNullException.ThrowIfNull(
            context);

        var serviceIntentNV =
            new Intent(
                context,
                typeof(DiscoveryServiceNV));

        serviceIntentNV.SetAction(
            actionNV);

        if (Build.VERSION.SdkInt >=
            BuildVersionCodes.O)
        {
            context.StartForegroundService(
                serviceIntentNV);
        }
        else
        {
            context.StartService(
                serviceIntentNV);
        }
    }
}
