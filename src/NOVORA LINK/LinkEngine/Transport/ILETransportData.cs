using System;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;

namespace NOVORA.LinkEngine.Transport;

internal interface ILETransportData : IAsyncDisposable
{
    Task<LECoreResult> StartAsync(
        string serial,
        CancellationToken cancellationToken = default);

    Task EnsureAsync(
        string serial,
        CancellationToken cancellationToken = default);

    Task StopAsync(
        string serial,
        CancellationToken cancellationToken = default);
}
