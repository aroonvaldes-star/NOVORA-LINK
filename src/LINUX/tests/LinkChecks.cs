using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NOVORA.LinkEngine.Network;
using NOVORA.LinkEngine.Recovery;
using NOVORA.Linux;
using NOVORA.Linux.Control;

/// <summary>
/// LinkEngine Linux parity: relay metrics and supervised recovery (fake and real relay),
/// the Windows SpeedTest server end to end over HTTP, current-session traffic and the
/// Windows session presentation. Run: dotnet run -- --link-native
/// </summary>
internal static class LinkChecks
{
    private static int _pass, _fail;

    public static async Task RunNativeAsync()
    {
        _pass = _fail = 0;
        MetricParsing();
        await FakeRelayRecoveryAsync();
        await RealRelayRecoveryAsync();
        await SpeedTestAndTrafficAsync();
        Presentation();
        await ControlSpeedTestAsync();
        Console.WriteLine($"LinkEngine Linux: {_pass} PASS / {_fail} FAIL");
        if (_fail != 0) throw new InvalidOperationException($"LinkEngine Linux checks failed: {_fail}.");
    }

    private static void Check(bool ok, string name)
    {
        if (ok) { _pass++; Console.WriteLine("PASS " + name); }
        else { _fail++; Console.WriteLine("FAIL " + name); }
    }

