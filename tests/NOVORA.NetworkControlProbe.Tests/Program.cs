using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NOVORA.Control;
using NOVORA.VisionEngine.Control;

void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
// This exercises the public channel without requiring a desktop/WPF build.
var configure=typeof(VEControlManager).GetMethod("ConfigureNetworkProbeVE");
Require(configure is not null,"RED: negotiated control RTT producer is absent");
var samplesProperty=typeof(VEControlManager).GetProperty("NetworkProbeSamplesVE");
Require(samplesProperty is not null,"Control RTT samples are unavailable");
var stopProbe=typeof(VEControlManager).GetMethod("StopNetworkProbeVE");
Require(stopProbe is not null,"Control RTT probe cannot be stopped independently");
void Configure(VEControlManager manager,long generation,string key,NLControlNetworkCapabilities? caps,TimeProvider clock) =>
    configure!.Invoke(manager,[generation,key,caps,clock]);
IReadOnlyList<NLNetworkSample> Samples(VEControlManager manager) => (IReadOnlyList<NLNetworkSample>)samplesProperty!.GetValue(manager)!;
async Task Until(Func<bool> condition,string reason)
{
    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));
    while(!condition()) { try {await Task.Delay(5,deadline.Token);}catch(OperationCanceledException){throw new Exception(reason);} }
}
using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(30));var ct=lifetime.Token;
using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
using var pc=new TcpClient();var connect=pc.ConnectAsync((IPEndPoint)listener.LocalEndpoint,ct);
using var android=await listener.AcceptTcpClientAsync(ct);await connect;
await using var manager=new VEControlManager();await manager.StartNativeAsync(pc.GetStream(),ct);
var clock=new ManualClock();
Configure(manager,42,"VE:LAN:device",null,clock);clock.Advance(3000);await Task.Delay(30,ct);
Require(android.Available==0,"Old Android received an unnegotiated echo");
Configure(manager,42,"VE:LAN:device",new(StabilityTelemetry:false),clock);clock.Advance(3000);await Task.Delay(30,ct);
Require(android.Available==0,"Peer without StabilityTelemetry received an echo");
manager.SetPrivacyGatesVE(message=>message.Type!=VEControlType.NetworkEcho,()=>true);
Configure(manager,42,"VE:LAN:device",new(StabilityTelemetry:true),clock);
await Task.Delay(30,ct);clock.Advance(1000);await Task.Delay(30,ct);
Require(android.Available==0,"RED: negotiated echo bypassed the existing protected-session control gate");
manager.SetPrivacyGatesVE(_=>true,()=>true);clock.Advance(1000);
var echo=await NLControlProtocol.ReadAsync<NLControlInputCommand>(android.GetStream(),ct);
Require(echo.Type==24 && echo.Sequence>0,"Negotiated probe did not use existing echo wire contract");
var clipboard=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
manager.ClipboardChangedVE+=(_,text)=>clipboard.TrySetResult(text);
await manager.SendAsync(VEControlMessage.TextVE("input while echo pending"),ct);
var input=await NLControlProtocol.ReadAsync<NLControlInputCommand>(android.GetStream(),ct);
Require(input.Type==1 && input.Text=="input while echo pending","Probe retained the input writer while awaiting echo");
clock.Advance(28);
await NLControlProtocol.WriteAsync(android.GetStream(),new NLControlInputResponse(4,Sequence:echo.Sequence+999),ct);
await NLControlProtocol.WriteAsync(android.GetStream(),new NLControlInputResponse(1,"clipboard during echo"),ct);
Require(await clipboard.Task.WaitAsync(TimeSpan.FromSeconds(3),ct)=="clipboard during echo","Probe added a competing response reader");
Require(Samples(manager).Count==0,"Unmatched sequence became RTT evidence");
await NLControlProtocol.WriteAsync(android.GetStream(),new NLControlInputResponse(4,Sequence:echo.Sequence),ct);
await Until(()=>Samples(manager).Count==2,"Matching echo was not measured");
Require(Samples(manager).All(s=>s.Valid && s.RttMs==28 && s.Generation==42 && s.Domain==NLNetworkDomain.DeviceLink && s.Source==NLNetworkSampleSource.LanControl),"Local echo RTT/source/domain/generation is incorrect");
clock.Advance(971);await Task.Delay(30,ct);Require(android.Available==0,"Probe exceeded one attempt per second");
clock.Advance(1);var unanswered=await NLControlProtocol.ReadAsync<NLControlInputCommand>(android.GetStream(),ct);
clock.Advance(1000);await Task.Delay(30,ct);Require(android.Available==0,"More than one probe was pending");
clock.Advance(1000);var replacement=await NLControlProtocol.ReadAsync<NLControlInputCommand>(android.GetStream(),ct);
Require(replacement.Sequence!=unanswered.Sequence,"Expired probe reused its wire identity");
Require(Samples(manager).Any(s=>!s.Valid && s.RttMs is null),"Timeout was reported as successful RTT or invented packet loss");
await NLControlProtocol.WriteAsync(android.GetStream(),new NLControlInputResponse(4,Sequence:unanswered.Sequence),ct);
await Task.Delay(30,ct);Require(!Samples(manager).Any(s=>s.Valid && s.ObservedAtTicks==clock.GetTimestamp()),"Late echo completed a newer request");
Configure(manager,43,"VE:USB:new",new(StabilityTelemetry:true),clock);
var next=await NLControlProtocol.ReadAsync<NLControlInputCommand>(android.GetStream(),ct);
Require(next.Sequence!=replacement.Sequence && Samples(manager).Count==0,"Route/generation switch retained probe identity or measurements");
await NLControlProtocol.WriteAsync(android.GetStream(),new NLControlInputResponse(4,Sequence:replacement.Sequence),ct);
clock.Advance(17);await NLControlProtocol.WriteAsync(android.GetStream(),new NLControlInputResponse(4,Sequence:next.Sequence),ct);
await Until(()=>Samples(manager).Count==2,"New-route echo was not received");
Require(Samples(manager).All(s=>s.RttMs==17 && s.Source==NLNetworkSampleSource.UsbControl && s.Generation==43),"Old-route reply contaminated new-route RTT");
stopProbe!.Invoke(manager,null);clock.Advance(5000);await Task.Delay(30,ct);
Require(android.Available==0 && Samples(manager).Count==0 && manager.IsReadyVE,"Stopping observation stopped control or left a producer alive");
Configure(manager,44,"VE:LAN:stop",new(StabilityTelemetry:true),clock);
await NLControlProtocol.ReadAsync<NLControlInputCommand>(android.GetStream(),ct);
await manager.StopAsync(ct);clock.Advance(5000);await Task.Delay(30,ct);
Require(Samples(manager).Count==0,"Control stop retained an old probe generation");
Console.WriteLine("PASS negotiated/private-session gates, real loopback echo, exact local RTT, one pending/1Hz, stale replies, timeout, input/clipboard coexistence, route and stop cleanup");

