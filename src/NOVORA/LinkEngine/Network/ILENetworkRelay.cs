using System;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.LinkEngine.Network;

internal interface ILENetworkRelay : IAsyncDisposable
{
    event EventHandler? ExitedLE;

    bool IsRunningLE { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task EnsureRunningAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
