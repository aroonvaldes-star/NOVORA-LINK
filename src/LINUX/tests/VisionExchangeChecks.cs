using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Threading;
using NOVORA.Linux;
using NOVORA.Linux.Control;
using NOVORA.Linux.VisionEngine;

/// <summary>
/// VisionEngine Exchange parity with Windows: clipboard over the AppControl control channel,
/// Ctrl+C/Ctrl+V and file drops injected into the real SDL video window, PNG capture of the
/// presented frame, live VSync, NOVORA folder import, transfer summary and the Android media/audio
/// control actions. Run: dotnet run -- --ve-exchange-native
/// </summary>
internal static class VisionExchangeChecks
{
    private static int _pass, _fail;

    private static void Check(bool ok, string name)
    {
        if (ok) { _pass++; Console.WriteLine("PASS " + name); }
        else { _fail++; Console.WriteLine("FAIL " + name); }
    }

    public static void RunNative()
    {
        _pass = _fail = 0;
        AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
        using var mainLoop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task work = Task.Run(async () =>
        {
            try
            {
                Units();
                await NovoraFolderAsync();
                await LiveRuntimeAsync();
                await ControlActionsAsync();
            }
            finally { mainLoop.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(mainLoop.Token);
        work.GetAwaiter().GetResult();
        Console.WriteLine($"VisionEngine Exchange Linux: {_pass} PASS / {_fail} FAIL");
        if (_fail != 0) throw new InvalidOperationException($"VisionEngine Exchange checks failed: {_fail}.");
    }

    /// <summary>Window wiring without a phone: Integración card, RECIBIR CARPETA NOVORA, summary, VE live text. Run: -- --ve-exchange-ui</summary>
    public static void RunUi()
    {
        _pass = _fail = 0;
        string root = Path.Combine(Path.GetTempPath(), "novora-ve-exchange-ui-" + Guid.NewGuid().ToString("N"));
        string? previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string? previousData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(root, "config"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(root, "data"));
        NativeEnvironment.Set("SDL_VIDEO_DRIVER", "dummy");
        try
        {
            AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
            var window = new MainWindow { Width = 1180, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
            object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
            string Message() => ((Avalonia.Controls.TextBlock)Field("_message")!).Text ?? "";
            IEnumerable<Avalonia.Controls.Control> All(Avalonia.Controls.Control control) =>
                Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(control).OfType<Avalonia.Controls.Control>();

            Call("ShowPage", "Integración");
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var page = ((Dictionary<string, Avalonia.Controls.Control>)Field("_pages")!)["Integración"];
            Check(All(page).OfType<Avalonia.Controls.TextBlock>().Any(t => t.Text == "Android ↔ Linux con Ctrl+C / Ctrl+V en la ventana de VisionEngine, sin historial sensible."),
                "Clipboard card describes the real ExchangeVE behaviour");
            var receive = All(page).OfType<Avalonia.Controls.Button>().First(b => b.Content as string == "RECIBIR CARPETA NOVORA");
            Check(receive.IsEnabled, "RECIBIR CARPETA NOVORA is enabled");
            Check(All(page).Any(c => Avalonia.Input.DragDrop.GetAllowDrop(c) &&
                    All(c).OfType<Avalonia.Controls.TextBlock>().Any(t => t.Text == "Archivos / Share")),
                "the Archivos / Share card accepts drops");
            receive.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check(Message() == "Conecta y autoriza un teléfono por USB antes de recibir archivos.", "receiving without a phone explains what is missing");

            var summary = new LinuxVisionTransferSummary();
            typeof(MainWindow).GetField("_veTransfers", flags)!.SetValue(window, summary);
            summary.RecordReceived(2, 3 * 1024 * 1024, "/home/u/NOVORA-Files/Videos/clip.mp4");
            Call("UpdateIntegrationSummaryVE");
            Check(((Avalonia.Controls.TextBlock)Field("_integrationReceivedSummary")!).Text == $"2 archivos · {3.0:0.0} MB" &&
                  ((Avalonia.Controls.TextBlock)Field("_integrationReceivedNames")!).Text == "clip.mp4" &&
                  ((Avalonia.Controls.TextBlock)Field("_integrationSentNames")!).Text == "Aún no hay archivos enviados.",
                "Integración shows this session's transfer summary");

            Call("OnVisionClipboardShortcutVE", false);
            Dispatcher.UIThread.RunJobs();
            Check(Message() == "ExchangeVE requiere VisionEngine activo.", "Ctrl+V without VE reports the Windows message");
            Call("OnVisionFilesDroppedVE", (object)new[] { "/tmp/a.txt" });
<<<<<<< Updated upstream
            Check(Message() == "VisionEngine debe estar activo para transferir archivos.", "dropping without VE reports the Windows message");
=======
            Check(Message() == "VisionEngine debe estar activo para transferir archivos; el archivo no se envió.", "dropping without VE reports the Windows message");
>>>>>>> Stashed changes

            Call("ShowPage", "VisionEngine");
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Call("RefreshFunctionalVisionState");
            var vision = ((Dictionary<string, Avalonia.Controls.Control>)Field("_pages")!)["VisionEngine"];
            Check(Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(vision).OfType<Avalonia.Controls.TextBlock>().Any(t => t.Text == "Ajusta la presentación al refresco de la pantalla. Puede cambiarse durante la sesión."),
                "Presentación dinámica is described as live, like Windows");
<<<<<<< Updated upstream
            Check(((Avalonia.Controls.TextBlock)Field("_visionMetrics")!).Text == "La pantalla aún no se está mostrando.", "idle live measurements use the Windows text");
            string live = MainWindow.VisionLiveTelemetryFormatVE(new LinuxVisionTelemetry("opengl", 120, 118, 2, 1280, 720, "FFmpeg h264", true, "Control remoto activo."), 59.94, 8.25);
            Check(live.StartsWith($"Monitor VE: salida 1280 × 720 · {59.94:F1} FPS · {8.25:F1} Mbps · paquetes 120, renderizados 118, sin presentar aprox. 2.", StringComparison.Ordinal),
                "live measurements report FPS and Mbps");
=======
>>>>>>> Stashed changes

            string output = Path.Combine(Path.GetTempPath(), "novora-ve-exchange-ui");
            Directory.CreateDirectory(output);
            foreach (string theme in new[] { LinuxTheme.Light, LinuxTheme.Dark })
            {
                LinuxTheme.Apply(theme);
                Call("ShowPage", "Integración");
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1180, 800));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(output, $"{theme}-Integracion.png"));
            }
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine("Capturas: " + output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousConfig);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previousData);
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine($"VisionEngine Exchange UI: {_pass} PASS / {_fail} FAIL");
        if (_fail != 0) throw new InvalidOperationException($"VisionEngine Exchange UI checks failed: {_fail}.");
    }

    private static void Units()
    {
        Check(LinuxSdlVideoRendererShortcut(6, 0x0040, out bool copy) && copy, "Ctrl+C (left Ctrl) is an ExchangeVE copy");
        Check(LinuxSdlVideoRendererShortcut(25, 0x0080, out copy) && !copy, "Ctrl+V (right Ctrl) is an ExchangeVE paste");
        Check(!LinuxSdlVideoRendererShortcut(6, 0x0040 | 0x0100, out _), "Ctrl+Alt+C stays a normal key for Android");
        Check(!LinuxSdlVideoRendererShortcut(6, 0, out _) && !LinuxSdlVideoRendererShortcut(7, 0x0040, out _), "plain C and Ctrl+D are not intercepted");

        Check(MainWindow.ParseUriListVE("file:///tmp/a%20b.txt\r\n# comment\nfile:///home/x/dir\n", false) is ["/tmp/a b.txt", "/home/x/dir"],
            "text/uri-list decodes local file URIs");
        Check(MainWindow.ParseUriListVE("copy\nfile:///tmp/a.png", false) is ["/tmp/a.png"], "GNOME copied-files list is accepted");
        Check(MainWindow.ParseUriListVE("hola\nfile:///tmp/a.png", true).Count == 0, "ordinary text is never treated as files");
        Check(MainWindow.ParseUriListVE("https://example.org/a", true).Count == 0, "remote URIs are not local files");
        Check(MainWindow.ClipboardDataPathsVE(Encoding.UTF8.GetBytes("file:///tmp/z.pdf\n")) is ["/tmp/z.pdf"], "uri-list bytes are decoded");

        Check(LinuxVisionTransferSummary.Format(1, 500) == "1 archivo · 500 B" &&
              LinuxVisionTransferSummary.Format(3, 2048) == $"3 archivos · {2.0:0.0} KB", "summary uses the Windows format");
        string dir = Path.Combine(Path.GetTempPath(), "novora-ve-summary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(dir, "sub", "b.bin"), new byte[24]);
        try
        {
            var summary = new LinuxVisionTransferSummary();
            int changes = 0; summary.Changed += () => changes++;
            summary.RecordSent(dir);
            summary.RecordReceived(1, 10, "/x/Imagenes/foto.png");
            summary.RecordSent(Path.Combine(dir, "missing"));
            Check(summary.SentText == $"2 archivos · {1024 / 1024d:0.0} KB" && summary.SentNames == Path.GetFileName(dir) &&
                  summary.ReceivedText == "1 archivo · 10 B" && summary.ReceivedNames == "foto.png" && changes == 2,
                "folders count every file; missing paths are ignored");
        }
        finally { Directory.Delete(dir, true); }
    }

    private static bool LinuxSdlVideoRendererShortcut(int scancode, ushort modifiers, out bool copy) =>
        LinuxSdlVideoRenderer.IsClipboardShortcut(scancode, modifiers, out copy);

    private static async Task NovoraFolderAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "novora-ve-folder-" + Guid.NewGuid().ToString("N"));
        var calls = new List<string>();
        var original = LinuxNovoraFolder.RunAdb;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Imagenes"));
            File.WriteAllText(Path.Combine(root, "Imagenes", "a.png"), "old");
            LinuxNovoraFolder.RunAdb = (serial, args, _) =>
            {
                calls.Add(serial + " " + string.Join(' ', args));
                if (args[0] == "pull")
                {
                    string target = Path.Combine(args[2], "NOVORA");
                    Directory.CreateDirectory(Path.Combine(target, "Imagenes"));
                    Directory.CreateDirectory(Path.Combine(target, "Mi carpeta"));
                    File.WriteAllText(Path.Combine(target, "Imagenes", "a.png"), "new");
                    File.WriteAllText(Path.Combine(target, "b.pdf"), "pdf");
                    File.WriteAllText(Path.Combine(target, "Mi carpeta", "c.txt"), "c");
                }
                return Task.FromResult((0, "3 files pulled"));
            };
            var result = await LinuxNovoraFolder.PullAsync("SER1", root, CancellationToken.None);
            Check(calls is ["SER1 shell mkdir -p /sdcard/NOVORA", var pull] && pull.StartsWith("SER1 pull /sdcard/NOVORA " + root, StringComparison.Ordinal),
                "pulls /sdcard/NOVORA from the authorized serial (Windows path)");
            Check(result.Success && result.Files == 3 && result.Bytes == 7, "result reports files and bytes");
            Check(File.ReadAllText(Path.Combine(root, "Imagenes", "a.png")) == "old" &&
                  File.ReadAllText(Path.Combine(root, "Imagenes", "a (1).png")) == "new" &&
                  File.Exists(Path.Combine(root, "Documentos", "b.pdf")),
                "files are organized by type without overwriting");
            Check(Directory.EnumerateDirectories(Path.Combine(root, "Otros"), "Mi carpeta-*").Any(d => File.Exists(Path.Combine(d, "c.txt"))),
                "user folders stay intact under Otros");
            Check(!Directory.EnumerateDirectories(root, ".novora-recepcion-*").Any(), "staging folder is removed");

            LinuxNovoraFolder.RunAdb = (_, args, _) => Task.FromResult(args[0] == "pull" ? (1, "adb: error: remote object does not exist") : (0, ""));
            var failed = await LinuxNovoraFolder.PullAsync("SER1", root, CancellationToken.None);
            Check(!failed.Success && failed.Message.Contains("remote object does not exist", StringComparison.Ordinal), "adb failures are reported");
            LinuxNovoraFolder.RunAdb = (_, _, _) => Task.FromResult((0, ""));
            var empty = await LinuxNovoraFolder.PullAsync("SER1", root, CancellationToken.None);
            Check(!empty.Success && empty.Message == "ADB terminó pero no aparecieron archivos locales.", "empty pull uses the Windows message");
        }
        finally
        {
            LinuxNovoraFolder.RunAdb = original;
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static async Task LiveRuntimeAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "novora-ve-exchange-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try
        {
            byte[] sample = await SampleAsync(stop.Token);
            using var video = await Pair.CreateAsync(stop.Token);
            using var control = await Pair.CreateAsync(stop.Token);
            await using var runtime = new LinuxVisionRuntime(LinuxVisionSettings.Default with
                { Acceleration = "Software", VSync = false, RemoteInput = true, AudioEnabled = false, RecordingDirectory = directory }, 23);
            var shortcuts = new List<bool>(); var drops = new List<string[]>(); var clipboard = new List<string>();
            runtime.ClipboardShortcut += shortcuts.Add;
            runtime.FilesDropped += drops.Add;
            runtime.ClipboardReceived += text => { lock (clipboard) clipboard.Add(text); };
            Check(Throws<InvalidOperationException>(() => runtime.RequestClipboard()), "clipboard needs an active VE stream");
            Task session = runtime.RunAsync(video.Server, control.Server, null, null, stop.Token);
            Task sender = Task.Run(async () =>
            {
                try
                {
                    byte[] header = new byte[16]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)VideoCodec.H264); header[4] = 0x80;
                    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 64); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), 32);
                    await video.Client.WriteAsync(header, stop.Token);
                    for (int i = 0; i < 400 && !stop.IsCancellationRequested; i++)
                    {
                        byte[] packet = new byte[12 + sample.Length]; BinaryPrimitives.WriteUInt64BigEndian(packet, (1UL << 61) | ((ulong)i * 40_000UL));
                        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), (uint)sample.Length); sample.CopyTo(packet, 12);
                        await video.Client.WriteAsync(packet, stop.Token); await Task.Delay(40, stop.Token);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            });
            while (runtime.Telemetry.FramesRendered == 0) { if (session.IsCompleted) await session; await Task.Delay(20, stop.Token); }
            Check(runtime.Telemetry.VideoBytesReceived > 0, "telemetry counts received video bytes for Mbps");

            // PNG capture of the presented frame (Windows VEMediaRecorder.CaptureAsync).
            string png = await runtime.CaptureAsync(stop.Token);
            var (width, height, pixel) = ReadPng(png);
            Check(Path.GetDirectoryName(png) == directory && Path.GetFileName(png).StartsWith("NOVORA-captura-", StringComparison.Ordinal),
                "capture is saved in the recordings folder");
            Check(width == 64 && height == 32 && pixel.R > 200 && pixel.G < 70 && pixel.B < 70, "capture is a valid PNG of the decoded red frame");
            string second = await runtime.CaptureAsync(stop.Token);
            Check(second != png && File.Exists(png) && File.Exists(second), "consecutive captures never overwrite");

            // Android clipboard → Linux (Type 8 → Type 1).
            runtime.RequestClipboard();
            JsonElement read = await ControlProtocol.ReadAsync<JsonElement>(control.Client, stop.Token);
            Check(read.GetProperty("Type").GetInt32() == 8 && read.GetProperty("CopyKey").GetInt32() == 1, "Ctrl+C asks Android to copy and return its clipboard");
            await ControlProtocol.WriteAsync(control.Client, new { Type = 1, Text = "texto Android ñ" }, stop.Token);
            Check(await Until(() => { lock (clipboard) return clipboard is ["texto Android ñ"]; }), "Android clipboard text reaches Linux");

            // Linux clipboard → Android with paste and ack (Type 9 → Type 2).
            Task paste = runtime.SetClipboardAsync("hola desde Linux", paste: true, stop.Token);
            JsonElement set = await ControlProtocol.ReadAsync<JsonElement>(control.Client, stop.Token);
            ulong sequence = set.GetProperty("Sequence").GetUInt64();
            Check(set.GetProperty("Type").GetInt32() == 9 && set.GetProperty("Text").GetString() == "hola desde Linux" &&
                  set.GetProperty("Paste").GetBoolean(), "Ctrl+V sets Android's clipboard and pastes");
            await ControlProtocol.WriteAsync(control.Client, new { Type = 2, Sequence = sequence + 100 }, stop.Token);
            await Task.Delay(150, stop.Token);
            Check(!paste.IsCompleted, "an ack for another sequence does not complete the paste");
            await ControlProtocol.WriteAsync(control.Client, new { Type = 2, Sequence = sequence }, stop.Token);
            await paste.WaitAsync(TimeSpan.FromSeconds(3), stop.Token);
            Check(paste.IsCompletedSuccessfully, "the matching Android ack completes the paste");
            await Check(ThrowsAsync<InvalidDataException>(() => runtime.SetClipboardAsync(new string('x', 70_000), true, stop.Token)),
                "oversized clipboard text is refused before it reaches the 64 KiB control frame");

            // SDL events on the real video window: Ctrl+C is ExchangeVE's, never a key for Android; drops arrive once.
            uint window = await Dispatcher.UIThread.InvokeAsync(SdlEvents.VideoWindowId);
            Check(window != 0, "the VE video window exists");
            await Dispatcher.UIThread.InvokeAsync(() => SdlEvents.Key(window, 6, 0x0040));
            Check(await Until(() => shortcuts is [true]), "Ctrl+C in the video window raises ExchangeVE copy");
            await Dispatcher.UIThread.InvokeAsync(() => SdlEvents.Key(window, 25, 0x0040));
            Check(await Until(() => shortcuts is [true, false]), "Ctrl+V in the video window raises ExchangeVE paste");
            runtime.RequestClipboard();
            // The test window is real and focused: desktop input may reach it, so only C/V keycodes (31/50) matter here.
            var others = new List<string>(); bool leaked = false;
            for (int i = 0; i < 64; i++)
            {
                JsonElement next = await ControlProtocol.ReadAsync<JsonElement>(control.Client, stop.Token);
                int kind = next.GetProperty("Type").GetInt32();
                if (kind == 8) break;
                others.Add(next.GetRawText());
                leaked |= kind == 0 && next.GetProperty("Keycode").GetUInt32() is 31 or 50;
            }
            if (others.Count > 0) Console.WriteLine("  (eventos ajenos de la ventana real: " + string.Join(" ", others.Take(4)) + ")");
            Check(!leaked, "the intercepted Ctrl+C/V produced no Android key events");
            using (var dropped = new SdlEvents.Strings("/tmp/uno.txt", "/tmp/carpeta dos"))
            {
                await Dispatcher.UIThread.InvokeAsync(() => SdlEvents.Drop(window, dropped));
                Check(await Until(() => drops.Count == 1) && drops[0] is ["/tmp/uno.txt", "/tmp/carpeta dos"], "a drop gesture delivers all paths once");
            }

            // Presentación dinámica can change during the session.
            runtime.SetVSync(true); runtime.SetVSync(false);
            long before = runtime.Telemetry.FramesRendered;
            Check(await Until(() => runtime.Telemetry.FramesRendered > before + 3) && !session.IsCompleted, "live VSync changes keep the stream presenting");

            await Dispatcher.UIThread.InvokeAsync(SdlEvents.Quit);
            await session.WaitAsync(TimeSpan.FromSeconds(5));
            stop.Cancel();
            await sender;
            await Check(ThrowsAsync<InvalidOperationException>(() => runtime.CaptureAsync()), "capture is refused after the session");
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } }
    }

    private static async Task ControlActionsAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "novora-ve-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var store = new LinuxTrustStore(directory);
        var server = new LinuxControlServer(IPAddress.Loopback, store, port: 0, allowLoopbackForTests: true);
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
            async Task<ControlReply> Send(long id, string action, string? value = null, long revision = -1)
            {
                await ControlProtocol.WriteAsync(tls, new ControlRequest(1, id, action, Value: value, Revision: revision), CancellationToken.None);
                return await ControlProtocol.ReadAsync<ControlReply>(tls, CancellationToken.None);
            }
            await ControlProtocol.WriteAsync(tls, new ControlRequest(1, 1, "pair.lan", Code: invitation.Code), CancellationToken.None);
            ControlReply paired = await ControlProtocol.ReadAsync<ControlReply>(tls, CancellationToken.None);
            Check(paired.Success, "LINK Control pairs over TLS");
            ControlSnapshot snapshot = (await Send(2, "get")).Snapshot!;
            Check(snapshot.Media is { CanCapture: false, CanRecord: false, Recording: false, Message: "VisionEngine no está preparado." },
                "without VE the snapshot offers no capture (Windows message)");
            ControlReply capture = await Send(3, "capture", revision: snapshot.Revision);
            Check(!capture.Success && capture.Message == "Inicia una sesión VE autorizada primero.", "capture requires an authorized VE session");
            ControlReply record = await Send(4, "startRecording", revision: snapshot.Revision);
            Check(!record.Success, "startRecording requires an authorized VE session");

            ControlReply stale = await Send(5, "audio", "Disabled", snapshot.Revision - 1);
            Check(!stale.Success && stale.Message == "Los ajustes cambiaron en PC. Revisa el estado actualizado y vuelve a aplicar.", "audio uses the Windows revision guard");
            ControlReply invalid = await Send(6, "audio", "Bluetooth", snapshot.Revision);
            Check(!invalid.Success && invalid.Message == "Valor no admitido por esta PC.", "audio only accepts advertised outputs");
            ControlReply disabled = await Send(7, "audio", "Disabled", snapshot.Revision);
            Check(disabled.Success && disabled.Message == "Salida guardada; requiere iniciar o reiniciar el video para activar esta configuración de audio." &&
                  disabled.Snapshot?.AudioOutput == "Disabled" && !server.VisionSettings.AudioEnabled,
                "audio Disabled is saved with the Windows message");
            ControlReply enabled = await Send(8, "audio", "Pc", disabled.Snapshot!.Revision);
            Check(enabled.Success && enabled.Snapshot?.AudioOutput == "Pc" && server.VisionSettings.AudioEnabled, "audio Pc re-enables Android audio");
        }
        finally
        {
            await server.DisposeAsync();
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    // ------------------------------------------------------------ helpers

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    private static async Task<bool> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); return false; } catch (T) { return true; }
    }

    private static async Task Check(Task<bool> check, string name) => Check(await check, name);

    private static async Task<bool> Until(Func<bool> condition, int ms = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < deadline) { if (condition()) return true; await Task.Delay(20); }
        return condition();
    }

    private static (int Width, int Height, (byte R, byte G, byte B) Pixel) ReadPng(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (!bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return (0, 0, default);
        int width = 0, height = 0; using var idat = new MemoryStream();
        for (int offset = 8; offset < bytes.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
            string type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (type == "IHDR") { width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 8)); height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 12)); }
            if (type == "IDAT") idat.Write(bytes, offset + 8, length);
            offset += 12 + length;
        }
        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        byte[] row = new byte[1 + width * 4];
        zlib.ReadExactly(row);
        int center = 1 + (width / 2) * 4;
        return (width, height, (row[center], row[center + 1], row[center + 2]));
    }

    private static async Task<byte[]> SampleAsync(CancellationToken token)
    {
        var start = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=red:s=64x32:r=25", "-frames:v", "3", "-c:v", "libopenh264", "-g", "1", "-f", "h264", "pipe:1" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; using var bytes = new MemoryStream();
        Task copy = process.StandardOutput.BaseStream.CopyToAsync(bytes, token); Task<string> error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token); await copy;
        if (process.ExitCode != 0) throw new Exception(await error);
        return bytes.ToArray();
    }

    private sealed class Pair(TcpClient server, TcpClient client) : IDisposable
    {
        public Stream Server => server.GetStream(); public Stream Client => client.GetStream();
        public static async Task<Pair> CreateAsync(CancellationToken token)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            try
            {
                var client = new TcpClient(); Task connect = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, token).AsTask();
                TcpClient server = await listener.AcceptTcpClientAsync(token); await connect; return new(server, client);
            }
            finally { listener.Stop(); }
        }
        public void Dispose() { server.Dispose(); client.Dispose(); }
    }

    /// <summary>Injects SDL 3 events into the queue the renderer polls (layouts from SDL_events.h).</summary>
    private static class SdlEvents
    {
        private static readonly IntPtr Library = NativeLibrary.Load("libSDL3.so.0");
        private static T Fn<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(Library, name));

        public static uint VideoWindowId()
        {
            IntPtr windows = Fn<GetWindows>("SDL_GetWindows")(out int count);
            try
            {
                var getTitle = Fn<GetWindowTitle>("SDL_GetWindowTitle"); var getId = Fn<GetWindowId>("SDL_GetWindowID");
                for (int i = 0; i < count; i++)
                {
                    IntPtr window = Marshal.ReadIntPtr(windows, i * IntPtr.Size);
                    if (Marshal.PtrToStringUTF8(getTitle(window)) == "NOVORA-LINK · VisionEngine") return getId(window);
                }
                return 0;
            }
            finally { Fn<Free>("SDL_free")(windows); }
        }

        public static void Key(uint window, int scancode, ushort modifiers)
        {
            Push(0x300, window, e => { Marshal.WriteInt32(e, 24, scancode); Marshal.WriteInt16(e, 32, (short)modifiers); Marshal.WriteByte(e, 36, 1); });
            Push(0x301, window, e => { Marshal.WriteInt32(e, 24, scancode); Marshal.WriteInt16(e, 32, (short)modifiers); });
        }

        public static void Drop(uint window, Strings paths)
        {
            Push(0x1002, window, _ => { });
            foreach (IntPtr path in paths.Pointers) Push(0x1000, window, e => Marshal.WriteIntPtr(e, 40, path));
            Push(0x1003, window, _ => { });
        }

        public static void Quit() => Push(0x100, 0, _ => { });

        private static void Push(uint type, uint window, Action<IntPtr> fill)
        {
            IntPtr e = Marshal.AllocHGlobal(128);
            try
            {
                Marshal.Copy(new byte[128], 0, e, 128);
                Marshal.WriteInt32(e, 0, (int)type); Marshal.WriteInt32(e, 16, (int)window); fill(e);
                if (!Fn<PushEvent>("SDL_PushEvent")(e)) throw new InvalidOperationException("SDL_PushEvent failed");
            }
            finally { Marshal.FreeHGlobal(e); }
        }

        /// <summary>UTF-8 strings that outlive the queued drop events until the renderer copies them.</summary>
        public sealed class Strings(params string[] values) : IDisposable
        {
            public IntPtr[] Pointers { get; } = values.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
            public void Dispose() { foreach (IntPtr p in Pointers) Marshal.FreeCoTaskMem(p); }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetWindows(out int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetWindowTitle(IntPtr window);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetWindowId(IntPtr window);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(IntPtr memory);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool PushEvent(IntPtr e);
    }
}
