using Avalonia;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using NOVORA.Contracts.Input;
using NOVORA.ExInEngine;
using NOVORA.Integration;
using NOVORA.Linux.ExIn;
using NOVORA.Service;

/// <summary>
/// ExInEngine on Linux end to end without hardware: an SDL3 virtual gamepad drives the real
/// ExInSdl adapter (with a renderer-style SDL_PollEvent loop draining the queue in parallel),
/// the unchanged Windows engine, and the Windows HID session over a fake `adb` that records
/// what Android's `hid -` would receive. Run: dotnet run -- --exin-native
/// </summary>
internal static class ExInChecks
{
    private const string FakeAdbScript = """
        #!/bin/bash
        echo "$*" >> "$FAKE_ADB_LOG.args"
        case "$*" in
          "start-server") exit 0;;
          "devices -l") printf 'List of devices attached\nFAKE123                device usb:1-1 product:p model:Pixel_8 device:d transport_id:1\n';;
          *"getprop ro.serialno"*) echo HWSERIAL;;
          *"shell hid -"*) exec cat >> "$FAKE_ADB_LOG.hid";;
          *) exit 0;;
        esac
        """;

    public static async Task RunNativeAsync()
    {
        int pass = 0, fail = 0;
        void Check(bool ok, string name) { if (ok) { pass++; Console.WriteLine("PASS " + name); } else { fail++; Console.WriteLine("FAIL " + name); } }
        async Task<bool> Until(Func<bool> condition, int ms = 3000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < deadline) { if (condition()) return true; await Task.Delay(10); }
            return condition();
        }

        string repo = FindRepositoryRoot();
        string windowsProtocolPath = Path.Combine(repo, "src", "NOVORA LINK", "Control", "NLControlProtocol.cs");
        string temp = Path.Combine(Path.GetTempPath(), "novora-exin-linux-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(temp, "data"));
        NativeEnvironment.Set("SDL_VIDEO_DRIVER", "dummy");

        // ── ADB parsing (same transport classification as AdbBridge) ─────────────────────
        var usb = NLServiceADB.ParseDevice("R5CT123 device usb:1-1 product:x model:Galaxy_S23 device:y transport_id:3");
        var wifi = NLServiceADB.ParseDevice("192.168.1.20:5555 device product:x model:Pixel_8 device:y transport_id:4");
        var mdns = NLServiceADB.ParseDevice("adb-R5CT123-abc._adb-tls-connect._tcp device product:x model:Pixel transport_id:5");
        var unauthorized = NLServiceADB.ParseDevice("R5CT999 unauthorized usb:1-2 transport_id:6");
        Check(usb is { IsUsb: true, Connected: true, IsWifiConnection: false, FriendlyName: "Galaxy S23" }, "adb USB device parsed");
        Check(wifi is { IsUsb: false, IsWifiConnection: true, Connected: true }, "adb host:port is Wi-Fi");
        Check(mdns is { IsUsb: false, IsWifiConnection: true }, "adb mDNS TLS is Wi-Fi");
        Check(unauthorized is { Connected: false }, "unauthorized device is not connected");

        // ── Settings round trip ──────────────────────────────────────────────────────────
        string settingsPath = Path.Combine(temp, "cfg", "exin.json");
        new LinuxExInSettings(false, 33, 140).Save(settingsPath);
        Check(LinuxExInSettings.Load(settingsPath) == new LinuxExInSettings(false, 33, 140), "settings persist");
        File.WriteAllText(settingsPath, "{\"Enabled\":true,\"PointerSensitivity\":99,\"GameStickSensitivity\":1}");
        Check(LinuxExInSettings.Load(settingsPath) == new LinuxExInSettings(true, 40, 25), "settings clamp like Windows");
        Check(LinuxExInSettings.Load(Path.Combine(temp, "missing.json")) == LinuxExInSettings.Default, "missing settings use defaults");

        // ── Control snapshot contract equals Windows NLControlExIn ───────────────────────
        string windowsProtocol = File.ReadAllText(windowsProtocolPath);
        static string[] WindowsParameters(string source, string record)
        {
            Match match = Regex.Match(source, @"record " + record + @"\((.*?)\);", RegexOptions.Singleline);
            return match.Groups[1].Value.Split(',').Select(part => Regex.Replace(part.Split('=')[0].Trim(), @"\s+", " "))
                .Where(part => part.Length > 0).Select(part => part.Split(' ').Last()).ToArray();
        }
        foreach ((string windows, Type linux) in new[] { ("NLControlExIn", typeof(LinuxControlExIn)),
                     ("NLControlExInTelemetry", typeof(LinuxControlExInTelemetry)),
                     ("NLControlExInCalibration", typeof(LinuxControlExInCalibration)) })
        {
            string[] expected = WindowsParameters(windowsProtocol, windows);
            string[] actual = linux.GetConstructors().Single().GetParameters().Select(p => p.Name!).ToArray();
            Check(expected.Length > 3 && expected.SequenceEqual(actual, StringComparer.Ordinal), $"{linux.Name} matches Windows {windows} JSON fields");
        }

        // ── SDL3 native: virtual gamepad, events through the watch, renderer loop coexists ──
        IntPtr sdl = NativeLibrary.Load("libSDL3.so.0");
        T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(sdl, name));
        var initSub = Load<InitSub>("SDL_InitSubSystem");
        var pump = Load<Pump>("SDL_PumpEvents");
        var poll = Load<Poll>("SDL_PollEvent");
        var attach = Load<Attach>("SDL_AttachVirtualJoystick");
        var detach = Load<Detach>("SDL_DetachVirtualJoystick");
        var openJoystick = Load<OpenJoystick>("SDL_OpenJoystick");
        var closeJoystick = Load<CloseJoystick>("SDL_CloseJoystick");
        var setAxis = Load<SetAxis>("SDL_SetJoystickVirtualAxis");
        var setButton = Load<SetButton>("SDL_SetJoystickVirtualButton");

