using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Punto de entrada de alto nivel para ExchangeVE.
///
/// No descubre dispositivos.
/// El serial siempre procede de la sesión activa de VE.
/// </summary>
internal static class VEExchangeManager
{
    public static ValueTask SendPathsAsync(
        string serial,
        VEExchangeTransfer transfer,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            serial);

        ArgumentNullException.ThrowIfNull(
            transfer);

        ArgumentNullException.ThrowIfNull(
            paths);

        return
            transfer.QueueAsync(
                serial,
                paths,
                cancellationToken);
    }

    public static Task<int> PasteAsync(
        string serial,
        VEExchangeTransfer transfer,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            serial);

        ArgumentNullException.ThrowIfNull(
            transfer);

        return
            VEExchangePaste.PasteAsync(
                serial,
                transfer,
                cancellationToken);
    }
}
