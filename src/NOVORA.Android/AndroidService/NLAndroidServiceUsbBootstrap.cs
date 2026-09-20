// NOVORA_AUTOUSB_V1 - one-shot, in-process handoff; never written to preferences, bundles or disk.
using NOVORA.Control;

namespace NOVORA.AndroidService;

internal static class NLAndroidServiceUsbBootstrap
{
    private static readonly object Gate = new();
    private static string? _pending;

    internal static NLControlLanInvitation Parse(string text)
    {
        if (String.IsNullOrWhiteSpace(text) || text.Length > 4096)
            throw new InvalidOperationException("Invitacion USB no valida.");
        var invitation = System.Text.Json.JsonSerializer.Deserialize<NLControlLanInvitation>(Convert.FromBase64String(text))
            ?? throw new InvalidOperationException("Invitacion USB vacia.");
        invitation.Validate(true);
        if (invitation.Host != "127.0.0.1" || invitation.Port != NLControlProtocol.Port)
            throw new InvalidOperationException("Destino USB no valido.");
        return invitation;
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