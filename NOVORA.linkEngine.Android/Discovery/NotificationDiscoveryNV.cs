using Android.App;
using Android.Content;
using Android.OS;

namespace NOVORA.LinkEngine.Android.Discovery;

public static class NotificationDiscoveryNV
{
    public const string ChannelIdNV =
        "novora_discovery";

    public const int NotificationIdNV =
        27182;

    public static void EnsureChannelNV(
        Context context)
    {
        ArgumentNullException.ThrowIfNull(
            context);

        if (Build.VERSION.SdkInt <
            BuildVersionCodes.O)
        {
            return;
        }

        var managerNV =
            context.GetSystemService(
                Context.NotificationService)
            as NotificationManager
            ??
            throw new InvalidOperationException(
                "NotificationManager no disponible.");

        var channelNV =
            new NotificationChannel(
                ChannelIdNV,
                "NOVORA-LINK",
                NotificationImportance.Low)
            {
                Description =
                    "Estado discreto de conexión NOVORA-LINK"
            };

        channelNV.EnableVibration(
            false);

        channelNV.SetSound(
            null,
            null);

        channelNV.SetShowBadge(
            false);

        managerNV.CreateNotificationChannel(
            channelNV);
    }

    public static Notification CreateNV(
        Context context,
        string messageNV,
        bool ongoingNV = true)
    {
        ArgumentNullException.ThrowIfNull(
            context);

        EnsureChannelNV(
            context);

        var openIntentNV =
            new Intent(
                context,
                typeof(MainActivity));

        openIntentNV.SetFlags(
            ActivityFlags.SingleTop |
            ActivityFlags.ClearTop);

        var pendingIntentNV =
            PendingIntent.GetActivity(
                context,
                0,
                openIntentNV,
                PendingIntentFlags.UpdateCurrent |
                PendingIntentFlags.Immutable);

        return new Notification.Builder(
                context,
                ChannelIdNV)
            .SetContentTitle(
                "NOVORA-LINK")
            .SetContentText(
                messageNV)
            .SetSmallIcon(
                Resource.Mipmap.appicon)
            .SetContentIntent(
                pendingIntentNV)
            .SetOnlyAlertOnce(
                true)
            .SetOngoing(
                ongoingNV)
            .Build();
    }

    public static void ShowNV(
        Context context,
        string messageNV,
        bool ongoingNV = true)
    {
        var managerNV =
            context.GetSystemService(
                Context.NotificationService)
            as NotificationManager;

        managerNV?.Notify(
            NotificationIdNV,
            CreateNV(
                context,
                messageNV,
                ongoingNV));
    }
}
