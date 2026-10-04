using NOVORA.Control;

namespace NOVORA.LinkClient;

public sealed record NovoraActionState(string Label, string Action, bool Enabled);

public sealed record NovoraViewState(
    string Connection,
    string PcName,
    string Transport,
    string LinkStatus,
    string VideoStatus,
    string ExInStatus,
    string VideoDetails,
    NovoraActionState VideoButton,
    NovoraActionState LinkButton,
    bool CaptureEnabled,
    NovoraActionState RecordButton,
    bool FileSharing,
    string MediaStatus,
    string ControllerDetails,
    string LeftStick,
    string RightStick,
    string LeftTrigger,
    string RightTrigger,
    bool VideoTransportSelectable)
{
    public static NovoraViewState From(NLControlSessionState session)
    {
        bool connected = session.Phase == NLControlSessionPhase.Connected && session.Snapshot is not null;
        NLControlSnapshot? snapshot = connected ? session.Snapshot : null;
        NLControlEngines? engines = snapshot?.Engines;
        NLControlMedia? media = snapshot?.Media;
        NLControlExIn? exIn = snapshot?.ExIn;
        bool available = connected && !session.Busy;
        bool transportStable = engines?.VideoPhase is null or "" or "Disconnected" or "Ready" or "Error";

        string connection = session.Phase switch
        {
            NLControlSessionPhase.Connected => session.Busy ? "Procesando orden…" : "Conectado",
            NLControlSessionPhase.Connecting => "Conectando…",
            NLControlSessionPhase.Lost => "Conexión perdida",
            _ => "Sin conexión"
        };
        string videoAction = engines?.VideoCanStop == true ? "stopVideo" : "startVideo";
        string linkAction = engines?.LinkCanStop == true ? "stopLink" : "startLink";
        bool recording = media?.Recording == true || media?.Starting == true;

        return new(
            connection,
            snapshot?.PcName ?? "Sin PC enlazada",
            connected ? session.Transport == "LAN" ? "LAN segura" : session.Transport : "Sin transporte",
            engines is null ? "No disponible" : $"{engines.LinkState} · {engines.LinkMessage}",
            engines is null ? "No disponible" : string.IsNullOrWhiteSpace(engines.VideoPhase)
                ? $"{engines.VideoState} · {engines.VideoMessage}"
                : $"{DescribeVePhase(engines.VideoPhase, engines.VideoDegraded)} · {engines.VideoMessage}",
            exIn?.Message ?? engines?.ExInMessage ?? "No detectado",
            snapshot is null ? "Sin datos de VisionEngine" :
                $"{(string.IsNullOrWhiteSpace(engines?.VideoTransport) ? "Sin transporte VE" : engines.VideoTransport)} · " +
                $"{snapshot.Profile} · {snapshot.Bitrate} · {snapshot.VideoSettings?.Resolution ?? "Resolución desconocida"} · {snapshot.VideoSettings?.Fps ?? "?"} FPS",
            new(videoAction == "stopVideo" ? "Detener VisionEngine" : "Iniciar VisionEngine", videoAction,
                available && (videoAction == "stopVideo" ? engines?.VideoCanStop == true : engines?.VideoCanStart == true)),
            new(linkAction == "stopLink" ? "Detener LinkEngine" : "Iniciar LinkEngine", linkAction,
                available && (linkAction == "stopLink" ? engines?.LinkCanStop == true : engines?.LinkCanStart == true)),
            available && media?.CanCapture == true,
            new(recording ? "Detener grabación" : "Iniciar grabación", recording ? "stopRecording" : "startRecording",
                available && (recording ? media?.Recording == true : media?.CanRecord == true)),
            snapshot?.FileSharing == true,
            media?.Message ?? "Multimedia no disponible",
            exIn is null ? "Sin control físico detectado" :
                $"{exIn.DeviceName}\n{exIn.VidPid} · {exIn.ConnectionType} · Batería {(exIn.BatteryPercent >= 0 ? $"{exIn.BatteryPercent}%" : exIn.BatteryState)}\nModo {exIn.Mode} · {exIn.Health}",
            exIn is null ? "Stick L (L3)\nSin datos" : $"Stick L (L3)\nX:{exIn.LeftX} Y:{exIn.LeftY}",
            exIn is null ? "Stick R (R3)\nSin datos" : $"Stick R (R3)\nX:{exIn.RightX} Y:{exIn.RightY}",
            exIn is null ? "LT / L2    —" : $"LT / L2    {exIn.LeftTrigger}",
            exIn is null ? "RT / R2    —" : $"RT / R2    {exIn.RightTrigger}",
            available && transportStable && snapshot?.VideoRunning != true &&
                engines?.VideoCanStop != true);
    }

    public static string DescribeVePhase(string? phase, bool degraded = false) =>
        degraded || string.Equals(phase, "Degraded", StringComparison.OrdinalIgnoreCase)
            ? "Transmitiendo sin audio"
            : phase switch
            {
                "Disconnected" => "Sin conexión",
                "Ready" => "Listo",
                "AwaitingPermission" => "Esperando permiso",
                "Preparing" => "Preparando",
                "Connecting" => "Conectando",
                "Streaming" => "Transmitiendo",
                "Error" => "Error",
                "Stopping" => "Deteniendo",
                _ => "Estado no disponible"
            };
}
