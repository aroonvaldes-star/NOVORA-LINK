using System.Reflection;
using NOVORA.Contracts.Input;
using NOVORA.ExInEngine;
using NOVORA.Service;

var tests = new (string Name, Func<Task> Run)[]
{
    ("observed Down and Up reach the gamepad in order", Buttons),
    ("queued SDL button payload survives a newer snapshot", ButtonPayload),
    ("event consumer preserves Down and Up while writer is blocked", EventConsumer),
    ("concurrent removal leaves other slot transmitting", Remove),
    ("unexpected send failure is reported as Failed", WorkerFailure),
    ("unexpected event stream completion fails the engine", EventCompletion),
    ("transient worker failure recovers without a manual action", Recovery),
    ("recovery continues after two unsuccessful starts", RecoveryBeyondTwo),
    ("worker recovery does not require a ready transport", RecoveryWithoutReady),
    ("repeated immediate worker failures retain backoff", RecoveryBackoff),
    ("disable cancels pending automatic recovery", Disable),
    ("disable cancels an output attachment already creating a device", DisableDuringAttachment),
    ("dispose cancels and joins an output attachment already creating a device", DisposeDuringAttachment),
    ("dispose cancels pending automatic recovery", Dispose),
    ("Stop prevents automatic restart", Stop),
    ("two queued mode toggles return to Game", ModeToggles),
    ("privacy discards queued input before resuming", Privacy),
    ("calibration emits neutral and discards old edges", Calibration),
    ("failed output binding allows the next target", Binding),
    ("failed first report retires the registered slot", OpenFailure),
    ("failed detach recreates the output on the next binding", DetachFailure),
    ("controller selection follows the chosen pad and can mute the others", Selection),
    ("same-model controllers never adopt or delete another calibration", ProfilesStayWithController),
};
int failures = 0;
var selected = args.Length == 0 ? tests : tests.Where(test => test.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase)).ToArray();
foreach (var test in selected)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
}
Console.WriteLine($"ExIn runtime: {selected.Length - failures} PASS / {failures} FAIL");
return failures == 0 ? 0 : 1;

