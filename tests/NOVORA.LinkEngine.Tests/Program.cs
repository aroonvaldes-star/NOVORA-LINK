using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Metrics;
using NOVORA.LinkEngine.Network;
using NOVORA.LinkEngine.Transport;
using NOVORA.Service;
using NOVORA.Control;
using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Video;
using NOVORA.VisionEngine.Transport;
using System.Text.Json;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

return await LinkEngineTransportDataTests.RunAsync(args);

internal static class LinkEngineTransportDataTests
{
    private const string FakeAdbLogVariable =
        "NOVORA_LINKENGINE_TEST_ADB_LOG";

    public static async Task<int> RunAsync(string[] args)
    {
        if (IsFakeAdbProcess())
        {
            RunFakeAdb();
            return 0;
        }

        string root = Path.Combine(
            Path.GetTempPath(),
            "novora-linkengine-tests-" + Guid.NewGuid().ToString("N"));

        try
        {
            if (args.Contains("--le-session-only", StringComparer.Ordinal))
            {
                LESessionMeasurementTests.Run();
                await TestSpeedTestDoesNotPersistAsync();
                Console.WriteLine("PASS: LE session measurements do not create history.");
                return 0;
            }
            if (args.Contains("--ve-startup-only", StringComparer.Ordinal))
            {
                VeStartupRegressionTests.Run();
                Console.WriteLine("PASS: VE startup tolerates state updates and preserves validation.");
                return 0;
            }
            if (args.Contains("--gateway-only", StringComparer.Ordinal))
            {
                await TestMalformedDataTokenDoesNotStopGatewayAsync(root);
                Console.WriteLine("PASS: DATA LAN gateway malformed token recovery.");
                return 0;
            }
            VeStartupRegressionTests.Run();
            LESessionMeasurementTests.Run();
            TestAndroidInstallPolicyAllowsWifiAdb();
            await TestAndroidInstallerAcceptsWifiAdbAsync();
            TestOwnedInternetSurvivesConnectedLan();
            TestVisionObservationWindowLastsTwentyMinutes();
            TestTransportHandshakeRequiresSessionToken();
            TestVeLanOfferAcceptsIndependentFileChannel();
            TestLanCodeHandoffRejectsInvalidCredentials();
            await TestLanDiscoveryReturnsPromptlyAsync();
            await TestLanDiscoveryRateLimitsAsync();
            await TestLanTrustServerSurvivesSlowlorisAsync(root);
            await TestLanInvitationSurvivesFailedPairingAsync(root);
            await TestVeInvalidTokenDoesNotInvalidateOfferAsync();
            await TestLanGatewaySurvivesSlowTlsAsync(root);
            await TestMalformedDataTokenDoesNotStopGatewayAsync(root);
            await TestLanFileSenderKeepsRelativeNovoraPathAsync();
            await TestLanFileSenderCancelsUnansweredRequestAsync(root);
            await TestLanFileSenderDisposalCancelsUnansweredRequestAsync(root);
            await TestLanFileSenderTimesOutUnansweredRequestAsync(root);
            await TestLanFileSenderTransfersVerifiedFilesAsync(root);
            TestFileProtocolV1FreezeAndPolicy();
            await TestLegacyFileReceiverRejectsInvalidTransfersAsync(root);
            await TestLegacyFileReceiverFuzzAsync(root);
            await TestVePacketBudgetAndPoolingAsync();
            await TestVeRejectsOversizedPacketsAsync();
            await TestVePooledPayloadStressAsync();
            string adbLog = await TestFailedVerificationCleansReverseAsync(root);
            await TestRelayRecoveryStopsWhenLastSessionEndsAsync(root);
            await TestSpeedTestServerAsync();
            TestRelayTrafficCounters();
            Console.WriteLine($"PASS: {adbLog}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex}");
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static async Task TestLanDiscoveryReturnsPromptlyAsync()
    {
        var peer = new NLControlLanPeer("PC", "127.0.0.1", 27215, new string('A', 64));
        await using var discovery = new NLControlLanDiscovery(peer,
            new IPEndPoint(IPAddress.Loopback, 0), allowLoopback: true);
        discovery.Start();
        var watch = Stopwatch.StartNew();
        var found = await NLControlLanDiscovery.SearchCoreAsync(CancellationToken.None,
            discovery.LocalEndpoint, allowLoopback: true);
        watch.Stop();
        Require(found.Count == 1 && found[0] == peer,
            "La búsqueda LAN debe detectar la PC anunciada.");
        Require(watch.Elapsed < TimeSpan.FromSeconds(2),
            "La búsqueda LAN no debe esperar los tres segundos completos tras recibir la PC.");
    }

    private static async Task TestLanDiscoveryRateLimitsAsync()
    {
        var peer = new NLControlLanPeer("PC", "127.0.0.1", 27215, new string('A', 64));
        await using var discovery = new NLControlLanDiscovery(peer,
            new IPEndPoint(IPAddress.Loopback, 0), allowLoopback: true, persistent: true);
        discovery.Start();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string nonce = new string('B', 32);
        byte[] query = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Protocol = "NOVORA-CONTROL-DISCOVER/2",
            Nonce = nonce
        });
        for (int index = 0; index < 40; index++)
            await udp.SendAsync(query, discovery.LocalEndpoint);

        int replies = 0;
        using var window = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        while (true)
        {
            try
            {
                await udp.ReceiveAsync(window.Token);
                replies++;
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
        }
        Require(replies is > 0 and < 40,
            $"El descubrimiento debe limitar las respuestas por equipo (recibidas {replies}).");
    }

    private static async Task TestLanTrustServerSurvivesSlowlorisAsync(string root)
    {
        using var store = new NLControlTrustStore(Path.Combine(root, "trust-slowloris"));
        await using var server = new NLControlTrustServer(IPAddress.Loopback,
            request => Task.FromResult(new NLControlReply(
                NLControlProtocol.Version, request.Id, true, "ok")),
            store, port: 0);
        server.Start();
        var stalled = new List<TcpClient>();
        try
        {
            for (int index = 0; index < 50; index++)
            {
                var client = new TcpClient(new IPEndPoint(
                    IPAddress.Parse($"127.0.0.{index + 2}"), 0));
                try
                {
                    await client.ConnectAsync(IPAddress.Loopback, server.Port);
                    stalled.Add(client);
                }
                catch
                {
                    client.Dispose();
                }
            }
            Require(stalled.Count >= 10,
                "La prueba debe abrir varias conexiones TLS incompletas.");
            await Task.Delay(150);

            var peer = new NLControlLanPeer("PC", "127.0.0.1", server.Port, store.Fingerprint);
            await using var control = new NLControlClient();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            NLControlReply reply = await control.ConnectLanAsync(
                peer, server.PairingCode, allowLoopback: true).WaitAsync(deadline.Token);
            Require(reply.Success,
                "Las conexiones TLS incompletas no deben impedir el emparejamiento legítimo.");
        }
        finally
        {
            foreach (TcpClient client in stalled) client.Dispose();
        }
    }

    private static async Task TestLanInvitationSurvivesFailedPairingAsync(string root)
    {
        using var store = new NLControlTrustStore(Path.Combine(root, "trust-failures"));
        await using var server = new NLControlTrustServer(IPAddress.Loopback,
            request => Task.FromResult(new NLControlReply(
                NLControlProtocol.Version, request.Id, true, "ok")),
            store, port: 0);
        server.Start();
        var peer = new NLControlLanPeer("PC", "127.0.0.1", server.Port, store.Fingerprint);
        string wrong = server.PairingCode == "000000" ? "000001" : "000000";
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await using var control = new NLControlClient();
            NLControlReply reply = await control.ConnectLanAsync(
                peer, wrong, allowLoopback: true).WaitAsync(TimeSpan.FromSeconds(5));
            Require(!reply.Success, "Un código LAN incorrecto debe rechazarse.");
        }
        Require(server.IsInvitationOpen,
            "Los fallos de un equipo no deben cancelar la invitación para los demás.");
    }

