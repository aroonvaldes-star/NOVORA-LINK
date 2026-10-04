using NOVORA.Control;

namespace NOVORA.LinkClient;

internal static class NovoraConnection
{
    private static readonly NLControlSession Session = new();

    static NovoraConnection() => Session.Changed += (_, state) => Changed?.Invoke(null, state);

    public static event EventHandler<NLControlSessionState>? Changed;
    public static NLControlSessionState Current => Session.Current;

    public static async Task PairAsync(string code)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        IReadOnlyList<NLControlLanPairing> matches = await NLControlLanDiscovery.PairAsync(code, deadline.Token);
        if (matches.Count == 0) throw new IOException("Código no encontrado o vencido. Revisa que ambos equipos estén en la misma LAN.");
        if (matches.Count != 1) throw new IOException("Más de una PC respondió. Genera un código nuevo en NOVORA.");
        await Session.ConnectLanAsync(matches[0].Invitation);
    }

    public static Task ConnectTunnelAsync(NLControlTunnelBootstrap bootstrap) =>
        Session.ConnectTunnelAsync(bootstrap.Invitation, bootstrap.Transport);

    public static Task<NLControlReply> SendAsync(string action, string? value = null) => Session.SendAsync(action, value);
}
