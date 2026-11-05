using NOVORA.LinkEngine.Network;

const string modeVariable = "NOVORA_RELAY_STARTUP_TEST_CHILD";
const string ready = "LE_READY {\"v\":1,\"shaping\":true,\"fairQueues\":true,\"queueTelemetry\":true}";
string? childMode = Environment.GetEnvironmentVariable(modeVariable);
if (childMode is not null)
{
    if (childMode == "stdout") Console.WriteLine(ready);
    if (childMode == "stderr") Console.Error.WriteLine(ready);
    if (childMode == "malformed") Console.WriteLine("LE_READY {\"v\":999}");
    await Task.Delay(TimeSpan.FromSeconds(30));
    return;
}

// A real child process speaks the existing stdout IPC contract. It deliberately
// opens no socket: readiness must come from this process, never a port probe.
string assembly = typeof(LENetworkRelay).Assembly.GetName().Name!;
string tools = Path.Combine(AppContext.BaseDirectory, "Tools", "LinkEngine");
Directory.CreateDirectory(tools);
string executable = Path.Combine(tools, LENetworkRelay.ExecutableLE);
string apphost = Path.Combine(AppContext.BaseDirectory, assembly + (OperatingSystem.IsWindows() ? ".exe" : ""));
File.Copy(apphost, executable, overwrite: true);
foreach (string suffix in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
    File.Copy(Path.Combine(AppContext.BaseDirectory, assembly + suffix), Path.Combine(tools, assembly + suffix), true);
if (!OperatingSystem.IsWindows())
    File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

try
{
    Environment.SetEnvironmentVariable(modeVariable, "stdout");
    await using (var relay = new LENetworkRelay())
    {
        await relay.StartAsync();
        Assert(relay.IsRunningLE, "The child acknowledged readiness but was not reported running.");
        await relay.StopAsync();
        Assert(!relay.IsRunningLE && !relay.SupportsPolicyLE, "Stop retained stale readiness.");
    }
    Console.WriteLine("PASS: stdout child readiness and stop reset.");

    string[] rejectedModes = ["silent", "stderr", "malformed", "foreign-port"];
    foreach (string mode in rejectedModes)
    {
        // An unrelated listener must never authorize a silent child.
        using var unrelated = mode == "foreign-port"
            ? new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, LENetworkRelay.PortLE)
            : null;
        unrelated?.Start();
        Environment.SetEnvironmentVariable(modeVariable, mode);
        await using var relay = new LENetworkRelay();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
        bool canceled = false;
        try { await relay.StartAsync(timeout.Token); }
        catch (OperationCanceledException) { canceled = true; }
        Assert(canceled, $"{mode}: a child without valid stdout readiness was accepted.");
        Assert(!relay.IsRunningLE && !relay.SupportsPolicyLE, $"{mode}: canceled startup retained readiness.");
        Console.WriteLine($"PASS: rejects {mode} readiness and cleans up canceled startup.");
    }
}
finally
{
    Environment.SetEnvironmentVariable(modeVariable, null);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
