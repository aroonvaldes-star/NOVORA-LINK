namespace NOVORA.Linux.VisionEngine;

internal static class LinuxVisionCleanup
{
    // Every owner must be released even when an earlier native/media operation failed.
    public static async Task RunAsync(params Func<Task>[] operations)
    {
        List<Exception>? errors=null;
        foreach (var operation in operations)
        {
            try { await operation().ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { (errors ??= []).Add(ex); }
        }
        if (errors is not null) throw new AggregateException("No se liberaron todos los recursos de VE.",errors);
    }
}
