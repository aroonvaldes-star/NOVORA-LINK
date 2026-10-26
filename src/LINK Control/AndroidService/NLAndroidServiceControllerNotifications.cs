using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using NOVORA.Control;
using Resource = NOVORA.AndroidApp.Resource;

namespace NOVORA.AndroidService;

public sealed class NLAndroidServiceControllerNotifications
{
    private const string Channel = "novora_controller_battery";
    private const int NotificationId = 314;
    private readonly Service _service;
    private readonly NotificationManager _manager;
    private long _generation = -1;
    private long _sequence;

    public NLAndroidServiceControllerNotifications(Service service)
    {
        _service = service;
        _manager = (NotificationManager)service.GetSystemService(Context.NotificationService)!;
        _manager.CreateNotificationChannel(new NotificationChannel(Channel, "Batería del control", NotificationImportance.Default)
        {
            Description = "Avisos de batería baja, crítica y carga completa del control activo",
            LockscreenVisibility = NotificationVisibility.Private
        });
    }

    public void Update(long generation, NLControlExIn? controller)
    {
        if (generation != _generation)
        {
            _generation = generation;
            _sequence = controller?.BatteryAlertSequence ?? 0;
            return;
        }
        if (controller is null || controller.BatteryAlertSequence <= _sequence) return;
        _sequence = controller.BatteryAlertSequence;
        if (string.IsNullOrWhiteSpace(controller.BatteryAlert) || !NotificationsAllowed()) return;

        string controllerName = SafeName(controller.DeviceName);
        string message = controller.BatteryAlert switch
        {
            "Low15" => $"La batería de {controllerName} está baja ({Percent(controller)}).",
            "Critical5" => $"La batería de {controllerName} está por agotarse ({Percent(controller)}). Conéctalo ahora.",
            "Full100" => $"{controllerName} llegó al 100% de carga.",
            _ => ""
        };
        if (message.Length == 0) return;
        string title = controller.BatteryAlert switch
        {
            "Low15" => "Batería del mando baja",
            "Critical5" => "Batería del mando crítica",
            _ => "Carga del mando completa"
        };
        var publicVersion = new Notification.Builder(_service, Channel)
            .SetSmallIcon(Resource.Drawable.novora_notification)
            .SetContentTitle(title).SetContentText($"Batería del mando: {Percent(controller)}")
            .SetVisibility(NotificationVisibility.Public).Build();

        var open = PendingIntent.GetActivity(_service, NotificationId,
            new Intent(_service, typeof(AndroidUI.NLAndroidUIActivity)).AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        var notification = new Notification.Builder(_service, Channel)
            .SetSmallIcon(Resource.Drawable.novora_notification)
            .SetContentTitle(title)
            .SetContentText(message)
            .SetStyle(new Notification.BigTextStyle().BigText(message))
            .SetContentIntent(open)
            .SetAutoCancel(true)
            .SetVisibility(NotificationVisibility.Private)
            .SetPublicVersion(publicVersion)
            .Build();
        _manager.Notify(NotificationId, notification);
    }

    private bool NotificationsAllowed() => !OperatingSystem.IsAndroidVersionAtLeast(33) ||
        _service.CheckSelfPermission(Android.Manifest.Permission.PostNotifications) == Permission.Granted;

    private static string Percent(NLControlExIn controller)
        => controller.BatteryPercent >= 0 ? controller.BatteryPercent + "%" : "nivel crítico";

    private static string SafeName(string value)
    {
        string safe = new(value.Take(48).Where(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' or '.').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "el control" : safe.Trim();
    }
}