    private static async Task TestVeInvalidTokenDoesNotInvalidateOfferAsync()
    {
        await using var transport = new VETransportAppControl();
        VETransportAppControlOffer offer = transport.PrepareVE();
        Require(offer.Port != VETransportAppControl.DevicePortVE,
            "El listener VE debe usar un puerto host efímero, no el puerto fijo del dispositivo.");
        Task<Stream> accepting = transport.AcceptAsync(offer, TimeSpan.FromSeconds(5));
        try
        {
            using (var invalid = new TcpClient())
            {
                await invalid.ConnectAsync(IPAddress.Loopback, offer.Port);
                await invalid.GetStream().WriteAsync(new byte[32]);
                await invalid.GetStream().FlushAsync();
            }

            using var valid = new TcpClient();
            await valid.ConnectAsync(IPAddress.Loopback, offer.Port);
            await valid.GetStream().WriteAsync(Convert.FromHexString(offer.Token));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            Stream stream = await accepting.WaitAsync(deadline.Token);
            Require(transport.IsConnectedVE,
                "Un token VE inválido no debe invalidar la oferta para el cliente correcto.");
            await stream.DisposeAsync();
        }
        finally
        {
            await transport.DisposeAsync();
            try { await accepting.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
        }
    }

    private static async Task TestLanGatewaySurvivesSlowTlsAsync(string root)
    {
        using var store = new NLControlTrustStore(Path.Combine(root, "gateway-slow-tls"));
        using var relay = new TcpListener(IPAddress.Loopback, 0);
        relay.Start();
        await using var gateway = new NLControlLanDataGateway(IPAddress.Loopback, store,
            ((IPEndPoint)relay.LocalEndpoint).Port, allowLoopback: true);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.StateChanged += (_, _) => { if (gateway.IsConnected) connected.TrySetResult(); };
        NLControlLinkOffer offer = gateway.Start();
        var stalled = new List<TcpClient>();
        try
        {
            for (int index = 0; index < 16; index++)
            {
                var client = new TcpClient(new IPEndPoint(
                    IPAddress.Parse($"127.0.0.{index + 2}"), 0));
                try
                {
                    await client.ConnectAsync(IPAddress.Loopback, offer.Port);
                    stalled.Add(client);
                }
                catch
                {
                    client.Dispose();
                }
            }
            Require(stalled.Count >= 8,
                "La prueba debe abrir varias conexiones TLS incompletas al gateway.");
            await Task.Delay(100);

            using var valid = new TcpClient();
            await valid.ConnectAsync(IPAddress.Loopback, offer.Port);
            using var tls = new SslStream(valid.GetStream(), false, (_, _, _, _) => true);
            await tls.AuthenticateAsClientAsync("NOVORA");
            await NLControlProtocol.WriteAsync(tls,
                new NLControlLinkHello(NLControlProtocol.Version, offer.Token), CancellationToken.None);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using TcpClient relayClient = await relay.AcceptTcpClientAsync(deadline.Token);
            await connected.Task.WaitAsync(deadline.Token);
            Require(gateway.IsConnected,
                "Las conexiones TLS incompletas no deben impedir el DATA LAN legítimo.");
        }
        finally
        {
            foreach (TcpClient client in stalled) client.Dispose();
        }
    }

    private static async Task TestMalformedDataTokenDoesNotStopGatewayAsync(string root)
    {
        const string relayToken =
            "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd";
        using var store = new NLControlTrustStore(Path.Combine(root, "gateway"));
        using var relay = new TcpListener(IPAddress.Loopback, 0);
        relay.Start();
        await using var gateway = new NLControlLanDataGateway(IPAddress.Loopback, store,
            ((IPEndPoint)relay.LocalEndpoint).Port, allowLoopback: true, relayToken: relayToken);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.StateChanged += (_, _) => { if (gateway.IsConnected) connected.TrySetResult(); };
        NLControlLinkOffer offer = gateway.Start();

        async Task<(TcpClient Client, SslStream Tls)> ConnectAsync(string? token)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, offer.Port);
            var tls = new SslStream(client.GetStream(), false, (_, _, _, _) => true);
            await tls.AuthenticateAsClientAsync("NOVORA");
            if (token is null)
                await tls.WriteAsync(new byte[] { 0, 0, 0, 1, (byte)'{' });
            else
                await NLControlProtocol.WriteAsync(tls,
                    new NLControlLinkHello(NLControlProtocol.Version, token), CancellationToken.None);
            return (client, tls);
        }

