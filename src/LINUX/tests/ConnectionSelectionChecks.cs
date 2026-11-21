using NOVORA.Linux.Control;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;

internal static class ConnectionSelectionChecks
{
    public static async Task RunAsync()
    {
        await using var host = new LinuxControlHost();
        bool rejected = false;
        try { host.RequestVision(); }
        catch (InvalidOperationException) { rejected = true; }
        if (!rejected) throw new Exception("VER PANTALLA must reject a missing authorized connection instead of silently succeeding.");
        foreach (bool usb in new[]{false,true})
        foreach (bool lan in new[]{false,true})
        {
            string? expected=usb ? "USB" : lan ? "LAN" : null;
            if(LinuxConnectionSelection.Resolve(null,usb,lan,null)!=expected) throw new Exception("Dynamic routing must use the authorized route, preferring USB.");
            string? usbPreferred=usb ? "USB" : lan ? "LAN" : null;
            if(LinuxConnectionSelection.Resolve("USB",usb,lan,null)!=usbPreferred) throw new Exception("An unauthorized preferred route must not block the authorized one.");
            string? lanPreferred=lan ? "LAN" : usb ? "USB" : null;
            if(LinuxConnectionSelection.Resolve("LAN",usb,lan,null)!=lanPreferred) throw new Exception("A preference must decide between two authorized routes.");
            if(LinuxConnectionSelection.Resolve("LAN",usb,lan,"USB")!="USB") throw new Exception("A capture must retain its owning transport.");
        }
        string directory=Path.Combine(Path.GetTempPath(),"novora-route-check-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var store=new LinuxTrustStore(directory);
            await using var dual=new LinuxControlHost();
            var usb=new LinuxControlServer(IPAddress.Loopback,store,port:0,transport:"USB",allowLoopbackForTests:true);
            var lan=new LinuxControlServer(IPAddress.Loopback,store,port:0,allowLoopbackForTests:true);
            Set(dual,"_usbServer",usb); Set(dual,"_lanServer",lan);
            usb.Start(); lan.Start();
            using var lanClient=await ConnectAsync(lan.Invitation.Host,lan.Invitation.Port,store.Fingerprint);
            await PairAsync(lanClient,"pair.lan",lan.Invitation.Code);
            if(dual.IsUsbAuthorized || !dual.IsLanAuthorized) throw new Exception("Waiting USB inherited LAN authorization.");
            // Preferred USB is still waiting: the authorized LAN route is used instead of blocking.
            dual.SelectTransport("USB");
            dual.RequestVision();
            if(!lan.VisionRequested || usb.VisionRequested) throw new Exception("A waiting USB preference blocked the authorized LAN route.");
            await dual.StopVisionAsync();
            if(lan.VisionRequested) throw new Exception("Stopping capture did not cancel the pending authorization request.");
            using var usbClient=await ConnectAsync(usb.UsbInvitation.Host,usb.UsbInvitation.Port,store.Fingerprint);
            await PairAsync(usbClient,"pair",usb.UsbInvitation.Secret);
            if(dual.SelectedTransport!="USB") throw new Exception("Both routes authorized must use the USB preference.");
            dual.SelectTransport("LAN"); dual.RequestVision();
            if(!lan.VisionRequested || usb.VisionRequested) throw new Exception("Selected LAN capture was requested on USB.");
            dual.SelectTransport("USB");
            if(dual.SelectedTransport!="LAN") throw new Exception("A pending capture must keep its route when the preference changes.");
            await dual.StopVisionAsync();
            if(lan.VisionRequested) throw new Exception("Stopping capture did not cancel the pending authorization request.");
            dual.RequestVision();
            if(!usb.VisionRequested || lan.VisionRequested) throw new Exception("USB capture/status disagreed on the active route.");
            await dual.StopVisionAsync();
            await dual.StopLanAsync();
            if(!dual.IsUsbAuthorized || dual.IsRunning) throw new Exception("Stopping LAN interrupted an authorized USB connection.");
        }
        finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
        Console.WriteLine("USB/LAN TLS authorization, dynamic capture routing, route ownership during capture, consent cancellation and independent LAN stop passed.");
    }

    private static void Set(object target,string name,object value) => typeof(LinuxControlHost).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(target,value);
    internal static async Task<SslStream> ConnectAsync(string host,int port,string fingerprint)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var socket=new Socket(SocketType.Stream,ProtocolType.Tcp);
        await socket.ConnectAsync(host,port,timeout.Token);
        var tls=new SslStream(new NetworkStream(socket,ownsSocket:true),false,(_,cert,_,_)=>cert is not null && Convert.ToHexString(SHA256.HashData(cert.GetRawCertData()))==fingerprint);
        try { await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost="NOVORA" },timeout.Token); return tls; }
        catch { await tls.DisposeAsync(); throw; }
    }
    internal static async Task PairAsync(SslStream stream,string action,string code)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ControlProtocol.WriteAsync(stream,new ControlRequest(1,1,action,Code:code),timeout.Token);
        var reply=await ControlProtocol.ReadAsync<ControlReply>(stream,timeout.Token);
        if(!reply.Success) throw new Exception("Fixture pairing failed: "+reply.Message);
    }
}
