using System;
using Android.App;
using Android.Content;
using Android.OS;

namespace NOVORA.LinkEngine.Android.LinkEngine;

public static class NotificationNetworkLE
{
    public const string ChannelIdLE =
        "novora_linkengine_network";

    public const int NotificationIdLE =
        27184;

    public static Notification CreateLE(
        Context context)
    {
        ArgumentNullException.ThrowIfNull(
            context);

        var manager =
            context.GetSystemService(
                Context.NotificationService)
            as NotificationManager
            ??
            throw new InvalidOperationException(
                "NotificationManager no disponible.");

        if (Build.VERSION.SdkInt >=
            BuildVersionCodes.O)
        {
            var channel =
                new NotificationChannel(
                    ChannelIdLE,
                    "NOVORA LinkEngine",
                    NotificationImportance.Low)
                {
                    Description =
                        "NOVORA LinkEngine USB network dataplane"
                };

            manager.CreateNotificationChannel(
                channel);
        }

        return new Notification.Builder(
                context,
                ChannelIdLE)
            .SetContentTitle(
                "NOVORA LinkEngine")
            .SetContentText(
                "LinkEngine USB Internet activo")
            .SetSmallIcon(
                Resource.Mipmap.novora_linkengine)
            .SetOngoing(
                true)
            .Build();
    }
}