using NOVORA.Control;
using System.IO;
using System.Linq;
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

await CheckAsync("UsbPush_UsesBackendSerialAndReturnsDestination", async () =>
{
    var backend = new FakeUsbBackend
    {
        PushHandler = (serial, local, ct) => Task.FromResult("/storage/emulated/0/Download/foto.jpg")
    };
    string root = NewRoot();
    try
    {
        string local = Path.Combine(root, "foto.jpg");
        File.WriteAllBytes(local, [1, 2, 3]);
        var transport = new VEExchangeUsbTransport(backend, " usb-A ", full, root);
        VEExchangeResult result = await transport.SendPathsAsync([local], CancellationToken.None);
        Require(result.Success, "Push failed: " + result.Message);
        Require(backend.Serials.Single() == "usb-A", "Serial was not trimmed and forwarded.");
        Require(result.Destination == "/storage/emulated/0/Download/foto.jpg", "Destination was not returned.");
        Require(result.ItemCount == 1 && result.Bytes == 3, "Counts were not reported.");
    }
    finally { DeleteRoot(root); }
});

await CheckAsync("UsbPull_CommitsIntoNovoraFiles", async () =>
{
    var backend = new FakeUsbBackend
    {
        PullHandler = (serial, remote, local, ct) => { File.WriteAllBytes(local, [9, 8, 7, 6]); return Task.CompletedTask; }
    };
    string root = NewRoot();
    try
    {
        var transport = new VEExchangeUsbTransport(backend, "usb-A", full, root);
        VEExchangeResult result = await transport.PullFileAsync("/storage/emulated/0/Download/nota.txt", CancellationToken.None);
        Require(result.Success, "Pull failed: " + result.Message);
        Require(File.Exists(result.Destination!), "Committed file is missing.");
        Require(File.ReadAllBytes(result.Destination!).SequenceEqual(new byte[] { 9, 8, 7, 6 }), "Committed bytes differ.");
        Require(Directory.EnumerateFiles(root, ".novora-*.partial", SearchOption.AllDirectories).Count() == 0, "Partial file left behind.");
    }
    finally { DeleteRoot(root); }
});

await CheckAsync("UsbFolderPull_PreservesRelativePaths", async () =>
{
    var backend = new FakeUsbBackend
    {
        PullFolderHandler = (serial, remote, local, ct) =>
        {
            Directory.CreateDirectory(Path.Combine(local, "NOVORA", "Proyecto", "sub"));
            File.WriteAllText(Path.Combine(local, "NOVORA", "Proyecto", "sub", "x.txt"), "hola");
            return Task.CompletedTask;
        }
    };
    string root = NewRoot();
    try
    {
        var transport = new VEExchangeUsbTransport(backend, "usb-A", full, root);
        VEExchangeResult result = await transport.PullNovoraFolderAsync(CancellationToken.None);
        Require(result.Success, "Folder pull failed: " + result.Message);
        string[] matches = Directory.GetFiles(root, "x.txt", SearchOption.AllDirectories);
        Require(matches.Length == 1, "Folder file missing.");
        Require(matches[0].Replace('\\', '/').Contains("/Proyecto-", StringComparison.Ordinal)
            && matches[0].Replace('\\', '/').EndsWith("/sub/x.txt", StringComparison.Ordinal),
            "Relative structure was not preserved: " + matches[0]);
    }
    finally { DeleteRoot(root); }
});

