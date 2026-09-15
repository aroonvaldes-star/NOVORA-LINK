namespace NOVORA.Control;

public static class NLControlCommands
{
    public static string? Validate(NLControlRequest request, NLControlSnapshot state)
    {
        if (request.Version != NLControlProtocol.Version || request.Id <= 0)
            return "Versión o solicitud no válida.";
        if (request.Action is "pair" or "get") return null;
        if (request.Action is "file.begin" or "file.chunk" or "file.end" or "file.cancel")
            return state.FileSharing ? null : "Esta PC no admite transferencia de archivos.";
        if (request.Action is not ("bitrate" or "profile" or "audio" or "resolution" or "fps" or "capture" or "startRecording" or "stopRecording" or "restartVideo" or "startVideo" or "stopVideo" or "startLink" or "stopLink"))
            return "Acción no disponible en este bloque.";
        if (request.Revision != state.Revision)
            return "Los ajustes cambiaron en PC. Revisa el estado actualizado y vuelve a aplicar.";
        if (request.Action is "capture" or "startRecording" or "stopRecording")
        {
            if (request.Value is not null) return "La orden no acepta valores adicionales.";
            bool available = request.Action switch
            {
                "capture" => state.Media?.CanCapture == true,
                "startRecording" => state.Media?.CanRecord == true && !state.Media.Recording,
                _ => state.Media?.Recording == true
            };
            return available ? null : "Captura o grabación no disponible en este estado.";
        }
        if (request.Action is "startVideo" or "stopVideo" or "startLink" or "stopLink")
        {
            if (request.Value is not null) return "La acción de motor no acepta valores adicionales.";
            var engines = state.Engines;
            if (engines is null) return "Esta PC no anuncia control de motores. Actualiza al bloque 5.";
            bool ready = request.Action switch
            {
                "startVideo" => engines.VideoCanStart,
                "stopVideo" => engines.VideoCanStop,
                "startLink" => engines.LinkCanStart,
                "stopLink" => engines.LinkCanStop,
                _ => false
            };
            return ready ? null : "El motor no está disponible para esa acción. Revisa su estado actual.";
        }
        NLControlOption[]? options = request.Action switch
        {
            "bitrate" => state.Bitrates,
            "profile" => state.Profiles,
            "audio" => state.AudioOutputs,
            "resolution" => state.VideoSettings?.Resolutions ?? [],
            "fps" => state.VideoSettings?.FrameRates ?? [],
            _ => null
        };
        if (options is not null && !options.Any(o => o.Value == request.Value))
            return "Valor no admitido por esta PC.";
        if (request.Action == "restartVideo" && !state.VideoRunning)
            return "El video está detenido. Inícialo en PC antes de reiniciarlo.";
        return null;
    }
}
