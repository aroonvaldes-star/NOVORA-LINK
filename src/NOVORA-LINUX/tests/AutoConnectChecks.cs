using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using NOVORA.Linux.Control;

/// <summary>Dynamic USB/LAN connectivity: policy, ADB hotplug framing and the connector state machine.</summary>
internal static class AutoConnectChecks
{
    public static async Task RunAsync()
    {
        CheckRetryDelays();
        CheckLanChoice();
        CheckUsbPolicy();
        await CheckTrackerFramingAsync();
        await CheckLanFollowsNetworkAsync();
        await CheckUsbLifecycleAsync();
        await CheckBoundedUsbAttemptsAsync();
        await CheckWirelessAdbAsync();
        await CheckAuthorizationTimeoutAndSessionEndAsync();
        await CheckManualControlAndSafetyAsync();
        Console.WriteLine("Dynamic connectivity policy, ADB hotplug framing, LAN follow, USB bootstrap/recovery, bounded retries and manual pause checks passed.");
    }

    private static void CheckRetryDelays()
    {
        int[] expected = [0, 1, 2, 4, 8, 15, 15, 15];
        for (int attempt = 0; attempt < expected.Length; attempt++)
            Assert(LinuxAutoConnectPolicy.RetryDelay(attempt) == TimeSpan.FromSeconds(expected[attempt]),
                $"Retry delay {attempt} must be {expected[attempt]} s.");
    }

    private static void CheckLanChoice()
    {
        var docker = Lan("docker0", "172.17.0.1", false, NetworkInterfaceType.Ethernet);
        var wifi = Lan("wlp2s0", "192.168.1.20", true, NetworkInterfaceType.Wireless80211);
        var wired = Lan("enp3s0", "192.168.1.21", true, NetworkInterfaceType.Ethernet);
        var isolated = Lan("enp4s0", "10.0.0.5", false, NetworkInterfaceType.Ethernet);
        var bridge = Lan("virbr0", "192.168.122.1", false, NetworkInterfaceType.Ethernet);
        Assert(LinuxAutoConnectPolicy.ChooseLan([], null) is null, "No private network must not start LAN.");
        Assert(LinuxAutoConnectPolicy.ChooseLan([docker, bridge], null) is null,
            "Container/VM bridges without a gateway are not the phone's network.");
        Assert(LinuxAutoConnectPolicy.ChooseLan([docker, isolated, wifi, wired], null) == wired,
            "A physical interface with a gateway, wired first, must be preferred.");
        Assert(LinuxAutoConnectPolicy.ChooseLan([docker, isolated, wifi], null) == wifi,
            "Wi-Fi with a gateway must beat an isolated wired network.");
        Assert(LinuxAutoConnectPolicy.ChooseLan([wired, wifi], wifi.Address) == wifi,
            "A working listener must not hop to a 'better' interface.");
        Assert(LinuxAutoConnectPolicy.ChooseLan([wired], wifi.Address) == wired,
            "A vanished listener address must move to the remaining network.");
    }

