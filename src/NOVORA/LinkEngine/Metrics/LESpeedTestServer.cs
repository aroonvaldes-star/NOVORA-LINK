using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NOVORA.LinkEngine.Metrics;

public sealed record LESpeedTestResult(DateTimeOffset SampledAtUtc, string Transport,
    double DownloadMbps, double UploadMbps, double LatencyMs, double JitterMs,
    string SessionId);
public sealed record LEInternetSpeedTestResult(DateTimeOffset SampledAtUtc, string Transport,
    double DownloadMbps, double UploadMbps, double LatencyMs, double JitterMs,
    string Provider, string SessionId);

// The browser reaches 10.0.2.2 through the Android VPN. NovoraRelay maps only
// this test port to the PC loopback listener; it never exposes this listener on LAN.
public sealed class LESpeedTestServer : IDisposable
{
    public const int Port = 28085;
    private readonly TcpListener _listener = new(IPAddress.Loopback, Port);
    private readonly CancellationTokenSource _stop = new();
    private string _token = "";
    private string _transport = "";
    private string _sessionId = "";
    private DateTimeOffset _expires;
    private bool _started;
    public event EventHandler<LESpeedTestResult>? ResultReceived;
    public event EventHandler<LEInternetSpeedTestResult>? InternetResultReceived;

