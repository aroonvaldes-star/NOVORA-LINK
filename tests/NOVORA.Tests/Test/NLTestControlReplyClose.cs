using System.Net;
using System.Net.Sockets;
using NOVORA.Control;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestControlReplyClose
{
    [Fact]
    public async Task ConfirmedRejectionSurvivesImmediatePeerClose()
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serve = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync();
                var request = await NLControlProtocol.ReadAsync<NLControlRequest>(socket.GetStream(), default);
                await NLControlProtocol.WriteAsync(socket.GetStream(), new NLControlReply(1, request.Id, false, "Invitación rechazada."), default);
            });
            await using var client = new NLControlClient();
            var reply = await client.ConnectAsync("12345678", port);
            Assert.False(reply.Success);
            Assert.Equal("Invitación rechazada.", reply.Message);
            await serve;
        }
    }
}