        // Renderer stand-in: VIDEO initialised and SDL_PollEvent drained continuously on another thread.
        int rendererGamepadEvents = 0, rendererPolls = 0;
        using var rendererStop = new CancellationTokenSource();
        var rendererReady = new ManualResetEventSlim();
        var renderer = new Thread(() =>
        {
            initSub(0x20);
            rendererReady.Set();
            IntPtr buffer = Marshal.AllocHGlobal(128);
            while (!rendererStop.IsCancellationRequested)
            {
                pump();
                Interlocked.Increment(ref rendererPolls);
                while (poll(buffer))
                {
                    uint type = (uint)Marshal.ReadInt32(buffer);
                    if (type is >= 0x600 and <= 0x6FF) Interlocked.Increment(ref rendererGamepadEvents);
                }
                Thread.Sleep(2);
            }
            Marshal.FreeHGlobal(buffer);
        }) { IsBackground = true };
        renderer.Start();
        rendererReady.Wait();

        var paths = new NLServiceNovoraPaths(temp);
        using (var exInSdl = new ExInSdl(paths))
        {
            exInSdl.InitializeVE();
            var received = new System.Collections.Concurrent.ConcurrentQueue<ExInEvent>();
            using var readStop = new CancellationTokenSource();
            Task reader = Task.Run(async () =>
            {
                await foreach (ExInEvent e in exInSdl.ReadEventsVE(readStop.Token)) received.Enqueue(e);
            });

            uint joystickId = AttachGamepad(attach, "NOVORA virtual pad");
            Check(joystickId != 0, "virtual gamepad attached");
            Check(await Until(() => received.Any(e => e.Type == ExInTypeEvent.Added && e.InstanceId == joystickId)), "Added event reaches ExIn");
            Check(exInSdl.GetDetectedIdsVE().Contains(joystickId), "detected ids include the pad");
            IntPtr pad = exInSdl.OpenVE(joystickId);
            Check(pad != IntPtr.Zero, "gamepad opens");
            var identity = exInSdl.GetIdentityVE(pad);
            Check(identity.VendorId == 0x045E && identity.ProductId == 0x028E, "identity reports VID/PID");

            IntPtr joystick = openJoystick(joystickId);
            setAxis(joystick, 0, 20000);
            Check(await Until(() => received.Any(e => e.Type == ExInTypeEvent.Axis && e.Axis == 0 && e.AxisValue == 20000)), "axis event carries value");
            Check(await Until(() => exInSdl.ReadStateVE(pad).LeftX == 20000), "ReadState sees axis");
            setButton(joystick, 0, true);
            Check(await Until(() => received.Any(e => e.Type == ExInTypeEvent.Button && e.Button == 0 && e.Pressed)), "button down event");
            Check(await Until(() => exInSdl.ReadStateVE(pad).Buttons.HasFlag(ExInButtons.South)), "ReadState sees South");
            setButton(joystick, 0, false);
            Check(await Until(() => received.Any(e => e.Type == ExInTypeEvent.Button && e.Button == 0 && !e.Pressed)), "button up event");
            Console.WriteLine($"  renderer polls={Volatile.Read(ref rendererPolls)} gamepad events it read={Volatile.Read(ref rendererGamepadEvents)}"); Check(Volatile.Read(ref rendererPolls) > 5, "renderer stand-in was draining SDL events concurrently");

            closeJoystick(joystick);
            exInSdl.CloseVE(pad);
            detach(joystickId);
            Check(await Until(() => received.Any(e => e.Type == ExInTypeEvent.Removed && e.InstanceId == joystickId)), "Removed event reaches ExIn");
            readStop.Cancel();
            await reader;
        }

