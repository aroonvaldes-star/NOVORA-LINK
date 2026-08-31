using System.Windows.Threading;

namespace NOVORA.Services;

/// <summary>
/// Coordina el refresco periódico de NOVORA desde un único reloj.
/// Agrupa solicitudes inmediatas y evita ejecutar dos ciclos de refresco a la vez.
/// </summary>
public sealed class PollingCoordinatorService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Func<Task> _refreshAsync;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private bool _pendingRefresh;
    private bool _disposed;

    public PollingCoordinatorService(
        Func<Task> refreshAsync,
        TimeSpan interval)
    {
        _refreshAsync =
            refreshAsync ??
            throw new ArgumentNullException(nameof(refreshAsync));

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                "El intervalo de polling debe ser mayor que cero.");
        }

        _timer =
            new DispatcherTimer
            {
                Interval = interval
            };

        _timer.Tick += Timer_Tick;
    }

    public TimeSpan Interval => _timer.Interval;

    public bool IsRunning => _timer.IsEnabled;

    public void Start(bool refreshImmediately = false)
    {
        ThrowIfDisposed();

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }

        if (refreshImmediately)
        {
            RequestRefresh();
        }
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Stop();
    }

    /// <summary>
    /// Solicita un refresco sin crear otro bucle.
    /// Si ya hay uno ejecutándose, se agrupa como una única pasada pendiente.
    /// </summary>
    public void RequestRefresh()
    {
        if (_disposed)
        {
            return;
        }

        _pendingRefresh = true;
        _ = RunRefreshLoopAsync();
    }

    private async void Timer_Tick(
        object? sender,
        EventArgs e)
    {
        _pendingRefresh = true;
        await RunRefreshLoopAsync();
    }

    private async Task RunRefreshLoopAsync()
    {
        if (!await _refreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            do
            {
                _pendingRefresh = false;

                try
                {
                    await _refreshAsync();
                }
                catch
                {
                    // El callback de refresco es responsable de registrar
                    // sus propios errores. El coordinador nunca debe romper
                    // el Dispatcher por una lectura fallida.
                }
            }
            while (_pendingRefresh && !_disposed);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
    }
}
