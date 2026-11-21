using NOVORA.Control;

namespace NOVORA.AndroidService;

internal static class NLAndroidServiceLanCodeBootstrap
{
    private static readonly object Gate = new();
    private static NLControlLanCodeHandoff? _pending;

    internal static void Put(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2048)
            throw new InvalidDataException("Entrega LAN no válida.");
        var handoff = System.Text.Json.JsonSerializer.Deserialize<NLControlLanCodeHandoff>(
            Convert.FromBase64String(text)) ?? throw new InvalidDataException("Entrega LAN vacía.");
        handoff.Validate();
        lock (Gate) _pending = handoff;
    }

    internal static NLControlLanCodeHandoff? Take()
    {
        lock (Gate)
        {
            NLControlLanCodeHandoff? value = _pending;
            _pending = null;
            return value;
        }
    }
}
