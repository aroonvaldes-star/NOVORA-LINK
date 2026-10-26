// NOVORA_AUTOUSB_V1 - one-shot, in-process handoff; never written to preferences, bundles or disk.
using NOVORA.Control;

namespace NOVORA.AndroidService;

internal static class NLAndroidServiceUsbBootstrap
{
    private static readonly object Gate = new();
    private static string? _pending;

    internal static NLControlTunnelBootstrap Parse(string text)
    {
        if (String.IsNullOrWhiteSpace(text) || text.Length > 4096)
            throw new InvalidOperationException("Invitacion USB no valida.");
        byte[] payload = Convert.FromBase64String(text);
        NLControlTunnelBootstrap? bootstrap = null;
        try { bootstrap = System.Text.Json.JsonSerializer.Deserialize<NLControlTunnelBootstrap>(payload); }
        catch (System.Text.Json.JsonException) { }
        if (bootstrap?.Invitation is null)
        {
            var legacy = System.Text.Json.JsonSerializer.Deserialize<NLControlLanInvitation>(payload)
                ?? throw new InvalidOperationException("Invitacion USB vacia.");
            bootstrap = new NLControlTunnelBootstrap(legacy, "USB");
        }
        var invitation = bootstrap.Invitation;
        invitation.Validate(true);
        if (invitation.Host != "127.0.0.1" || invitation.Port != NLControlProtocol.Port)
            throw new InvalidOperationException("Destino USB no valido.");
        if (bootstrap.Transport is not ("USB" or "LAN"))
            throw new InvalidOperationException("Transporte de NOVORA no valido.");
        return bootstrap;
    }

    internal static void Put(string text)
    {
        _ = Parse(text);
        lock (Gate) _pending = text;
    }

    internal static string? Take()
    {
        lock (Gate)
        {
            string? value = _pending;
            _pending = null;
            return value;
        }
    }
}