    private static void CheckUsbPolicy()
    {
        var a = new AdbDevice("A", "device", "Phone A", true);
        var b = new AdbDevice("B", "device", "Phone B", true);
        var pending = new AdbDevice("C", "unauthorized", "", true);
        var wireless = new AdbDevice("192.168.1.9:5555", "device", "Phone W", false);
        UsbAutoState Base(params AdbDevice[] devices) => new(devices, null, false, false, TimeSpan.Zero,
            false, false, null, new HashSet<string>(), 0, true);

        Expect(Base(), UsbAutoDecision.Idle, null, "No phone");
        Expect(Base(pending), UsbAutoDecision.Idle, null, "Unauthorized ADB is not a route");
        Expect(Base(pending, wireless), UsbAutoDecision.Start, wireless.Serial, "Paired ADB Wi-Fi reaches LINK Control without a cable");
        Expect(Base(wireless) with { LanAuthorized = true }, UsbAutoDecision.Idle, null, "ADB Wi-Fi is not used when LAN is already connected");
        Expect(Base(a, wireless), UsbAutoDecision.Start, "A", "The cable is preferred over ADB Wi-Fi");
        Expect(Base(wireless) with { RunningSerial = wireless.Serial, Authorized = true }, UsbAutoDecision.Idle, wireless.Serial,
            "An authorized ADB Wi-Fi route stays as is");
        Expect(Base(a), UsbAutoDecision.Start, "A", "One USB phone");
        Expect(Base(a, b), UsbAutoDecision.WaitAmbiguous, null, "Two phones without a choice");
        Expect(Base(a, b) with { PreferredSerial = "B" }, UsbAutoDecision.Start, "B", "Two phones with a previous choice");
        Expect(Base(a) with { PausedSerials = new HashSet<string> { "A" } }, UsbAutoDecision.WaitPaused, "A", "Manual stop");
        Expect(Base(a) with { LanAuthorized = true, LanBusy = true }, UsbAutoDecision.WaitLanBusy, "A",
            "USB must not take over a LAN session with running engines");
        Expect(Base(a) with { LanAuthorized = true }, UsbAutoDecision.Start, "A", "Idle LAN may move to USB");
        Expect(Base(a) with { Attempts = LinuxAutoConnectPolicy.MaxUsbBootstrapAttempts }, UsbAutoDecision.WaitGaveUp, "A",
            "Bootstrap attempts are bounded");
        Expect(Base(a) with { Attempts = 1, BackoffElapsed = false }, UsbAutoDecision.WaitBackoff, "A", "Backoff");
        Expect(Base(a) with { RunningSerial = "A", Authorized = true }, UsbAutoDecision.Idle, "A", "Authorized USB");
        Expect(Base(b) with { RunningSerial = "A" }, UsbAutoDecision.Stop, null, "Unplugged phone");
        Expect(Base(a) with { RunningSerial = "A", PendingFor = TimeSpan.FromSeconds(5) },
            UsbAutoDecision.WaitAuthorization, "A", "Waiting for LINK Control");
        Expect(Base(a) with { RunningSerial = "A", PendingFor = LinuxAutoConnectPolicy.UsbAuthorizationTimeout },
            UsbAutoDecision.Restart, "A", "Unconfirmed bootstrap");
        Expect(Base(a) with { RunningSerial = "A", SessionEnded = true }, UsbAutoDecision.Restart, "A", "Ended session");
    }

