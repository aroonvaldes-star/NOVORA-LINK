namespace NOVORA.AndroidService;

public sealed record NLAndroidNotificationState(string Title, string Message, string PublicMessage,
    bool Ongoing, bool CanStopVideo, long Generation);

/// <summary>One pending state callback and one retained notification, independent of telemetry.</summary>
public sealed class NLAndroidNotificationPolicy
{
    private int _queued;
    private NLAndroidNotificationState? _published;

    public bool TryQueue() => Interlocked.CompareExchange(ref _queued, 1, 0) == 0;
    public void BeginDrain() => Volatile.Write(ref _queued, 0);
    public bool NeedsPublish(NLAndroidNotificationState state) => state != _published;
    public void MarkPublished(NLAndroidNotificationState state) => _published = state;
    public void Reset() => _published = null;

    public static string PublicSummary(string transport, bool connected, bool lost,
        bool videoRunning, bool linkRunning, bool recording, bool preparingRecording)
    {
        if (lost) return "Enlace con la PC perdido. Abre LINK Control para reconectar.";
        if (!connected) return "Preparando el enlace con la PC.";
        string route = transport == "USB" ? "USB" : "Wi-Fi";
        string video = preparingRecording ? "Preparando grabación" : recording ? "Grabación activa" :
            videoRunning ? "VisionEngine comparte la pantalla" : "VisionEngine detenido";
        return $"Conectado por {route} · {video} · LinkEngine {(linkRunning ? "activo" : "detenido")}.";
    }
}
