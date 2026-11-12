using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace NOVORA.Streaming;

// Shared streaming backend. Compiled unchanged into WINDOWS (WPF) and LINUX (Avalonia),
// so both platforms send the same commands and report the same states.
public enum NLStreamMode
{
    Rtmp,
    ObsWebSocket
}

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
}

public sealed class NLStreamingService : IAsyncDisposable
{
    private Process? _ffmpeg;
    private ClientWebSocket? _obs;

    // Raised from background threads; UI code must marshal to its dispatcher.
    public event Action<string>? StatusChanged;

    public bool IsLive { get; private set; }

    public async Task StartAsync(NLStreamSettings settings, CancellationToken ct = default)
    {
        if (IsLive) throw new InvalidOperationException("La transmisión ya está activa.");
        if (settings.Mode == NLStreamMode.Rtmp)
        {
            StartFfmpeg(settings);
        }
        else
        {
            await ObsConnectAsync(settings, ct);
            JsonObject response = await ObsRequestAsync("StartStream", ct);
            EnsureOk(response, "OBS no pudo iniciar la transmisión");
            IsLive = true;
            Report("OBS: transmisión iniciada.");
        }
    }

    public async Task StopAsync(NLStreamSettings settings, CancellationToken ct = default)
    {
        if (settings.Mode == NLStreamMode.Rtmp)
        {
            await StopFfmpegAsync();
        }
        else
        {
            if (_obs is null || _obs.State != WebSocketState.Open)
                await ObsConnectAsync(settings, ct);
            JsonObject response = await ObsRequestAsync("StopStream", ct);
            EnsureOk(response, "OBS no pudo detener la transmisión");
            Report("OBS: transmisión detenida.");
        }
        IsLive = false;
    }

    // Returns the command line used for RTMP. The stream key is never reported to the UI.
    public static string BuildFfmpegArguments(NLStreamSettings s)
    {
        string input = OperatingSystem.IsWindows()
            ? $"-f gdigrab -framerate {s.Fps} -i desktop"
            : $"-f x11grab -framerate {s.Fps} -i {Environment.GetEnvironmentVariable("DISPLAY") ?? ":0"}.0";
        string target = s.RtmpUrl.TrimEnd('/') + "/" + s.RtmpKey;
        return $"-hide_banner -loglevel warning {input} -c:v libx264 -preset veryfast -tune zerolatency " +
               $"-b:v {s.BitrateKbps}k -maxrate {s.BitrateKbps}k -bufsize {s.BitrateKbps * 2}k " +
               $"-pix_fmt yuv420p -g {s.Fps * 2} -an -f flv \"{target}\"";
    }

    private void StartFfmpeg(NLStreamSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.RtmpUrl) || string.IsNullOrWhiteSpace(s.RtmpKey))
            throw new InvalidOperationException("Falta la URL o la clave de transmisión.");

        var info = new ProcessStartInfo("ffmpeg", BuildFfmpegArguments(s))
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        try
        {
            _ffmpeg = Process.Start(info)
                ?? throw new InvalidOperationException("No se pudo iniciar ffmpeg.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("ffmpeg no está instalado o no está en el PATH.");
        }
        _ffmpeg.EnableRaisingEvents = true;
        // Drain stderr so ffmpeg never blocks on a full pipe during a long stream.
        _ffmpeg.ErrorDataReceived += (_, _) => { };
        _ffmpeg.BeginErrorReadLine();
        _ffmpeg.Exited += (_, _) =>
        {
            if (IsLive)
            {
                IsLive = false;
                Report("ffmpeg terminó la transmisión.");
            }
        };
        IsLive = true;
        Report("RTMP: transmisión iniciada.");
    }

    private async Task StopFfmpegAsync()
    {
        if (_ffmpeg is null || _ffmpeg.HasExited) return;
        try
        {
            // "q" asks ffmpeg to finish the stream cleanly.
            await _ffmpeg.StandardInput.WriteLineAsync("q");
            await _ffmpeg.StandardInput.FlushAsync();
            if (!_ffmpeg.WaitForExit(5000)) _ffmpeg.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
        Report("RTMP: transmisión detenida.");
    }

    private async Task ObsConnectAsync(NLStreamSettings s, CancellationToken ct)
    {
        _obs?.Dispose();
        _obs = new ClientWebSocket();
        _obs.Options.AddSubProtocol("obswebsocket.json");
        await _obs.ConnectAsync(new Uri($"ws://{s.ObsHost}:{s.ObsPort}"), ct);

        JsonObject hello = await ReceiveAsync(ct);
        JsonObject helloData = (JsonObject)hello["d"]!;
        var identify = new JsonObject { ["rpcVersion"] = 1 };
        if (helloData["authentication"] is JsonObject auth)
        {
            // OBS WebSocket v5: base64(sha256(base64(sha256(password + salt)) + challenge))
            string salt = auth["salt"]!.GetValue<string>();
            string challenge = auth["challenge"]!.GetValue<string>();
            string secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s.ObsPassword + salt)));
            identify["authentication"] = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
        }
        await SendAsync(new JsonObject { ["op"] = 1, ["d"] = identify }, ct);

        JsonObject identified = await ReceiveAsync(ct);
        if (identified["op"]?.GetValue<int>() != 2)
            throw new InvalidOperationException("OBS rechazó la conexión. Revisa la contraseña de WebSocket.");
        Report("OBS: conectado.");
    }

    private async Task<JsonObject> ObsRequestAsync(string requestType, CancellationToken ct)
    {
        if (_obs is null || _obs.State != WebSocketState.Open)
            throw new InvalidOperationException("OBS no está conectado.");
        string requestId = Guid.NewGuid().ToString("N");
        await SendAsync(new JsonObject
        {
            ["op"] = 6,
            ["d"] = new JsonObject { ["requestType"] = requestType, ["requestId"] = requestId }
        }, ct);

        while (true)
        {
            JsonObject message = await ReceiveAsync(ct);
            // Op 7 is RequestResponse; other ops (events) are ignored here.
            if (message["op"]?.GetValue<int>() == 7 &&
                message["d"]?["requestId"]?.GetValue<string>() == requestId)
            {
                return (JsonObject)message["d"]!;
            }
        }
    }

    private static void EnsureOk(JsonObject response, string failure)
    {
        bool ok = response["requestStatus"]?["result"]?.GetValue<bool>() ?? false;
        if (!ok)
        {
            string? comment = response["requestStatus"]?["comment"]?.GetValue<string>();
            throw new InvalidOperationException(comment is null ? failure : $"{failure}: {comment}");
        }
    }

    private async Task SendAsync(JsonObject payload, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
        await _obs!.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private async Task<JsonObject> ReceiveAsync(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            WebSocketReceiveResult result = await _obs!.ReceiveAsync(chunk, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("OBS cerró la conexión.");
            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return (JsonObject)JsonNode.Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))!;
    }

    private void Report(string message) => StatusChanged?.Invoke(message);

    public async ValueTask DisposeAsync()
    {
        IsLive = false;
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
                    await _obs.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch (WebSocketException) { }
            _obs.Dispose();
        }
        await Task.CompletedTask;
    }
}
