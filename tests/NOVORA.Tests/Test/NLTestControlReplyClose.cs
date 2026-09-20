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
            await using var server = new NLTestControlTrustHarness(request =>
                Task.FromResult(new NLControlReply(1, request.Id, false, "Invitación rechazada.")));
            await using var client = new NLControlClient();
            var reply = await server.ConnectAsync(client);
            Assert.False(reply.Success);
            Assert.Equal("Invitación rechazada.", reply.Message);
        }
    }
}
