using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Ejecuta Ctrl+V sobre VisionEngine.
///
/// Archivo/carpeta/APK:
///     TransferExchangeVE
///
/// Texto/URL:
///     TextExchangeVE
/// </summary>
internal static class PasteExchangeVE
{
    public static async Task<int> PasteAsync(
        string serial,
        TransferExchangeVE transfer,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            serial);

        ArgumentNullException.ThrowIfNull(
            transfer);

        IReadOnlyList<RequestExchangeVE> requests =
            ClipboardExchangeVE.ReadVE();

        if (requests.Count == 0)
        {
            return 0;
        }

        List<string> paths =
            [];

        foreach (RequestExchangeVE request in requests)
        {
            switch (request.KindVE)
            {
                case KindExchangeVE.File:
                case KindExchangeVE.Directory:
                case KindExchangeVE.Apk:

                    paths.Add(
                        request.ValueVE);

                    break;

                case KindExchangeVE.Text:
                case KindExchangeVE.Url:

                    await TextExchangeVE
                        .SendAsync(
                            serial,
                            request.ValueVE,
                            cancellationToken)
                        .ConfigureAwait(false);

                    break;
            }
        }

        if (paths.Count > 0)
        {
            await transfer
                .QueueAsync(
                    serial,
                    paths,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return
            requests.Count;
    }
}