        // ── Full Windows engine on Linux: virtual pad → manager → HID output (fake) ─────────
        var output = new RecordingOutput();
        uint enginePad;
        await using (var engine = new ExInCoreEngine(paths))
        {
            await engine.InitializeAsync();
            enginePad = AttachGamepad(attach, "NOVORA engine pad");
            Check(await Until(() => engine.Status.ConnectedGamepads == 1), "engine detects pad");
            await engine.AttachOutputAsync(output);
            Check(await Until(() => output.Created.Count == 1), "engine registers HID device on the output");
            IntPtr joystick = openJoystick(enginePad);
            int before = output.Reports;
            setAxis(joystick, 0, 30000);
            setButton(joystick, 0, true);
            Check(await Until(() => output.Reports > before), "engine sends HID reports for input");
            Check(await Until(() => engine.LiveSnapshot.State.LeftX == 30000 && engine.LiveSnapshot.State.Buttons.HasFlag(ExInButtons.South)),
                "live snapshot follows pad");
            ExInModeResult mode = await engine.Manager.SetModeAsync(ExInInputMode.Ui);
            Check(mode.Success && engine.Manager.ModeVE == ExInInputMode.Ui, "mode switch to UI");
            await engine.Manager.SetModeAsync(ExInInputMode.Game);
            Check(engine.Manager.BeginCalibrationVE() && engine.LiveSnapshot.Calibrating, "calibration starts");
            engine.Manager.ResetCalibrationVE();
            closeJoystick(joystick);
            await engine.DetachOutputAsync(output);
        }
        detach(enginePad);


