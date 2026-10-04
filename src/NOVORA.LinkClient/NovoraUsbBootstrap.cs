using System.Text.Json;
using NOVORA.Control;

namespace NOVORA.LinkClient;

internal static class NovoraUsbBootstrap
{
    internal static NLControlTunnelBootstrap Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096)
            throw new InvalidOperationException("Invitación USB no válida.");

        byte[] payload;
        try { payload = Convert.FromBase64String(text); }
        catch (FormatException ex) { throw new InvalidOperationException("Invitación USB no válida.", ex); }

        NLControlTunnelBootstrap? bootstrap = null;
        try { bootstrap = JsonSerializer.Deserialize<NLControlTunnelBootstrap>(payload); }
        catch (JsonException) { }
        if (bootstrap?.Invitation is null)
            throw new InvalidOperationException("Invitación USB vacía.");

        bootstrap.Invitation.Validate(allowLoopback: true);
        if (bootstrap.Invitation.Host != "127.0.0.1" || bootstrap.Invitation.Port != NLControlProtocol.Port)
            throw new InvalidOperationException("Destino USB no válido.");
        if (bootstrap.Transport is not ("USB" or "LAN"))
            throw new InvalidOperationException("Transporte NOVORA no válido.");
        return bootstrap;
    }
}