    public string Begin(string transport)
    {
        if (transport is not ("USB" or "LAN"))
            throw new ArgumentException("Transporte LE no válido.", nameof(transport));
        if (!_started)
        {
            _listener.Start();
            _started = true;
            _ = Task.Run(AcceptAsync);
        }
        _token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        _transport = transport;
        _sessionId = Guid.NewGuid().ToString("N");
        _expires = DateTimeOffset.UtcNow.AddMinutes(5);
        return $"http://10.0.2.2:{Port}/{_token}/";
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => HandleAsync(client));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (SocketException) when (_stop.IsCancellationRequested) { break; }
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            client.ReceiveTimeout = client.SendTimeout = 15000;
            await using NetworkStream stream = client.GetStream();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                string request = await ReadLineAsync(stream, 2048, timeout.Token);
                string[] parts = request.Split(' ');
                if (parts.Length < 2) return;
                int contentLength = 0;
                for (int i = 0; i < 80; i++)
                {
                    string line = await ReadLineAsync(stream, 8192, timeout.Token);
                    if (line.Length == 0) break;
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line[15..].Trim(), NumberStyles.None,
                            CultureInfo.InvariantCulture, out contentLength);
                }
                string prefix = "/" + _token + "/";
                if (DateTimeOffset.UtcNow > _expires || !parts[1].StartsWith(prefix, StringComparison.Ordinal))
                {
                    await RespondAsync(stream, 404, "text/plain", Encoding.UTF8.GetBytes("No disponible"), timeout.Token);
                    return;
                }
                string action = parts[1][prefix.Length..].Split('?')[0];
                if (parts[0] == "GET" && action == "")
                    await RespondAsync(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page), timeout.Token);
                else if (parts[0] == "GET" && action == "ping")
                    await RespondAsync(stream, 200, "text/plain", [1], timeout.Token);
                else if (parts[0] == "GET" && action == "download")
                    await DownloadAsync(stream, timeout.Token);
                else if (parts[0] == "POST" && action == "upload" && contentLength == TestBytes)
                {
                    byte[] buffer = new byte[65536];
                    int left = contentLength;
                    while (left > 0)
                    {
                        int count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(left, buffer.Length)), timeout.Token);
                        if (count == 0) return;
                        left -= count;
                    }
                    await RespondAsync(stream, 200, "text/plain", [1], timeout.Token);
                }
                else if (parts[0] == "POST" && action == "result" && contentLength is > 0 and <= 4096)
                {
                    byte[] body = new byte[contentLength];
                    await stream.ReadExactlyAsync(body, timeout.Token);
                    var numbers = JsonSerializer.Deserialize<double[]>(body);
                    if (numbers is not { Length: 4 } || numbers.Any(x => !double.IsFinite(x) || x < 0 || x > 100000))
                        return;
                    var result = new LESpeedTestResult(DateTimeOffset.UtcNow, _transport,
                        numbers[0], numbers[1], numbers[2], numbers[3], _sessionId);
                    string directory = Path.Combine(Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData), "NOVORA", "LinkEngine", "SpeedTests");
                    Directory.CreateDirectory(directory);
                    await File.WriteAllTextAsync(Path.Combine(directory, result.SessionId + ".json"),
                        JsonSerializer.Serialize(result), timeout.Token);
                    ResultReceived?.Invoke(this, result);
                    await RespondAsync(stream, 200, "text/plain", Encoding.UTF8.GetBytes("Guardado"), timeout.Token);
                }
                else if (parts[0] == "POST" && action == "result-internet" && contentLength is > 0 and <= 4096)
                {
                    byte[] body = new byte[contentLength];
                    await stream.ReadExactlyAsync(body, timeout.Token);
                    var numbers = JsonSerializer.Deserialize<double[]>(body);
                    if (numbers is not { Length: 4 } || numbers.Any(x => !double.IsFinite(x) || x < 0 || x > 100000))
                        return;
                    var result = new LEInternetSpeedTestResult(DateTimeOffset.UtcNow, _transport,
                        numbers[0], numbers[1], numbers[2], numbers[3], "Cloudflare", _sessionId);
                    string directory = Path.Combine(Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData), "NOVORA", "LinkEngine", "SpeedTests");
                    Directory.CreateDirectory(directory);
                    await File.WriteAllTextAsync(Path.Combine(directory, result.SessionId + "-internet.json"),
                        JsonSerializer.Serialize(result), timeout.Token);
                    InternetResultReceived?.Invoke(this, result);
                    await RespondAsync(stream, 200, "text/plain", Encoding.UTF8.GetBytes("Guardado"), timeout.Token);
                }
                else await RespondAsync(stream, 404, "text/plain", Encoding.UTF8.GetBytes("No disponible"), timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or SocketException)
            {
                // The browser can cancel an individual measurement without stopping LE.
            }
        }
    }

    private const int TestBytes = 8 * 1024 * 1024;
    private static async Task DownloadAsync(Stream stream, CancellationToken token)
    {
        await WriteHeaderAsync(stream, 200, "application/octet-stream", TestBytes, token);
        byte[] chunk = new byte[65536];
        for (int remaining = TestBytes; remaining > 0; remaining -= chunk.Length)
            await stream.WriteAsync(chunk, token);
    }
    private static async Task RespondAsync(Stream stream, int status, string type, byte[] body, CancellationToken token)
    {
        await WriteHeaderAsync(stream, status, type, body.Length, token);
        await stream.WriteAsync(body, token);
    }
    private static Task WriteHeaderAsync(Stream stream, int status, string type, int length, CancellationToken token) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\n" +
            $"Content-Type: {type}\r\nContent-Length: {length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n"), token).AsTask();
    private static async Task<string> ReadLineAsync(Stream stream, int max, CancellationToken token)
    {
        byte[] one = new byte[1];
        var buffer = new List<byte>();
        while (buffer.Count < max)
        {
            if (await stream.ReadAsync(one, token) == 0) throw new EndOfStreamException();
            if (one[0] == '\n') return Encoding.ASCII.GetString(buffer.ToArray()).TrimEnd('\r');
            buffer.Add(one[0]);
        }
        throw new InvalidDataException("Encabezado HTTP demasiado largo.");
    }
    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }

    private const string Page = """
<!doctype html><html lang="es"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>LinkEngine · Prueba del túnel</title>
<style>body{font:16px system-ui;background:#101827;color:#f4f7ff;max-width:560px;margin:40px auto;padding:20px}button{padding:14px 22px;border:0;border-radius:9px;background:#338cff;color:white;font-weight:700}pre{white-space:pre-wrap;line-height:1.6}</style>
<h1>LinkEngine · SpeedTest</h1><p>Pruebas separadas desde el navegador del teléfono por el VPN de LE. Cada prueba usa 8 MiB de bajada y 8 MiB de subida.</p>
<button id="start">Medir túnel PC ↔ Android</button> <button id="internet">Medir Internet por LE</button>
<p>Internet se mide contra Cloudflare. Si el navegador bloquea esa prueba, el resultado se mostrará como no disponible.</p><pre id="result">Esperando inicio.</pre>
<script>
const base=location.pathname;const result=document.getElementById('result');
document.getElementById('start').onclick=async()=>{document.getElementById('start').disabled=true;
try{let p=[];for(let i=0;i<5;i++){let t=performance.now();let r=await fetch(base+'ping?i='+i,{cache:'no-store'});if(!r.ok)throw Error('Ping');await r.arrayBuffer();p.push(performance.now()-t)}
result.textContent='Midiendo descarga…';let t=performance.now();let d=await fetch(base+'download',{cache:'no-store'});if(!d.ok)throw Error('Descarga');let bytes=(await d.arrayBuffer()).byteLength;let down=bytes*8/(performance.now()-t)/1000;
result.textContent='Midiendo subida…';let payload=new Uint8Array(8388608);t=performance.now();let u=await fetch(base+'upload',{method:'POST',body:payload});if(!u.ok)throw Error('Subida');let up=payload.byteLength*8/(performance.now()-t)/1000;
let latency=p.reduce((a,b)=>a+b,0)/p.length;let jitter=p.slice(1).reduce((a,b,i)=>a+Math.abs(b-p[i]),0)/(p.length-1);
let saved=await fetch(base+'result',{method:'POST',body:JSON.stringify([down,up,latency,jitter])});if(!saved.ok)throw Error('Guardar resultado');
result.textContent=`Descarga: ${down.toFixed(1)} Mbps\nSubida: ${up.toFixed(1)} Mbps\nLatencia HTTP al relay: ${latency.toFixed(1)} ms\nVariación: ${jitter.toFixed(1)} ms\nResultado guardado en NOVORA PC.`;
}catch(e){result.textContent='Prueba incompleta: '+e.message}finally{document.getElementById('start').disabled=false}};
document.getElementById('internet').onclick=async()=>{document.getElementById('internet').disabled=true;
try{let p=[];for(let i=0;i<5;i++){let t=performance.now();let r=await fetch('https://speed.cloudflare.com/__down?bytes=0&i='+i,{cache:'no-store'});if(!r.ok)throw Error('Latencia');await r.arrayBuffer();p.push(performance.now()-t)}
result.textContent='Midiendo Internet: descarga…';let t=performance.now();let d=await fetch('https://speed.cloudflare.com/__down?bytes=8388608',{cache:'no-store'});if(!d.ok)throw Error('Descarga');let bytes=(await d.arrayBuffer()).byteLength;if(bytes!==8388608)throw Error('Descarga incompleta');let down=bytes*8/(performance.now()-t)/1000;
result.textContent='Midiendo Internet: subida…';let payload=new Uint8Array(8388608);t=performance.now();let u=await fetch('https://speed.cloudflare.com/__up',{method:'POST',body:payload});if(!u.ok)throw Error('Subida');await u.arrayBuffer();let up=payload.byteLength*8/(performance.now()-t)/1000;
let latency=p.reduce((a,b)=>a+b,0)/p.length;let jitter=p.slice(1).reduce((a,b,i)=>a+Math.abs(b-p[i]),0)/(p.length-1);
let saved=await fetch(base+'result-internet',{method:'POST',body:JSON.stringify([down,up,latency,jitter])});if(!saved.ok)throw Error('Guardar resultado');
result.textContent=`INTERNET POR LE · Cloudflare\nDescarga: ${down.toFixed(1)} Mbps\nSubida: ${up.toFixed(1)} Mbps\nLatencia HTTP: ${latency.toFixed(1)} ms\nVariación: ${jitter.toFixed(1)} ms\nResultado guardado en NOVORA PC.`;
}catch(e){result.innerHTML='Internet no disponible: '+String(e.message).replace(/[&<>]/g,'')+'<br><a style="color:#7cb6ff" href="https://speed.cloudflare.com/">Abrir SpeedTest de Cloudflare</a>'}finally{document.getElementById('internet').disabled=false}};
const mode=new URLSearchParams(location.search).get('mode');if(mode==='internet')document.getElementById('internet').click();else if(mode==='tunnel')document.getElementById('start').click();
</script></html>
""";
}
