using NOVORA.VisionEngine.Exchange;

int passed = 0, failed = 0;

VEExchangeCapabilities full = new(
    Clipboard: true, SendFiles: true, ReceiveFiles: true,
    ReceiveFolder: true, DragDrop: true, InstallApk: true);

Check("Route_IsTyped", () =>
{
    IVEExchangeTransport usb = new FakeTransport(VEExchangeRoute.UsbAppControl, full);
    IVEExchangeTransport lan = new FakeTransport(VEExchangeRoute.NativeLan, full);
    Require(usb.Route == VEExchangeRoute.UsbAppControl, "USB route is not typed.");
    Require(lan.Route == VEExchangeRoute.NativeLan, "LAN route is not typed.");
    Require(usb.Route != lan.Route, "Routes collapsed.");
});

Check("Capabilities_DoNotImplyEachOther", () =>
{
    var sendOnly = new VEExchangeCapabilities(
        Clipboard: false, SendFiles: true, ReceiveFiles: false,
        ReceiveFolder: false, DragDrop: false, InstallApk: false);
    Require(sendOnly.SendFiles && !sendOnly.ReceiveFiles, "Send implied receive.");
    Require(!sendOnly.DragDrop && !sendOnly.Clipboard && !sendOnly.InstallApk, "A capability implied another.");
    var noDrag = full with { DragDrop = false };
    Require(noDrag.SendFiles && noDrag.ReceiveFiles && !noDrag.DragDrop, "DragDrop is coupled to file transfer.");
});

await CheckAsync("SessionDispose_IsIdempotent", async () =>
{
    var transport = new FakeTransport(VEExchangeRoute.UsbAppControl, full);
    var session = new VEExchangeSession(1, null, transport);
    await session.DisposeAsync();
    await session.DisposeAsync();
    Require(transport.DisposeCount == 1, $"Transport disposed {transport.DisposeCount} times.");
    Require(session.IsDisposedVE, "Session is not marked disposed.");
});

await CheckAsync("Router_RejectsStaleGeneration", async () =>
{
    var router = new VEExchangeRouter();
    var newTransport = new FakeTransport(VEExchangeRoute.NativeLan, full);
    router.Bind(new VEExchangeSession(1, null, new FakeTransport(VEExchangeRoute.UsbAppControl, full)));
    router.Bind(new VEExchangeSession(2, null, newTransport));

    Require(!router.TryGet(1, out _), "Stale generation resolved.");
    Require(router.TryGet(2, out var current) && current!.Transport == newTransport, "Current session lost.");

    await router.ClearAsync(1);
    Require(router.TryGet(2, out _), "A stale clear removed the new session.");

    bool replacedByOlder = true;
    try { router.Bind(new VEExchangeSession(1, null, new FakeTransport(VEExchangeRoute.UsbAppControl, full))); }
    catch (InvalidOperationException) { replacedByOlder = false; }
    Require(!replacedByOlder, "An older generation replaced the current session.");

    await router.ClearAsync(2);
    Require(router.Current is null && newTransport.DisposeCount == 1, "Clearing the current session did not dispose its transport.");
});

Console.WriteLine($"VisionEngine Exchange: {passed} PASS / {failed} FAIL");
return failed == 0 ? 0 : 1;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}

async Task CheckAsync(string name, Func<Task> test)
{
    try { await test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}

sealed class FakeTransport(VEExchangeRoute route, VEExchangeCapabilities capabilities) : IVEExchangeTransport
{
    public VEExchangeRoute Route { get; } = route;
    public VEExchangeCapabilities Capabilities { get; } = capabilities;
    public int DisposeCount { get; private set; }

    public Task<VEExchangeResult> SendPathsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
        => Task.FromResult(VEExchangeResult.OkVE("sent"));

    public Task<VEExchangeResult> PullFileAsync(string remotePath, CancellationToken cancellationToken)
        => Task.FromResult(VEExchangeResult.OkVE("pulled"));

    public Task<VEExchangeResult> PullNovoraFolderAsync(CancellationToken cancellationToken)
        => Task.FromResult(VEExchangeResult.OkVE("folder"));

    public Task<VEExchangeApkInstallResult> InstallApkAsync(string androidPath, CancellationToken cancellationToken)
        => Task.FromResult(new VEExchangeApkInstallResult(VEExchangeApkInstallState.Pending, "pending"));

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}
