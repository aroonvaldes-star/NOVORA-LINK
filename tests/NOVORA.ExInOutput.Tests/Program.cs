using NOVORA.Integration;
using NOVORA.Service;
using System.Diagnostics;
using System.Reflection;

int failures = 0;
await Check("missing HID process signals failure once", async () =>
{
    await using var output = new ExInHidOutput(new(), new(), "usb-A");
    int events=0; output.Failed += (_,_) => events++;
    for (int i=0;i<2;i++) { try { await output.SendAsync(7,[0],default); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { } }
    Require(!output.IsReady && events==1, $"Ready={output.IsReady}, failure events={events}");
});
await Check("closed writer invalidates transport readiness", async () =>
{
    var start = new ProcessStartInfo("powershell.exe") { UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true };
    foreach (string arg in new[]{"-NoLogo","-NoProfile","-Command","[Console]::ReadLine() | Out-Null; Start-Sleep -Seconds 5"}) start.ArgumentList.Add(arg);
    using var process=Process.Start(start) ?? throw new Exception("Cannot start test pipe.");
    process.StandardInput.Close();
    await using var output = new ExInHidOutput(new(),new(),"usb-A");
    var devices=(Dictionary<ushort,Process>)typeof(ExInHidOutput).GetField("_devices",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(output)!;
    devices.Add(7,process);
    int events=0; output.Failed += (_,_) => events++;
    try { await output.SendAsync(7,[0],default); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
    Require(!output.IsReady && events==1,$"Closed pipe left Ready={output.IsReady}, failure events={events}");
});
await Check("caller cancellation does not invent a transport failure", async () =>
{
    await using var output = new ExInHidOutput(new(),new(),"usb-A");
    int events=0; output.Failed += (_,_) => events++;
    using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
    try { await output.SendAsync(7,[0],cancellation.Token); } catch (OperationCanceledException) { }
    Require(output.IsReady && events==0,"Cancellation falsely disconnected HID.");
});
Console.WriteLine($"ExIn HID output: {3-failures} PASS / {failures} FAIL");
return failures==0?0:1;
async Task Check(string name,Func<Task> test) { try {await test();Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;Console.WriteLine("FAIL "+name+": "+ex.Message);} }
static void Require(bool value,string reason) {if(!value)throw new Exception(reason);}