        // ── Android HID session over (fake) adb: register + reports + clean stop ───────────
        string log = Path.Combine(temp, "adb");
        Environment.SetEnvironmentVariable("FAKE_ADB_LOG", log);
        string fakeBin = Path.Combine(temp, "fakebin");
        Directory.CreateDirectory(fakeBin);
        File.WriteAllText(Path.Combine(fakeBin, "adb"), FakeAdbScript);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(fakeBin, "adb"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string originalPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        Environment.SetEnvironmentVariable("PATH", fakeBin + ":" + originalPath);
        var adb = new NLServiceADB(paths);
        await using (var engine = new ExInCoreEngine(paths))
        await using (var session = new ExInControlSession(engine, paths, adb))
        {
            await engine.InitializeAsync();
            uint sessionPad = AttachGamepad(attach, "NOVORA session pad");
            Check(await Until(() => engine.Status.ConnectedGamepads == 1), "session engine detects pad");
            await session.StartAsync("FAKE123");
            Check(session.IsRunning && session.Serial == "FAKE123", "session running on the phone serial");
            Check(await Until(() => File.Exists(log + ".hid") && File.ReadAllText(log + ".hid").Contains("\"command\":\"register\"")),
                "Android `hid -` receives register");
            IntPtr joystick = openJoystick(sessionPad);
            setAxis(joystick, 1, -25000);
            Check(await Until(() => File.ReadAllText(log + ".hid").Contains("\"command\":\"report\"")), "Android `hid -` receives reports");
            string adbArgs = File.ReadAllText(log + ".args");
            Check(adbArgs.Contains("-s FAKE123 shell getprop ro.serialno") && adbArgs.Contains("-s FAKE123 shell hid -"), "identity check and hid shell use the serial");
            closeJoystick(joystick);
            await session.StopAsync();
            Check(!session.IsRunning, "session stops");
            detach(sessionPad);
        }

        Environment.SetEnvironmentVariable("PATH", originalPath);
        rendererStop.Cancel();
        renderer.Join();
        try { Directory.Delete(temp, true); } catch { }
        Console.WriteLine($"ExIn Linux: {pass} PASS / {fail} FAIL");
        if (fail != 0) throw new InvalidOperationException($"ExIn Linux checks failed: {fail}.");
    }