    private static async Task CheckTrackerFramingAsync()
    {
        string frame = "A1B2\tdevice usb:1-2 product:p model:Pixel_8 device:d transport_id:1\n" +
            "C3D4\tunauthorized usb:1-3 transport_id:2\n";
        byte[] payload = Encoding.UTF8.GetBytes(frame);
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("0000" + payload.Length.ToString("x4"))
            .Concat(payload).ToArray());
        string? empty = await AdbDeviceTracker.ReadFrameAsync(stream, CancellationToken.None);
        Assert(empty == "", "An empty ADB frame means no devices.");
        string? read = await AdbDeviceTracker.ReadFrameAsync(stream, CancellationToken.None);
        IReadOnlyList<AdbDevice> devices = AdbDeviceTracker.ParseFrame(read!);
        Assert(devices.Count == 2 && devices[0] == new AdbDevice("A1B2", "device", "Pixel 8", true) &&
            devices[1].State == "unauthorized" && devices[1].IsUsb, "ADB track-devices -l frame was misparsed.");
        Assert(await AdbDeviceTracker.ReadFrameAsync(stream, CancellationToken.None) is null, "End of ADB stream.");
        using var truncated = new MemoryStream(Encoding.ASCII.GetBytes("0010abc"));
        bool rejected = false;
        try { await AdbDeviceTracker.ReadFrameAsync(truncated, CancellationToken.None); }
        catch (FormatException) { rejected = true; }
        Assert(rejected, "A truncated ADB frame must be rejected, not published as a device list.");
    }

    private static async Task CheckLanFollowsNetworkAsync()
    {
        var target = new FakeTarget();
        var time = new ManualTime();
        var wifi = Lan("wlp2s0", "192.168.1.20", true, NetworkInterfaceType.Wireless80211);
        var wired = Lan("enp3s0", "10.1.0.4", true, NetworkInterfaceType.Ethernet);
        IReadOnlyList<LanCandidate> networks = [wifi];
        await using var connector = new LinuxAutoConnector(target, () => [], () => networks, time);
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.IsRunning && Equals(target.LanAddress, wifi.Address) && target.LanStarts == 1,
            "LAN must start automatically on the active private network.");
        Assert(connector.Summary == "Esperando teléfono (USB o Wi-Fi)", "Summary while waiting: " + connector.Summary);
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.LanStarts == 1 && target.LanStops == 0, "A healthy LAN listener must not restart.");

        networks = [wired];
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.LanStops == 1 && Equals(target.LanAddress, wired.Address), "LAN must follow a network change.");

        networks = [];
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(!target.IsRunning && target.LanStops == 2, "A vanished network must close the LAN listener.");

        target.FailLan = true;
        networks = [wifi];
        await connector.EvaluateOnceAsync(CancellationToken.None);
        int attempts = target.LanStarts;
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.LanStarts == attempts, "A failed LAN start must back off.");
        target.FailLan = false;
        time.Advance(TimeSpan.FromSeconds(1));
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.IsRunning, "LAN must recover after the retry delay.");
    }

    private static async Task CheckUsbLifecycleAsync()
    {
        var target = new FakeTarget();
        var time = new ManualTime();
        IReadOnlyList<AdbDevice> devices = [];
        await using var connector = new LinuxAutoConnector(target, () => devices, () => [], time);
        var phone = new AdbDevice("A", "device", "Phone A", true);

        devices = [new AdbDevice("A", "unauthorized", "", true)];
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbStarts == 0, "USB must wait until ADB debugging is authorized.");

        devices = [phone];
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbStarts == 1 && target.UsbSerial == "A", "An authorized USB phone must be bootstrapped automatically.");
        Assert(connector.Summary == "Conectando por USB…", "Summary while bootstrapping: " + connector.Summary);
        target.IsUsbAuthorized = true;
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(connector.Summary == "Conectado por USB" && target.UsbStarts == 1, "Authorized USB must stay as is.");

        devices = [];
        target.FailUsbStop = true; // An unplugged phone cannot remove its adb reverse routes.
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(!target.IsUsbRunning && target.UsbStops == 1, "Unplugging must release the USB route.");
        Assert(connector.Summary == "Reconectando…", "Summary after losing the phone: " + connector.Summary);
        target.FailUsbStop = false;

        devices = [phone];
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbStarts == 2, "Replugging must bootstrap USB again.");
        target.IsUsbAuthorized = true;
        await connector.EvaluateOnceAsync(CancellationToken.None);

        // ADB state unknown (server restarting): never tear down on missing information.
        devices = null!;
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.IsUsbRunning && target.UsbStops == 1, "Unknown ADB state must not close the USB route.");

        // LAN with running engines: a newly plugged phone must not move the session.
        var lanTarget = new FakeTarget { IsLanAuthorizedValue = true, LanBusyValue = true };
        IReadOnlyList<AdbDevice> lanDevices = [phone];
        await using var lanConnector = new LinuxAutoConnector(lanTarget, () => lanDevices, () => [], time);
        await lanConnector.EvaluateOnceAsync(CancellationToken.None);
        Assert(lanTarget.UsbStarts == 0 && lanConnector.Summary == "Conectado por LAN", "USB interrupted a busy LAN session.");
        lanTarget.LanBusyValue = false;
        await lanConnector.EvaluateOnceAsync(CancellationToken.None);
        Assert(lanTarget.UsbStarts == 1, "USB must be prepared once the LAN session is idle.");
    }

    private static async Task CheckWirelessAdbAsync()
    {
        var target = new FakeTarget();
        var wireless = new AdbDevice("10.0.0.15:39973", "device", "SM A566E", false);
        IReadOnlyList<AdbDevice> devices = [wireless];
        await using var connector = new LinuxAutoConnector(target, () => devices, () => [], new ManualTime());
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbSerial == wireless.Serial && target.UsbIsWireless && connector.Summary == "Conectando por ADB Wi-Fi…",
            "Paired ADB Wi-Fi must bootstrap LINK Control: " + connector.Summary);
        target.IsUsbAuthorized = true;
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(connector.Summary == "Conectado por ADB Wi-Fi", "Summary must name ADB Wi-Fi: " + connector.Summary);
        devices = [];
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(!target.IsUsbRunning, "Losing the wireless ADB device must release its route.");
    }

    private static async Task CheckBoundedUsbAttemptsAsync()
    {
        var target = new FakeTarget { FailUsb = true };
        var time = new ManualTime();
        var phone = new AdbDevice("A", "device", "Phone A", true);
        IReadOnlyList<AdbDevice> devices = [phone];
        await using var connector = new LinuxAutoConnector(target, () => devices, () => [], time);
        for (int pass = 0; pass < 20; pass++)
        {
            await connector.EvaluateOnceAsync(CancellationToken.None);
            time.Advance(TimeSpan.FromSeconds(16));
        }
        Assert(target.UsbStarts == LinuxAutoConnectPolicy.MaxUsbBootstrapAttempts,
            $"USB bootstrap must stop after {LinuxAutoConnectPolicy.MaxUsbBootstrapAttempts} failures; ran {target.UsbStarts}.");

        devices = [];
        await connector.EvaluateOnceAsync(CancellationToken.None);
        devices = [phone];
        target.FailUsb = false;
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbStarts == LinuxAutoConnectPolicy.MaxUsbBootstrapAttempts + 1 && target.IsUsbRunning,
            "Replugging must reset the bounded attempts.");
    }

    private static async Task CheckAuthorizationTimeoutAndSessionEndAsync()
    {
        var target = new FakeTarget();
        var time = new ManualTime();
        IReadOnlyList<AdbDevice> devices = [new AdbDevice("A", "device", "Phone A", true)];
        await using var connector = new LinuxAutoConnector(target, () => devices, () => [], time);
        await connector.EvaluateOnceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(10));
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbStarts == 1 && target.UsbStops == 0, "USB must wait for LINK Control before retrying.");
        time.Advance(LinuxAutoConnectPolicy.UsbAuthorizationTimeout);
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbStops == 1 && !target.IsUsbRunning && connector.UsbAttempts == 1,
            "An unconfirmed bootstrap must be released and counted.");
        time.Advance(TimeSpan.FromSeconds(1));
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbStarts == 2, "The bootstrap must be retried after the delay.");

        target.IsUsbAuthorized = true;
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(connector.UsbAttempts == 0, "A confirmed session resets the attempts.");
        target.IsUsbAuthorized = false; // LINK Control closed the session; the cable is still connected.
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbStops == 2 && !target.IsUsbRunning, "An ended USB session must be bootstrapped again.");
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbStarts == 2, "Reconnection after a session end waits briefly.");
        time.Advance(TimeSpan.FromSeconds(2));
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbStarts == 3 && connector.Summary == "Conectando por USB…", "USB session must be restored.");
    }

    private static async Task CheckManualControlAndSafetyAsync()
    {
        var target = new FakeTarget();
        var time = new ManualTime();
        var a = new AdbDevice("A", "device", "Phone A", true);
        var b = new AdbDevice("B", "device", "Phone B", true);
        IReadOnlyList<AdbDevice> devices = [a, b];
        IReadOnlyList<LanCandidate> networks = [Lan("wlp2s0", "192.168.1.20", true, NetworkInterfaceType.Wireless80211)];
        var notices = new List<string>();
        await using var connector = new LinuxAutoConnector(target, () => devices, () => networks, time);
        connector.Notice += (message, _) => notices.Add(message);

        await connector.EvaluateOnceAsync(CancellationToken.None);
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbStarts == 0, "Two USB phones must never be chosen silently.");
        Assert(notices.Count(message => message.Contains("varios teléfonos", StringComparison.Ordinal)) == 1,
            "A repeated situation must be announced once, not every pass.");

        connector.PreferUsb("B");
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbSerial == "B", "The phone chosen manually must be used.");

        connector.PauseUsb("B");
        await target.StopUsbAsync();
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(!target.IsUsbRunning, "A manual USB stop must not be undone automatically.");
        devices = [a];
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbSerial == "A", "With B unplugged, the single remaining phone is used.");
        await target.StopUsbAsync();
        devices = [a, b];
        connector.PauseUsb("A");
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.UsbSerial == "B", "Unplugging B cleared its manual pause and B is still preferred.");

        connector.PauseLan(true);
        await target.StopLanAsync();
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(!target.IsRunning, "A manual LAN stop must keep LAN off.");
        connector.PauseLan(false);
        await connector.EvaluateOnceAsync(CancellationToken.None);
        Assert(target.IsRunning, "Preparing LAN again re-enables automatic LAN.");
    }

    private static void Expect(UsbAutoState state, UsbAutoDecision decision, string? serial, string scenario)
    {
        UsbAutoPlan plan = LinuxAutoConnectPolicy.DecideUsb(state);
        Assert(plan.Decision == decision && (serial is null || plan.Device?.Serial == serial),
            $"{scenario}: expected {decision}/{serial}, got {plan.Decision}/{plan.Device?.Serial}.");
    }

    private static LanCandidate Lan(string name, string address, bool gateway, NetworkInterfaceType type) =>
        new(name, IPAddress.Parse(address), gateway, type);

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class FakeTarget : IAutoConnectTarget
    {
        public bool IsRunning { get; private set; }
        public IPAddress? LanAddress { get; private set; }
        public bool IsLanAuthorizedValue { get; set; }
        public bool IsLanAuthorized => IsLanAuthorizedValue;
        public bool LanBusyValue { get; set; }
        public bool LanBusy => LanBusyValue;
        public bool IsUsbRunning { get; private set; }
        public string? UsbSerial { get; private set; }
        public bool IsUsbAuthorized { get; set; }
        public bool UsbIsWireless { get; private set; }
        public bool FailLan, FailUsb, FailUsbStop;
        public int LanStarts, LanStops, UsbStarts, UsbStops;

        public Task StartLanAsync(IPAddress address, CancellationToken cancellationToken)
        {
            LanStarts++;
            if (FailLan) throw new IOException("fixture: address in use");
            IsRunning = true;
            LanAddress = address;
            return Task.CompletedTask;
        }

        public Task StopLanAsync()
        {
            LanStops++;
            IsRunning = false;
            LanAddress = null;
            return Task.CompletedTask;
        }

        public Task StartUsbAsync(AdbDevice device, CancellationToken cancellationToken)
        {
            UsbStarts++;
            if (FailUsb) throw new InvalidOperationException("fixture: LINK Control no está instalado");
            IsUsbRunning = true;
            UsbSerial = device.Serial;
            UsbIsWireless = !device.IsUsb;
            return Task.CompletedTask;
        }

        public Task StopUsbAsync()
        {
            UsbStops++;
            IsUsbRunning = false;
            IsUsbAuthorized = false;
            UsbSerial = null;
            if (FailUsbStop) throw new InvalidOperationException("fixture: device not found");
            return Task.CompletedTask;
        }
    }
}