    private static async Task<bool> Until(Func<bool> condition, int ms = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < deadline) { if (condition()) return true; await Task.Delay(20); }
        return condition();
    }

    private static void MetricParsing()
    {
        var now = DateTimeOffset.UtcNow;
        Check(LinuxLinkRelay.TryParseMetric("LE_METRIC tx=10 rx=20 txp=1 rxp=2", now, out var ok) &&
              ok is { TxBytes: 10, RxBytes: 20, TxPackets: 1, RxPackets: 2 }, "LE_METRIC parsed like Windows");
        Check(LinuxLinkRelay.TryParseMetric("LE_METRIC tx=-1 rx=20 txp=1 rxp=2", now, out var negative) && negative is null,
            "negative counters rejected");
        Check(LinuxLinkRelay.TryParseMetric("LE_METRIC tx=1 rx=2", now, out var shortLine) && shortLine is null,
            "incomplete metric rejected");
        Check(!LinuxLinkRelay.TryParseMetric("LE_QOS {}", now, out _), "other relay lines are not metrics");
    }

    private static Process Script(string body)
    {
        var start = new ProcessStartInfo("/bin/bash", ["-c", body])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.Start();
        return process;
    }

    private static async Task FakeRelayRecoveryAsync()
    {
        var policy = new LERecoveryPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5), 3);
        string token = new('a', 64);
        var tokens = new List<string>();
        int launches = 0;
        await using (var relay = new LinuxLinkRelay(policy, (t, _) =>
        {
            lock (tokens) tokens.Add(t);
            Interlocked.Increment(ref launches);
            return Script("echo 'LE_READY 27184'; while true; do echo 'LE_METRIC tx=100 rx=200 txp=1 rxp=2'; sleep 0.1; done");
        }))
        {
            int recovering = 0, recovered = 0;
            string? gaveUp = null;
            relay.Recovering += (_, _) => Interlocked.Increment(ref recovering);
            relay.Recovered += (_, _) => Interlocked.Increment(ref recovered);
            relay.ExitedUnexpectedly += (_, message) => gaveUp = message;
            await relay.StartAsync(token, CancellationToken.None);
            Check(await Until(() => relay.TrafficSnapshot is { TxBytes: 100, RxBytes: 200 }), "relay metrics become the traffic snapshot");
            KillRelayChild();
            Check(await Until(() => Volatile.Read(ref recovered) == 1 && relay.IsRunning), "killed relay is restarted");
            Check(Volatile.Read(ref recovering) == 1 && relay.Restarts == 1 && gaveUp is null, "one recovery attempt, no failure");
            lock (tokens) Check(tokens.Count == 2 && tokens.All(t => t == token), "restart reuses the session token");
            Check(await Until(() => relay.TrafficSnapshot is not null), "metrics resume after restart");
            await relay.StopAsync();
            int afterStop = Volatile.Read(ref launches);
            await Task.Delay(400);
            Check(Volatile.Read(ref launches) == afterStop && !relay.IsRunning, "stop does not trigger recovery");
        }

        int attempts = 0;
        await using (var failing = new LinuxLinkRelay(policy, (_, _) =>
        {
            // First launch stays up; every restart dies before LE_READY.
            return Interlocked.Increment(ref attempts) == 1
                ? Script("echo 'LE_READY 27184'; sleep 0.3; exit 3")
                : Script("exit 4");
        }))
        {
            string? gaveUp = null;
            failing.ExitedUnexpectedly += (_, message) => gaveUp = message;
            await failing.StartAsync(token, CancellationToken.None);
            Check(await Until(() => gaveUp is not null), "recovery gives up after the Windows failure limit");
            Check(Volatile.Read(ref attempts) == 1 + policy.MaximumConsecutiveFailures, "exactly MaximumConsecutiveFailures restarts");
        }

        static void KillRelayChild()
        {
            // The fake relay is the newest bash child of this test process.
            int self = Environment.ProcessId;
            Process? child = Process.GetProcessesByName("bash")
                .Where(p => { try { return File.ReadAllText($"/proc/{p.Id}/stat").Split(' ')[3] == self.ToString(); } catch { return false; } })
                .OrderByDescending(p => p.Id).FirstOrDefault();
            child?.Kill(entireProcessTree: true);
        }
    }

    private static async Task RealRelayRecoveryAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Tools", "LinkEngine", "novora-linkengine-relay");
        if (!File.Exists(path)) { Check(false, "real relay binary is published next to the app"); return; }
        var policy = new LERecoveryPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), 3);
        await using var relay = new LinuxLinkRelay(policy);
        int recovered = 0;
        relay.Recovered += (_, _) => Interlocked.Increment(ref recovered);
        string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await relay.StartAsync(token, CancellationToken.None);
        // Like the Windows relay (same source), LE_METRIC is emitted only during an Android DATA session.
        await Task.Delay(1500);
        Check(relay.TrafficSnapshot is null && relay.IsRunning, "real relay invents no traffic without an Android session");
        TcpClient? data = await OpenDataSessionAsync(token);
        Check(data is not null, "authenticated DATA session accepted by the real relay");
        Check(await Until(() => relay.TrafficSnapshot is { TxBytes: >= 0 }, 4000), "real relay reports LE_METRIC during the session");
        data?.Dispose();
        Process? real = Process.GetProcessesByName("novora-linkengine-relay").OrderByDescending(p => p.Id).FirstOrDefault();
        Check(real is not null, "real relay process found");
        real?.Kill();
        Check(await Until(() => Volatile.Read(ref recovered) == 1 && relay.IsRunning, 8000), "real relay restarts after being killed");
        using (TcpClient? again = await OpenDataSessionAsync(token))
        {
            Check(again is not null, "Android DATA reconnects to the restarted relay with the same token");
            Check(await Until(() => relay.TrafficSnapshot is not null, 4000), "real relay metrics resume after recovery");
        }
        await relay.StopAsync();
    }

    private static async Task<TcpClient?> OpenDataSessionAsync(string token)
    {
        var client = new TcpClient();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(IPAddress.Loopback, 27184, timeout.Token);
            NetworkStream stream = client.GetStream();
            await LinuxLinkProtocol.WriteFrameAsync(stream, LinuxLinkProtocol.CreateDataAuthentication(token), timeout.Token);
            string ack = await LinuxLinkProtocol.ReadFrameAsync(stream, timeout.Token);
            if (ack == LinuxLinkProtocol.DataAuthenticationAck) return client;
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or InvalidDataException) { }
        client.Dispose();
        return null;
    }

    private static async Task SpeedTestAndTrafficAsync()
    {
        using var measurements = new LinuxLinkMeasurements();
        var engine = new SessionEngine();
        measurements.Sync(engine);
        Check(measurements.SessionKey is null && measurements.TrafficText == LinuxLinkMeasurements.NoSession,
            "no measurement session while LE is not connected");
        engine.Connect("USB", "R5CT123");
        measurements.Sync(engine);
        Check(measurements.SessionKey is not null && measurements.TrafficText == LinuxLinkMeasurements.ManualTraffic,
            "connected session opens a measurement record");

        string url = measurements.Begin(engine, "tunnel");
        Check(url.StartsWith("http://10.0.2.2:28085/", StringComparison.Ordinal) && url.EndsWith("/?mode=tunnel", StringComparison.Ordinal),
            "SpeedTest URL goes through the LE VPN gateway");
        string local = url.Replace("http://10.0.2.2:", "http://127.0.0.1:", StringComparison.Ordinal).Split('?')[0];
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        Check((await http.GetAsync(local)).IsSuccessStatusCode, "SpeedTest page served on PC loopback");
        Check((await http.GetByteArrayAsync(local + "ping")).Length == 1, "ping endpoint");
        Check((await http.GetByteArrayAsync(local + "download")).Length == 8 * 1024 * 1024, "8 MiB download");
        var posted = await http.PostAsync(local + "result", new StringContent("[94.5,41.2,3.4,0.8]", Encoding.UTF8));
        Check(posted.IsSuccessStatusCode && await Until(() => measurements.TunnelText.Contains($"↓ {94.5:F1} Mbps")),
            "tunnel result recorded for this session");
        var internet = await http.PostAsync(local + "result-internet", new StringContent("[300,120,12,2]", Encoding.UTF8));
        Check(internet.IsSuccessStatusCode && await Until(() => measurements.InternetText.Contains("Cloudflare")),
            "internet result recorded for this session");
        Check((await http.GetAsync(local.Replace("/" + local.Split('/')[3] + "/", "/wrongtoken/"))).StatusCode == HttpStatusCode.NotFound,
            "other tokens are rejected");

        var start = DateTimeOffset.UtcNow;
        engine.Traffic = new LERelayTrafficSnapshot(start, 1_000_000, 2_000_000, 10, 20);
        measurements.ObserveTraffic(engine);
        await Task.Delay(300);
        engine.Traffic = new LERelayTrafficSnapshot(DateTimeOffset.UtcNow, 2_000_000, 6_000_000, 20, 40);
        measurements.ObserveTraffic(engine);
        Check(measurements.TrafficText.StartsWith("Subida desde Android:", StringComparison.Ordinal) &&
              measurements.TrafficText.Contains($"↑ {1.0:F2} MB") && measurements.TrafficText.Contains($"↓ {4.0:F2} MB"),
            "manual traffic sample uses relay counters");
        string? export = measurements.ExportJson();
        Check(export is not null && export.Contains("\"Tunnel\"") && export.Contains("94.5"), "session export contains this session only");

        string oldLocal = local;
        engine.Connect("USB", "R5CT123"); // reconnection = new session key
        measurements.Sync(engine);
        Check(measurements.TunnelText == LinuxLinkMeasurements.NoTunnel && measurements.InternetText == LinuxLinkMeasurements.NoInternet,
            "a new session never shows previous measurements");
        Check((await http.GetAsync(oldLocal + "ping")).StatusCode == HttpStatusCode.NotFound, "previous session token is invalidated");
        engine.Connect("LAN", null);
        Check(measurements.Begin(engine).StartsWith("http://10.0.2.2:28085/", StringComparison.Ordinal), "LAN session also gets a SpeedTest");
    }

    private static void Presentation()
    {
        var engine = new SessionEngine();
        Check(MainWindow.LinkPresentationVE(null) == ("Detenida", "Sin transporte · Detenida", "En espera"), "stopped presentation");
        engine.Connect("USB", "R5CT123");
        Check(MainWindow.LinkPresentationVE(engine) == ("Conectada", "USB activo", "Sin incidencias"), "connected presentation");
        engine.SetState("Degraded", connected: false);
        Check(MainWindow.LinkPresentationVE(engine).State == "Reconectando…", "relay recovery shows reconnecting");
        engine.SetState("Error", connected: false);
        Check(MainWindow.LinkPresentationVE(engine) == ("Necesita atención", "USB · Necesita atención", "Necesita atención"), "failed presentation");
        engine.Connect("USB", "R5CT123");
        Check(MainWindow.LinkTechnicalDetailsVE(engine).Contains("Client ID: android-1 - Heartbeat: 7"), "technical details show client and heartbeats");
    }

    private static async Task ControlSpeedTestAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "novora-link-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var store = new LinuxTrustStore(directory);
        var engine = new SessionEngine();
        using var measurements = new LinuxLinkMeasurements();
        var server = new LinuxControlServer(IPAddress.Loopback, store, port: 0, allowLoopbackForTests: true, linkEngine: engine)
        {
            LinkMeasurements = measurements
        };
        server.Start();
        try
        {
            LanInvitation invitation = server.Invitation;
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse(invitation.Host), invitation.Port);
            byte[] expected = Convert.FromHexString(invitation.Fingerprint);
            using var tls = new System.Net.Security.SslStream(client.GetStream(), false, (_, certificate, _, _) =>
                certificate is not null && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Security.Cryptography.SHA256.HashData(certificate.GetRawCertData()), expected));
            await tls.AuthenticateAsClientAsync(new System.Net.Security.SslClientAuthenticationOptions { TargetHost = "NOVORA" });
            async Task<ControlReply> Send(long id, string action, string? value = null)
            {
                await ControlProtocol.WriteAsync(tls, new ControlRequest(1, id, action, Value: value), CancellationToken.None);
                return await ControlProtocol.ReadAsync<ControlReply>(tls, CancellationToken.None);
            }
            await ControlProtocol.WriteAsync(tls, new ControlRequest(1, 1, "pair.lan", Code: invitation.Code), CancellationToken.None);
            Check((await ControlProtocol.ReadAsync<ControlReply>(tls, CancellationToken.None)).Success, "LINK Control pairs over TLS");
            ControlReply early = await Send(2, "link.speedtest");
            Check(!early.Success && early.Message == "Inicia LinkEngine antes de medir el túnel.", "link.speedtest requires a running LE (Windows message)");
            engine.Connect("LAN", null);
            ControlReply withValue = await Send(3, "link.speedtest", "x");
            Check(!withValue.Success, "link.speedtest rejects extra values like Windows");
            ControlReply started = await Send(4, "link.speedtest");
            Check(started.Success && started.Message == "Abre la prueba del túnel en Android." &&
                  started.Value?.StartsWith("http://10.0.2.2:28085/", StringComparison.Ordinal) == true,
                "link.speedtest returns the SpeedTest URL to Android");
            Check(measurements.TunnelText == "SpeedTest preparado. Ejecútalo en el navegador de Android.", "PC shows the prepared test");
        }
        finally
        {
            await server.DisposeAsync();
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private sealed class SessionEngine : ILinuxLinkEngine
    {
        private string _state = "Stopped";
        private bool _connected;
        private string _transport = "USB";
        private string? _serial;
        private string? _key;
        public event EventHandler? StateChanged;
        public bool IsStarted => _state != "Stopped";
        public bool IsConnected => _connected;
        public string State => _state;
        public string Message => _state;
        public string Transport => _transport;
        public string? Serial => _serial;
        public string? SessionKey => _connected ? _key : null;
        public string ClientId => "android-1";
        public long HeartbeatCount => 7;
        public int RelayRestarts => 0;
        public LERelayTrafficSnapshot? Traffic { get; set; }
        public LERelayTrafficSnapshot? TrafficSnapshot => Traffic;
        public Task<string> StartAsync(CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task StopAsync() => Task.CompletedTask;

        public void Connect(string transport, string? serial)
        {
            _transport = transport;
            _serial = serial;
            _key = Guid.NewGuid().ToString("N");
            SetState("Connected", connected: true);
        }

        public void SetState(string state, bool connected)
        {
            _state = state;
            _connected = connected;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
