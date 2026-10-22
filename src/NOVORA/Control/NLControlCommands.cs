namespace NOVORA.Control;

public static class NLControlCommands
{
    public static string? Validate(NLControlRequest request, NLControlSnapshot state)
    {
        if (request.Version != NLControlProtocol.Version || request.Id <= 0)
            return "Versión o solicitud no válida.";
        if (request.Action is "pair" or "get") return null;
        if (request.Action == "update.chunk")
            return state.AppUpdate is not null && request.Value is { Length: > 0 and <= 48 }
                ? null : "No hay una actualización verificada disponible.";
        if (request.Action is "network.capabilities" or "network.policy" or "files.acquire" or "files.cancelLease")
            return state.Capabilities?.Version == 1 && request.Value is { Length: <= 8192 }
                ? null : "La función experimental no está disponible.";
        if (request.Action is "file.begin" or "file.chunk" or "file.end" or "file.cancel")
            return state.FileSharing ? null : "Esta PC no admite transferencia de archivos.";
        if (request.Action is not ("applyVideoSettings" or "bitrate" or "profile" or "audio" or "resolution" or "fps" or "capture" or "startRecording" or "stopRecording" or "restartVideo" or "startAppVideo" or "startVideoLan" or "stopVideo" or "startLink" or "stopLink" or "link.speedtest" or "exin.start" or "exin.mode" or "exin.synchronize"))
            return "Acción no disponible en este bloque.";
        // Capture permission belongs to the authorized screen/session. Status
        // updates while Android displays consent must not invalidate that grant.
        // Readiness and capture options are still checked against current state.
        if (request.Revision != state.Revision && request.Action is not ("startAppVideo" or "startVideoLan"))
            return "Los ajustes cambiaron en PC. Revisa el estado actualizado y vuelve a aplicar.";
        if (request.Action == "link.speedtest")
            return request.Value is null && state.Engines?.LinkRunning == true
                ? null : "Inicia LinkEngine antes de medir el túnel.";
        if (request.Action == "applyVideoSettings")
        {
            if (state.VideoSettings is not { CanApplyTogether: true } video) return "Actualiza NOVORA PC para aplicar los ajustes juntos.";
            if (state.Media is { Recording: true } or { Starting: true }) return "Termina la grabación antes de guardar los ajustes de VE.";
            NLControlVideoChanges? changes;
            try { changes = System.Text.Json.JsonSerializer.Deserialize<NLControlVideoChanges>(request.Value ?? "null"); }
            catch (System.Text.Json.JsonException) { return "Los ajustes recibidos no son válidos."; }
            if (changes is null) return "Faltan los ajustes de VisionEngine.";
            bool Has(NLControlOption[]? options, string value) => options?.Any(x => x.Value == value) == true;
            return Has(state.Profiles, changes.Profile) && Has(state.Bitrates, changes.Bitrate) &&
                Has(video.Resolutions, changes.Resolution) && Has(video.FrameRates, changes.Fps) &&
                Has(state.AudioOutputs, changes.Audio) && Has(video.Monitors, changes.Monitor) &&
                Has(video.PresentationModes, changes.PresentationMode)
                ? null : "Un ajuste o monitor ya no está disponible. Revisa los valores.";
        }
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
        if (request.Action == "exin.mode")
        {
            if (state.ExIn?.Detected != true) return "ExInEngine necesita un control físico detectado.";
            if (state.ExIn.Transitioning || !state.ExIn.CanSetMode) return "ExInEngine está cambiando de estado.";
            return request.Value is "Game" or "Ui" ? null : "Modo de control no válido.";
        }
        if (request.Action == "exin.start")
            return request.Value is null && state.Engines?.ExInCanStart == true
                ? null : "ExInEngine no está disponible para iniciar en este estado.";
        if (request.Action == "exin.synchronize")
        {
            if (request.Value is not null) return "La sincronización no acepta valores adicionales.";
            if (state.ExIn?.Detected != true) return "No hay un control físico para sincronizar.";
            if (state.ExIn.Transitioning) return "ExInEngine está cambiando de estado.";
            return null;
        }
        if (request.Action is "startAppVideo" or "startVideoLan" or "stopVideo" or "startLink" or "stopLink")
        {
            // startLink acepta la ruta explícita del bloque de AppControl ("USB" o "LAN").
            bool routeValue = request.Action is "startLink" && request.Value is "USB" or "LAN";
            if (request.Value is not null && !routeValue &&
                (request.Action is not ("startAppVideo" or "startVideoLan") ||
                 request.Value != NLControlProtocol.VideoOnlyCapture))
                return "La acción de motor no acepta valores adicionales.";
            var engines = state.Engines;
            if (engines is null) return "Esta PC no anuncia control de motores. Actualiza al bloque 5.";
            bool ready = request.Action switch
            {
                "startAppVideo" => engines.VideoCanStart,
                "startVideoLan" => engines.VideoCanStart,
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
