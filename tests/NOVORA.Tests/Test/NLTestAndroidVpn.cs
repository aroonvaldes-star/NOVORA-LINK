using NOVORA.Control;
using NOVORA.LinkEngine.Transport;
using NOVORA.LinkEngine.Protocol;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Xunit;
namespace NOVORA.Tests;
public sealed class NLTestAndroidVpn
{
    [Fact] public async Task AndroidControlFramesMatchExistingPcProtocol()
    {
        using var wire = new MemoryStream();
        await NLControlVpnProtocol.WriteControlAsync(wire,"NOVORA-LINK|1|HELLO|test-phone",default);
        wire.Position=0;
        Assert.Equal("NOVORA-LINK|1|HELLO|test-phone",await LETransportHandshake.ReadFrameLEAsync(wire));
        wire.SetLength(0);wire.Position=0;
        await LETransportHandshake.WriteFrameLEAsync(wire,"NOVORA-LINK|1|ACK");wire.Position=0;
        Assert.Equal("NOVORA-LINK|1|ACK",await NLControlVpnProtocol.ReadControlAsync(wire,default));
    }
    [Fact] public async Task RelayIdentifierIsConsumedBeforeFirstPacket()
    {
        byte[] id = [0x12,0x34,0x56,0x78];
        using var stream = new NLTestFragmentedStream([..id,..Packet(20)]);
        Assert.Equal(0x12345678u,await NLControlVpnProtocol.ReadRelayIdAsync(stream,default));
        Assert.Equal(20,await NLControlVpnProtocol.ReadPacketAsync(stream,new byte[65535],default));
    }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(1025)]
    public async Task BadFrameBoundsRejectedBeforeBody(int length)
    {
        byte[] bytes=new byte[4];BinaryPrimitives.WriteInt32BigEndian(bytes,length);
        using var stream=new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(()=>NLControlVpnProtocol.ReadControlAsync(stream,default));
    }
    [Fact] public async Task FragmentedAndCoalescedPacketsRetainPacketBoundaries()
    {
        byte[] first=Packet(31),second=Packet(20);
        using var stream=new NLTestFragmentedStream([..first,..second]);byte[] buffer=new byte[65535];
        Assert.Equal(31,await NLControlVpnProtocol.ReadPacketAsync(stream,buffer,default));Assert.Equal(first,buffer[..31]);
        Assert.Equal(20,await NLControlVpnProtocol.ReadPacketAsync(stream,buffer,default));Assert.Equal(second,buffer[..20]);
        await Assert.ThrowsAsync<EndOfStreamException>(()=>NLControlVpnProtocol.ReadPacketAsync(stream,buffer,default));
    }
    [Fact] public async Task TruncatedPacketFailsInsteadOfEmittingPartialPacket()
    {
        using var stream=new MemoryStream(Packet(31)[..25]);
        await Assert.ThrowsAsync<EndOfStreamException>(()=>NLControlVpnProtocol.ReadPacketAsync(stream,new byte[65535],default));
    }
    [Theory][InlineData(0x65,20)][InlineData(0x44,20)][InlineData(0x4f,20)][InlineData(0x45,19)]
    public void InvalidIpv4Rejected(byte header,int length)
    {
        byte[] packet=Packet(20);packet[0]=header;BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)length);
        Assert.Throws<InvalidDataException>(()=>NLControlVpnProtocol.ValidateIpv4(packet));
    }
    [Fact] public async Task HeartbeatMatchesPcAndCancellationStopsWaiting()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        using var phone=new TcpClient();await phone.ConnectAsync(IPAddress.Loopback,((IPEndPoint)listener.LocalEndpoint).Port);
        using var pc=await listener.AcceptTcpClientAsync();using var cancel=new CancellationTokenSource();
        Task heartbeats=NLControlVpnProtocol.HeartbeatAsync(phone.GetStream(),cancel.Token);
        string payload=await LETransportHandshake.ReadFrameLEAsync(pc.GetStream()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(LEProtocolHeartbeat.TryParseHeartbeatLE(payload,out long sequence,out long timestamp,out _));
        Assert.Equal(1,sequence);
        await LETransportHandshake.WriteFrameLEAsync(pc.GetStream(),LEProtocolHeartbeat.CreateAckPayloadLE(sequence,timestamp));
        cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>heartbeats.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    [Fact] public async Task WrongHeartbeatAckClosesProtocol()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        using var phone=new TcpClient();await phone.ConnectAsync(IPAddress.Loopback,((IPEndPoint)listener.LocalEndpoint).Port);
        using var pc=await listener.AcceptTcpClientAsync();using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task heartbeats=NLControlVpnProtocol.HeartbeatAsync(phone.GetStream(),cancel.Token);
        await LETransportHandshake.ReadFrameLEAsync(pc.GetStream(),cancel.Token);
        await LETransportHandshake.WriteFrameLEAsync(pc.GetStream(),"NOVORA-LINK|1|HEARTBEAT_ACK|999|1|1",cancel.Token);
        await Assert.ThrowsAsync<InvalidDataException>(()=>heartbeats);
    }
    private static byte[] Packet(int length) { byte[] packet=new byte[length];packet[0]=0x45;BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2),(ushort)length);return packet; }
    private sealed class NLTestFragmentedStream(byte[] bytes):MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default) => base.ReadAsync(buffer[..Math.Min(3,buffer.Length)],cancellationToken);
    }
}
