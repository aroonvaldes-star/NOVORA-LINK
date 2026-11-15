using NOVORA.Control;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Canal FILES v2 negociado sobre la sesión LAN. Envía desde PC y notifica las recepciones
/// iniciadas por el teléfono, cuando su contenido ya fue confirmado por tamaño y SHA-256.
/// </summary>
public interface IVEExchangeLanFiles : IAsyncDisposable
{
    /// <summary>Recepción confirmada por el motor FILES.</summary>
    event Action<NLFileTransferResultV2>? Received;

    /// <summary>Envía rutas locales desde PC al teléfono y devuelve el número de archivos enviados.</summary>
    Task<int> SendPathsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
}