using var pcBusy=new TcpClient();var busyConnect=pcBusy.ConnectAsync((IPEndPoint)listener.LocalEndpoint,ct);
using var androidBusy=await listener.AcceptTcpClientAsync(ct);await busyConnect;
using var busyStream=new BlockingWriteStream(pcBusy.GetStream());
await using var busyManager=new VEControlManager();await busyManager.StartNativeAsync(busyStream,ct);
var busyClock=new ManualClock();
var priorityInput=busyManager.SendAsync(VEControlMessage.TextVE("priority input"),ct);
await busyStream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3),ct);
Configure(busyManager,50,"VE:LAN:busy",new(StabilityTelemetry:true),busyClock);
await Task.Delay(40,ct);busyStream.Release.TrySetResult();await priorityInput;
var priority=await NLControlProtocol.ReadAsync<NLControlInputCommand>(androidBusy.GetStream(),ct);
Require(priority.Text=="priority input","Probe preempted the input writer");
await Task.Delay(40,ct);Require(androidBusy.Available==0,"Probe queued behind busy input instead of skipping the observation turn");
busyClock.Advance(1000);var busyEcho=await NLControlProtocol.ReadAsync<NLControlInputCommand>(androidBusy.GetStream(),ct);
busyClock.Advance(19);await NLControlProtocol.WriteAsync(androidBusy.GetStream(),new NLControlInputResponse(4,Sequence:busyEcho.Sequence),ct);
await Until(()=>Samples(busyManager).Count==2,"Busy writer never recovered its observation");
androidBusy.Dispose();
await Until(()=>!busyManager.IsReadyVE,"EOF did not update control state");
await Until(()=>Samples(busyManager).Count==0,"RED: closed control retained RTT evidence and its probe generation");
Console.WriteLine("PASS busy input skips observation without queued echo, EOF invalidates RTT generation");

