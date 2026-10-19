using NOVORA.Control;
using NOVORA.AndroidVideo;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

var tests = new (string, Action)[]
{
    ("OldPeerGetsNoActuation", () => {
        int bitrate = 2_000_000, fps = 60;
        var session = new NLControlCapturePolicySession(4, bitrate, fps, new(), b => {bitrate=b; return true;}, f => {fps=f; return true;});
        Check(!session.Apply(new(4,1,1_000_000,30)).Accepted && bitrate==2_000_000 && fps==60);
    }),
    ("GenerationAndSequenceRejectOldCommands", () => {
        int bitrate=2_000_000, fps=60;
        var session=NewSession(4, bitrate, fps, b=> {bitrate=b; return true;}, f=> {fps=f; return true;});
        Check(!session.Apply(new(3,1,1_000_000,30)).Accepted);
        Check(session.Apply(new(4,2,1_000_000,30)).Accepted);
        Check(!session.Apply(new(4,1,500_000,20)).Accepted && bitrate==1_000_000 && fps==30);
        session.Close(); Check(!session.Apply(new(4,3,1_500_000,45)).Accepted);
    }),
    ("ProfileBoundsAndGeometryCannotRestartCapture", () => {
        int calls=0;
        var session=NewSession(4,2_000_000,30,_=> {calls++; return true;}, _=> {calls++; return true;});
        Check(!session.Apply(new(4,1,2_000_001,30)).Accepted);
        Check(!session.Apply(new(4,2,249_999,30)).Accepted);
        Check(!session.Apply(new(4,3,1_000_000,31)).Accepted);
        Check(!session.Apply(new(4,4,1_000_000,20,720)).Accepted && calls==0);
    }),
    ("AcceptedCodecCallDoesNotConfirmObservedRate", () => {
        int bitrate=2_000_000, fps=60;
        var session=NewSession(4,bitrate,fps,b=>{bitrate=b;return true;},f=>{fps=f;return true;});
        var ack=session.Apply(new(4,1,1_000_000,30));
        Check(ack.Accepted && ack.ObservedBitrate is null && ack.RequestedBitrate==1_000_000 && bitrate==1_000_000 && fps==30);
    }),
    ("RejectedCodecKeepsFrameProfile", () => {
        int fps=60;
        var session=NewSession(4,2_000_000,fps,_=>false,f=>{fps=f;return true;});
        Check(!session.Apply(new(4,1,1_000_000,30)).Accepted && fps==60);
    }),
    ("FailedFrameActuatorRestoresBitrate", () => {
        int bitrate=2_000_000;
        var session=NewSession(4,bitrate,60,b=>{bitrate=b;return true;},_=>throw new ObjectDisposedException("compositor"));
        Check(!session.Apply(new(4,1,1_000_000,30)).Accepted && bitrate==2_000_000);
    }),
    ("FramePacingConsumesLatestWithoutCatchup", () => {
        var clock=new ManualClock(); var pacer=new NLControlCaptureFramePacer(30,clock);
        Check(pacer.TryPresent()); clock.Advance(TimeSpan.FromMilliseconds(10)); Check(!pacer.TryPresent());
        clock.Advance(TimeSpan.FromMilliseconds(24)); Check(pacer.TryPresent());
        clock.Advance(TimeSpan.FromSeconds(1)); Check(pacer.TryPresent()); Check(!pacer.TryPresent());
        Check(pacer.FramesSeen==5 && pacer.FramesPresented==3 && pacer.FramesSkipped==2);
        Check(pacer.SetTargetFps(15)); clock.Advance(TimeSpan.FromMilliseconds(34)); Check(!pacer.TryPresent());
        clock.Advance(TimeSpan.FromMilliseconds(33)); Check(pacer.TryPresent()); Check(!pacer.SetTargetFps(31));
    }),
    ("KeyframeRequestsCoalescedAndRateLimited", () => {
        var clock=new ManualClock(); var gate=new NLControlKeyframeGate(clock);
        Check(gate.TryRequest()); Check(!gate.TryRequest()); gate.ObserveKeyFrame(); Check(!gate.TryRequest());
        clock.Advance(TimeSpan.FromMilliseconds(999)); Check(!gate.TryRequest());
        clock.Advance(TimeSpan.FromMilliseconds(1)); Check(gate.TryRequest());
        clock.Advance(TimeSpan.FromSeconds(1)); Check(gate.TryRequest());
    }),
    ("FramePacingToleratesVsyncJitterAtSourceRate", () => {
        var clock=new ManualClock(); var pacer=new NLControlCaptureFramePacer(60,clock);
        // 60 Hz source, every other frame 0.5 ms early: all frames must still be presented.
        for(int n=0;n<60;n++){ pacer.TryPresent(); clock.Advance(TimeSpan.FromTicks(n%2==0?161667:171667)); }
        Check(pacer.FramesPresented==60);
    }),
    ("FramePacingPresentsSkippedFrameWhenItsSlotArrives", () => {
        var clock=new ManualClock(); var pacer=new NLControlCaptureFramePacer(30,clock);
        Check(pacer.TryPresent()); clock.Advance(TimeSpan.FromMilliseconds(10)); Check(!pacer.TryPresent());
        Check(pacer.UntilNextPresentation > TimeSpan.Zero && !pacer.TryPresent(deferred:true));
        clock.Advance(pacer.UntilNextPresentation); Check(pacer.TryPresent(deferred:true));
        Check(pacer.FramesSeen==2 && pacer.FramesPresented==2);
    }),
    ("FramePacingKeepsThirtyFromSixtyHzSource", () => {
        var clock=new ManualClock(); var pacer=new NLControlCaptureFramePacer(30,clock);
        for(int n=0;n<60;n++){ pacer.TryPresent(); clock.Advance(TimeSpan.FromTicks(166667)); }
        Check(pacer.FramesPresented==30);
    }),
    ("FramePacingKeepsFortyFiveFromSixtyHzSource", () => {
        var clock=new ManualClock(); var pacer=new NLControlCaptureFramePacer(45,clock);
        for(int n=0;n<60;n++){ pacer.TryPresent(); clock.Advance(TimeSpan.FromTicks(166667)); }
        Check(pacer.FramesPresented==45);
    }),
    ("TelemetryUsesWindowDeltasAndBoundedTimingSamples", () => {
        var clock=new ManualClock(); var metrics=new NLControlCaptureTelemetryWindow(4,clock);
        metrics.RecordWrite(1000,1012,TimeSpan.FromMilliseconds(20)); metrics.RecordQueueAge(TimeSpan.FromMilliseconds(40));
        metrics.RecordDrop(); metrics.SetQueueBytes(3000); clock.Advance(TimeSpan.FromSeconds(1));
        var first=metrics.Take(); Check(first is not null && first.Generation==4 && first.UsefulBytes==1000 && first.ProtocolBytes==1012 && first.QueueBytes==3000 && first.Dropped==1 && first.WriteWaitP95Ms==20 && first.QueueAgeP95Ms==40);
        clock.Advance(TimeSpan.FromSeconds(1)); var second=metrics.Take();
        Check(second is not null && second.Sequence==2 && second.UsefulBytes==0 && second.Dropped==0 && second.WriteWaitP95Ms is null);
    }),
    ("TelemetryShowsAnOngoingBlockedWrite", () => {
        var clock=new ManualClock(); var metrics=new NLControlCaptureTelemetryWindow(4,clock);
        metrics.BeginWrite(); clock.Advance(TimeSpan.FromSeconds(1)); var sample=metrics.Take();
        Check(sample is not null && sample.UsefulBytes==0 && sample.WriteWaitP95Ms==1000);
    }),
    ("EncoderReplacementKeepsTelemetrySequenceMonotonic", () => {
        var clock=new ManualClock(); var sequence=new NLControlCaptureTelemetrySequencer();
        var firstEncoder=new NLControlCaptureTelemetryWindow(4,clock); clock.Advance(TimeSpan.FromSeconds(1));
        var first=sequence.Stamp(firstEncoder.Take()!);
        var nextEncoder=new NLControlCaptureTelemetryWindow(4,clock); clock.Advance(TimeSpan.FromSeconds(1));
        var second=sequence.Stamp(nextEncoder.Take()!);
        Check(first.Generation==4 && first.Sequence==1 && second.Generation==4 && second.Sequence==2);
    })
};
int failed=0; foreach(var (name,test) in tests) {try {test(); Console.WriteLine($"PASS {name}");}catch(Exception e){failed++; Console.WriteLine($"FAIL {name}: {e.Message}");}}
try { await TestTelemetryWriter(); Console.WriteLine("PASS TelemetrySharesWriterWithClipboardAck"); } catch(Exception e) { failed++; Console.WriteLine($"FAIL TelemetrySharesWriterWithClipboardAck: {e}"); }
try { await TestPartialReplyCloses(); Console.WriteLine("PASS PartialReplyFailureIsTerminal"); } catch(Exception e) { failed++; Console.WriteLine($"FAIL PartialReplyFailureIsTerminal: {e.Message}"); }
try { await TestOptionalFilesFailure(); Console.WriteLine("PASS OptionalFilesFailurePreservesVideoAndControl"); } catch(Exception e) { failed++; Console.WriteLine($"FAIL OptionalFilesFailurePreservesVideoAndControl: {e}"); }
Console.WriteLine($"{tests.Length+3-failed}/{tests.Length+3} passed"); return failed==0 ? 0 : 1;
static async Task TestOptionalFilesFailure()
{
    using var rsa=RSA.Create(2048); var request=new CertificateRequest("CN=NOVORA",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
    using var generated=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
    // Schannel requires an imported private key rather than the ephemeral CreateSelfSigned key.
    using var certificate=new X509Certificate2(generated.Export(X509ContentType.Pfx),(string?)null,X509KeyStorageFlags.DefaultKeySet);
    using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var video=new TcpListener(IPAddress.Loopback,0); using var control=new TcpListener(IPAddress.Loopback,0); using var missing=new TcpListener(IPAddress.Loopback,0);
    video.Start(); control.Start(); missing.Start();
    int absent=((IPEndPoint)missing.LocalEndpoint).Port; missing.Stop();
    var offer=new NLControlVeLanOffer(1,"test-session", "127.0.0.1",Convert.ToHexString(SHA256.HashData(certificate.RawData)),DateTimeOffset.UtcNow.AddSeconds(100).ToUnixTimeSeconds(),
        new(((IPEndPoint)video.LocalEndpoint).Port,new string('A',64),"VIDEO"),new(((IPEndPoint)control.LocalEndpoint).Port,new string('B',64),"CONTROL"),null,2_000_000,720,60,false,new(absent,new string('C',64),"FILES"));
    async Task Server(TcpListener listener,NLControlVeLanEndpoint endpoint)
    {
        using var socket=await listener.AcceptTcpClientAsync(stop.Token); await using var tls=new SslStream(socket.GetStream());
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions{ServerCertificate=certificate,EnabledSslProtocols=SslProtocols.Tls12|SslProtocols.Tls13},stop.Token);
        var hello=await NLControlProtocol.ReadAsync<NLControlVeLanHello>(tls,stop.Token); hello.Validate(offer.SessionId,endpoint);
        byte[] payload=new byte[1]; await tls.ReadExactlyAsync(payload,stop.Token); Check(payload[0]==42);
    }
    Task videoServer=Server(video,offer.Video),controlServer=Server(control,offer.Control);
    try
    {
        await using var streams=await new NLAndroidVideoLanClient(true).ConnectAsync(offer,true,stop.Token);
        Check(streams.Files is null); await streams.Video.WriteAsync(new byte[]{42},stop.Token); await streams.Control.WriteAsync(new byte[]{42},stop.Token);
        await Task.WhenAll(videoServer,controlServer);
    }
    finally {stop.Cancel();try{await Task.WhenAll(videoServer,controlServer);}catch{if(videoServer.Exception is { } serverFault)Console.WriteLine(serverFault.GetBaseException());} }
}
static async Task TestPartialReplyCloses()
{
    using var stream=new DuplexCommands { FailWriteAfter=1 }; using var stop=new CancellationTokenSource(TimeSpan.FromMilliseconds(250)); int recovered=0; bool terminal=false;
    try {await NLControlInputLoop.RunDuplexAsync(stream,(command,_)=>Task.FromResult<NLControlInputResponse?>(new(2,Sequence:command.Sequence)),(_,_,_)=> {recovered++; return Task.CompletedTask;},TimeSpan.FromSeconds(2),stop.Token);}
    catch(IOException) { terminal=true; } catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
    Check(terminal && recovered==0 && stream.WriteAttempts==2);
}
static async Task TestTelemetryWriter()
{
    using var stream=new DuplexCommands(); using var stop=new CancellationTokenSource(TimeSpan.FromMilliseconds(1600));
    try {await NLControlInputLoop.RunDuplexAsync(stream,(command,_)=>Task.FromResult<NLControlInputResponse?>(new(2,Sequence:command.Sequence)),(_,_,_)=>Task.CompletedTask,TimeSpan.FromSeconds(2),stop.Token,
        ()=>new(5,CaptureTelemetry:new(4,1,1,1000,1012,0,null,2,0))); }
    catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
    Check(stream.MaximumWriters==1);
    using var captured=new MemoryStream(stream.Output.ToArray()); bool clipboard=false,telemetry=false;
    while(captured.Position<captured.Length){ var response=await NLControlProtocol.ReadAsync<NLControlInputResponse>(captured,CancellationToken.None); clipboard|=response.Type==2; telemetry|=response.Type==5 && response.CaptureTelemetry?.UsefulBytes==1000; }
    Check(clipboard && telemetry);
}
static void Check(bool condition) { if(!condition) throw new InvalidOperationException("Behavior assertion failed."); }
static NLControlCapturePolicySession NewSession(long generation,int b,int f,Func<int,bool> setB,Func<int,bool> setF) => new(generation,b,f,new(LiveVideoBitrate:true,CaptureFramePacing:true),setB,setF);
sealed class ManualClock : TimeProvider {private long _ticks; public override long TimestampFrequency=>TimeSpan.TicksPerSecond; public override long GetTimestamp()=>_ticks; public void Advance(TimeSpan elapsed)=>_ticks+=elapsed.Ticks;}
sealed class DuplexCommands : Stream
{
    private readonly byte[] _command; private int _position,_writers; public int MaximumWriters; public int WriteAttempts,FailWriteAfter=int.MaxValue; public MemoryStream Output { get; }=new();
    public DuplexCommands(){using var command=new MemoryStream(); NLControlProtocol.WriteAsync(command,new NLControlInputCommand(8,Sequence:1),CancellationToken.None).GetAwaiter().GetResult();_command=command.ToArray();}
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){if(_position==0) await Task.Delay(20,token); int count=Math.Min(buffer.Length,_command.Length-_position);_command.AsMemory(_position,count).CopyTo(buffer); _position+=count;if(_position==_command.Length)_position=0;return count;}
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken token=default){if(Interlocked.Increment(ref WriteAttempts)>FailWriteAfter)throw new IOException("Payload write failed after header");int active=Interlocked.Increment(ref _writers);MaximumWriters=Math.Max(MaximumWriters,active);try {await Task.Delay(1,CancellationToken.None); lock(Output) Output.Write(buffer.Span);}finally {Interlocked.Decrement(ref _writers);} }
    public override bool CanRead=>true;public override bool CanWrite=>true;public override bool CanSeek=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
    public override void Flush(){}public override Task FlushAsync(CancellationToken token)=>Task.CompletedTask;public override int Read(byte[]b,int o,int c)=>throw new NotSupportedException();public override void Write(byte[]b,int o,int c)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();
}
