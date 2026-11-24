using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NOVORA.Streaming;

public enum NLStreamMode
{
    Rtmp,
    ObsWebSocket
}

public enum NLStreamMonitorState
{
    Idle,
    Active,
    Reconnecting,
    Error,
    NoSignal
}

/// <summary>
/// Muestra mínima del monitor. Un valor null significa "la fuente no lo reporta",
/// nunca cero. La UI debe marshalear el evento a su dispatcher.
/// </summary>
public sealed record NLStreamMonitorSample(
    DateTimeOffset UpdatedAtUtc,
    NLStreamMode Mode,
    NLStreamMonitorState State,
    double? Fps,
    double? BitrateKbps,
    TimeSpan? Duration,
    double? SpeedX,
    double? Congestion,
    long? SkippedFrames,
    string Detail)
{
    public string ModeLabel => Mode == NLStreamMode.ObsWebSocket ? "OBS WebSocket" : "RTMP / ffmpeg";
    public string StateLabel => State switch
    {
        NLStreamMonitorState.Active => "Activo",
        NLStreamMonitorState.Reconnecting => "Reconectando",
        NLStreamMonitorState.Error => "Error reportado por la fuente",
        NLStreamMonitorState.NoSignal => "Sin señal del codificador",
        _ => "Detenido"
    };
    public bool IsActive => State is NLStreamMonitorState.Active or NLStreamMonitorState.Reconnecting;

    public string FpsLabel => Fps is { } fps
        ? fps.ToString("0.0", CultureInfo.InvariantCulture)
        : "—";
    public string BitrateLabel => BitrateKbps is { } kbps && kbps > 0
        ? kbps >= 1000
            ? (kbps / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " Mbps"
            : kbps.ToString("0", CultureInfo.InvariantCulture) + " kbps"
        : "—";
    public string DurationLabel => Duration is { } duration
        ? duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
        : "—";
    public string HealthCaption => Mode == NLStreamMode.Rtmp ? "VELOCIDAD ffmpeg" : "CONGESTIÓN OBS";
    public string HealthLabel => Mode == NLStreamMode.Rtmp
        ? SpeedX is { } speed ? speed.ToString("0.00", CultureInfo.InvariantCulture) + "x" : "—"
        : Congestion is { } congestion ? congestion.ToString("0.##", CultureInfo.InvariantCulture) : "—";
}

/// <summary>
/// Ajustes de transmisión. Los valores avanzados son opcionales: con los valores
/// por defecto el comportamiento sigue siendo el mismo que en la versión anterior.
/// Las credenciales nunca se incluyen en los eventos de estado o monitor.
/// </summary>
public sealed class NLStreamSettings
{
    public NLStreamMode Mode { get; set; } = NLStreamMode.Rtmp;
    public string RtmpUrl { get; set; } = "rtmp://";
    public string RtmpKey { get; set; } = "";
    public int BitrateKbps { get; set; } = 4500;
    public int Fps { get; set; } = 30;

    public string ObsHost { get; set; } = "127.0.0.1";
    public int ObsPort { get; set; } = 4455;
    public string ObsPassword { get; set; } = "";
    /// <summary>Escena de OBS a activar antes de StartStream. Vacío = no cambiar escena.</summary>
    public string ObsScene { get; set; } = "";

    /// <summary>auto, libx264, h264_nvenc, h264_qsv o h264_amf.</summary>
    public string Encoder { get; set; } = "auto";
    public string Preset { get; set; } = "veryfast";
    public string Tune { get; set; } = "zerolatency";
    public int KeyframeSeconds { get; set; } = 2;
    public bool AudioEnabled { get; set; }
    public int AudioBitrateKbps { get; set; } = 160;
    /// <summary>Nombre del dispositivo dshow en Windows o PulseAudio en Linux. Vacío = default en Linux.</summary>
    public string AudioDevice { get; set; } = "";
    public string VideoFilter { get; set; } = "";
    public string PixelFormat { get; set; } = "yuv420p";
    public bool LowLatency { get; set; } = true;

    public static IReadOnlyList<string> Encoders => ["auto", "libx264", "h264_nvenc", "h264_qsv", "h264_amf"];
    public static IReadOnlyList<string> Presets =>
        ["ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"];
}

/// <summary>
/// Backend compartido por Windows (WPF) y Linux (Avalonia). RTMP usa ffmpeg;
/// OBS usa WebSocket v5. El monitor se alimenta de stderr de ffmpeg o de
/// GetStreamStatus de OBS, según el modo.
/// </summary>
public sealed class NLStreamingService : IAsyncDisposable
{
    private static readonly Regex FfmpegProgress = new(
        @"frame=\s*(?<frame>\d+)\s+fps=\s*(?<fps>[\d.]+).*?(?:q=\s*(?<q>[\d.]+))?.*?(?:time=\s*(?<time>\d+:\d+:\d+(?:\.\d+)?))?.*?bitrate=\s*(?<bitrate>[\d.]+)\s*kbits/s.*?speed=\s*(?<speed>[\d.]+)x",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Sin señal del codificador durante este tiempo se marca NoSignal, no 0.</summary>
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(4);
    private static readonly Regex FfmpegFailure = new(
        @"(?i)\b(error|failed|failure|refused|unauthorized|forbidden|not found|broken pipe|connection reset|could not|invalid)\b|\b(40[34]|50[023])\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SemaphoreSlim _obsGate = new(1, 1);
    private Process? _ffmpeg;
    private ClientWebSocket? _obs;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private NLStreamMonitorSample? _lastSample;
    private volatile bool _isLive;
    private bool _stoppingFfmpeg;
    private NLStreamMode _monitorMode = NLStreamMode.Rtmp;
    private DateTimeOffset? _lastSignalUtc;
    private long? _obsLastBytes;
    private DateTimeOffset? _obsLastBytesAt;
    private double? _obsBitrateKbps;

    public event Action<string>? StatusChanged;
    public event Action<NLStreamMonitorSample>? MonitorChanged;

    public bool IsLive => _isLive;
    public NLStreamMonitorSample? LastSample => _lastSample;

    public async Task StartAsync(NLStreamSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_isLive) throw new InvalidOperationException("La transmisión ya está activa.");

        NLStreamSettings normalized = Normalize(settings);
        if (normalized.Mode == NLStreamMode.Rtmp)
        {
            StartFfmpeg(normalized);
        }
        else
        {
            await ObsConnectAsync(normalized, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(normalized.ObsScene))
            {
                var scene = new JsonObject { ["sceneName"] = normalized.ObsScene.Trim() };
                JsonObject response = await ObsRequestAsync("SetCurrentScene", ct, scene).ConfigureAwait(false);
                EnsureOk(response, "OBS no pudo activar la escena solicitada");
            }
            JsonObject started = await ObsRequestAsync("StartStream", ct).ConfigureAwait(false);
            EnsureOk(started, "OBS no pudo iniciar la transmisión");
            _isLive = true;
            Report("OBS: transmisión iniciada.");
        }

        _lastSignalUtc = null;
        _obsLastBytes = null;
        _obsLastBytesAt = null;
        _obsBitrateKbps = null;
        StartMonitor(normalized.Mode);
        Publish(IdleSample(normalized.Mode,
            "Esperando la primera respuesta real del codificador u OBS."));
    }

    public async Task StopAsync(NLStreamSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await StopMonitorAsync().ConfigureAwait(false);

        if (settings.Mode == NLStreamMode.Rtmp)
        {
            await StopFfmpegAsync().ConfigureAwait(false);
        }
        else
        {
            if (_obs is null || _obs.State != WebSocketState.Open)
                await ObsConnectAsync(Normalize(settings), ct).ConfigureAwait(false);
            JsonObject response = await ObsRequestAsync("StopStream", ct).ConfigureAwait(false);
            EnsureOk(response, "OBS no pudo detener la transmisión");
            Report("OBS: transmisión detenida.");
        }

        _isLive = false;
        _lastSignalUtc = null;
        _obsLastBytes = null;
        _obsLastBytesAt = null;
        _obsBitrateKbps = null;
        Publish(IdleSample(settings.Mode, "Transmisión detenida."));
    }

    /// <summary>Fuerza una lectura inmediata del monitor sin reiniciar la transmisión.</summary>
    public async Task RefreshMonitorAsync(CancellationToken ct = default)
    {
        if (_monitorMode == NLStreamMode.ObsWebSocket && _isLive && _obs is { State: WebSocketState.Open })
        {
            NLStreamMonitorSample? sample = await ReadObsStatusAsync(ct).ConfigureAwait(false);
            if (sample is not null)
            {
                Publish(sample);
                return;
            }
        }

        if (_monitorMode == NLStreamMode.Rtmp && _isLive)
        {
            // Nunca republica una métrica antigua como si fuera fresca.
            if (IsFfmpegSignalStale)
            {
                Publish(NoSignalSample("ffmpeg sigue abierto, pero no reporta progreso."));
            }
            else if (_lastSample is not null)
            {
                Publish(_lastSample with { UpdatedAtUtc = DateTimeOffset.UtcNow });
            }
            return;
        }

        Publish(IdleSample(_monitorMode, "Transmisión detenida."));
    }

    private bool IsFfmpegSignalStale =>
        _lastSignalUtc is null || DateTimeOffset.UtcNow - _lastSignalUtc.Value > SignalTimeout;

    /// <summary>
    /// Línea de comandos para inspección. La clave se enmascara por defecto.
    /// El proceso real usa ArgumentList, no esta cadena.
    /// </summary>
    public static string BuildFfmpegArguments(NLStreamSettings settings, bool revealKey = false)
    {
        var args = BuildFfmpegArgumentList(settings);
        string target = args[^1];
        if (!revealKey)
        {
            int slash = target.LastIndexOf('/');
            if (slash >= 0) target = target[..(slash + 1)] + "***";
        }
        args[^1] = target;
        return string.Join(' ', args.Select(QuoteForDisplay));
    }

    private static IReadOnlyList<string> BuildFfmpegArgumentList(NLStreamSettings input)
    {
        NLStreamSettings s = Normalize(input);
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "info", "-stats"
        };

        if (OperatingSystem.IsWindows())
        {
            args.AddRange(["-f", "gdigrab", "-framerate", Fmt(s.Fps), "-draw_mouse", "1", "-i", "desktop"]);
        }
        else if (OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "La captura de pantalla RTMP con ffmpeg está implementada para Windows y Linux; en macOS usa OBS WebSocket.");
        }
        else
        {
            string display = Environment.GetEnvironmentVariable("DISPLAY");
            if (string.IsNullOrWhiteSpace(display)) display = ":0";
            if (!display.Contains('.')) display += ".0";
            args.AddRange(["-f", "x11grab", "-framerate", Fmt(s.Fps), "-i", display]);
        }

        if (s.AudioEnabled)
        {
            if (OperatingSystem.IsWindows())
            {
                if (string.IsNullOrWhiteSpace(s.AudioDevice))
                    throw new InvalidOperationException("Indica el dispositivo de audio dshow para incluir audio en RTMP.");
                string device = s.AudioDevice.StartsWith("audio=", StringComparison.OrdinalIgnoreCase)
                    ? s.AudioDevice
                    : "audio=" + s.AudioDevice;
                args.AddRange(["-f", "dshow", "-i", device]);
            }
            else
            {
                string device = string.IsNullOrWhiteSpace(s.AudioDevice) ? "default" : s.AudioDevice;
                args.AddRange(["-f", "pulse", "-i", device]);
            }
        }

        string encoder = s.Encoder;
        args.AddRange(["-c:v", encoder]);
        switch (encoder)
        {
            case "h264_nvenc":
                args.AddRange(["-preset", MapPreset(encoder, s.Preset), "-rc", "cbr"]);
                break;
            case "h264_qsv":
                args.AddRange(["-preset", MapPreset(encoder, s.Preset)]);
                break;
            case "h264_amf":
                args.AddRange(["-quality", MapPreset(encoder, s.Preset)]);
                break;
            default:
                args.AddRange(["-preset", MapPreset(encoder, s.Preset)]);
                if (s.LowLatency && !string.Equals(s.Tune, "none", StringComparison.OrdinalIgnoreCase))
                    args.AddRange(["-tune", s.Tune]);
                break;
        }

        if (!string.IsNullOrWhiteSpace(s.VideoFilter))
            args.AddRange(["-vf", s.VideoFilter.Trim()]);
        args.AddRange(["-pix_fmt", s.PixelFormat, "-g", Fmt(s.Fps * s.KeyframeSeconds)]);

        if (s.AudioEnabled)
            args.AddRange(["-c:a", "aac", "-b:a", Fmt(s.AudioBitrateKbps) + "k"]);

        args.AddRange(["-b:v", Fmt(s.BitrateKbps) + "k", "-maxrate", Fmt(s.BitrateKbps) + "k",
            "-bufsize", Fmt(s.BitrateKbps * 2) + "k", "-f", "flv"]);
        args.Add(s.RtmpUrl.TrimEnd('/') + "/" + s.RtmpKey);
        return args;
    }

    private static NLStreamSettings Normalize(NLStreamSettings input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var s = new NLStreamSettings
        {
            Mode = input.Mode,
            RtmpUrl = input.RtmpUrl?.Trim() ?? "",
            RtmpKey = input.RtmpKey?.Trim() ?? "",
            BitrateKbps = input.BitrateKbps,
            Fps = input.Fps,
            ObsHost = string.IsNullOrWhiteSpace(input.ObsHost) ? "127.0.0.1" : input.ObsHost.Trim(),
            ObsPort = input.ObsPort,
            ObsPassword = input.ObsPassword ?? "",
            ObsScene = input.ObsScene?.Trim() ?? "",
            Encoder = Pick(input.Encoder, new[]
            {
                ("auto", "auto"), ("libx264", "libx264"), ("x264", "libx264"),
                ("h264_nvenc", "h264_nvenc"), ("nvenc", "h264_nvenc"),
                ("h264_qsv", "h264_qsv"), ("qsv", "h264_qsv"),
                ("h264_amf", "h264_amf"), ("amf", "h264_amf")
            }, "auto"),
            Preset = input.Preset?.Trim().ToLowerInvariant() ?? "veryfast",
            Tune = Pick(input.Tune, new[]
            {
                ("zerolatency", "zerolatency"), ("film", "film"), ("animation", "animation"),
                ("grain", "grain"), ("stillimage", "stillimage"), ("fastdecode", "fastdecode"), ("none", "none")
            }, "zerolatency"),
            KeyframeSeconds = input.KeyframeSeconds,
            AudioEnabled = input.AudioEnabled,
            AudioBitrateKbps = input.AudioBitrateKbps,
            AudioDevice = input.AudioDevice?.Trim() ?? "",
            VideoFilter = input.VideoFilter?.Trim() ?? "",
            PixelFormat = Pick(input.PixelFormat, new[]
            {
                ("yuv420p", "yuv420p"), ("nv12", "nv12"), ("yuv422p", "yuv422p")
            }, "yuv420p"),
            LowLatency = input.LowLatency
        };

        if (s.Mode == NLStreamMode.Rtmp)
        {
            if (!Uri.TryCreate(s.RtmpUrl, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme is not ("rtmp" or "rtmps" or "rtmpte"))
                throw new InvalidOperationException("La URL RTMP debe usar rtmp://, rtmps:// o rtmpte://.");
            if (s.RtmpKey.Length == 0) throw new InvalidOperationException("Falta la clave de transmisión.");
        }
        if (s.ObsPort is < 1 or > 65535) throw new InvalidOperationException("El puerto de OBS no es válido.");
        if (s.BitrateKbps is < 100 or > 100_000) throw new InvalidOperationException("El bitrate debe estar entre 100 y 100000 kbps.");
        if (s.Fps is < 1 or > 120) throw new InvalidOperationException("Los FPS deben estar entre 1 y 120.");
        if (s.KeyframeSeconds is < 1 or > 10) throw new InvalidOperationException("El intervalo de keyframe debe estar entre 1 y 10 segundos.");
        if (s.AudioBitrateKbps is < 32 or > 320) throw new InvalidOperationException("El bitrate de audio debe estar entre 32 y 320 kbps.");
        if (s.VideoFilter.Length > 500) throw new InvalidOperationException("El filtro de video es demasiado largo.");
        return s;
    }

    private static string Pick(string? value, (string Alias, string Canonical)[] options, string fallback)
    {
        string key = value?.Trim().ToLowerInvariant() ?? "";
        foreach ((string alias, string canonical) in options)
            if (string.Equals(key, alias, StringComparison.Ordinal)) return canonical;
        return fallback;
    }

    private static string MapPreset(string encoder, string preset)
    {
        string value = preset.ToLowerInvariant();
        if (encoder == "h264_nvenc")
            return value switch
            {
                "ultrafast" or "superfast" or "veryfast" => "p1",
                "faster" or "fast" => "p2",
                "medium" => "p3",
                "slow" => "p4",
                "slower" or "veryslow" => "p5",
                _ => "p3"
            };
        if (encoder == "h264_qsv")
            return value is "ultrafast" or "superfast" or "veryfast" ? "veryfast" :
                value is "slow" or "slower" or "veryslow" ? "slow" : "medium";
        if (encoder == "h264_amf")
            return value is "ultrafast" or "superfast" or "veryfast" ? "speed" :
                value is "slow" or "slower" or "veryslow" ? "quality" : "balanced";
        return NLStreamSettings.Presets.Contains(value) ? value : "veryfast";
    }

    private void StartFfmpeg(NLStreamSettings s)
    {
        string executable = Environment.GetEnvironmentVariable("NOVORA_FFMPEG");
        if (string.IsNullOrWhiteSpace(executable)) executable = "ffmpeg";
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (string argument in BuildFfmpegArgumentList(s)) info.ArgumentList.Add(argument);

        try
        {
            _ffmpeg = Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar ffmpeg.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("ffmpeg no está instalado o no está en el PATH.");
        }

        _ffmpeg.EnableRaisingEvents = true;
        _ffmpeg.ErrorDataReceived += (_, e) => OnFfmpegLine(e.Data);
        _ffmpeg.OutputDataReceived += (_, _) => { };
        _ffmpeg.BeginErrorReadLine();
        _ffmpeg.BeginOutputReadLine();
        _ffmpeg.Exited += (_, _) =>
        {
            int code = _ffmpeg.ExitCode;
            if (_stoppingFfmpeg) return;
            _isLive = false;
            Report(code == 0
                ? "RTMP: ffmpeg terminó la transmisión."
                : $"RTMP: ffmpeg terminó con código {code}.");
            // Código de salida real; no se presenta como una métrica de red.
            Publish(new NLStreamMonitorSample(
                DateTimeOffset.UtcNow, NLStreamMode.Rtmp,
                code == 0 ? NLStreamMonitorState.Idle : NLStreamMonitorState.Error,
                null, null, null, null, null, null,
                $"ffmpeg terminó con código {code}."));
        };
        _isLive = true;
        Report("RTMP: transmisión iniciada.");
    }

    private async Task StopFfmpegAsync()
    {
        Process? process = _ffmpeg;
        _stoppingFfmpeg = true;
        try
        {
            if (process is null || process.HasExited) return;
            try
            {
                await process.StandardInput.WriteLineAsync("q").ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
                if (!process.WaitForExit(5000)) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // El proceso ya terminó entre la comprobación y la escritura.
            }
            Report("RTMP: transmisión detenida.");
        }
        finally
        {
            _stoppingFfmpeg = false;
            _isLive = false;
        }
    }

    private void OnFfmpegLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || _monitorMode != NLStreamMode.Rtmp) return;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string text = line.Trim();

        // Los errores de ffmpeg son una respuesta real del transporte: se muestran
        // tal cual, sin rellenar métricas que el proceso no entregó.
        if (FfmpegFailure.IsMatch(text))
        {
            _lastSignalUtc = now;
            Report("RTMP: " + text);
            Publish(new NLStreamMonitorSample(
                now, NLStreamMode.Rtmp, NLStreamMonitorState.Error,
                null, null, null, null, null, null, Clip(text)));
            return;
        }

        Match match = FfmpegProgress.Match(text);
        if (!match.Success) return;

        double? fps = ParseOptional(match.Groups["fps"].Value);
        double? bitrate = ParseOptional(match.Groups["bitrate"].Value);
        double? speed = ParseOptional(match.Groups["speed"].Value);
        double? quality = ParseOptional(match.Groups["q"].Value);
        TimeSpan? duration = ParseOptionalTimestamp(match.Groups["time"].Value);

        _lastSignalUtc = now;
        Publish(new NLStreamMonitorSample(
            now, NLStreamMode.Rtmp,
            _isLive ? NLStreamMonitorState.Active : NLStreamMonitorState.Idle,
            fps, bitrate, duration, speed, null, null,
            $"Muestra real de ffmpeg · q={quality?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—"}"));
    }

    private static string Clip(string value) => value.Length <= 180 ? value : value[..180] + "…";

    private async Task ObsConnectAsync(NLStreamSettings s, CancellationToken ct)
    {
        await _obsGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _obs?.Dispose();
            _obs = new ClientWebSocket();
            _obs.Options.AddSubProtocol("obswebsocket.json");
            await _obs.ConnectAsync(new Uri($"ws://{s.ObsHost}:{s.ObsPort}"), ct).ConfigureAwait(false);

            JsonObject hello = await ReceiveAsync(ct).ConfigureAwait(false);
            JsonObject helloData = (hello["d"] as JsonObject) ?? new JsonObject();
            var identify = new JsonObject { ["rpcVersion"] = 1 };
            if (helloData["authentication"] is JsonObject auth &&
                TryString(auth, "salt", out string? salt) && TryString(auth, "challenge", out string? challenge))
            {
                // OBS WebSocket v5: base64(sha256(base64(sha256(password + salt)) + challenge))
                string secret = Convert.ToBase64String(
                    SHA256.HashData(Encoding.UTF8.GetBytes(s.ObsPassword + salt)));
                identify["authentication"] = Convert.ToBase64String(
                    SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
            }
            await SendAsync(new JsonObject { ["op"] = 1, ["d"] = identify }, ct).ConfigureAwait(false);

            JsonObject identified = await ReceiveAsync(ct).ConfigureAwait(false);
            if (identified["op"]?.GetValue<int>() != 2)
                throw new InvalidOperationException("OBS rechazó la conexión. Revisa la contraseña de WebSocket.");
            Report("OBS: conectado.");
        }
        finally
        {
            _obsGate.Release();
        }
    }

    private async Task<JsonObject> ObsRequestAsync(
        string requestType, CancellationToken ct, JsonObject? requestData = null)
    {
        await _obsGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ObsRequestCoreAsync(requestType, ct, requestData).ConfigureAwait(false);
        }
        finally
        {
            _obsGate.Release();
        }
    }

    private async Task<JsonObject> ObsRequestCoreAsync(
        string requestType, CancellationToken ct, JsonObject? requestData)
    {
        if (_obs is null || _obs.State != WebSocketState.Open)
            throw new InvalidOperationException("OBS no está conectado.");
        string requestId = Guid.NewGuid().ToString("N");
        var payload = new JsonObject
        {
            ["op"] = 6,
            ["d"] = new JsonObject
            {
                ["requestType"] = requestType,
                ["requestId"] = requestId
            }
        };
        if (requestData is not null) payload["d"]!["requestData"] = requestData;
        await SendAsync(payload, ct).ConfigureAwait(false);

        while (true)
        {
            JsonObject message = await ReceiveAsync(ct).ConfigureAwait(false);
            // Op 7 = RequestResponse. Los eventos (op 5) se ignoran; el monitor usa polling.
            if (message["op"]?.GetValue<int>() == 7 &&
                message["d"]?["requestId"]?.GetValue<string>() == requestId)
                return (JsonObject)message["d"]!;
        }
    }

    private async Task<NLStreamMonitorSample?> ReadObsStatusAsync(CancellationToken ct)
    {
        if (_obs is null || _obs.State != WebSocketState.Open) return null;
        try
        {
            // Campos verificado en obs-websocket v5: GetStreamStatus y GetStats.
            JsonObject response = await ObsRequestAsync("GetStreamStatus", ct).ConfigureAwait(false);
            if (!IsOk(response)) return null;
            JsonObject data = response["responseData"] as JsonObject ?? new JsonObject();

            bool active = TryBool(data, "outputActive", out bool parsedActive) && parsedActive;
            bool reconnecting = TryBool(data, "outputReconnecting", out bool parsedReconnecting) && parsedReconnecting;
            long? skipped = TryLong(data, "outputSkippedFrames", out long parsedSkipped) ? parsedSkipped : null;
            long? bytes = TryLong(data, "outputBytes", out long parsedBytes) ? parsedBytes : null;
            double? congestion = TryDouble(data, "outputCongestion", out double parsedCongestion) ? parsedCongestion : null;
            TimeSpan? duration = TryDouble(data, "outputDuration", out double parsedDuration)
                ? TimeSpan.FromMilliseconds(Math.Max(0, parsedDuration))
                : null;
            TryString(data, "outputTimecode", out string? timecode);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            _lastSignalUtc = now; // la respuesta de OBS es una señal real.

            // OBS no entrega bits/s. El bitrate se mide del delta de bytes enviados;
            // si el contador se reinicia no se inventa un valor.
            if (bytes is { } total)
            {
                if (_obsLastBytes is { } previous && _obsLastBytesAt is { } previousAt && total >= previous)
                {
                    double seconds = (now - previousAt).TotalSeconds;
                    if (seconds >= 0.25)
                        _obsBitrateKbps = (total - previous) * 8.0 / seconds / 1000.0;
                }
                else
                {
                    _obsBitrateKbps = null;
                }
                if (_obsLastBytes != total || _obsLastBytesAt is null)
                {
                    _obsLastBytes = total;
                    _obsLastBytesAt = now;
                }
            }

            // activeFps es FPS de render de OBS, no FPS de red. Se identifica como tal.
            double? fps = null;
            try
            {
                JsonObject stats = await ObsRequestAsync("GetStats", ct).ConfigureAwait(false);
                if (IsOk(stats) && stats["responseData"] is JsonObject statsData &&
                    TryDouble(statsData, "activeFps", out double parsedFps))
                    fps = parsedFps;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // OBS no reportó FPS: el campo queda en "—", nunca en 0.
            }

            if (!active && _isLive)
            {
                _isLive = false;
                Report("OBS: la transmisión se detuvo desde OBS.");
            }

            NLStreamMonitorState state = !active
                ? NLStreamMonitorState.Idle
                : reconnecting
                    ? NLStreamMonitorState.Reconnecting
                    : NLStreamMonitorState.Active;

            return new NLStreamMonitorSample(
                now, NLStreamMode.ObsWebSocket, state, fps, _obsBitrateKbps, duration,
                null, congestion, skipped,
                $"OBS · timecode {timecode ?? "--:--:--"} · frames omitidos {skipped?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"} · bitrate medido de bytes enviados · FPS de render OBS");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void StartMonitor(NLStreamMode mode)
    {
        _monitorCts?.Cancel();
        _monitorCts = new CancellationTokenSource();
        CancellationToken token = _monitorCts.Token;
        _monitorMode = mode;
        _monitorTask = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (mode == NLStreamMode.ObsWebSocket)
                    {
                        NLStreamMonitorSample? sample = await ReadObsStatusAsync(token).ConfigureAwait(false);
                        if (sample is not null) Publish(sample);
                    }
                    else if (_isLive && IsFfmpegSignalStale && _lastSample?.State != NLStreamMonitorState.Error)
                    {
                        // ffmpeg abierto sin progreso no es "en vivo confirmado".
                        if (_lastSample?.State != NLStreamMonitorState.NoSignal)
                            Publish(NoSignalSample("ffmpeg sigue abierto, pero no reporta progreso."));
                    }
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Monitor detenido a propósito.
            }
            catch (Exception ex)
            {
                Report("Monitor de streaming: " + ex.Message);
            }
        }, token);
    }

    private async Task StopMonitorAsync()
    {
        _monitorCts?.Cancel();
        Task? task = _monitorTask;
        if (task is not null)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }
        _monitorCts?.Dispose();
        _monitorCts = null;
        _monitorTask = null;
    }

    private static NLStreamMonitorSample NoSignalSample(string detail) => new(
        DateTimeOffset.UtcNow, NLStreamMode.Rtmp, NLStreamMonitorState.NoSignal,
        null, null, null, null, null, null, detail);

    private static NLStreamMonitorSample IdleSample(NLStreamMode mode, string? detail = null) => new(
        DateTimeOffset.UtcNow, mode, NLStreamMonitorState.Idle,
        null, null, null, null, null, null,
        detail ?? "Sin datos. Inicia la transmisión para ver respuestas reales del codificador.");

    private void Publish(NLStreamMonitorSample sample)
    {
        _lastSample = sample;
        MonitorChanged?.Invoke(sample);
    }

    private static void EnsureOk(JsonObject response, string failure)
    {
        if (IsOk(response)) return;
        string? comment = response["requestStatus"]?["comment"]?.GetValue<string>();
        throw new InvalidOperationException(comment is null ? failure : $"{failure}: {comment}");
    }

    private static bool IsOk(JsonObject response) =>
        response["requestStatus"]?["result"]?.GetValue<bool>() ?? false;

    private async Task SendAsync(JsonObject payload, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
        await _obs!.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private async Task<JsonObject> ReceiveAsync(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            WebSocketReceiveResult result = await _obs!.ReceiveAsync(chunk, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("OBS cerró la conexión.");
            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return JsonNode.Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)) as JsonObject
            ?? throw new InvalidDataException("OBS envió una respuesta no válida.");
    }

    private static bool TryBool(JsonObject o, string key, out bool value)
    {
        value = false;
        try { if (o[key] is JsonNode node) { value = node.GetValue<bool>(); return true; } }
        catch (InvalidOperationException) { }
        return false;
    }

    private static bool TryDouble(JsonObject o, string key, out double value)
    {
        value = 0;
        try { if (o[key] is JsonNode node) { value = node.GetValue<double>(); return true; } }
        catch (InvalidOperationException) { }
        return false;
    }

    private static bool TryLong(JsonObject o, string key, out long value)
    {
        value = 0;
        try { if (o[key] is JsonNode node) { value = node.GetValue<long>(); return true; } }
        catch (InvalidOperationException) { }
        return false;
    }

    private static bool TryString(JsonObject o, string key, out string? value)
    {
        value = null;
        try { if (o[key] is JsonNode node) { value = node.GetValue<string>(); return true; } }
        catch (InvalidOperationException) { }
        return false;
    }

    /// <summary>Vector vacío cuando la fuente no entregó el dato; nunca un cero falso.</summary>
    private static double? ParseOptional(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            ? result
            : null;

    private static TimeSpan? ParseOptionalTimestamp(string value)
    {
        if (TimeSpan.TryParseExact(value, @"hh\:mm\:ss\.FFFFFFF", CultureInfo.InvariantCulture, out TimeSpan result) ||
            TimeSpan.TryParseExact(value, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out result))
            return result;
        return null;
    }

    private static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Fmt(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string QuoteForDisplay(string value)
    {
        if (value.Length > 0 && value.All(c =>
                char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '/' or ':' or '=' or '@' or '?' or '&'))
            return value;
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private void Report(string message) => StatusChanged?.Invoke(message);

    public async ValueTask DisposeAsync()
    {
        await StopMonitorAsync().ConfigureAwait(false);
        _isLive = false;
        if (_ffmpeg is { HasExited: false })
        {
            try { _ffmpeg.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
        _ffmpeg?.Dispose();
        if (_obs is not null)
        {
            try
            {
                if (_obs.State == WebSocketState.Open)
                    await _obs.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
            }
            catch (WebSocketException) { }
            _obs.Dispose();
        }
        _obsGate.Dispose();
    }
}