static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Until(Func<bool> condition, string message, int timeoutMs = 1200)
{
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(5);
    Assert(condition(), message);
}
static Task Invoke(ExInManager manager, string method, params object[] args)
{
    object? result = typeof(ExInManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, args);
    return result as Task ?? Task.CompletedTask;
}
static async Task<ExInManager> Create(Sink sink, params uint[] ids)
{
    ExInSdl.InitialIds = ids;
    var manager = new ExInManager(sink, new NLServiceNovoraPaths());
    await manager.StartAsync();
    return manager;
}
static Task ProfilesStayWithController()
{
    string directory = Path.Combine(Path.GetTempPath(), "novora-exin-profiles-" + Guid.NewGuid().ToString("N"));
    const string first = "exin-045e-028e-first";
    const string second = "exin-045e-028e-second";
    try
    {
        var store = new ExInProfileStore(directory);
        var center = new ExInState(0, 0, 0, 0, 0, 0, ExInButtons.None);
        var minimum = new ExInState(-20000, -20000, -20000, -20000, 0, 0, ExInButtons.None);
        var maximum = new ExInState(20000, 20000, 20000, 20000, 20000, 20000, ExInButtons.None);
        Assert(store.SaveVE(ExInCalibrationProfile.CreateVE(first, center, minimum, maximum)), "fixture profile was not saved");
        Assert(store.SavePaddleMappingsVE(first, ExInPaddleMappings.DefaultVE with { P1 = ExInButtons.South }), "fixture paddles were not saved");

        Assert(store.LoadVE(second, Array.Empty<string>()) is null, "a different controller adopted calibration by model");
        Assert(File.Exists(Path.Combine(directory, first + ".json")), "the first controller profile was deleted");
        Assert(File.Exists(Path.Combine(directory, first + ".paddles.json")), "the first controller paddles were moved");
        Assert(!File.Exists(Path.Combine(directory, second + ".json")), "a profile was created for the second controller");
        return Task.CompletedTask;
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
static ExInEvent Button(uint id, bool pressed) => new(ExInTypeEvent.Button, id, 0, 0, 0, pressed);
static async Task Buttons()
{
    var sink = new Sink();
    await using var manager = await Create(sink, 1);
    var block = sink.BlockNext(3);
    await block.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
    ExInSdl.Last.States[1] = default(ExInState) with { Buttons = ExInButtons.South };
    await Invoke(manager, "ApplyButtonVE", Button(1, true), CancellationToken.None);
    ExInSdl.Last.States[1] = default;
    await Invoke(manager, "ApplyButtonVE", Button(1, false), CancellationToken.None);
    sink.Clear();
    block.Release.TrySetResult();
    await Until(() => sink.Reports.Length >= 4, "worker did not resume");
    var buttons = sink.Reports.Where(r => r.Id == 3 && r.Bytes.Length == 15).Select(r => (r.Bytes[12] & 1) != 0).ToArray();
    int down = Array.IndexOf(buttons, true);
    Assert(down >= 0 && buttons.Skip(down + 1).Contains(false), "lost observed button press/release");
}
static async Task ButtonPayload()
{
    var sink = new Sink();
    await using var manager = await Create(sink, 1);
    var block = sink.BlockNext(3);
    await block.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
    // Native state is already released when SDL's older Down and Up reach Manager.
    ExInSdl.Last.States[1] = default;
    await Invoke(manager, "ApplyButtonVE", Button(1, true), CancellationToken.None);
    await Invoke(manager, "ApplyButtonVE", Button(1, false), CancellationToken.None);
    sink.Clear(); block.Release.TrySetResult();
    await Until(() => sink.Reports.Length >= 4, "worker did not resume");
    Assert(sink.Reports.Any(r => r.Id == 3 && r.Bytes.Length == 15 && (r.Bytes[12] & 1) != 0), "SDL event payload was discarded in favor of current state");
}
static async Task EventConsumer()
{
    var sink = new Sink();
    await using var manager = await Create(sink, 1);
    var block = sink.BlockNext(3);
    await block.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
    ExInSdl.Last.Events.Writer.TryWrite(Button(1, true));
    ExInSdl.Last.Events.Writer.TryWrite(Button(1, false));
    try { await Until(() => manager.LiveSnapshotVE.Sequence >= 2, "consumer did not observe both events"); }
    finally { block.Release.TrySetResult(); }
    await Until(() =>
    {
        var buttons = sink.Reports.Where(r => r.Id == 3 && r.Bytes.Length == 15).Select(r => (r.Bytes[12] & 1) != 0).ToArray();
        int down = Array.IndexOf(buttons, true);
        return down >= 0 && buttons.Skip(down + 1).Contains(false);
    }, "consumer lost Down or the following Up before periodic output");
}
static async Task Remove()
{
    var sink = new Sink();
    await using var manager = await Create(sink, 1, 2);
    var block = sink.BlockNext(3);
    await block.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Task removal = Invoke(manager, "RemoveDeviceVE", (uint)2, CancellationToken.None);
    block.Release.TrySetResult();
    await removal.WaitAsync(TimeSpan.FromSeconds(1));
    int count = sink.Reports.Count(r => r.Id == 3);
    await Until(() => sink.Reports.Count(r => r.Id == 3) > count + 2, "removing slot 2 killed output for slot 1");
}
static async Task Selection()
{
    var sink = new Sink();
    await using var manager = await Create(sink, 1, 2);
    await Until(() => manager.ControllersVE.Count == 2, "both pads were not offered");
    Assert(manager.SelectedControllerVE == 1, "default selection is not the lowest instance");
    Assert(manager.SelectControllerVE(2) && manager.SelectedControllerVE == 2, "selection did not move to pad 2");
    Assert(!manager.SelectControllerVE(9), "an unknown pad was accepted");
    manager.ForwardSelectedOnlyVE = true;
    await Task.Delay(100);
    ushort first = 3;
    int mutedBefore = sink.Reports.Count(r => r.Id == first);
    int othersBefore = sink.Reports.Count(r => r.Id != first);
    await Task.Delay(200);
    Assert(sink.Reports.Count(r => r.Id == first) == mutedBefore, "a deselected pad kept transmitting");
    Assert(sink.Reports.Count(r => r.Id != first) > othersBefore, "the selected pad stopped transmitting");
    manager.ForwardSelectedOnlyVE = false;
    await Until(() => sink.Reports.Count(r => r.Id == first) > mutedBefore, "unmuting did not resume the pad");
}
static async Task WorkerFailure()
{
    var sink = new Sink();
    await using var manager = await Create(sink, 1);
    sink.FailSend = true;
    await Until(() => manager.StatusVE.State == ExInStates.Failed, "worker terminated without Failed");
    await Invoke(manager, "MaybePublishVE");
    Assert(manager.StatusVE.State == ExInStates.Failed, "event publication hid the failed worker");
}
static async Task EventCompletion()
{
    var sink = new Sink();
    await using var manager = await Create(sink, 1);
    ExInSdl.Last.Events.Writer.TryComplete();
    await Until(() => manager.StatusVE.State == ExInStates.Failed, "completed native event source left engine Running");
}
static async Task<ExInCoreEngine> Core(Sink sink)
{
    ExInSdl.InitialIds = [1];
    var core = new ExInCoreEngine(new NLServiceNovoraPaths());
    await core.AttachOutputAsync(sink);
    await core.InitializeAsync();
    return core;
}
static async Task Recovery()
{
    var sink = new Sink();
    await using var core = await Core(sink);
    int initial = sink.Creates;
    sink.FailSend = true;
    await Until(() => sink.Creates > initial && core.Status.State == ExInStates.Running, "worker did not recover automatically", 3000);
}
static async Task RecoveryBeyondTwo()
{
    var sink = new Sink();
    await using var core = await Core(sink);
    int initial = sink.Creates;
    ExInSdl.Last.FailInitializations = 2;
    sink.FailSend = true;
    await Until(() => sink.Creates > initial && core.Status.State == ExInStates.Running, "automatic recovery stopped after two failures", 12000);
}
static async Task RecoveryWithoutReady()
{
    var sink = new Sink();
    await using var core = await Core(sink);
    int initializes = ExInSdl.Last.InitializeCount;
    sink.CloseOnFailure = true; sink.FailSend = true;
    await Until(() => ExInSdl.Last.InitializeCount > initializes, "not-ready transport suppressed native worker recovery", 2500);
    sink.IsReady = true;
    await core.AttachOutputAsync(sink);
    Assert(sink.Creates >= 2, "transport reattachment did not create the new generations");
}
static async Task RecoveryBackoff()
{
    var sink = new Sink { FailFirstPeriodicReport = true };
    await using var core = await Core(sink);
    await Until(() => sink.Creates >= 2, "first automatic retry was not reached", 2500);
    int creates = sink.Creates;
    await Task.Delay(2000);
    Assert(sink.Creates == creates, "successful Start reset backoff before failed first periodic report");
}
static async Task Disable()
{
    var sink = new Sink();
    await using var core = await Core(sink);
    sink.FailSend = true;
    await Until(() => sink.SendFailureObserved, "fixture send failure not reached");
    await core.SetEnabledAsync(false);
    int creates = sink.Creates;
    await Task.Delay(1300);
    Assert(sink.Creates == creates && core.Status.State == ExInStates.Stopped, "disable reconnected the output");
}
static async Task DisableDuringAttachment()
{
    ExInSdl.InitialIds = [1];
    await using var core = new ExInCoreEngine(new NLServiceNovoraPaths());
    await core.InitializeAsync();
    var sink = new Sink { BlockCreate = true };
    Task attach = core.AttachOutputAsync(sink);
    await sink.CreateEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Task disable = core.SetEnabledAsync(false);
    bool immediate = !core.IsEnabledVE;
    Task first = await Task.WhenAny(attach, Task.Delay(250));
    bool cancelled = ReferenceEquals(first, attach) && attach.IsCanceled;
    sink.CreateRelease.TrySetResult();
    try { await attach; } catch (OperationCanceledException) { }
    await disable.WaitAsync(TimeSpan.FromSeconds(1));
    Assert(immediate, "disable intent stayed enabled while lifecycle was occupied");
    Assert(cancelled && sink.Reports.Length == 0, "attachment completed after disable instead of being cancelled");
}
static async Task DisposeDuringAttachment()
{
    ExInSdl.InitialIds = [1];
    var core = new ExInCoreEngine(new NLServiceNovoraPaths());
    await core.InitializeAsync();
    var sink = new Sink { BlockCreate = true };
    Task attach = core.AttachOutputAsync(sink);
    await sink.CreateEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Task dispose = core.DisposeAsync().AsTask();
    Task first = await Task.WhenAny(dispose, Task.Delay(250));
    bool disposedWithoutFinishingCreate = ReferenceEquals(first, dispose);
    sink.CreateRelease.TrySetResult();
    try { await attach; } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
    await dispose.WaitAsync(TimeSpan.FromSeconds(1));
    Assert(disposedWithoutFinishingCreate && attach.IsCanceled, "dispose did not cancel and join attachment before destroying the manager");
    Assert(sink.Reports.Length == 0, "attachment emitted state after dispose intent");
}
static async Task Dispose()
{
    var sink = new Sink();
    var core = await Core(sink);
    sink.FailSend = true;
    await Until(() => sink.SendFailureObserved, "fixture send failure not reached");
    await core.DisposeAsync();
    int creates = sink.Creates;
    await Task.Delay(1300);
    Assert(sink.Creates == creates, "dispose reconnected the output");
}
static async Task Stop()
{
    var sink = new Sink();
    await using var core = await Core(sink);
    sink.FailSend = true;
    await Until(() => sink.SendFailureObserved, "fixture send failure not reached");
    await core.Manager.StopAsync();
    int creates = sink.Creates;
    await Task.Delay(1300);
    Assert(sink.Creates == creates && core.Status.State == ExInStates.Stopped, "Stop restarted the output");
}
static async Task ModeToggles()
{
    var sink = new Sink();
    await using var manager = await Create(sink, 1);
    var modeGate = (SemaphoreSlim)typeof(ExInManager).GetField("_modeTransitionVE", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
    int creates = sink.Creates;
    await modeGate.WaitAsync();
    Task one = Task.CompletedTask, two = Task.CompletedTask;
    try
    {
        async Task Edge(byte button, bool down)
        {
            ExInButtons flag = button == 4 ? ExInButtons.Back : ExInButtons.Start;
            ExInState state = ExInSdl.Last.States[1];
            ExInSdl.Last.States[1] = state with { Buttons = down ? state.Buttons | flag : state.Buttons & ~flag };
            await Invoke(manager, "ApplyButtonVE", new ExInEvent(ExInTypeEvent.Button, 1, 0, 0, button, down), CancellationToken.None);
        }
        await Edge(4, true); one = Edge(6, true);
        await Edge(4, false); await Edge(6, false);
        await Edge(4, true); two = Edge(6, true);
    }
    finally { modeGate.Release(); }
    await Task.WhenAll(one, two).WaitAsync(TimeSpan.FromSeconds(1));
    await Until(() => sink.Creates >= creates + 2, "two toggles did not create both destination generations");
    Assert(manager.ModeVE == ExInInputMode.Game, "two toggles collapsed into one transition");
}
static async Task BlockedInput(bool calibration)
{
    var sink = new Sink();
    await using var manager = await Create(sink, 1);
    var block = sink.BlockNext(3);
    await block.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
    ExInSdl.Last.States[1] = default(ExInState) with { Buttons = ExInButtons.South };
    await Invoke(manager, "ApplyButtonVE", Button(1, true), CancellationToken.None);
    if (calibration) Assert(manager.BeginCalibrationVE(), "calibration was not started");
    else manager.SetPrivacyProtectedVE(true);
    ExInSdl.Last.States[1] = default;
    await Invoke(manager, "ApplyButtonVE", Button(1, false), CancellationToken.None);
    sink.Clear(); block.Release.TrySetResult();
    await Until(() => sink.Reports.Length >= 4, "neutral output did not resume");
    Assert(sink.Reports.All(r => r.Bytes.Length != 15 || (r.Bytes[12] & 1) == 0), "queued input escaped neutral protection");
    if (calibration) manager.ResetCalibrationVE(); else manager.SetPrivacyProtectedVE(false);
    sink.Clear();
    await Until(() => sink.Reports.Length >= 4, "unprotected output did not resume");
    Assert(sink.Reports.All(r => r.Bytes.Length != 15 || (r.Bytes[12] & 1) == 0), "stale edge replayed after protection");
}
static Task Privacy() => BlockedInput(false);
static Task Calibration() => BlockedInput(true);
static async Task Binding()
{
    ExInSdl.InitialIds = [1];
    await using var core = new ExInCoreEngine(new NLServiceNovoraPaths());
    await core.InitializeAsync();
    var bad = new Sink { FailSend = true };
    try { await core.AttachOutputAsync(bad); throw new Exception("fixture attachment should fail"); }
    catch (IOException) { }
    var good = new Sink();
    await core.AttachOutputAsync(good);
    await Until(() => good.Reports.Length >= 3, "failed binding retained old target");
}
static async Task OpenFailure()
{
    ExInSdl.InitialIds = [1];
    var sink = new Sink { FailSendNumber = 2 };
    await using var manager = new ExInManager(sink, new NLServiceNovoraPaths());
    try { await manager.StartAsync(); throw new Exception("first state send should fail"); }
    catch (IOException) { }
    int creates = sink.Creates;
    await manager.StartAsync();
    Assert(sink.Creates > creates, "closed native handle remained registered after first report failure");
}
static async Task DetachFailure()
{
    var sink = new Sink();
    await using var core = await Core(sink);
    var block = sink.BlockNext(3);
    await block.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Task detach = core.DetachOutputAsync(sink);
    // Fail neutral after the already-started worker report completes.
    sink.FailSendNumber = sink.SendCount + 2;
    block.Release.TrySetResult();
    await detach.WaitAsync(TimeSpan.FromSeconds(1));
    var replacement = new Sink();
    await core.AttachOutputAsync(replacement);
    Assert(replacement.Creates > 0, "failed detach left generation marked created");
}

sealed class Sink : INLInputOutput
{
    readonly object gate = new();
    readonly List<(ushort Id, byte[] Bytes)> reports = [];
    public (ushort Id, byte[] Bytes)[] Reports { get { lock (gate) return reports.ToArray(); } }
    public bool IsReady { get; set; } = true;
    public volatile bool FailSend;
    public volatile bool SendFailureObserved;
    public int Creates;
    public int SendCount;
    public int FailSendNumber;
    public bool CloseOnFailure;
    public bool FailFirstPeriodicReport;
    public bool BlockCreate;
    public TaskCompletionSource CreateEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CreateRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Block? next;
    public Block BlockNext(ushort id) { lock (gate) return next = new Block(id); }
    public void Clear() { lock (gate) reports.Clear(); }
    public async Task CreateAsync(NLInputDevice device, byte[] descriptor, CancellationToken token)
    {
        Interlocked.Increment(ref Creates);
        if (FailFirstPeriodicReport) FailSendNumber = SendCount + 3;
        if (BlockCreate) { CreateEntered.TrySetResult(); await CreateRelease.Task.WaitAsync(token); }
    }
    public async Task SendAsync(ushort id, byte[] report, CancellationToken token)
    {
        Block? block = null;
        lock (gate)
        {
            if (next?.Id == id) { block = next; next = null; }
        }
        if (block is not null) { block.Entered.TrySetResult(); await block.Release.Task.WaitAsync(token); }
        int number = Interlocked.Increment(ref SendCount);
        if (FailSend || number == FailSendNumber)
        {
            FailSend = false; SendFailureObserved = true;
            if (CloseOnFailure) IsReady = false;
            throw new IOException("transient output send failure");
        }
        lock (gate) reports.Add((id, report.ToArray()));
    }
    public Task DestroyAsync(ushort id, CancellationToken token) => Task.CompletedTask;
    public sealed record Block(ushort Id)
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
