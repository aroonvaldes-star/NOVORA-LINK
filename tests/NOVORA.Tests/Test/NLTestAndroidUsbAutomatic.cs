using System.Net;
using NOVORA.Control;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestAndroidUsbAutomatic
{
    [Fact]
    public void AutomaticUsbReconnectionChangesEpochForSameSerial()
    {
        var policy = new NLControlUsbAutoPolicy();

        Assert.True(policy.Observe("PHONE", true, false, true, "PHONE|transport_id:2"));
        Assert.True(policy.TryBegin(false));
        long firstEpoch = policy.Epoch;

        Assert.True(policy.Observe("PHONE", true, false, true, "PHONE|transport_id:3"));
        Assert.NotEqual(firstEpoch, policy.Epoch);
        Assert.True(policy.TryBegin(false));
    }

    [Fact]
    public void AutomaticUsbParsesLongTrackDevicesTransportIdentity()
    {
        const string snapshot = "PHONE device product:x model:y transport_id:17\nOTHER offline transport_id:18";

        Assert.Equal("PHONE|transport_id:17",
            NLControlUsbAutoPolicy.ParseTransportIdentity(snapshot, "PHONE"));
    }

    [Fact]
    public void AutomaticUsbPrefersPhysicalTransportWhenWifiEntryIsSelected()
    {
        var candidates = new[]
        {
            new NLControlUsbCandidate("10.0.0.33:5555", true, true),
            new NLControlUsbCandidate("R5CY3118MEW", true, false)
        };

        string serial = NLControlUsbAutoPolicy.SelectPhysicalSerial(
            candidates,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "10.0.0.33:5555",
                "R5CY3118MEW"
            },
            "10.0.0.33:5555");

        Assert.Equal("R5CY3118MEW", serial);
    }

    [Fact]
    public void AutomaticTransportAcceptsWifiOnlyWhenFailoverWasPrepared()
    {
        var policy = new NLControlUsbAutoPolicy();

        Assert.False(policy.Observe("10.0.0.31:5555", true, true, true,
            "10.0.0.31:5555|transport_id:9"));
        Assert.True(policy.Observe("10.0.0.31:5555", true, true, true,
            "10.0.0.31:5555|transport_id:9", allowWifi: true));
        Assert.Equal("10.0.0.31:5555", policy.Serial);
        Assert.True(policy.TryBegin(false));
    }

    [Theory]
    [InlineData("USB-1", "USB-1", "10.0.0.31:5555", true, true, "10.0.0.31:5555")]
    [InlineData("USB-1", "USB-1", "10.0.0.31:5555", false, true, "USB-1")]
    [InlineData("USB-1", "USB-1", "10.0.0.31:5555", true, false, "USB-1")]
    [InlineData("USB-2", "USB-1", "10.0.0.31:5555", true, true, "USB-2")]
    [InlineData("10.0.0.31:5555", "USB-1", "10.0.0.31:5555", true, true, "10.0.0.31:5555")]
    public void VisionUsesPreparedLanOnlyWhenLinkEngineOwnsSelectedUsb(
        string selected,
        string preparedUsb,
        string preparedLan,
        bool linkUsesSelectedUsb,
        bool lanOnline,
        string expected)
    {
        Assert.Equal(expected, NLUIWindowMain.ResolveVisionStartSerialVE(
            selected, preparedUsb, preparedLan, linkUsesSelectedUsb, lanOnline));
    }

    [Fact]
    public void ExistingSingleWifiRouteIsAdoptedWithoutRestartingAdbTcpip()
    {
        NLControlUsbCandidate[] candidates =
        [
            new("USB-1", true, false),
            new("10.0.0.31:5555", true, true)
        ];

        string? lan = NLUIWindowMain.ResolvePreparedVisionLanSerialVE(
            candidates,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "USB-1",
                "10.0.0.31:5555"
            });

        Assert.Equal("10.0.0.31:5555", lan);
    }

    [Fact]
    public void MultipleWifiRoutesAreNotGuessedAsTheActivePhone()
    {
        NLControlUsbCandidate[] candidates =
        [
            new("USB-1", true, false),
            new("10.0.0.31:5555", true, true),
            new("10.0.0.32:5555", true, true)
        ];

        Assert.Null(NLUIWindowMain.ResolvePreparedVisionLanSerialVE(candidates, null));
    }

    [Theory]
    [InlineData(null, "USB-1", "USB-1")]
    [InlineData("", "10.0.0.31:5555", "10.0.0.31:5555")]
    [InlineData("10.0.0.44:5555", "10.0.0.31:5555", "10.0.0.44:5555")]
    public void VisionRestartPreservesFailoverWithoutOverwritingANewerRoute(
        string? current,
        string? captured,
        string expected)
    {
        Assert.Equal(expected, NLUIWindowMain.PreserveVisionFailoverRouteVE(current, captured));
    }

    [Theory]
    [InlineData("package:/data/app/com.novora.appcontrol/base.apk", true)]
    [InlineData("", false)]
    [InlineData("package:/data/app/com.example.other/base.apk", false)]
    public void PcRecognizesInstalledNovoraAndroidPackage(string output, bool expected)
    {
        Assert.Equal(expected, NLUIWindowMain.IsNovoraAndroidInstalled(output));
    }

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

    [Fact]
    public async Task PreparedWifiTunnelIsReportedAsLanWithoutOpeningRawLanControl()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "NOVORA-LanTunnelTest-" + Guid.NewGuid().ToString("N"));
        var snapshot = new NLControlSnapshot(1, "PC", "1.4", "8M", "Balanced",
            "default", "default", false, [], [], []);
        Task<NLControlReply> Handle(NLControlRequest request) => Task.FromResult(
            new NLControlReply(1, request.Id, true, "OK", snapshot));
        try
        {
            using var store = new NLControlTrustStore(directory);
            await using var server = new NLControlTrustServer(IPAddress.Loopback, Handle, store,
                NLControlProtocol.Port, transport: "USB");
            server.Start();
            await using var session = new NLControlSession();
            await session.ConnectTunnelAsync(server.Invitation, "LAN");
            Assert.Equal(NLControlSessionPhase.Connected, session.Current.Phase);
            Assert.Equal("LAN", session.Current.Transport);
            Assert.True(session.Current.Tunnel);
        }
        finally
        {
            string resolved = System.IO.Path.GetFullPath(directory);
            string root = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            if (resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                System.IO.Path.GetFileName(resolved).StartsWith("NOVORA-LanTunnelTest-", StringComparison.Ordinal) &&
                System.IO.Directory.Exists(resolved))
                System.IO.Directory.Delete(resolved, true);
        }
    }
}
