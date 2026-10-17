using NOVORA.VisionEngine.Control;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Contexto de Exchange de una generación VisionEngine concreta.
/// Posee el transporte: al disponer la sesión se libera el adaptador una sola vez.
/// </summary>
public sealed class VEExchangeSession : IAsyncDisposable
{
    private int _disposedVE;

    public VEExchangeSession(
        long generation,
        VEControlManager? control,
        IVEExchangeTransport transport)
    {
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        Generation = generation;
        Control = control;
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public long Generation { get; }

    /// <summary>Control de la sesión; el portapapeles de texto se usa a través de él.</summary>
    public VEControlManager? Control { get; }

    public IVEExchangeTransport Transport { get; }

    public bool IsDisposedVE => Volatile.Read(ref _disposedVE) != 0;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposedVE, 1) != 0) return;
        await Transport.DisposeAsync().ConfigureAwait(false);
    }
}