await CheckAsync("UsbOperations_PropagateCancellation", async () =>
{
    var backend = new FakeUsbBackend
    {
        PullHandler = async (serial, remote, local, ct) =>
        {
            File.WriteAllBytes(local, [1]);
            await Task.Delay(Timeout.Infinite, ct);
        }
    };
    string root = NewRoot();
    try
    {
        var transport = new VEExchangeUsbTransport(backend, "usb-A", full, root);
        using var cts = new CancellationTokenSource(50);
        bool cancelled = false;
        try { await transport.PullFileAsync("/storage/emulated/0/Download/a.bin", cts.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled, "Cancellation was swallowed.");
        Require(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count() == 0, "Partial survived cancellation.");
    }
    finally { DeleteRoot(root); }
});

Check("UsbBackend_RejectsLanEndpoint", () =>
{
    bool rejected = false;
    try { _ = new VEExchangeUsbTransport(new FakeUsbBackend(), "LAN:192.168.1.20:5555", full, NewRoot()); }
    catch (ArgumentException) { rejected = true; }
    Require(rejected, "A LAN endpoint was accepted as an ADB serial.");
});

await CheckAsync("LanSend_ForwardsPathsAndReportsCount", async () =>
{
    var files = new FakeLanFiles { SendResult = 2 };
    await using var lan = new VEExchangeLanTransport(files, full);
    VEExchangeResult result = await lan.SendPathsAsync(["a.txt", "b.txt"], CancellationToken.None);
    Require(result.Success && result.ItemCount == 2, "LAN send did not report the count.");
    Require(files.SentPaths.SequenceEqual(["a.txt", "b.txt"]), "Paths were not forwarded.");
});

await CheckAsync("LanReceive_ReportsConfirmedDestination", async () =>
{
    var files = new FakeLanFiles();
    await using var lan = new VEExchangeLanTransport(files, full);
    VEExchangeResult? seen = null;
    lan.ReceivedVE += result => seen = result;
    files.Raise(new NLFileTransferResultV2(Guid.NewGuid(), 42, new byte[32], "C:/NOVORA-Files/Documentos/a.txt"));
    Require(seen is { Success: true, Destination: "C:/NOVORA-Files/Documentos/a.txt", Bytes: 42 },
        "Confirmed receive was not reported with its destination.");
});

await CheckAsync("LanPull_IsNotPcInitiated", async () =>
{
    var files = new FakeLanFiles();
    await using var lan = new VEExchangeLanTransport(files, full);
    VEExchangeResult file = await lan.PullFileAsync("/sdcard/Download/a.txt", CancellationToken.None);
    VEExchangeResult folder = await lan.PullNovoraFolderAsync(CancellationToken.None);
    Require(!file.Success && !folder.Success, "LAN pulled a file without phone initiation.");
    Require(files.SentPaths.Count == 0, "A pull reached the LAN send path.");
});

await CheckAsync("LanCapabilities_GateSendAndReceive", async () =>
{
    var files = new FakeLanFiles();
    var sendOff = full with { SendFiles = false };
    await using var lan = new VEExchangeLanTransport(files, sendOff);
    VEExchangeResult result = await lan.SendPathsAsync(["a.txt"], CancellationToken.None);
    Require(!result.Success && files.SentPaths.Count == 0, "Disabled send capability still sent.");
});

await CheckAsync("LanPrivacy_BlocksBeforeEngine", async () =>
{
    var files = new FakeLanFiles();
    await using var lan = new VEExchangeLanTransport(files, full, () => false);
    VEExchangeResult result = await lan.SendPathsAsync(["a.txt"], CancellationToken.None);
    Require(!result.Success && files.SentPaths.Count == 0, "Privacy did not block the LAN send.");
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

static string NewRoot()
{
    string root = Path.Combine(Path.GetTempPath(), "novora-exchange-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
}

static void DeleteRoot(string root)
{
    try { Directory.Delete(root, true); } catch (IOException) { }
}

sealed class FakeUsbBackend : IVEExchangeUsbBackend
{
    public List<string> Serials { get; } = [];
    public Func<string, string, CancellationToken, Task<string>> PushHandler { get; init; } =
        (_, _, _) => Task.FromResult("/storage/emulated/0/Download/unset");
    public Func<string, string, string, CancellationToken, Task> PullHandler { get; init; } =
        (_, _, _, _) => Task.CompletedTask;
    public Func<string, string, string, CancellationToken, Task> PullFolderHandler { get; init; } =
        (_, _, _, _) => Task.CompletedTask;

    public Task<string> PushAsync(string serial, string localPath, CancellationToken cancellationToken)
    {
        Serials.Add(serial);
        return PushHandler(serial, localPath, cancellationToken);
    }

    public Task PullAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken)
    {
        Serials.Add(serial);
        return PullHandler(serial, remotePath, localPath, cancellationToken);
    }

    public Task PullFolderAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken)
    {
        Serials.Add(serial);
        return PullFolderHandler(serial, remotePath, localPath, cancellationToken);
    }
}

sealed class FakeLanFiles : IVEExchangeLanFiles
{
    public List<string> SentPaths { get; } = [];
    public int SendResult { get; init; } = 1;
    public event Action<NLFileTransferResultV2>? Received;

    public Task<int> SendPathsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        SentPaths.AddRange(paths);
        return Task.FromResult(SendResult);
    }

    public void Raise(NLFileTransferResultV2 result) => Received?.Invoke(result);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
