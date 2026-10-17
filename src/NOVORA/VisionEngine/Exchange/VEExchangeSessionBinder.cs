using NOVORA.VisionEngine.Control;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Enlaza el Exchange a la generación de inicio VisionEngine: al iniciar una fuente USB o LAN
/// se crea su adaptador y, al reemplazarla o detenerla, el anterior se libera.
/// </summary>
public sealed class VEExchangeSessionBinder
{
    private readonly VEExchangeRouter _router;

    public VEExchangeSessionBinder(VEExchangeRouter router)
        => _router = router ?? throw new ArgumentNullException(nameof(router));

    public VEExchangeSession? Current => _router.Current;

    public Task<VEExchangeSession> BindUsbAsync(
        long generation,
        IVEExchangeUsbBackend backend,
        string serial,
        VEExchangeCapabilities capabilities,
        string receiveRoot,
        Func<bool>? canExchangeFiles = null,
        VEControlManager? control = null)
    {
        var transport = new VEExchangeUsbTransport(backend, serial, capabilities, receiveRoot, canExchangeFiles);
        return BindAsync(generation, control, transport);
    }

    public Task<VEExchangeSession> BindNativeLanAsync(
        long generation,
        IVEExchangeLanFiles files,
        VEExchangeCapabilities capabilities,
        Func<bool>? canExchangeFiles = null,
        VEControlManager? control = null)
    {
        var transport = new VEExchangeLanTransport(files, capabilities, canExchangeFiles);
        return BindAsync(generation, control, transport);
    }

    /// <summary>Libera la sesión de esta generación; una generación distinta no se toca.</summary>
    public ValueTask ReleaseAsync(long generation) => _router.ClearAsync(generation);

    private async Task<VEExchangeSession> BindAsync(long generation, VEControlManager? control, IVEExchangeTransport transport)
    {
        var session = new VEExchangeSession(generation, control, transport);
        VEExchangeSession? previous;
        try
        {
            previous = _router.Bind(session);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        if (previous is not null)
            await previous.DisposeAsync().ConfigureAwait(false);
        return session;
    }
}
