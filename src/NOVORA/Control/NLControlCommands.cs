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
        if (request.Action is not ("applyVideoSettings" or "bitrate" or "profile" or "audio" or "resolution" or "fps" or "capture" or "startRecording" or "stopRecording" or "restartVideo" or "startVideo" or "stopVideo" or "startLink" or "stopLink" or "exin.calibration.start" or "exin.calibration.finish" or "exin.calibration.reset" or "exin.mode" or "exin.reactivate"))
            return "Acción no disponible en este bloque.";
        if (request.Revision != state.Revision)
            return "Los ajustes cambiaron en PC. Revisa el estado actualizado y vuelve a aplicar.";
        if (request.Action == "applyVideoSettings")
        {
            if (state.VideoSettings is not { CanApplyTogether: true } video) return "Actualiza NOVORA PC para aplicar los ajustes juntos.";
            if (state.Media is { Recording: true } or { Starting: true }) return "Termina la grabación antes de aplicar y reiniciar VE.";
            if (state.Engines is not { } engines || !(state.VideoRunning ? engines.VideoCanStop : engines.VideoCanStart))
                return "VisionEngine no está disponible para aplicar los ajustes.";
            NLControlVideoChanges? changes;
            try { changes = System.Text.Json.JsonSerializer.Deserialize<NLControlVideoChanges>(request.Value ?? "null"); }
            catch (System.Text.Json.JsonException) { return "Los ajustes recibidos no son válidos."; }
            if (changes is null) return "Faltan los ajustes de VisionEngine.";
            bool Has(NLControlOption[]? options, string value) => options?.Any(x => x.Value == value) == true;
            return Has(state.Profiles, changes.Profile) && Has(state.Bitrates, changes.Bitrate) &&
                Has(video.Resolutions, changes.Resolution) && Has(video.FrameRates, changes.Fps) &&
                Has(state.AudioOutputs, changes.Audio) && Has(video.Monitors, changes.Monitor)
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
        if (request.Action.StartsWith("exin.calibration.", StringComparison.Ordinal))
        {
            if (request.Value is not null) return "La calibración no acepta valores adicionales.";
            if (state.ExIn?.Detected != true) return "ExInEngine necesita un control físico detectado.";
            if (state.ExIn.Transitioning || !state.ExIn.CanCalibrate) return "La calibración no está disponible en este estado.";
            if (request.Action == "exin.calibration.finish" && state.ExIn.Calibrating != true)
                return "No hay una calibración activa.";
            return null;
        }
        if (request.Action == "exin.mode")
        {
            if (state.ExIn?.Detected != true) return "ExInEngine necesita un control físico detectado.";
            if (state.ExIn.Transitioning || !state.ExIn.CanSetMode) return "ExInEngine está cambiando de estado.";
            return request.Value is "Game" or "Ui" ? null : "Modo de control no válido.";
        }
        if (request.Action == "exin.reactivate")
        {
            if (request.Value is not null) return "La reactivación no acepta valores adicionales.";
            if (state.ExIn?.Detected != true) return "No hay un control físico para reactivar.";
            if (state.ExIn.Transitioning || !state.ExIn.CanReactivate) return "ExInEngine está cambiando de estado.";
            return null;
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
