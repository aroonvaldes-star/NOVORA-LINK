using System.Net;
using NOVORA.Control;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestAndroidUsbAutomatic
{
    [Fact]
    public void UsbInvitationAcceptsSmallPhonePcClockSkew()
    {
        var invitation = new NLControlLanInvitation(
            1,
            "127.0.0.1",
            NLControlProtocol.Port,
            new string('A', 64),
            new string('B', 64),
            DateTimeOffset.UtcNow.AddMinutes(3).ToUnixTimeSeconds());

        invitation.Validate(allowLoopback: true);
    }

    [Theory]
    [InlineData("192.168.1.2",27214)]
    [InlineData("127.0.0.1",27215)]
    public void UsbSavedDestinationCannotBecomeLan(string host, int port) {
        var pc=new NLControlTrustedPc("PC",host,port,new string('A',64),Guid.NewGuid().ToString("N"),new string('B',64),"USB");
        Assert.Throws<System.IO.InvalidDataException>(()=>pc.Validate());
    }
    [Fact]
    public async Task UsbBootstrapEnrollAndResumeUsePinnedTlsAndPcConsent()
    {
        string directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"NOVORA-UsbTest-"+Guid.NewGuid().ToString("N"));
        var snapshot=new NLControlSnapshot(1,"PC","1.4","8M","Balanced","default","default",false,[],[],[]);
        Task<NLControlReply> Handle(NLControlRequest request)=>Task.FromResult(new NLControlReply(1,request.Id,true,"OK",snapshot));
        NLControlTrustedPc trusted;
        try {
            using var store=new NLControlTrustStore(directory);
            bool remember=false;
            await using(var server=new NLControlTrustServer(IPAddress.Loopback,Handle,store,NLControlProtocol.Port,allowRemember:()=>remember,transport:"USB")) {
                server.Start();
                await using var session=new NLControlSession();
                await session.ConnectUsbAsync(server.Invitation);
                Assert.Equal("USB",session.Current.Transport);
                Assert.Equal(NLControlSessionPhase.Connected,session.Current.Phase);
                // The first connection still requires the PC-delivered invitation.
                Assert.False((await session.SendAsync("trust.enroll","Phone")).Success);
                Assert.Empty(store.Devices);
                remember=true;
                var reply=await session.SendAsync("trust.enroll","Phone");
                Assert.True(reply.Success);
                trusted=Assert.IsType<NLControlTrustedPc>(reply.TrustedPc);
                trusted.Validate(); Assert.Equal("USB",trusted.Transport); Assert.Single(store.Devices);
            }
            await using(var server=new NLControlTrustServer(IPAddress.Loopback,Handle,store,NLControlProtocol.Port,false,transport:"USB")) {
                server.Start();
                await using var session=new NLControlSession();
                await session.ConnectTrustedAsync(trusted);
                Assert.Equal("USB",session.Current.Transport);
                Assert.Equal(NLControlSessionPhase.Connected,session.Current.Phase);
                store.Revoke(trusted.DeviceId);
                await session.DisposeAsync();
                await using var revoked = new NLControlClient();
                await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(()=>revoked.ConnectTrustedAsync(trusted));
            }
        } finally {
            string resolved=System.IO.Path.GetFullPath(directory);
            string root=System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            if(resolved.StartsWith(root,StringComparison.OrdinalIgnoreCase) && System.IO.Path.GetFileName(resolved).StartsWith("NOVORA-UsbTest-",StringComparison.Ordinal) && System.IO.Directory.Exists(resolved)) System.IO.Directory.Delete(resolved,true);
        }
    }
}
