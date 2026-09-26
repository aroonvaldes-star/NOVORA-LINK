namespace NOVORA.Service;

public sealed class NLServiceSingleInstance : IDisposable
{
    public const string ApplicationNameNV = "Local\\NOVORA-LINK.App.Singleton.v1";

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
