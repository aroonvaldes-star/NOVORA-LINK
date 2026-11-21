using NOVORA.Linux.Control;
using System.Net;
using System.Net.Security;

// Comprobación en vivo del código de enlace LAN en una IP real de la PC, con un almacén de confianza temporal.
// Uso: dotnet run --no-build -- --lan-live-pair <IPv4 privada de esta PC>
internal static class LanLivePairProbe
{
    public static async Task RunAsync(string address)
    {
        string directory = Path.Combine(Path.GetTempPath(), "novora-lan-live-" + Guid.NewGuid().ToString("N"));
        using var store = new LinuxTrustStore(directory);
        await using var server = new LinuxControlServer(IPAddress.Parse(address), store);
        server.Start();
        var invitation = server.Invitation;
        Console.WriteLine($"invitacion: host={invitation.Host} puerto={invitation.Port} codigo={invitation.Code} caduca_unix={invitation.ExpiresUnix}");

        string wrong = invitation.Code == "000000" ? "111111" : "000000";
        await using (var badStream = await ConnectionSelectionChecks.ConnectAsync(invitation.Host, invitation.Port, store.Fingerprint))
        {
            var reply = await SendAsync(badStream, wrong);
            Console.WriteLine($"1) codigo incorrecto -> success={reply.Success} mensaje={reply.Message}");
        }

        await using (var goodStream = await ConnectionSelectionChecks.ConnectAsync(invitation.Host, invitation.Port, store.Fingerprint))
        {
            var reply = await SendAsync(goodStream, invitation.Code);
            Console.WriteLine($"2) codigo correcto -> success={reply.Success} mensaje={reply.Message}");
        }

        await using (var reuseStream = await ConnectionSelectionChecks.ConnectAsync(invitation.Host, invitation.Port, store.Fingerprint))
        {
            var reply = await SendAsync(reuseStream, invitation.Code);
            Console.WriteLine($"3) reutilizar el mismo codigo -> success={reply.Success} mensaje={reply.Message}");
        }
    }

    private static async Task<ControlReply> SendAsync(SslStream stream, string code)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ControlProtocol.WriteAsync(stream, new ControlRequest(1, 1, "pair.lan", Code: code), timeout.Token);
        return await ControlProtocol.ReadAsync<ControlReply>(stream, timeout.Token);
    }
}