    /// <summary>The real window: whole ExIn page rendered, idle state and the LINK Control bridge.</summary>
    public static void RunUi()
    {
        string root = Path.Combine(Path.GetTempPath(), "novora-exin-ui-" + Guid.NewGuid().ToString("N"));
        string? previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string? previousData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(root, "config"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(root, "data"));
        NativeEnvironment.Set("SDL_VIDEO_DRIVER", "dummy");
        try
        {
            Avalonia.AppBuilder.Configure<NOVORA.Linux.App>().UsePlatformDetect().SetupWithoutStarting();
            var window = new NOVORA.Linux.MainWindow { Width = 1180, Height = 800 };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            object Field(string name) => typeof(NOVORA.Linux.MainWindow).GetField(name, flags)!.GetValue(window)!;
            var page = (Avalonia.Controls.ScrollViewer)((Dictionary<string, Avalonia.Controls.Control>)Field("_pages"))["ExInEngine"];
            Require(Field("_exInEngine") is ExInCoreEngine, "ExIn page owns a live ExInCoreEngine");
            Require(Field("_exInControlSession") is ExInControlSession, "ExIn page owns the Windows HID session");
            var host = (NOVORA.Linux.Control.LinuxControlHost)Field("_host");
            Require(host.ExIn is not null, "control host publishes the ExIn bridge");
            LinuxExInEngineState engineState = host.ExIn!.EngineState;
            Require(engineState.State is "Active" or "NotDetected" or "Error", "bridge reports a Windows ExInState value");
            LinuxControlExIn snapshot = host.ExIn.CaptureSnapshot()!;
            Require(!snapshot.Detected && snapshot.DeviceName == "Sin control físico" && !snapshot.CanSetMode,
                "snapshot without a pad matches Windows");
            Require(JsonSerializer.Serialize(snapshot).Contains("\"CalibrationProgress\":null"), "snapshot serializes with Windows names");
            var start = (Avalonia.Controls.Button)Field("_exInCalibrationStartButton");
            var game = (Avalonia.Controls.Button)Field("_exInGameModeButton");
            Require(!start.IsEnabled && !game.IsEnabled, "calibration and mode are disabled without a pad");
            var phones = (Avalonia.Controls.ComboBox)Field("_exInPhoneComboBox");
            Require(phones.Items.Cast<object>().FirstOrDefault()?.ToString() == "Automático", "phone selector starts on Automático");
            var paddles = (Avalonia.Controls.ComboBox[])Field("_exInPaddleMappingComboBoxes");
            Require(paddles.Length == 4 && paddles.All(combo => combo.Items.Count == 17), "P1–P4 offer the 17 Windows targets");
            Require(host.ExIn.HandleAsync("exin.mode", "Game", CancellationToken.None).GetAwaiter().GetResult() is (false, "ExInEngine necesita un control físico detectado."),
                "exin.mode without a pad is rejected like Windows");
            Require(host.ExIn.HandleAsync("exin.synchronize", "x", CancellationToken.None).GetAwaiter().GetResult() is (false, "La sincronización no acepta valores adicionales."),
                "exin.synchronize validates its value like Windows");

            string output = Path.Combine(Path.GetTempPath(), "novora-exin-ui");
            Directory.CreateDirectory(output);
            foreach (string theme in new[] { NOVORA.Linux.LinuxTheme.Light, NOVORA.Linux.LinuxTheme.Dark })
            {
                NOVORA.Linux.LinuxTheme.Apply(theme);
                typeof(NOVORA.Linux.MainWindow).GetMethod("ShowPage", flags)!.Invoke(window, ["ExInEngine"]);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                for (int part = 0; part < 3; part++)
                {
                    page.Offset = new Avalonia.Vector(0, part == 0 ? 0 : part == 1 ? page.Extent.Height / 2 - page.Viewport.Height / 2 : page.Extent.Height);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(1180, 800));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(output, $"{theme}-ExInEngine-{part + 1}.png"));
                }
                page.Offset = default;
            }
            window.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Console.WriteLine("ExIn Linux UI: page, idle state and LINK Control bridge checks passed: " + output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousConfig);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previousData);
            try { Directory.Delete(root, true); } catch { }
        }

        static void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("ExIn UI: " + name);
            Console.WriteLine("PASS " + name);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "NOVORA LINK", "Control")))
                return directory.FullName;
        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio NOVORA.");
    }

    private static uint AttachGamepad(Attach attach, string name)
    {
        // SDL_VirtualJoystickDesc (SDL 3.2, x64): version=sizeof, type=GAMEPAD, 6 axes, 21 buttons.
        IntPtr desc = Marshal.AllocHGlobal(136);
        IntPtr nameUtf8 = Marshal.StringToCoTaskMemUTF8(name);
        try
        {
            for (int i = 0; i < 136; i++) Marshal.WriteByte(desc, i, 0);
            Marshal.WriteInt32(desc, 0, 136);
            Marshal.WriteInt16(desc, 4, 1);
            Marshal.WriteInt16(desc, 8, unchecked((short)0x045E));
            Marshal.WriteInt16(desc, 10, 0x028E);
            Marshal.WriteInt16(desc, 12, 6);
            Marshal.WriteInt16(desc, 14, 21);
            Marshal.WriteIntPtr(desc, 40, nameUtf8);
            return attach(desc);
        }
        finally { Marshal.FreeHGlobal(desc); }
    }

    private sealed class RecordingOutput : INLInputOutput
    {
        public readonly List<NLInputDevice> Created = [];
        private int _reports;
        public int Reports => Volatile.Read(ref _reports);
        public bool IsReady => true;
        public Task CreateAsync(NLInputDevice device, byte[] descriptor, CancellationToken cancellationToken) { lock (Created) Created.Add(device); return Task.CompletedTask; }
        public Task SendAsync(ushort deviceId, byte[] report, CancellationToken cancellationToken) { Interlocked.Increment(ref _reports); return Task.CompletedTask; }
        public Task DestroyAsync(ushort deviceId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool InitSub(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Pump();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool Poll(IntPtr e);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Attach(IntPtr desc);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool Detach(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr OpenJoystick(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CloseJoystick(IntPtr joystick);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SetAxis(IntPtr joystick, int axis, short value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SetButton(IntPtr joystick, int button, [MarshalAs(UnmanagedType.I1)] bool down);
}
