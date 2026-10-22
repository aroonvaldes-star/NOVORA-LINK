using System.Diagnostics;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Metrics;
using NOVORA.LinkEngine.Network;
using NOVORA.LinkEngine.Transport;
using NOVORA.Service;
using NOVORA.Control;
using NOVORA.VisionEngine.Metrics;
using System.Text.Json;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

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
            TestAndroidInstallPolicyAllowsWifiAdb();
            await TestAndroidInstallerAcceptsWifiAdbAsync();
            TestOwnedInternetSurvivesConnectedLan();
            TestVisionObservationWindowLastsTwentyMinutes();
            TestVeLanOfferAcceptsIndependentFileChannel();
            TestLanCodeHandoffRejectsInvalidCredentials();
            await TestLanDiscoveryReturnsPromptlyAsync();
            await TestMalformedDataTokenDoesNotStopGatewayAsync(root);
            await TestLanFileSenderKeepsRelativeNovoraPathAsync();
            await TestLanFileSenderCancelsUnansweredRequestAsync(root);
            await TestLanFileSenderDisposalCancelsUnansweredRequestAsync(root);
            await TestLanFileSenderTimesOutUnansweredRequestAsync(root);
            await TestLanFileSenderTransfersVerifiedFilesAsync(root);
            string adbLog = await TestFailedVerificationCleansReverseAsync(root);
            await TestRelayRecoveryStopsWhenLastSessionEndsAsync(root);
            await TestSpeedTestServerAsync();
            TestRelayTrafficCounters();
            Console.WriteLine($"PASS: {adbLog}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex.Message}");
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

    private static async Task TestMalformedDataTokenDoesNotStopGatewayAsync(string root)
    {
        using var store = new NLControlTrustStore(Path.Combine(root, "gateway"));
        using var relay = new TcpListener(IPAddress.Loopback, 0);
        relay.Start();
        await using var gateway = new NLControlLanDataGateway(IPAddress.Loopback, store,
            ((IPEndPoint)relay.LocalEndpoint).Port, allowLoopback: true);
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
        await connected.Task.WaitAsync(acceptTimeout.Token);
        Require(gateway.IsConnected, "DATA LAN debe aceptar una conexión válida tras rechazar la inválida.");
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
            "unused.apk", "com.novora.appcontrol", 65, "1.4.65", 26, new string('A', 64));
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
                "-s 192.168.1.24:5555 shell pm list packages -u --user 0 com.novora.appcontrol" => "",
                "-s 192.168.1.24:5555 shell dumpsys package com.novora.appcontrol" =>
                    "Unable to find package: com.novora.appcontrol",
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
            LECoreResult started = await manager.StartAsync("serial-002");
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

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsRunningLE = true;
            return Task.CompletedTask;
        }

        public async Task EnsureRunningAsync(
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