        var (rejected, rejectedTls) = await ConnectAsync(new string('Z', 64));
        using (rejected)
        using (rejectedTls)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            int read = await rejectedTls.ReadAsync(new byte[1], timeout.Token);
            Require(read == 0, "DATA LAN debe cerrar un token hexadecimal inválido.");
        }

        var (malformed, malformedTls) = await ConnectAsync(null);
        using (malformed)
        using (malformedTls)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            int read = await malformedTls.ReadAsync(new byte[1], timeout.Token);
            Require(read == 0, "DATA LAN debe cerrar un saludo JSON inválido.");
        }

        var (accepted, acceptedTls) = await ConnectAsync(offer.Token);
        using TcpClient acceptedConnection = accepted;
        using SslStream validTls = acceptedTls;
        using var acceptTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using TcpClient relayClient = await relay.AcceptTcpClientAsync(acceptTimeout.Token);
        NetworkStream relayStream = relayClient.GetStream();
        string relayAuthentication = await NLControlVpnProtocol.ReadControlAsync(
            relayStream, acceptTimeout.Token);
        Require(relayAuthentication == NLControlVpnProtocol.CreateDataAuthentication(relayToken),
            "El gateway DATA LAN debe autenticarse con el token del relay.");
        await NLControlVpnProtocol.WriteControlAsync(
            relayStream,
            NLControlVpnProtocol.CreateDataAuthenticationAck(),
            acceptTimeout.Token);
        await connected.Task.WaitAsync(acceptTimeout.Token);
        Require(gateway.IsConnected, "DATA LAN debe aceptar una conexión válida tras rechazar la inválida.");
    }

    private static async Task TestSpeedTestDoesNotPersistAsync()
    {
        using var server = new LESpeedTestServer();
        LESpeedTestResult? received = null;
        server.ResultReceived += (_, result) => received = result;
        string url = server.Begin("USB").Replace("10.0.2.2", "127.0.0.1", StringComparison.Ordinal);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        string? generatedPath = null;
        try
        {
            using var response = await client.PostAsync(url + "result", new StringContent("[10,5,20,1]"));
            Require(response.IsSuccessStatusCode && received is not null, "La sesión debe recibir el resultado real.");
            generatedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NOVORA", "LinkEngine", "SpeedTests", received!.SessionId + ".json");
            Require(!File.Exists(generatedPath), "Una medición de sesión no debe crear un histórico automáticamente.");
            LEInternetSpeedTestResult? internet = null;
            server.InternetResultReceived += (_, result) => internet = result;
            using var internetResponse = await client.PostAsync(url + "result-internet", new StringContent("[12,6,22,2]"));
            Require(internetResponse.IsSuccessStatusCode && internet is not null, "La sesión debe recibir la prueba de Internet.");
            string internetPath = Path.Combine(Path.GetDirectoryName(generatedPath)!, internet!.SessionId + "-internet.json");
            Require(!File.Exists(internetPath), "La prueba de Internet tampoco debe crear un histórico.");
            server.Invalidate();
            using var expired = await client.PostAsync(url + "result", new StringContent("[100,100,1,1]"));
            Require(expired.StatusCode == HttpStatusCode.NotFound && received!.DownloadMbps == 10,
                "Una prueba invalidada debe rechazar resultados tardíos.");
            string nextUrl = server.Begin("LAN").Replace("10.0.2.2", "127.0.0.1", StringComparison.Ordinal);
            using var obsolete = await client.GetAsync(url + "ping");
            using var current = await client.GetAsync(nextUrl + "ping");
            Require(obsolete.StatusCode == HttpStatusCode.NotFound && current.IsSuccessStatusCode,
                "Solo el token de la prueba actual debe seguir activo.");
        }
        finally
        {
            if (generatedPath is not null && File.Exists(generatedPath)) File.Delete(generatedPath);
        }
    }

    private static async Task TestSpeedTestServerAsync()
    {
        using var server = new LESpeedTestServer();
        string url = server.Begin("USB").Replace("10.0.2.2", "127.0.0.1", StringComparison.Ordinal);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        client.Timeout = TimeSpan.FromSeconds(20);
        string page = await client.GetStringAsync(url);
        Require(page.Contains("LinkEngine · SpeedTest", StringComparison.Ordinal),
            "La prueba del túnel debe publicar una página identificable.");
        byte[] download = await client.GetByteArrayAsync(url + "download");
        Require(download.Length == 8 * 1024 * 1024,
            "La descarga del túnel debe transferir exactamente 8 MiB.");
        using var upload = new ByteArrayContent(new byte[8 * 1024 * 1024]);
        using var response = await client.PostAsync(url + "upload", upload);
        Require(response.IsSuccessStatusCode,
            "La subida del túnel debe aceptar exactamente 8 MiB.");
        using var denied = await client.GetAsync("http://127.0.0.1:28085/invalid/ping");
        Require(denied.StatusCode == System.Net.HttpStatusCode.NotFound,
            "La prueba debe rechazar rutas sin token.");
    }

    private static void TestRelayTrafficCounters()
    {
        var relay = new LENetworkRelay();
        var register = typeof(LENetworkRelay).GetMethod("RegisterRelayLineLE",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Require(register is not null, "El relay debe procesar sus líneas de métricas.");
        register!.Invoke(relay, ["LE_METRIC tx=1000 rx=2000 txp=3 rxp=4", false]);
        var sample = relay.GetTrafficSnapshotLE();
        Require(sample is { TxBytes: 1000, RxBytes: 2000, TxPackets: 3, RxPackets: 4 },
            "El monitor de PC debe leer contadores reales del relay.");
        register.Invoke(relay, ["LE_METRIC tx=-1 rx=2 txp=3 rxp=4", false]);
        Require(ReferenceEquals(sample, relay.GetTrafficSnapshotLE()),
            "El monitor no debe aceptar contadores negativos.");
    }

    private static void TestAndroidInstallPolicyAllowsWifiAdb()
    {
        Type? policy = typeof(NLServiceADB)
            .Assembly
            .GetType("NOVORA.NLAndroidInstallDevicePolicy");

        Require(policy is not null,
            "La política de instalación ADB debe existir.");

        var method = policy!.GetMethod(
            "CanInstallOverAdb",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);

        Require(method is not null,
            "La política de instalación ADB debe exponer CanInstallOverAdb.");

        bool allowed = (bool)(method!.Invoke(
            null,
            new object?[] { true, "192.168.1.24:5555" }) ?? false);

        Require(allowed,
            "Un teléfono ADB conectado por Wi-Fi debe poder instalar la APK.");
    }

    private static async Task TestAndroidInstallerAcceptsWifiAdbAsync()
    {
        const string serial = "192.168.1.24:5555";
        var calls = new List<string>();
        var package = new NLServiceAndroidPackageInfo(
            "unused.apk", "com.linkcontrol.novora", 65, "1.4.65", 26, new string('A', 64));
        var installer = new NLServiceAndroidInstaller((arguments, _) =>
        {
            string command = string.Join(' ', arguments);
            calls.Add(command);
            string response = command switch
            {
                "devices -l" => $"List of devices attached\n{serial} device product:test\n",
                "-s 192.168.1.24:5555 get-serialno" => serial,
                "-s 192.168.1.24:5555 get-state" => "device",
                "-s 192.168.1.24:5555 shell getprop ro.build.version.sdk" => "36",
                "-s 192.168.1.24:5555 shell am get-current-user" => "0",
                "-s 192.168.1.24:5555 shell pm list packages -u --user 0 com.linkcontrol.novora" => "",
                "-s 192.168.1.24:5555 shell dumpsys package com.linkcontrol.novora" =>
                    "Unable to find package: com.linkcontrol.novora",
                _ => throw new InvalidOperationException("Comando ADB inesperado: " + command)
            };
            return Task.FromResult(response);
        });

        NLServiceAndroidInstallState state = await installer.InspectAsync(serial, package);
        Require(state.Action == NLServiceAndroidInstallAction.Install,
            "La instalación ADB Wi-Fi debe quedar disponible cuando el teléfono responde.");
        Require(calls.Contains("-s 192.168.1.24:5555 get-serialno") &&
                !calls.Any(call => call.StartsWith("-d ", StringComparison.Ordinal)),
            "El instalador debe comprobar el dispositivo Wi-Fi seleccionado sin cambiar a USB.");
    }

    private static void TestOwnedInternetSurvivesConnectedLan()
    {
        var method = typeof(NLControlSessionState).GetMethod("ShouldStopOwnedInternet");
        Require(method is not null, "Falta la política de ciclo de vida de la VPN compartida.");

        bool StopFor(string transport, NLControlSessionPhase phase, bool starting = false)
        {
            var session = new NLControlSessionState(1, phase, transport, "", null);
            return (bool)(method!.Invoke(session, [starting]) ?? true);
        }

        Require(!StopFor("LAN", NLControlSessionPhase.Connected),
            "LinkEngine LAN no debe detener la VPN al publicar un estado conectado.");
        Require(!StopFor("USB", NLControlSessionPhase.Connected),
            "LinkEngine USB debe conservar la VPN mientras la sesión siga conectada.");
        Require(!StopFor("LAN", NLControlSessionPhase.Connected, starting: true),
            "El inicio LAN debe conservar la VPN durante la negociación.");
        Require(StopFor("LAN", NLControlSessionPhase.Lost) &&
                StopFor("USB", NLControlSessionPhase.Disconnected) &&
                StopFor("OTHER", NLControlSessionPhase.Connected),
            "La VPN debe detenerse cuando se pierde la sesión o el transporte deja de ser válido.");
    }

    private static void TestVisionObservationWindowLastsTwentyMinutes()
    {
        var window = new VEObservationWindow();
        DateTimeOffset start = new(2026, 10, 8, 18, 0, 0, TimeSpan.Zero);

        VEObservationWindowSnapshot initial = window.Observe(isStreaming: true, start);
        Require(initial.IsActive && !initial.IsCompleted && initial.RemainingSeconds == 20 * 60,
            "La observación VE debe iniciar activa con veinte minutos disponibles.");

        VEObservationWindowSnapshot beforeDeadline = window.Observe(
            isStreaming: true, start.AddMinutes(20).AddSeconds(-1));
        Require(beforeDeadline.IsActive && beforeDeadline.RemainingSeconds == 1,
            "La observación VE debe permanecer activa hasta el último segundo.");

        VEObservationWindowSnapshot completed = window.Observe(isStreaming: true, start.AddMinutes(20));
        Require(!completed.IsActive && completed.IsCompleted && completed.RemainingSeconds == 0,
            "La observación VE debe terminar exactamente a los veinte minutos sin detener el stream.");

        window.Observe(isStreaming: false, start.AddMinutes(21));
        VEObservationWindowSnapshot nextSession = window.Observe(isStreaming: true, start.AddMinutes(22));
        Require(nextSession.IsActive && nextSession.RemainingSeconds == 20 * 60,
            "Una nueva sesión VE debe disponer de una nueva ventana de observación.");
    }

    private static void TestTransportHandshakeRequiresSessionToken()
    {
        string token = new('a', 64);
        byte[] frame = LETransportHandshake.CreateHelloLE("android-test", token);
        string payload = Encoding.UTF8.GetString(frame, 4, frame.Length - 4);

        Require(payload == $"NOVORA-LINK|2|HELLO|android-test|{token}",
            "El HELLO USB v2 debe incluir el token de sesión.");
        Require(LETransportHandshake.TryParseHelloLE(payload, out LETransportHandshakeHello? hello, out _) &&
                hello?.ClientId == "android-test" && hello.Token == token,
            "El HELLO USB v2 válido debe aceptarse con su token.");
        Require(!LETransportHandshake.TryParseHelloLE(
                $"NOVORA-LINK|1|HELLO|android-test|{token}", out _, out _),
            "El protocolo USB v1 sin token debe rechazarse.");
        Require(!LETransportHandshake.TryParseHelloLE(
                "NOVORA-LINK|2|HELLO|android-test|short", out _, out _),
            "Un token USB mal formado debe rechazarse.");
    }

    private static void TestVeLanOfferAcceptsIndependentFileChannel()
    {
        const string token = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        var video = new NLControlVeLanEndpoint(27190, token, "VIDEO");
        var control = new NLControlVeLanEndpoint(27191, token.Replace('0', '1'), "CONTROL");
        var files = new NLControlVeLanEndpoint(27192, token.Replace('0', '2'), "FILES");
        var offer = new NLControlVeLanOffer(
            NLControlProtocol.Version, "0123456789abcdef", "192.168.1.24", token,
            DateTimeOffset.UtcNow.AddSeconds(20).ToUnixTimeSeconds(), video, control, null,
            5_000_000, 1280, 60, false, files);

        offer.Validate(DateTimeOffset.UtcNow);
        Require(offer.Files == files,
            "La oferta VE LAN debe conservar un canal FILES independiente y autenticado.");

        DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
        var permissionOffer = offer with
        {
            ExpiresUnix = issuedAt.AddSeconds(NLControlVeLanOffer.OfferLifetimeSeconds).ToUnixTimeSeconds()
        };
        permissionOffer.Validate(issuedAt.AddSeconds(90));
        permissionOffer.Validate(issuedAt.AddSeconds(-2));
        bool expiredRejected = false;
        try { permissionOffer.Validate(issuedAt.AddSeconds(121)); }
        catch (InvalidDataException) { expiredRejected = true; }
        Require(expiredRejected, "La oferta VE LAN vencida debe rechazarse tras el permiso de captura.");
    }

    private static void TestLanCodeHandoffRejectsInvalidCredentials()
    {
        var handoff = new NLControlLanCodeHandoff("NOVORA PC", "192.168.1.24", 27214,
            new string('A', 64), "012345", DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds());
        handoff.Validate();
        foreach (var invalid in new[]
        {
            handoff with { Code = "12345a" },
            handoff with { Host = "8.8.8.8" },
            handoff with { ExpiresUnix = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds() }
        })
        {
            bool rejected = false;
            try { invalid.Validate(); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, "AppControl sólo debe recibir un código LAN vigente para una PC local.");
        }
    }

    private static async Task TestLanFileSenderKeepsRelativeNovoraPathAsync()
    {
        NLControlFileMessage? begin = null;
        await using var source = new MemoryStream([0x4E, 0x4C]);
        await NLControlFileSender.SendAsync(source, "nota.txt", 2,
            (action, json) =>
            {
                var message = JsonSerializer.Deserialize<NLControlFileMessage>(json);
                if (action == "file.begin") begin = message;
                return Task.FromResult(new NLControlReply(
                    NLControlProtocol.Version, 1, true, "ok"));
            },
            relativePath: "Otros/carpeta/nota.txt");

        Require(begin?.RelativePath == "Otros/carpeta/nota.txt",
            "El emisor LAN debe conservar la ruta relativa dentro de la raíz NOVORA.");
    }

    private static async Task TestLanFileSenderCancelsUnansweredRequestAsync(string root)
    {
        string path = Path.Combine(root, "cancelled.txt");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(path, "NOVORA");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using TcpClient peer = await listener.AcceptTcpClientAsync();
        var (sender, send) = CreateLanFileSender(client.GetStream());
        await using var ownedSender = sender;
        using var cancellation = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<int> transfer = send([path], cancellation.Token);
        try
        {
            NLControlRequest request = await NLControlProtocol.ReadAsync<NLControlRequest>(
                peer.GetStream(), deadline.Token);
            Require(request.Action == "file.begin", "La prueba debe cancelar una solicitud FILES pendiente.");
            cancellation.Cancel();
            bool cancelled = false;
            try { await transfer.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (OperationCanceledException) { cancelled = true; }
            catch (TimeoutException)
            {
                throw new InvalidOperationException("Cancelar FILES LAN debe interrumpir la espera aunque Android no responda.");
            }
            Require(cancelled, "La transferencia FILES LAN debe terminar cancelada.");
            Require(await peer.GetStream().ReadAsync(new byte[1], deadline.Token) == 0,
                "Cancelar un mensaje pendiente debe cerrar FILES para evitar respuestas atrasadas.");
        }
        finally
        {
            peer.Dispose();
            client.Dispose();
            try { await transfer.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { }
        }
    }

    private static async Task TestLanFileSenderDisposalCancelsUnansweredRequestAsync(string root)
    {
        string path = Path.Combine(root, "closed.txt");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(path, "NOVORA");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using TcpClient peer = await listener.AcceptTcpClientAsync();
        var (sender, send) = CreateLanFileSender(client.GetStream());
        await using var ownedSender = sender;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<int> transfer = send([path], CancellationToken.None);
        Task? disposal = null;
        Task<int>? queued = null;
        try
        {
            NLControlRequest request = await NLControlProtocol.ReadAsync<NLControlRequest>(
                peer.GetStream(), deadline.Token);
            Require(request.Action == "file.begin", "La prueba debe cerrar una solicitud FILES pendiente.");
            queued = send([path], CancellationToken.None);
            disposal = sender.DisposeAsync().AsTask();
            bool cancelled = false;
            try { await transfer.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (OperationCanceledException) { cancelled = true; }
            catch (TimeoutException)
            {
                throw new InvalidOperationException("Cerrar FILES LAN debe interrumpir la transferencia antes de liberar sus recursos.");
            }
            Require(cancelled, "Cerrar FILES LAN debe cancelar el envío activo.");
            bool queuedCancelled = false;
            try { await queued.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (OperationCanceledException) { queuedCancelled = true; }
            Require(queuedCancelled, "Cerrar FILES LAN debe cancelar también los envíos en espera.");
            await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            peer.Dispose();
            client.Dispose();
            try { await transfer.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { }
            if (queued is not null)
            {
                try { await queued.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch { }
            }
            if (disposal is not null)
            {
                try { await disposal.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch { }
            }
        }
    }

    private static async Task TestLanFileSenderTimesOutUnansweredRequestAsync(string root)
    {
        string path = Path.Combine(root, "timeout.txt");
        await File.WriteAllTextAsync(path, "NOVORA");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using TcpClient peer = await listener.AcceptTcpClientAsync();
        var (sender, send) = CreateLanFileSender(client.GetStream(), TimeSpan.FromMilliseconds(200));
        await using var ownedSender = sender;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<int> transfer = send([path], CancellationToken.None);
        try
        {
            NLControlRequest request = await NLControlProtocol.ReadAsync<NLControlRequest>(
                peer.GetStream(), deadline.Token);
            Require(request.Action == "file.begin", "La prueba debe esperar una respuesta FILES ausente.");
            bool timedOut = false;
            try { await transfer.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) when (transfer.IsCompleted) { timedOut = true; }
            catch (TimeoutException)
            {
                throw new InvalidOperationException("FILES LAN debe limitar la espera incluso sin cancelación del usuario.");
            }
            Require(timedOut, "Un receptor FILES que no responde debe producir un timeout.");
            Require(await peer.GetStream().ReadAsync(new byte[1], deadline.Token) == 0,
                "El timeout debe cerrar FILES y liberar el receptor de la transferencia incompleta.");
        }
        finally
        {
            peer.Dispose();
            client.Dispose();
            try { await transfer.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { }
        }
    }

    private static async Task TestLanFileSenderTransfersVerifiedFilesAsync(string root)
    {
        string sourceRoot = Path.Combine(root, "files-source");
        string destinationRoot = Path.Combine(root, "files-destination");
        Directory.CreateDirectory(sourceRoot);
        byte[] expected = Enumerable.Range(0, 60017).Select(index => (byte)(index % 256)).ToArray();
        string fullPath = Path.Combine(sourceRoot, "complete.txt");
        string emptyPath = Path.Combine(sourceRoot, "empty.txt");
        await File.WriteAllBytesAsync(fullPath, expected);
        await File.WriteAllBytesAsync(emptyPath, []);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using TcpClient peer = await listener.AcceptTcpClientAsync();
        var (sender, send) = CreateLanFileSender(client.GetStream());
        await using var ownedSender = sender;
        using var receiver = new NLControlFileReceiver(destinationRoot);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        async Task ReceiveAsync()
        {
            int completed = 0;
            while (completed < 2)
            {
                NLControlRequest request = await NLControlProtocol.ReadAsync<NLControlRequest>(
                    peer.GetStream(), deadline.Token);
                string message = await receiver.HandleAsync(request.Action, request.Value, deadline.Token);
                await NLControlProtocol.WriteAsync(peer.GetStream(),
                    new NLControlReply(NLControlProtocol.Version, request.Id, true, message), deadline.Token);
                if (request.Action == "file.end") completed++;
            }
        }
        Task receiving = ReceiveAsync();
        Task<int> sending = send([fullPath, emptyPath], deadline.Token);
        try
        {
            await Task.WhenAll(receiving, sending).WaitAsync(deadline.Token);
            Require(sending.Result == 2, "FILES LAN debe confirmar ambos archivos tras validar sus hashes.");
            byte[] actual = await File.ReadAllBytesAsync(Path.Combine(destinationRoot, "Documentos", "complete.txt"));
            Require(actual.SequenceEqual(expected), "FILES LAN debe conservar todos los bytes entre varios bloques.");
            Require(new FileInfo(Path.Combine(destinationRoot, "Documentos", "empty.txt")).Length == 0,
                "FILES LAN debe poder confirmar un archivo vacío en el mismo canal.");
            Require(!Directory.EnumerateFiles(destinationRoot, "*.partial", SearchOption.AllDirectories).Any(),
                "Una transferencia correcta no debe dejar archivos parciales.");
        }
        finally
        {
            deadline.Cancel();
            peer.Dispose();
            client.Dispose();
            try { await Task.WhenAll(receiving, sending).WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { }
        }
    }

    private static void TestFileProtocolV1FreezeAndPolicy()
    {
        Require(NLControlFileProtocolV1.Version == 1 &&
                NLControlFileProtocolV2.Version == 2 &&
                NLControlFileProtocolV1.MaxFileBytes == 2L * 1024 * 1024 * 1024 &&
                NLControlFileProtocolV1.ChunkBytes == 24 * 1024 &&
                NLControlFileProtocolV1.MaxJsonMessageBytes == 51_000,
            "El contrato FILES v1 debe permanecer congelado y documentado.");
        Require(NLFileProtocolPolicy.LegacyDeprecatedOn == new DateOnly(2026, 10, 23) &&
                NLFileProtocolPolicy.LegacyRemovalOn == new DateOnly(2027, 10, 23) &&
                NLFileProtocolPolicy.MinimumV2PeerVersion == "1.4.118" &&
                NLFileProtocolPolicy.LastLegacyPeerVersion == "1.4.117",
            "La política FILES debe declarar fecha y versión mínima de retirada.");

        var beforeRemoval = new DateTimeOffset(2027, 10, 22, 23, 59, 59, TimeSpan.Zero);
        var atRemoval = new DateTimeOffset(2027, 10, 23, 0, 0, 0, TimeSpan.Zero);
        Require(NLFileProtocolPolicy.AdvertisedVersions(beforeRemoval).SequenceEqual([1, 2]) &&
                NLFileProtocolPolicy.AdvertisedVersions(atRemoval).SequenceEqual([2]),
            "La publicidad de versiones debe retirar v1 exactamente en la fecha límite.");
        Require(NLFileProtocolPolicy.IsSupported(1, beforeRemoval) &&
                !NLFileProtocolPolicy.IsSupported(1, atRemoval) &&
                NLFileProtocolPolicy.IsSupported(2, atRemoval),
            "La política debe rechazar FILES v1 después de la fecha de retirada.");

        var before = NLFileProtocolTelemetry.Snapshot();
        NLFileProtocolTelemetry.RecordLegacyTransfer();
        NLFileProtocolTelemetry.RecordCurrentTransfer();
        NLFileProtocolTelemetry.RecordLegacyFallback("contract-test");
        var after = NLFileProtocolTelemetry.Snapshot();
        Require(after.LegacyTransfers == before.LegacyTransfers + 1 &&
                after.CurrentTransfers == before.CurrentTransfers + 1 &&
                after.LegacyFallbacks == before.LegacyFallbacks + 1 &&
                after.LastLegacyReason == "contract-test" &&
                after.LastLegacyAtUtc is not null,
            "La telemetría debe hacer visible cada uso y fallback de FILES v1.");
    }

    private static async Task TestLegacyFileReceiverRejectsInvalidTransfersAsync(string root)
    {
        string v1Root = Path.Combine(root, "v1-contract");
        Directory.CreateDirectory(v1Root);
        using var receiver = new NLControlFileReceiver(v1Root);
        string id = Guid.NewGuid().ToString("N");
        string Json(NLControlFileMessage message) => JsonSerializer.Serialize(message);

        async Task RequireRejected(Func<Task> action, string message)
        {
            bool rejected = false;
            try { await action(); }
            catch (InvalidDataException) { rejected = true; }
            catch (IOException) { rejected = true; }
            Require(rejected, message);
        }

        await RequireRejected(
            () => receiver.HandleAsync(NLControlFileProtocolV1.BeginAction,
                Json(new(id, "../escape.txt", 1))),
            "FILES v1 debe rechazar rutas de salida.");
        await RequireRejected(
            () => receiver.HandleAsync(NLControlFileProtocolV1.BeginAction,
                Json(new(id, "CON.txt", 1))),
            "FILES v1 debe rechazar nombres reservados de Windows.");
        await RequireRejected(
            () => receiver.HandleAsync(NLControlFileProtocolV1.BeginAction,
                Json(new(id, "large.bin", NLControlFileProtocolV1.MaxFileBytes + 1))),
            "FILES v1 debe rechazar tamaños por encima de 2 GiB.");

        string valid = Guid.NewGuid().ToString("N");
        await receiver.HandleAsync(NLControlFileProtocolV1.BeginAction,
            Json(new(valid, "contract.txt", 3)));
        await RequireRejected(
            () => receiver.HandleAsync(NLControlFileProtocolV1.ChunkAction,
                Json(new(valid, Data: "zz", Offset: 0))),
            "FILES v1 debe rechazar hexadecimal malformado.");
        Require(!Directory.EnumerateFiles(v1Root, "*.partial", SearchOption.AllDirectories).Any(),
            "Un bloque inválido debe limpiar el archivo parcial de FILES v1.");

        string wrongOffset = Guid.NewGuid().ToString("N");
        await receiver.HandleAsync(NLControlFileProtocolV1.BeginAction,
            Json(new(wrongOffset, "contract.txt", 3)));
        await RequireRejected(
            () => receiver.HandleAsync(NLControlFileProtocolV1.ChunkAction,
                Json(new(wrongOffset, Data: "01", Offset: 1))),
            "FILES v1 debe rechazar bloques fuera de secuencia.");
        Require(!Directory.EnumerateFiles(v1Root, "*.partial", SearchOption.AllDirectories).Any(),
            "Un bloque fuera de orden debe limpiar el archivo parcial de FILES v1.");

        string completed = Guid.NewGuid().ToString("N");
        string? savedPath = null;
        long savedLength = 0;
        receiver.Received += (path, length) => { savedPath = path; savedLength = length; };
        await receiver.HandleAsync(NLControlFileProtocolV1.BeginAction,
            Json(new(completed, "contract.txt", 3)));
        await receiver.HandleAsync(NLControlFileProtocolV1.ChunkAction,
            Json(new(completed, Data: "0102", Offset: 0)));
        await receiver.HandleAsync(NLControlFileProtocolV1.ChunkAction,
            Json(new(completed, Data: "03", Offset: 2)));
        await receiver.HandleAsync(NLControlFileProtocolV1.EndAction,
            Json(new(completed, Sha256: Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData([1, 2, 3])))));
        Require(savedPath is not null && savedLength == 3 &&
                File.ReadAllBytes(savedPath).SequenceEqual(new byte[] { 1, 2, 3 }),
            "FILES v1 congelado debe conservar el contrato de bloques y SHA-256.");

        string target = Path.Combine(root, "v1-link-target");
        string link = Path.Combine(root, "v1-link-root");
        Directory.CreateDirectory(target);
        bool linked = false;
        try
        {
            Directory.CreateSymbolicLink(link, target);
            linked = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Directory symlinks may require Developer Mode; the name contract
            // above still covers traversal and reserved names.
        }
        if (linked)
        {
            try
            {
                using var linkedReceiver = new NLControlFileReceiver(link);
                await RequireRejected(
                    () => linkedReceiver.HandleAsync(NLControlFileProtocolV1.BeginAction,
                        Json(new(Guid.NewGuid().ToString("N"), "link.txt", 1))),
                    "FILES v1 debe rechazar una raíz que sea enlace.");
            }
            finally
            {
                try { Directory.Delete(link, false); } catch { }
            }
        }
    }

    private static async Task TestLegacyFileReceiverFuzzAsync(string root)
    {
        string fuzzRoot = Path.Combine(root, "v1-fuzz");
        Directory.CreateDirectory(fuzzRoot);
        using var receiver = new NLControlFileReceiver(fuzzRoot);
        var random = new Random(0x5EED);
        string Json(NLControlFileMessage message) => JsonSerializer.Serialize(message);

        for (int iteration = 0; iteration < 300; iteration++)
        {
            string id = Guid.NewGuid().ToString("N");
            long length = random.Next(0, 4096);
            try
            {
                await receiver.HandleAsync(NLControlFileProtocolV1.BeginAction,
                    Json(new(id, "fuzz.bin", length)));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or
                InvalidOperationException or JsonException)
            {
                continue;
            }

            int operations = random.Next(1, 8);
            for (int operation = 0; operation < operations; operation++)
            {
                byte[] bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(
                    random.Next(0, 64));
                long offset = random.Next(-8, 4096);
                try
                {
                    await receiver.HandleAsync(NLControlFileProtocolV1.ChunkAction,
                        Json(new(id, Data: Convert.ToHexString(bytes), Offset: offset)));
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or
                    InvalidOperationException or JsonException)
                {
                    // Fuzz inputs are expected to be rejected without crashing
                    // or leaving an active transfer behind.
                }
            }

            try
            {
                await receiver.HandleAsync(NLControlFileProtocolV1.CancelAction,
                    Json(new(id)));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or
                InvalidOperationException or JsonException)
            {
            }
        }

        Require(!Directory.EnumerateFiles(fuzzRoot, "*.partial", SearchOption.AllDirectories).Any(),
            "La fuzz de FILES v1 no debe dejar archivos parciales.");
    }

    private static async Task TestVePacketBudgetAndPoolingAsync()
    {
        int limit = VEProtocolConstants.NegotiateMaxPacketLengthVE(
            1920, 1080, 20_000_000, 60);
        Require(limit > 0 &&
                limit <= VEProtocolConstants.MaxPacketLengthVE &&
                limit <= VEProtocolConstants.MaxPacketLengthForResolutionVE(1920, 1080) &&
                limit % VEProtocolConstants.PacketLengthUnitVE == 0,
            "El presupuesto de paquete VE debe ser finito, alineado y dependiente de la sesión.");

        var pool = new TrackingArrayPool();
        using var wire = new MemoryStream();
        var writer = new VEProtocolWriter(wire);
        var session = new VEProtocolSession(1920, 1080, false, 0, limit);
        await writer.WriteVideoSessionAsync(VEProtocolCodec.H264, session);
        byte[] payload = Enumerable.Range(0, 64 * 1024)
            .Select(index => (byte)index).ToArray();
        await writer.WriteVideoPacketAsync(payload, 123, false, true);
        wire.Position = 0;

        var demuxer = new VEVideoDemuxer(wire, pool);
        VEProtocolSession readSession = await demuxer.OpenAsync();
        Require(readSession.MaxPacketLength == limit,
            "El receptor debe leer el límite negociado del session header.");
        VEVideoPacket? packet = await demuxer.ReadPacketAsync();
        Require(packet is not null &&
                packet.LengthVE == payload.Length &&
                packet.PayloadVE.Span.SequenceEqual(payload),
            "El payload pooled debe exponer exactamente los bytes válidos.");
        Require(pool.Outstanding == 1,
            "El paquete de video debe mantener un lease pooled mientras está en uso.");
        packet!.Dispose();
        Require(pool.Outstanding == 0,
            "Dispose del paquete debe devolver el buffer al pool exactamente una vez.");

        var truncated = new VEProtocolReader(
            new MemoryStream(new byte[4]), pool);
        bool shortReadRejected = false;
        try { await truncated.ReadPayloadPooledAsync(8); }
        catch (EndOfStreamException) { shortReadRejected = true; }
        Require(shortReadRejected && pool.Outstanding == 0,
            "Un stream truncado debe devolver el buffer pooled antes de propagar el error.");
    }

    private static async Task TestVeRejectsOversizedPacketsAsync()
    {
        int sessionLimit = VEProtocolConstants.RoundUpToPacketUnitVE(2 * 1024 * 1024);
        using (var writerWire = new MemoryStream())
        {
            var writer = new VEProtocolWriter(writerWire);
            await writer.WriteVideoSessionAsync(
                VEProtocolCodec.H264,
                new VEProtocolSession(1280, 720, false, 0, sessionLimit));
            bool writerRejected = false;
            try
            {
                await writer.WriteVideoPacketAsync(
                    new byte[sessionLimit + 1], 0, false, true);
            }
            catch (ArgumentException) { writerRejected = true; }
            Require(writerRejected,
                "El emisor VE debe rechazar un frame por encima del límite de sesión.");
        }

        using var wire = new MemoryStream();
        var protocol = new VEProtocolWriter(wire);
        await protocol.WriteVideoSessionAsync(
            VEProtocolCodec.H264,
            new VEProtocolSession(1280, 720, false, 0, sessionLimit));
        byte[] oversizedHeader = new byte[VEProtocolConstants.PacketHeaderSizeVE];
        BinaryPrimitives.WriteUInt32BigEndian(
            oversizedHeader.AsSpan(8, 4),
            checked((uint)(sessionLimit + 1)));
        await wire.WriteAsync(oversizedHeader);
        wire.Position = 0;
        var demuxer = new VEVideoDemuxer(wire);
        await demuxer.OpenAsync();
        bool readerRejected = false;
        try { await demuxer.ReadPacketAsync(); }
        catch (InvalidDataException) { readerRejected = true; }
        Require(readerRejected,
            "El receptor VE debe rechazar un frame por encima del límite de sesión.");

        using var legacyWire = new MemoryStream();
        byte[] codecId = new byte[VEProtocolConstants.CodecIdSizeVE];
        BinaryPrimitives.WriteUInt32BigEndian(codecId, (uint)VEProtocolCodec.H264);
        byte[] legacyHeader = new byte[VEProtocolConstants.PacketHeaderSizeVE];
        legacyHeader[0] = 0x80;
        BinaryPrimitives.WriteUInt32BigEndian(legacyHeader.AsSpan(4, 4), 1280);
        BinaryPrimitives.WriteUInt32BigEndian(legacyHeader.AsSpan(8, 4), 720);
        await legacyWire.WriteAsync(codecId);
        await legacyWire.WriteAsync(legacyHeader);
        legacyWire.Position = 0;
        var legacyDemuxer = new VEVideoDemuxer(legacyWire);
        VEProtocolSession legacySession = await legacyDemuxer.OpenAsync();
        Require(legacySession.MaxPacketLength ==
                VEProtocolConstants.MaxPacketLengthForResolutionVE(1280, 720),
            "Un emisor v1 sin límite debe quedar acotado por la resolución.");

        using var invalidWire = new MemoryStream();
        byte[] invalidHeader = new byte[VEProtocolConstants.PacketHeaderSizeVE];
        invalidHeader[0] = 0x80;
        invalidHeader[2] = (byte)(VEProtocolConstants.MaxPacketLengthUnitsVE + 1);
        BinaryPrimitives.WriteUInt32BigEndian(invalidHeader.AsSpan(4, 4), 1280);
        BinaryPrimitives.WriteUInt32BigEndian(invalidHeader.AsSpan(8, 4), 720);
        await invalidWire.WriteAsync(codecId);
        await invalidWire.WriteAsync(invalidHeader);
        invalidWire.Position = 0;
        bool invalidSessionRejected = false;
        try { await new VEVideoDemuxer(invalidWire).OpenAsync(); }
        catch (InvalidDataException) { invalidSessionRejected = true; }
        Require(invalidSessionRejected,
            "El receptor VE debe rechazar límites de sesión fuera del rango de 16 MiB.");
    }

    private static async Task TestVePooledPayloadStressAsync()
    {
        const int frames = 1200;
        const int frameBytes = 16 * 1024;
        int limit = VEProtocolConstants.NegotiateMaxPacketLengthVE(
            1280, 720, 8_000_000, 60);
        var pool = new TrackingArrayPool();
        using var wire = new MemoryStream();
        var writer = new VEProtocolWriter(wire);
        await writer.WriteVideoSessionAsync(
            VEProtocolCodec.H264,
            new VEProtocolSession(1280, 720, false, 0, limit));
        byte[] payload = Enumerable.Range(0, frameBytes)
            .Select(index => (byte)(index % 251)).ToArray();
        for (int index = 0; index < frames; index++)
            await writer.WriteVideoPacketAsync(payload, index, false, index == 0);
        wire.Position = 0;

        var demuxer = new VEVideoDemuxer(wire, pool);
        await demuxer.OpenAsync();
        for (int index = 0; index < frames; index++)
        {
            VEVideoPacket? packet = await demuxer.ReadPacketAsync();
            Require(packet is not null && packet.LengthVE == frameBytes,
                "La prueba de carga debe recibir cada frame pooled completo.");
            packet!.Dispose();
        }
        Require(pool.Rents == frames && pool.Returns == frames &&
                pool.Outstanding == 0,
            "La carga de frames no debe dejar buffers pooled sin devolver.");
    }

    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        private int _rents;
        private int _returns;
        private int _outstanding;

        public int Rents => Volatile.Read(ref _rents);
        public int Returns => Volatile.Read(ref _returns);
        public int Outstanding => Volatile.Read(ref _outstanding);

        public override byte[] Rent(int minimumLength)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(minimumLength);
            Interlocked.Increment(ref _rents);
            Interlocked.Increment(ref _outstanding);
            return buffer;
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            Interlocked.Increment(ref _returns);
            Interlocked.Decrement(ref _outstanding);
            ArrayPool<byte>.Shared.Return(array, clearArray);
        }
    }

    private static (IAsyncDisposable Sender, Func<IReadOnlyList<string>, CancellationToken, Task<int>> Send)
        CreateLanFileSender(Stream stream, TimeSpan? requestTimeout = null)
    {
        Type type = typeof(NLControlFileSender).Assembly.GetType(
            "NOVORA.VisionEngine.Exchange.VEExchangeLanFileSender")
            ?? throw new InvalidOperationException("El emisor FILES LAN debe existir.");
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var constructor = type.GetConstructor(flags, null,
            requestTimeout is null ? [typeof(Stream)] : [typeof(Stream), typeof(TimeSpan)], null)
            ?? throw new InvalidOperationException("El emisor FILES LAN debe aceptar el stream autenticado.");
        object instance = constructor.GetParameters().Length == 1
            ? constructor.Invoke([stream]) : constructor.Invoke([stream, requestTimeout!.Value]);
        var method = type.GetMethod("SendPathsAsync", flags)
            ?? throw new InvalidOperationException("El emisor FILES LAN debe poder enviar archivos.");
        return ((IAsyncDisposable)instance,
            method.CreateDelegate<Func<IReadOnlyList<string>, CancellationToken, Task<int>>>(instance));
    }

    private static async Task TestRelayRecoveryStopsWhenLastSessionEndsAsync(
        string root)
    {
        var relay = new CoordinatedRelay();
        var data = new SuccessfulTransportData();
        var manager = new LENetworkManager(
            new NLServiceADB(new NLServiceNovoraPaths(root)),
            new LEMetricsCollector(),
            relay,
            data);

        try
        {
            await manager.InitializeAsync();
            LECoreResult started = await manager.StartAsync(
                "serial-002",
                new string('a', 64));
            Require(started.Success, "La sesión DATA simulada debe iniciar.");

            relay.RaiseUnexpectedExit();
            await relay.RecoveryEnsureEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            await manager.StopAsync("serial-002");
            relay.AllowRecoveryEnsure.TrySetResult();

            Task completed = await Task.WhenAny(
                relay.SecondStop.Task,
                Task.Delay(TimeSpan.FromSeconds(2)));

            Require(ReferenceEquals(completed, relay.SecondStop.Task),
                "El relay reiniciado debe detenerse si ya no quedan sesiones DATA.");
        }
        finally
        {
            await manager.DisposeAsync();
        }
    }

    private static async Task<string> TestFailedVerificationCleansReverseAsync(
        string root)
    {
        string tools = Path.Combine(root, "Tools");
        string log = Path.Combine(root, "adb.log");

        Directory.CreateDirectory(tools);
        CopyTestHost(tools);
        File.WriteAllText(Path.Combine(tools, "AdbWinApi.dll"), string.Empty);
        File.WriteAllText(Path.Combine(tools, "AdbWinUsbApi.dll"), string.Empty);
        Environment.SetEnvironmentVariable(FakeAdbLogVariable, log);

        var data = new LETransportData(
            new NLServiceADB(new NLServiceNovoraPaths(root)));

        try
        {
            var result = await data.StartAsync("serial-001");

            Require(!result.Success,
                "La verificación simulada debe fallar.");

            string[] commands = File.ReadAllLines(log);
            Require(commands.Contains("-s|serial-001|reverse|--remove|tcp:27184"),
                "El reverse DATA debe eliminarse cuando su verificación falla.");

            return log;
        }
        finally
        {
            await data.DisposeAsync();
            Environment.SetEnvironmentVariable(FakeAdbLogVariable, null);
        }
    }

    private static bool IsFakeAdbProcess()
        => string.Equals(
            Path.GetFileName(Environment.ProcessPath),
            "adb.exe",
            StringComparison.OrdinalIgnoreCase);

    private static void RunFakeAdb()
    {
        string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        string? log = Environment.GetEnvironmentVariable(FakeAdbLogVariable);

        if (!string.IsNullOrWhiteSpace(log))
        {
            File.AppendAllLines(log, new[] { string.Join('|', args) });
        }

        if (args.Contains("--list", StringComparer.OrdinalIgnoreCase))
        {
            // Sin mapeo: la prueba debe comprobar el rollback posterior.
            return;
        }
    }

    private static void CopyTestHost(string destination)
    {
        foreach (string source in Directory.EnumerateFiles(AppContext.BaseDirectory))
        {
            string name = Path.GetFileName(source);
            string target = Path.Combine(destination, name);
            File.Copy(source, target, overwrite: true);
        }

        File.Copy(
            Path.ChangeExtension(
                System.Reflection.Assembly
                    .GetExecutingAssembly()
                    .Location,
                ".exe"),
            Path.Combine(destination, "adb.exe"),
            overwrite: true);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SuccessfulTransportData : ILETransportData
    {
        public Task<LECoreResult> StartAsync(
            string serial,
            CancellationToken cancellationToken = default)
            => Task.FromResult(LECoreResult.Ok("DATA ready"));

        public Task EnsureAsync(
            string serial,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(
            string serial,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CoordinatedRelay : ILENetworkRelay
    {
        private int _stopCalls;

        public event EventHandler? ExitedLE;

        public bool IsRunningLE { get; private set; }

        public TaskCompletionSource RecoveryEnsureEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowRecoveryEnsure { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondStop { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(string authenticationToken, CancellationToken cancellationToken = default)
        {
            IsRunningLE = true;
            return Task.CompletedTask;
        }

        public async Task EnsureRunningAsync(
            string authenticationToken,
            CancellationToken cancellationToken = default)
        {
            if (IsRunningLE)
            {
                return;
            }

            RecoveryEnsureEntered.TrySetResult();
            await AllowRecoveryEnsure.Task.WaitAsync(cancellationToken);
            IsRunningLE = true;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunningLE = false;

            if (Interlocked.Increment(ref _stopCalls) >= 2)
            {
                SecondStop.TrySetResult();
            }

            return Task.CompletedTask;
        }

        public void RaiseUnexpectedExit()
        {
            IsRunningLE = false;
            ExitedLE?.Invoke(this, EventArgs.Empty);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
