using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Ejecuta Ctrl+V sobre VisionEngine.
///
/// Archivo/carpeta/APK:
///     VEExchangeTransfer
///
/// Texto/URL:
///     VEExchangeText
/// </summary>
internal static class VEExchangePaste
{
    public static async Task<int> PasteAsync(
        string serial,
        VEExchangeTransfer transfer,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            serial);

        ArgumentNullException.ThrowIfNull(
            transfer);

        IReadOnlyList<VEExchangeRequest> requests =
            VEExchangeClipboard.ReadVE();

        if (requests.Count == 0)
        {
            return 0;
        }

        List<string> paths =
            [];

        foreach (VEExchangeRequest request in requests)
        {
            switch (request.KindVE)
            {
                case VEExchangeKind.File:
                case VEExchangeKind.Directory:
                case VEExchangeKind.Apk:

                    paths.Add(
                        request.ValueVE);

                    break;

                case VEExchangeKind.Text:
                case VEExchangeKind.Url:

                    await VEExchangeText
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
