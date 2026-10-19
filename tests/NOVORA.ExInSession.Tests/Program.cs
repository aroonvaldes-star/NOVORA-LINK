using NOVORA.ExInEngine;
using NOVORA.Integration;
using NOVORA.Service;
using System.Reflection;

int passed = 0, failed = 0;
await Check("startup failures recover without a manual command", async () =>
{
    var adb = new NLServiceADB();
    var engine = new ExInCoreEngine { FailAttachCount = 3 };
    var clock = new ControlledTime();
    await using var session = Create(engine, adb, clock);
    try { await session.StartAsync("usb-A"); } catch (IOException) { }
    for (int i = 0; i < 5 && !session.IsRunning; i++)
        Require(await clock.TickAsync(), "No automatic retry after startup failure.");
    Require(session.IsRunning && engine.AttachAttempts >= 4, "The HID output did not reconnect.");
    Require(adb.Outputs.Where(o => !ReferenceEquals(o, engine.Bound)).All(o => o.Disposed), "Failed output leaked.");
});
await Check("transport outage retries and keeps the phone identity", async () =>
{
    var adb = new NLServiceADB(); var engine = new ExInCoreEngine(); var clock = new ControlledTime();
    await using var session = Create(engine, adb, clock);
    await session.StartAsync("usb-A");
    adb.Devices = [new("wifi-B", true, true)]; adb.Identity["wifi-B"] = "phone-B";
    engine.Bound!.Fail();
    int originalAttempts = engine.AttachAttempts;
    Require(await clock.TickAsync(), "No transport supervisor is running.");
    Require(await clock.TickAsync(), "Transport failure exhausted recovery.");
    Require(engine.AttachAttempts == originalAttempts, "Recovery selected a different phone.");
    adb.Devices = [new("wifi-A", true, true)]; adb.Identity["wifi-A"] = "phone-A";
    for (int i = 0; i < 4 && !session.IsRunning; i++) Require(await clock.TickAsync(), "Recovery stopped before phone returned.");
    Require(session.IsRunning && engine.Bound?.Serial == "wifi-A", "Same-phone Wi-Fi transport was not recovered.");
});
await Check("stop cancels an in-progress transport lookup", async () =>
{
    var adb = new NLServiceADB(); var engine = new ExInCoreEngine(); var clock = new ControlledTime();
    await using var session = Create(engine, adb, clock);
    await session.StartAsync("usb-A");
    adb.BlockLookup = true;
    engine.Bound!.Fail();
    Task tick = clock.TickAsync();
    await adb.LookupBlocked.Task.WaitAsync(TimeSpan.FromSeconds(1));
    await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));
    await tick;
    Require(!session.IsRunning && engine.Bound is null, "Stop left a live or late HID binding.");
    int attempts = engine.AttachAttempts;
    adb.BlockLookup = false;
    await clock.TickAsync(TimeSpan.FromMilliseconds(150));
    Require(engine.AttachAttempts == attempts, "Stop allowed a late reconnect.");
});
await Check("stop cancels initial HID attachment before the UI timeout", async () =>
{
    var adb=new NLServiceADB(); var engine=new ExInCoreEngine { BlockAttach=true }; var clock=new ControlledTime();
    await using var session=Create(engine,adb,clock);
    using var startCancellation=new CancellationTokenSource();
    Task start=session.StartAsync("usb-A",startCancellation.Token);
    await engine.AttachBlocked.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Task stop=session.StopAsync();
    try { await stop.WaitAsync(TimeSpan.FromSeconds(1)); }
    finally
    {
        startCancellation.Cancel();
        try { await start; } catch(OperationCanceledException) { }
        await stop;
    }
    Require(engine.Bound is null && !session.HasRequestedTarget,"Stop retained a pending output or reconnect intent.");
});
await Check("disabled engine never reconnects", async () =>
{
    var adb = new NLServiceADB(); var engine = new ExInCoreEngine(); var clock = new ControlledTime();
    await using var session = Create(engine, adb, clock);
    await session.StartAsync("usb-A");
    int attempts = engine.AttachAttempts;
    engine.IsEnabledVE = false; engine.Bound!.Fail();
    await clock.TickAsync(TimeSpan.FromMilliseconds(150));
    Require(engine.AttachAttempts == attempts, "Disabled ExIn reattached an output.");
});
await Check("readiness polling recovers even without a failure event", async () =>
{
    var adb=new NLServiceADB(); var engine=new ExInCoreEngine(); var clock=new ControlledTime();
    await using var session=Create(engine,adb,clock);
    await session.StartAsync("usb-A");
    ExInHidOutput failed=engine.Bound!; failed.FailedState=true;
    for(int i=0;i<3 && ReferenceEquals(engine.Bound,failed);i++) Require(await clock.TickAsync(),"No readiness monitor.");
    Require(session.IsRunning && !ReferenceEquals(engine.Bound,failed),"Missed notification prevented recovery.");
});
await Check("reenable racing with supervisor exit does not lose the start request", async () =>
{
    var exitEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var allowExit=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondAttempt=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    int checks=0, attempts=0;
    bool ShouldRun()
    {
        if(Interlocked.Increment(ref checks)==3) {exitEntered.TrySetResult();allowExit.Task.GetAwaiter().GetResult();return false;}
        return true;
    }
    var supervisor=new ExInConnectionSupervisor(_ => {if(Interlocked.Increment(ref attempts)==2)secondAttempt.TrySetResult();return Task.CompletedTask;},ShouldRun,new ControlledTime());
    try
    {
        supervisor.Start();
        await exitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        supervisor.Start();
        allowExit.TrySetResult();
        await secondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
    finally {allowExit.TrySetResult();await supervisor.StopAsync();}
});
Console.WriteLine($"ExIn automatic session: {passed} PASS / {failed} FAIL");
return failed == 0 ? 0 : 1;

async Task Check(string name, Func<Task> test)
{
    try { await test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}"); }
}
static void Require(bool value, string why) { if (!value) throw new Exception(why); }
static ExInControlSession Create(ExInCoreEngine engine, NLServiceADB adb, TimeProvider clock)
{
    var args = new object[] { engine, new NLServiceNovoraPaths(), adb, clock };
    ConstructorInfo? timed = typeof(ExInControlSession).GetConstructor([typeof(ExInCoreEngine), typeof(NLServiceNovoraPaths), typeof(NLServiceADB), typeof(TimeProvider)]);
    return timed is null ? new(engine, new NLServiceNovoraPaths(), adb) : (ExInControlSession)timed.Invoke(args);
}
