namespace NOVORA.Service;

public sealed class NLServiceSingleInstance : IDisposable
{
    // La versión experimental debe poder convivir con la instalación normal para probar
    // los motores sin que la instancia de producción cierre este ejecutable.
    public const string ApplicationNameNV = "Local\\NOVORA-LINK.Experimental.App.Singleton.v1";

    private EventWaitHandle? _handleNV;

    private NLServiceSingleInstance(EventWaitHandle handle)
    {
        _handleNV = handle;
    }

    public static NLServiceSingleInstance? TryAcquireNV(string name = ApplicationNameNV)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EventWaitHandle handle = new(false, EventResetMode.ManualReset, name, out bool createdNew);
        if (createdNew) return new(handle);
        handle.Dispose();
        return null;
    }

    public void Dispose()
    {
        EventWaitHandle? handle = Interlocked.Exchange(ref _handleNV, null);
        handle?.Dispose();
    }
}