using var pcFrame=new TcpClient();var frameConnect=pcFrame.ConnectAsync((IPEndPoint)listener.LocalEndpoint,ct);
using var androidFrame=await listener.AcceptTcpClientAsync(ct);await frameConnect;
using var frameStream=new BlockingWriteStream(pcFrame.GetStream());
await using var frameManager=new VEControlManager();await frameManager.StartNativeAsync(frameStream,ct);
Configure(frameManager,60,"VE:LAN:frame",new(StabilityTelemetry:true),new ManualClock());
await frameStream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3),ct);
stopProbe!.Invoke(frameManager,null);
var followingInput=frameManager.SendAsync(VEControlMessage.TextVE("input after disable"),ct);
await Task.Delay(40,ct);
Require(!followingInput.IsCompleted,"RED: disabling observation cancelled a started CONTROL frame and released an uncertain writer");
frameStream.Release.TrySetResult();
var completeFrame=await NLControlProtocol.ReadAsync<NLControlInputCommand>(androidFrame.GetStream(),ct);
var followingFrame=await NLControlProtocol.ReadAsync<NLControlInputCommand>(androidFrame.GetStream(),ct);
await followingInput;
Require(completeFrame.Type==24 && followingFrame.Text=="input after disable" && frameManager.IsReadyVE,"Disabling observation corrupted framing or stopped input");
Console.WriteLine("PASS disabling observation finishes an already started frame and preserves input channel");

using var pcTimeout=new TcpClient();var timeoutConnect=pcTimeout.ConnectAsync((IPEndPoint)listener.LocalEndpoint,ct);
using var androidTimeout=await listener.AcceptTcpClientAsync(ct);await timeoutConnect;
using var timeoutStream=new BlockingWriteStream(pcTimeout.GetStream());
await using var timeoutManager=new VEControlManager();await timeoutManager.StartNativeAsync(timeoutStream,ct);
var timeoutClock=new ManualClock();Configure(timeoutManager,70,"VE:LAN:timeout",new(StabilityTelemetry:true),timeoutClock);
await timeoutStream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3),ct);
timeoutClock.Advance(1999);await Task.Delay(30,ct);Require(timeoutManager.IsReadyVE,"Control write expired before its deadline");
timeoutClock.Advance(1);
await Until(()=>!timeoutManager.IsReadyVE,"RED: a stalled probe write retained the input writer beyond its deadline");
Require(Samples(timeoutManager).Count==0,"Failed frame retained its probe generation");
var eof=new byte[1];Require(await androidTimeout.GetStream().ReadAsync(eof,ct)==0,"Uncertain CONTROL framing left the stream open");
Console.WriteLine("PASS stalled probe write closes uncertain framing at monotonic 2s deadline");

sealed class BlockingWriteStream(Stream inner) : Stream
{
    private int _first=1;
    public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override bool CanRead=>inner.CanRead;
    public override bool CanWrite=>inner.CanWrite;
    public override bool CanSeek=>false;
    public override long Length=>throw new NotSupportedException();
    public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
    public override int Read(byte[] buffer,int offset,int count)=>inner.Read(buffer,offset,count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)=>inner.ReadAsync(buffer,token);
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken token=default)
    {
        if(Interlocked.Exchange(ref _first,0)==1){Entered.TrySetResult();await Release.Task.WaitAsync(token);}
        await inner.WriteAsync(buffer,token);
    }
    public override void Write(byte[] buffer,int offset,int count)=>inner.Write(buffer,offset,count);
    public override void Flush()=>inner.Flush();
    public override Task FlushAsync(CancellationToken token)=>inner.FlushAsync(token);
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long value)=>throw new NotSupportedException();
    protected override void Dispose(bool disposing){if(disposing)inner.Dispose();base.Dispose(disposing);}
}
