namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Guarda la sesión Exchange vigente. Una generación anterior no puede
/// reemplazar ni limpiar la sesión nueva.
/// </summary>
public sealed class VEExchangeRouter
{
    private readonly object _gate = new();
    private VEExchangeSession? _current;

    public VEExchangeSession? Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    /// <summary>
    /// Vincula una sesión. Si había otra, la devuelve para que el llamador la libere.
    /// </summary>
    public VEExchangeSession? Bind(VEExchangeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_gate)
        {
            VEExchangeSession? previous = _current;
            if (previous is not null && previous.Generation > session.Generation)
                throw new InvalidOperationException("Una generación más nueva ya está vinculada.");
            _current = session;
            return previous;
        }
    }

    public bool TryGet(long generation, out VEExchangeSession? session)
    {
        lock (_gate)
        {
            if (_current is not null && _current.Generation == generation)
            {
                session = _current;
                return true;
            }

            session = null;
            return false;
        }
    }

    /// <summary>
    /// Limpia la sesión solo si pertenece a esta generación.
    /// </summary>
    public async ValueTask ClearAsync(long generation)
    {
        VEExchangeSession? toDispose = null;
        lock (_gate)
        {
            if (_current is not null && _current.Generation == generation)
            {
                toDispose = _current;
                _current = null;
            }
        }

        if (toDispose is not null)
            await toDispose.DisposeAsync().ConfigureAwait(false);
    }
}
